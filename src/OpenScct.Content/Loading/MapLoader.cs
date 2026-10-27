using System;
using System.Collections.Generic;
using OpenScct.Content.Actors;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Content.Loading;

/// <summary>
/// Creates the OpenScct.Core actors of a map, the way a UE2 map load builds its level:
/// <list type="number">
/// <item>The level's LevelInfo takes the map's LevelInfo record. Every other record becomes the C# class the
/// <see cref="Registry"/> names (the generated class, with whatever is ported in its partial files), or an
/// <see cref="UnportedActor"/> marker when the class has none.</item>
/// <item>Class defaults: a new object already holds its generated defaults (SetDefaults). Where the map's class table
/// (<c>sim/classes.json</c>) differs from them for the created C# class, the table's values are applied first; for a
/// marker that is every Actor field its class sets.</item>
/// <item>The record's props are copied onto members by UnrealScript name (<see cref="PropApplier"/>), the actors are
/// spawned in level order between <see cref="Level.BeginLoad"/> and <see cref="Level.EndLoad"/>, and actor
/// references (also inside structs and arrays) are applied once every actor exists. A stored <c>Base</c> or
/// <c>Owner</c> is set as serialized data, with no events (<see cref="Actor.SetLoadedBase"/>).</item>
/// <item>After the begin-play passes, actors with an <c>AttachTag</c> are based on the first actor in level order whose
/// Tag or name is that tag (<see cref="MapLoadResult.AttachStartupBases"/>); the <c>bShouldBaseAtStartup</c> floor
/// search needs a collision world and is left to <see cref="MapLoadResult.FindStartupBases"/>.</item>
/// </list>
/// Everything that could not be applied is counted per class and prop in the result.
/// </summary>
public sealed class MapLoader
{
    /// <summary>Props the loader applies itself, never through the member copy.</summary>
    private static readonly HashSet<string> s_loaderProps = new(AsciiCaseComparer.Instance) { "Base", "Owner", "Level" };

    /// <summary>
    /// Class default values the loader never copies: identity and placement (set per actor), engine bookkeeping and
    /// object internals.
    /// </summary>
    private static readonly HashSet<string> s_skipDefaults = new(AsciiCaseComparer.Instance)
    {
        "Tag", "Location", "Rotation", "Base", "Owner", "Level", "XLevel", "Region", "Attached", "Touching", "Name", "Class",
        "Outer", "ObjectFlags", "ObjectInternal", "bDeleteMe",
    };

    private readonly PropApplier _applier = new();
    private readonly Dictionary<(string Class, Type Type), ClassDefaultsPlan> _plans = new();
    private ClassTable? _plansTable;

    public MapLoader(ActorClassRegistry? registry = null)
    {
        Registry = registry ?? ActorClassRegistry.CreateDefault();
    }

    public ActorClassRegistry Registry { get; }

    /// <summary>Apply the map's class table on top of the generated defaults (default on).</summary>
    public bool ApplyClassDefaults { get; init; } = true;

    /// <summary>Loads <paramref name="map"/> into the world's level, with its class table.</summary>
    public MapLoadResult Load(World world, SvMap map)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(map);
        return Load(world.Level, map.Gameplay, map.Classes);
    }

    /// <inheritdoc cref="Load(Level, GameplayDoc, ClassTable)"/>
    public MapLoadResult Load(Level level, GameplayDoc gameplay) => Load(level, gameplay, null);

    /// <summary>
    /// Spawns the actors of <paramref name="gameplay"/> into <paramref name="level"/>, which must not have ticked.
    /// When the level is not loading yet the loader begins and ends the load itself and attaches the AttachTag bases;
    /// when the caller already began one, the loader only spawns, and the caller ends the load and then calls
    /// <see cref="MapLoadResult.AttachStartupBases"/> (to add game actors first, for example).
    /// </summary>
    public MapLoadResult Load(Level level, GameplayDoc gameplay, ClassTable? classes)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(gameplay);
        bool ownLoad = !level.IsLoading;
        if (ownLoad)
        {
            level.BeginLoad();
        }

        if (!ReferenceEquals(_plansTable, classes))
        {
            _plans.Clear();
            _plansTable = classes;
        }

        IReadOnlyList<ActorRecord> records = gameplay.AllActors;
        var actors = new Actor?[records.Count];
        var recordById = new Dictionary<int, ActorRecord>(records.Count);
        var report = new LoadReport();

        // LevelInfo: the level owns it, the map record sets its properties.
        LevelInfo info = level.Info;
        actors[0] = info;
        recordById[info.ActorId] = gameplay.LevelInfo;
        report.Use(gameplay.LevelInfo.Class, info, MapClassKind.LevelInfo);
        ApplyClassDefaultsTo(info, gameplay.LevelInfo.Class, classes, report,
            () => info.GetUcClass().Create() as Actor ?? (Actor)Activator.CreateInstance(info.GetType())!);
        info.Tag = new Name(gameplay.LevelInfo.Tag);
        ApplyValues(info, gameplay.LevelInfo, report);

        for (int i = 1; i < records.Count; i++)
        {
            ActorRecord record = records[i];
            MapClassKind kind;
            if (Registry.TryCreate(record.Class, out Actor? created))
            {
                kind = Registry.IsGenerated(record.Class) ? MapClassKind.Generated : MapClassKind.Registered;
            }
            else
            {
                created = new UnportedActor { ClassPath = record.Class, Record = record, ClassDefaults = classes?.DefaultsOf(record.Class) };
                kind = MapClassKind.Marker;
            }

            Actor actor = created!;
            report.Use(record.Class, actor, kind);
            string path = record.Class;
            ApplyClassDefaultsTo(actor, path, classes, report, kind == MapClassKind.Marker
                ? static () => new UnportedActor()
                : () => Registry.TryCreate(path, out Actor? fresh) ? fresh! : throw new InvalidOperationException(path));
            actor.Tag = new Name(record.Tag);
            ApplyValues(actor, record, report);
            Actor? spawned = level.SpawnActor(actor, actor.Location, actor.Rotation);
            if (spawned is null)
            {
                report.FailedSpawns++;
                continue;
            }

            actors[i] = spawned;
            recordById[spawned.ActorId] = record;
        }

        Actor? Resolve(ActorRef r) => r.Target is { } target ? actors[target.Index] : null;
        for (int i = 0; i < records.Count; i++)
        {
            if (actors[i] is { } actor)
            {
                ActorRecord record = records[i];
                _applier.Apply(actor, actor.GetUcClass(), record.Props, PropApplier.Pass.References, Resolve,
                    (prop, reason) => report.Unapplied(record.Class, prop, reason), s_loaderProps.Contains);
                ApplyLoadedBaseOwnerLevel(actor, record, Resolve, info, report);
            }
        }

        var result = new MapLoadResult(level, gameplay, actors, recordById, report);
        if (ownLoad)
        {
            level.EndLoad();
            result.AttachStartupBases();
        }

        return result;
    }

    private void ApplyValues(Actor actor, ActorRecord record, LoadReport report)
    {
        string cls = record.Class;
        _applier.Apply(actor, actor.GetUcClass(), record.Props, PropApplier.Pass.Values, null,
            (prop, reason) => report.Unapplied(cls, prop, reason), s_loaderProps.Contains);
    }

    /// <summary>Base and Owner as serialized data (no events); Level must name the LevelInfo.</summary>
    private static void ApplyLoadedBaseOwnerLevel(Actor actor, ActorRecord record, Func<ActorRef, Actor?> resolve, LevelInfo info, LoadReport report)
    {
        if (record.Props.TryGetValue("Owner", out PropValue owner) && LoadedRef(owner, resolve, record.Class, "Owner", report, out Actor? o))
        {
            actor.SetLoadedOwner(o);
        }

        if (record.Props.TryGetValue("Base", out PropValue b) && LoadedRef(b, resolve, record.Class, "Base", report, out Actor? newBase) &&
            !actor.SetLoadedBase(newBase))
        {
            report.Unapplied(record.Class, "Base", UnappliedReason.TypeMismatch);
        }

        if (record.Props.TryGetValue("Level", out PropValue level) &&
            (!LoadedRef(level, resolve, record.Class, "Level", report, out Actor? l) || !ReferenceEquals(l, info)))
        {
            report.Unapplied(record.Class, "Level", UnappliedReason.WrongActorClass);
        }
    }

    private static bool LoadedRef(PropValue value, Func<ActorRef, Actor?> resolve, string cls, string prop, LoadReport report, out Actor? actor)
    {
        actor = null;
        switch (value.Kind)
        {
            case PropKind.Null:
                return true;
            case PropKind.Actor:
                actor = resolve(value.AsActor());
                if (actor is null)
                {
                    report.Unapplied(cls, prop, UnappliedReason.MissingActor);
                }

                return true;
            case PropKind.Asset:
                report.Unapplied(cls, prop, UnappliedReason.RenderOnlyActor);
                return true;
            default:
                report.Unapplied(cls, prop, UnappliedReason.TypeMismatch);
                return false;
        }
    }

    // ---- class defaults ------------------------------------------------------------------------------------

    /// <summary>True for a class default the loader never copies (identity, placement, engine bookkeeping).</summary>
    internal static bool SkipsDefault(string prop) => s_skipDefaults.Contains(prop);

    private void ApplyClassDefaultsTo(Actor actor, string classPath, ClassTable? classes, LoadReport report, Func<Actor> create)
    {
        if (!ApplyClassDefaults || classes?.DefaultsOf(classPath) is not { } defaults)
        {
            return;
        }

        ClassDefaultsPlan plan = PlanFor(classPath, actor.GetType(), defaults, create);
        report.Plan(classPath, plan);
        foreach (DefaultChange change in plan.Changes)
        {
            if (change.Value is PropValue value)
            {
                _applier.ApplyOne(actor, change.Member, change.Prop, value, static _ => null, static (_, _) => { });
            }
            else
            {
                PropApplier.Reset(actor, change.Member);
            }
        }
    }

    /// <summary>
    /// What differs between the class table and a new object of the C# class (made by <paramref name="create"/>, the
    /// factory the actor came from): table values that change a member, and script properties the table leaves at zero
    /// that the object does not hold at zero. Members holding actor or other object references are left alone: class
    /// defaults never name map actors, and map data creates no objects.
    /// </summary>
    private ClassDefaultsPlan PlanFor(string classPath, Type type, PropBag defaults, Func<Actor> create)
    {
        if (_plans.TryGetValue((classPath, type), out ClassDefaultsPlan? cached))
        {
            return cached;
        }

        Actor pristine = create();
        PropApplier.TypeMembers members = _applier.MembersOf(type, pristine.GetUcClass());
        var changes = new List<DefaultChange>();
        var unapplied = new List<string>();
        foreach (KeyValuePair<string, PropValue> d in defaults)
        {
            if (s_skipDefaults.Contains(d.Key))
            {
                continue;
            }

            if (!members.ByName.TryGetValue(d.Key, out PropApplier.Member? m))
            {
                unapplied.Add(d.Key);
                continue;
            }

            if (m.HoldsActors || m.HoldsObjects)
            {
                continue;
            }

            object? current = m.Get(pristine);
            if (!_applier.TryPreview(m, current, d.Value, out object? converted))
            {
                unapplied.Add(d.Key);
                continue;
            }

            if (!PropApplier.ValueEquals(current, converted))
            {
                changes.Add(new DefaultChange(d.Key, m, d.Value));
            }
        }

        foreach (KeyValuePair<UcPropertyInfo, PropApplier.Member> p in members.ScriptMembers)
        {
            string name = p.Key.Name;
            if (s_skipDefaults.Contains(name) || defaults.Contains(name) || p.Value.HoldsActors || p.Value.HoldsObjects)
            {
                continue;
            }

            if (!PropApplier.IsZero(p.Value.Get(pristine), p.Value.Type))
            {
                changes.Add(new DefaultChange(name, p.Value, null));
            }
        }

        var plan = new ClassDefaultsPlan(changes, unapplied);
        _plans[(classPath, type)] = plan;
        return plan;
    }

    internal sealed record DefaultChange(string Prop, PropApplier.Member Member, PropValue? Value);

    internal sealed record ClassDefaultsPlan(List<DefaultChange> Changes, List<string> Unapplied);

    /// <summary>Counters filled while loading; turned into the result's reports.</summary>
    internal sealed class LoadReport
    {
        public readonly SortedDictionary<string, ClassUse> Classes = new(StringComparer.Ordinal);
        public readonly SortedDictionary<(string Class, string Prop, UnappliedReason Reason), int> Props = new(PropKeyOrder.Instance);
        public int FailedSpawns;

        public void Use(string cls, Actor actor, MapClassKind kind)
        {
            if (!Classes.TryGetValue(cls, out ClassUse? use))
            {
                use = new ClassUse(actor.GetType(), kind);
                Classes[cls] = use;
            }

            use.Count++;
        }

        public void Plan(string cls, ClassDefaultsPlan plan)
        {
            if (Classes.TryGetValue(cls, out ClassUse? use))
            {
                use.Plan ??= plan;
            }
        }

        public void Unapplied(string cls, string prop, UnappliedReason reason)
        {
            var key = (cls, prop, reason);
            Props[key] = Props.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>Report order: class and prop by ordinal, then reason.</summary>
    private sealed class PropKeyOrder : IComparer<(string Class, string Prop, UnappliedReason Reason)>
    {
        public static readonly PropKeyOrder Instance = new();

        public int Compare((string Class, string Prop, UnappliedReason Reason) x, (string Class, string Prop, UnappliedReason Reason) y)
        {
            int c = string.CompareOrdinal(x.Class, y.Class);
            if (c == 0)
            {
                c = string.CompareOrdinal(x.Prop, y.Prop);
            }

            return c != 0 ? c : x.Reason.CompareTo(y.Reason);
        }
    }

    internal sealed class ClassUse
    {
        public ClassUse(Type type, MapClassKind kind)
        {
            Type = type;
            Kind = kind;
        }

        public Type Type { get; }

        public MapClassKind Kind { get; }

        public int Count { get; set; }

        public ClassDefaultsPlan? Plan { get; set; }
    }
}

/// <summary>How the actors of one class were created.</summary>
public enum MapClassKind : byte
{
    /// <summary>The level's own LevelInfo.</summary>
    LevelInfo,

    /// <summary>The generated class of a package registered with <see cref="ActorClassRegistry.RegisterPackage"/>.</summary>
    Generated,

    /// <summary>A class registered explicitly (<see cref="ActorClassRegistry.Register{T}"/>).</summary>
    Registered,

    /// <summary>An <see cref="UnportedActor"/> marker: no C# class.</summary>
    Marker,
}

/// <summary>One class a map uses: how many actors, the C# class created for them and what of the class table applied.</summary>
/// <param name="ClassPath">The original <c>Package.Class</c>.</param>
/// <param name="Count">Actors of the class (the LevelInfo counts for its class).</param>
/// <param name="CSharpType">The C# class created.</param>
/// <param name="Kind">Generated, explicitly registered, marker, or the LevelInfo.</param>
/// <param name="HasPortedCode">True when the C# class declares ported functions (<c>[Uc]</c> members), false for a bare generated class or marker.</param>
/// <param name="DefaultsApplied">Class table values that differed from the C# class's own defaults and were applied.</param>
/// <param name="DefaultsUnapplied">Class table values with no member in the C# class (or that did not fit it).</param>
public sealed record MapClassReport(string ClassPath, int Count, Type CSharpType, MapClassKind Kind, bool HasPortedCode,
    IReadOnlyList<string> DefaultsApplied, IReadOnlyList<string> DefaultsUnapplied);

/// <summary>A prop that was not (or not fully) applied: class, prop, why, and how many actors.</summary>
public sealed record UnappliedProp(string ClassPath, string Prop, UnappliedReason Reason, int Count);

/// <summary>What <see cref="MapLoader"/> created: the actor of every record, and what it could not port.</summary>
public sealed class MapLoadResult
{
    private readonly Actor?[] _actors;
    private readonly Dictionary<int, ActorRecord> _recordById;
    private bool _attached;

    internal MapLoadResult(Level level, GameplayDoc gameplay, Actor?[] actors, Dictionary<int, ActorRecord> recordById,
        MapLoader.LoadReport report)
    {
        Level = level;
        Gameplay = gameplay;
        _actors = actors;
        _recordById = recordById;
        FailedSpawns = report.FailedSpawns;

        var classes = new List<MapClassReport>();
        var unknown = new List<KeyValuePair<string, int>>();
        int known = 0;
        int unknownCount = 0;
        foreach (KeyValuePair<string, MapLoader.ClassUse> c in report.Classes)
        {
            MapLoader.ClassUse use = c.Value;
            var applied = new List<string>();
            foreach (var change in use.Plan?.Changes ?? [])
            {
                applied.Add(change.Prop);
            }

            classes.Add(new MapClassReport(c.Key, use.Count, use.Type, use.Kind, HasPortedCode(use.Type), applied,
                (IReadOnlyList<string>?)use.Plan?.Unapplied ?? Array.Empty<string>()));
            switch (use.Kind)
            {
                case MapClassKind.Marker:
                    unknown.Add(new KeyValuePair<string, int>(c.Key, use.Count));
                    unknownCount += use.Count;
                    break;
                case MapClassKind.Generated:
                case MapClassKind.Registered:
                    known += use.Count;
                    break;
            }
        }

        Classes = classes;
        UnknownClasses = unknown;
        UnknownCount = unknownCount;
        KnownCount = known;

        var details = new List<UnappliedProp>();
        var byProp = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<(string Class, string Prop, UnappliedReason Reason), int> p in report.Props)
        {
            details.Add(new UnappliedProp(p.Key.Class, p.Key.Prop, p.Key.Reason, p.Value));
            string key = $"{p.Key.Class}.{p.Key.Prop}";
            byProp[key] = byProp.GetValueOrDefault(key) + p.Value;
        }

        Unapplied = details;
        UnappliedProps = new List<KeyValuePair<string, int>>(byProp);
    }

    public Level Level { get; }

    public GameplayDoc Gameplay { get; }

    /// <summary>The actor of each record by <see cref="ActorRecord.Index"/> (0 is the LevelInfo); null when its spawn failed.</summary>
    public IReadOnlyList<Actor?> Actors => _actors;

    /// <summary>Placed actors (LevelInfo excluded) created from a generated or registered C# class.</summary>
    public int KnownCount { get; }

    /// <summary>Placed actors created as <see cref="UnportedActor"/> markers.</summary>
    public int UnknownCount { get; }

    /// <summary>Classes with no C# class (created as markers) and how many placed actors use each, by class path.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> UnknownClasses { get; }

    /// <summary>Every class the map uses, by class path: the C# class created and what of the class table applied.</summary>
    public IReadOnlyList<MapClassReport> Classes { get; }

    /// <summary>
    /// Props that found no matching member, did not fit it, or were applied with references left None, as
    /// <c>Package.Class.Prop</c> with the number of actors affected. Shrinks as classes are ported.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, int>> UnappliedProps { get; }

    /// <summary><see cref="UnappliedProps"/> with the reason, by class, prop and reason.</summary>
    public IReadOnlyList<UnappliedProp> Unapplied { get; }

    /// <summary>Records whose spawn returned null.</summary>
    public int FailedSpawns { get; }

    /// <summary>Actors based by <see cref="AttachStartupBases"/>: the actor, its AttachTag and the base found (null when none was).</summary>
    public IReadOnlyList<(Actor Actor, Name AttachTag, Actor? Base)> AttachTagBases { get; private set; } = [];

    /// <summary>The actor created for <paramref name="id"/> (ignoring ASCII case), or null.</summary>
    public Actor? FindActor(string id) => Gameplay.FindActor(id) is { } record ? _actors[record.Index] : null;

    /// <summary>The actor created for <paramref name="record"/>, or null.</summary>
    public Actor? ActorOf(ActorRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return (uint)record.Index < (uint)_actors.Length ? _actors[record.Index] : null;
    }

    /// <summary>The map record an actor was created from, or null for actors the map did not place.</summary>
    public ActorRecord? RecordOf(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return _recordById.TryGetValue(actor.ActorId, out ActorRecord? record) && ReferenceEquals(_actors[record.Index], actor) ? record : null;
    }

    /// <summary>The actor a reference names, or null when it is None, dangling or failed to spawn.</summary>
    public Actor? Resolve(ActorRef? reference) => reference?.Target is { } record ? _actors[record.Index] : null;

    /// <summary>
    /// The UE2 load-time AttachTag rule, after the begin-play passes: each actor of the level with an AttachTag is
    /// based on the first actor in level order whose Tag equals it or whose name (map id) equals it, ignoring ASCII
    /// case, without a BaseChange notification. Runs once; the loader calls it when it owns the load.
    /// </summary>
    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Estimated,
        "UE2 map load 'find bases' pass after SetInitialState: AttachTag matches Tag or object name, SetBase without notify")]
    public void AttachStartupBases()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        var based = new List<(Actor, Name, Actor?)>();
        IReadOnlyList<Actor> all = Level.Actors;
        for (int i = 0; i < all.Count; i++)
        {
            Actor actor = all[i];
            if (actor.bDeleteMe || actor.AttachTag.IsNone)
            {
                continue;
            }

            string attachTag = actor.AttachTag.ToString();
            Actor? target = null;
            for (int j = 0; j < all.Count && target is null; j++)
            {
                Actor candidate = all[j];
                if (!candidate.bDeleteMe &&
                    (candidate.Tag == actor.AttachTag || (RecordOf(candidate) is { } r && AsciiCaseComparer.AreEqual(r.Id, attachTag))))
                {
                    target = candidate;
                }
            }

            if (target is not null)
            {
                actor.SetBase(target, bNotifyActor: false);
            }

            based.Add((actor, actor.AttachTag, target));
        }

        AttachTagBases = based;
    }

    /// <summary>
    /// Map actors that the UE2 load bases on what is under them (<c>bShouldBaseAtStartup</c> with <c>bCollideWorld</c>,
    /// physics None or Rotating, no AttachTag). The floor search needs collision: pass them to
    /// <see cref="FindStartupBases"/> with a trace.
    /// </summary>
    public List<Actor> StartupBaseCandidates()
    {
        var list = new List<Actor>();
        foreach (Actor? a in _actors)
        {
            if (a is { bDeleteMe: false } actor && actor.AttachTag.IsNone && actor.bCollideWorld && actor.bShouldBaseAtStartup &&
                actor.Physics is EPhysics.PHYS_None or EPhysics.PHYS_Rotating)
            {
                list.Add(actor);
            }
        }

        return list;
    }

    /// <summary>
    /// Runs the UE2 load-time floor search on <see cref="StartupBaseCandidates"/>: <paramref name="findBase"/> returns
    /// what is under the actor (UE2 FindBase traces 8 uu down with the actor's cylinder), and the actor is based on it
    /// with notification. Returns how many actors got a base.
    /// </summary>
    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Estimated,
        "UE2 map load 'find bases' pass: bCollideWorld, bShouldBaseAtStartup, PHYS_None or PHYS_Rotating -> FindBase")]
    public int FindStartupBases(Func<Actor, Actor?> findBase)
    {
        ArgumentNullException.ThrowIfNull(findBase);
        int count = 0;
        foreach (Actor actor in StartupBaseCandidates())
        {
            Actor? found = findBase(actor);
            if (!ReferenceEquals(found, actor.Base))
            {
                actor.SetBase(found);
            }

            if (found is not null)
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasPortedCode(Type type)
    {
        const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
        foreach (System.Reflection.MemberInfo m in type.GetMembers(Flags))
        {
            if (m.IsDefined(typeof(OpenScct.Provenance.UcAttribute), false))
            {
                return true;
            }
        }

        foreach (Type nested in type.GetNestedTypes(Flags))
        {
            if (nested.IsDefined(typeof(OpenScct.Provenance.UcAttribute), false))
            {
                return true;
            }
        }

        return false;
    }
}
