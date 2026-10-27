using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>One of the impact manager's 50 marks: the effect, the decal and when the slot frees again.</summary>
public sealed class ImpactMark
{
    /// <summary>The impact class (SImpactEffectBeton...).</summary>
    public Name Type { get; internal set; }

    /// <summary>Where the effect sits, facing out of the surface.</summary>
    public FVector Location { get; internal set; }

    public FRotator Rotation { get; internal set; }

    /// <summary>The decal projector's place: 10 uu along the rotation's forward axis.</summary>
    public FVector DecalLocation { get; internal set; }

    /// <summary>The decal texture (ImpactMaterial), None for no decal.</summary>
    public AssetRef Material { get; internal set; }

    public AssetRef Sound { get; internal set; }

    public ClassRef Spark { get; internal set; }

    /// <summary>The effect's static mesh when the type is drawn as one (the bullet hole), else None.</summary>
    public AssetRef Mesh { get; internal set; }

    public float DrawScale { get; internal set; } = 1f;

    public bool Unlit { get; internal set; }

    /// <summary>The whole second the mark expires (TabTimer), -1 when the slot is free.</summary>
    public int Timer { get; internal set; } = -1;

    /// <summary>Counts the impacts written to this slot, so a view sees a reused slot as a new mark.</summary>
    public int Serial { get; internal set; }

    public bool Live => Timer != -1;
}

/// <summary>
/// The level's impact marks (Engine.SImpactManager, spec/equipment/impact-manager.md): a ring of 50 slots, each a
/// decal, an effect and a timer; the views draw the live ones. One per machine, made on first use.
/// </summary>
public sealed class ImpactTable
{
    private readonly ImpactMark[] _slots;
    private readonly Dictionary<Name, ImpactEffectDefaults> _defaults = new();
    private float _nextTimer = -1f;

    public ImpactTable()
    {
        _slots = new ImpactMark[K.ImpactSlots];
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new ImpactMark();
        }
    }

    public IReadOnlyList<ImpactMark> Slots => _slots;

    /// <summary>Impacts added so far on this machine.</summary>
    public int Added { get; private set; }

    /// <summary>The table of <paramref name="level"/>, made on first use.</summary>
    public static ImpactTable Of(Level level)
    {
        ImpactTable? table = level.GetService<ImpactTable>();
        if (table is null)
        {
            table = new ImpactTable();
            level.SetService(table);
        }

        return table;
    }

    /// <summary>The class defaults of an impact class, read once per machine.</summary>
    public ImpactEffectDefaults Defaults(Name type)
    {
        if (!_defaults.TryGetValue(type, out ImpactEffectDefaults? d))
        {
            d = ImpactEffectDefaults.Read(type);
            _defaults[type] = d;
        }

        return d;
    }

    /// <summary>The live marks.</summary>
    public int LiveCount
    {
        get
        {
            int n = 0;
            foreach (ImpactMark m in _slots)
            {
                if (m.Live)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>
    /// SImpactManager.Timer (rule 4): every second, marks whose whole-second timer has passed are hidden and freed.
    /// Run from <see cref="Add"/> and <see cref="Update"/>, at the 1 s cadence of the original's looping timer.
    /// </summary>
    [Uc("Engine.SImpactManager.Timer")]
    [PortOf("uc:Engine.SImpactManager:Timer", Spec = "spec/equipment/impact-manager.md#rules")]
    public void Update(float now)
    {
        if (_nextTimer < 0f)
        {
            _nextTimer = now + K.ImpactTimerPeriod;
            return;
        }

        while (now >= _nextTimer)
        {
            foreach (ImpactMark m in _slots)
            {
                if (m.Timer != -1 && _nextTimer > m.Timer)
                {
                    m.Timer = -1;
                }
            }

            _nextTimer += K.ImpactTimerPeriod;
        }
    }

    /// <summary>
    /// SImpactManager.AddImpact on this machine (rule 3): the first free slot, else the oldest; the type's sound,
    /// spark, decal and mesh; the decal 10 uu out along the rotation. Returns the slot used.
    /// </summary>
    [Uc("Engine.SImpactManager.AddImpact")]
    [PortOf("uc:Engine.SImpactManager:AddImpact", Spec = "spec/equipment/impact-manager.md#rules")]
    public ImpactMark Add(ImpactEffectDefaults type, FVector Loc, FRotator Rot, float now)
    {
        Update(now);
        int found = -1;
        int oldest = int.MaxValue;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Timer == -1)
            {
                found = i;
                break;
            }

            if (_slots[i].Timer < oldest)
            {
                found = i;
                oldest = _slots[i].Timer;
            }
        }

        ImpactMark m = _slots[found];
        m.Timer = SvmMath.Trunc(now + K.ImpactLifetime);
        m.Serial++;
        m.Type = type.Type;
        m.Sound = type.SndBullet;
        m.Material = type.ImpactMaterial;
        m.Spark = type.SparkEmitter;
        m.Location = Loc;
        m.Rotation = Rot;
        m.Mesh = type.DrawType == EDrawType.DT_StaticMesh ? type.StaticMesh : AssetRef.None;
        m.DrawScale = type.DrawScale;
        m.Unlit = type.bUnlit;
        Rot.GetAxes(out FVector X, out _, out _);
        m.DecalLocation = Loc + X * K.ImpactDecalOffset;
        Added++;
        return m;
    }
}

/// <summary>The class defaults of an impact class that AddImpact reads (Type.default.*).</summary>
public sealed record ImpactEffectDefaults(Name Type, AssetRef SndBullet, ClassRef SparkEmitter, AssetRef ImpactMaterial, EDrawType DrawType,
    AssetRef StaticMesh, float DrawScale, bool bUnlit)
{
    /// <summary>Reads the defaults of the impact class named <paramref name="type"/> (SImpactEffectBeton or SBase.SImpactEffectBeton).</summary>
    public static ImpactEffectDefaults Read(Name type)
    {
        string path = type.ToString();
        UcClassInfo? info = DeviceClasses.Index.Find(path.Contains('.', System.StringComparison.Ordinal) ? path : "SBase." + path)
            ?? DeviceClasses.Index.Find("Engine." + path);
        return info?.Create() is SImpactEffectBase e
            ? new ImpactEffectDefaults(type, e.SndBullet, e.SparkEmitter, e.ImpactMaterial, e.DrawType, e.StaticMesh, e.DrawScale, e.bUnlit)
            : new ImpactEffectDefaults(type, AssetRef.None, ClassRef.None, AssetRef.None, EDrawType.DT_None, AssetRef.None, 1f, false);
    }
}

/// <summary>
/// The impact manager's calls (spec/equipment/impact-manager.md): the server's traces add an impact, which goes to
/// every player's machine and runs on this one unless it is a dedicated server; each machine fills its own
/// <see cref="ImpactTable"/> and raises the spark and the sound for the views.
/// </summary>
public static class ImpactManager
{
    public static readonly Name CNAME_AddImpact = new("AddImpact");

    /// <summary>The view event of a new mark: Location is the mark, Slot its table index.</summary>
    public static readonly Name ImpactAdded = new("ImpactAdded");

    /// <summary>
    /// Level.ImpactManager.AddImpact from a server trace (rule 3): broadcast to every client (sent to each remote
    /// player, the machines that have the always-relevant manager), and run here on a listen server or standalone.
    /// </summary>
    [Uc("Engine.SImpactManager.AddImpact")]
    [PortOf("uc:Engine.SImpactManager:AddImpact", Spec = "spec/equipment/impact-manager.md#network")]
    [PortOf("rep:Engine.SImpactManager.AddImpact", Spec = "spec/equipment/impact-manager.md#network")]
    [Provenance(ProvenanceKind.Estimated, "The broadcast travels as one server-to-owner combat call per remote player instead of on the manager's channel")]
    public static void AddImpact(Actor Source, Name Type, FVector Loc, FRotator Rot)
    {
        Level level = Source.XLevel;
        if (level.Info.NetMode == ENetMode.NM_Client)
        {
            return;
        }

        var call = new CombatCall(CNAME_AddImpact) { Name1 = Type, Vector1 = Loc, Rotation = Rot };
        foreach (SPlayerController pc in level.AllActors<SPlayerController>())
        {
            if (!pc.bDeleteMe && pc.IsRemotePlayer)
            {
                CombatServices.Net(pc).ClientCall(pc, call);
            }
        }

        if (level.Info.NetMode != ENetMode.NM_DedicatedServer)
        {
            AddImpactHere(level, Type, Loc, Rot);
        }
    }

    /// <summary>The receiving side (and the listen server's own run): the table, then Effects() for the views.</summary>
    [Uc("Engine.SImpactEffectBase.Effects")]
    [PortOf("uc:Engine.SImpactEffectBase:Effects", Spec = "spec/equipment/impact-manager.md#rules")]
    [PortOf("uc:Engine.SImpactEffectBase:Timer", Spec = "spec/equipment/impact-manager.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The impact sound is raised at once; its travel delay (distance / 43561 s, about a tick at 1000 uu) is left to the view")]
    public static ImpactMark AddImpactHere(Level level, Name Type, FVector Loc, FRotator Rot)
    {
        ImpactTable table = ImpactTable.Of(level);
        ImpactEffectDefaults type = table.Defaults(Type);
        ImpactMark mark = table.Add(type, Loc, Rot, level.TimeSeconds);
        SimEvents events = level.World.Events;
        events.Add(new SimEvent
        {
            Kind = SimEventKind.Notify,
            What = ImpactAdded,
            Location = Loc,
            Rotation = Rot,
            Slot = IndexOf(table, mark),
        });
        if (!type.SparkEmitter.IsNone)
        {
            events.SpawnEmitter(null, new Name(ShortName(type.SparkEmitter.Path!)), Loc, Rot);
        }

        if (!type.SndBullet.IsNone)
        {
            events.Add(new SimEvent
            {
                Kind = SimEventKind.PlaySound,
                What = new Name(type.SndBullet.Path!),
                Location = Loc,
                Rotation = Rot,
                Volume = K.ImpactSoundVolume,
                Radius = K.ImpactSoundRadius,
                Pitch = 1f,
            });
        }

        return mark;
    }

    private static int IndexOf(ImpactTable table, ImpactMark mark)
    {
        for (int i = 0; i < table.Slots.Count; i++)
        {
            if (ReferenceEquals(table.Slots[i], mark))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ShortName(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot >= 0 ? path[(dot + 1)..] : path;
    }
}
