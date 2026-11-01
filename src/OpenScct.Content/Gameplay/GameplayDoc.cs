using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content.Json;

namespace OpenScct.Content;

/// <summary>
/// <c>sim/gameplay.json</c>: the LevelInfo and every placed actor in level order with typed properties, plus the
/// derived tag index, event edges and mission tree (spec/svmap/1.0/README.md), and in SVMap 1.1 the animation sets
/// of the skeletal meshes actors use (spec/svmap/1.1/README.md). Actor references inside properties resolve on first
/// use. The derived indexes are read as stored; <see cref="CheckDerived"/> rebuilds and compares them with the rules
/// of the minor version the document declares, the way the pipeline validator does.
/// </summary>
public sealed class GameplayDoc
{
    public const string RelativePath = "sim/gameplay.json";

    private const string LegacyMercDeathmatchKey = "7";
    private const string MercDeathmatchKey = "MercDeathmatch";

    /// <summary>Properties that name an event (the Tag of the receivers) under the 1.0 rules: only <c>Event</c>.</summary>
    public static readonly IReadOnlyList<string> EventProperties = ["Event"];

    /// <summary>1.1: keys (at any depth) whose actor values are sender filters (accept edges).</summary>
    public static readonly IReadOnlyList<string> AcceptKeys = ["DestroyOnEventFrom", "AlertOnEventFrom", "EventFromActor", "EventSender"];

    /// <summary>1.1: name-array properties that list the sequences an actor plays (notify edges).</summary>
    public static readonly IReadOnlyList<string> SequenceKeys = ["Animations", "AnimsFish"];

    private readonly ActorIndex _index;
    private readonly Dictionary<string, int> _tagLookup;
    private readonly Dictionary<string, int> _modeLookup;
    private readonly List<ActorRecord> _all;
    private readonly ActorRecord[][] _tagActors;
    private readonly List<AnimationSet>? _animations;
    private readonly Dictionary<string, AnimationSet> _animationByMesh = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AnimationSet> _animationByMeshCaseless = new(AsciiCaseComparer.Instance);

    private GameplayDoc(string formatVersion, string map, ActorIndex index, List<ActorRecord> all,
        List<KeyValuePair<string, PropBag>> mapSettings, List<TagEntry> tagIndex, List<EventEdge> eventEdges,
        List<MissionNode> missionTree, ActorRecord[][] tagActors, List<AnimationSet>? animations)
    {
        FormatVersion = formatVersion;
        FormatMinor = MinorOf(formatVersion);
        Map = map;
        _index = index;
        _all = all;
        LevelInfo = all[0];
        Actors = all.GetRange(1, all.Count - 1);
        MapSettings = mapSettings;
        TagIndex = tagIndex;
        EventEdges = eventEdges;
        MissionTree = missionTree;
        _tagActors = tagActors;
        _animations = animations;
        GEActivationMask = LevelInfo.Props.GetByte("GEActivationMask");
        foreach (AnimationSet set in animations ?? [])
        {
            _animationByMesh.TryAdd(set.Mesh, set);
            _animationByMeshCaseless.TryAdd(set.Mesh, set);
        }

        _tagLookup = new Dictionary<string, int>(tagIndex.Count, AsciiCaseComparer.Instance);
        for (int i = 0; i < tagIndex.Count; i++)
        {
            _tagLookup.TryAdd(tagIndex[i].Tag, i);
        }

        _modeLookup = new Dictionary<string, int>(mapSettings.Count, AsciiCaseComparer.Instance);
        for (int i = 0; i < mapSettings.Count; i++)
        {
            _modeLookup.TryAdd(mapSettings[i].Key, i);
        }

        if (_modeLookup.TryGetValue(LegacyMercDeathmatchKey, out int legacyMercDeathmatch))
        {
            _modeLookup.TryAdd(MercDeathmatchKey, legacyMercDeathmatch);
        }

        var assets = new List<string>();
        var seenAssets = new HashSet<string>(AsciiCaseComparer.Instance);
        foreach (ActorRecord a in all)
        {
            CollectAssets(a.Props, assets, seenAssets);
        }

        AssetPaths = assets;
    }

    public string FormatVersion { get; }

    /// <summary>The minor version of <see cref="FormatVersion"/> (0 for 1.0 documents, 1 for converted maps).</summary>
    public int FormatMinor { get; }

    /// <summary>Map id.</summary>
    public string Map { get; }

    /// <summary>The LevelInfo actor, <c>Level.Actors[0]</c>.</summary>
    public ActorRecord LevelInfo { get; }

    /// <summary>Every placed actor except the LevelInfo, in level order.</summary>
    public IReadOnlyList<ActorRecord> Actors { get; }

    /// <summary>The LevelInfo, then every placed actor: <c>Level.Actors</c> order. Index i is the record with <see cref="ActorRecord.Index"/> i.</summary>
    public IReadOnlyList<ActorRecord> AllActors => _all;

    /// <summary>GE ID bits live when the round starts (<c>LevelInfo.GEActivationMask</c>).</summary>
    public byte GEActivationMask { get; }

    /// <summary>Per game mode settings (<c>Story</c>, <c>DiskHunt</c>...), preserving stored keys and document order.</summary>
    public IReadOnlyList<KeyValuePair<string, PropBag>> MapSettings { get; }

    /// <summary>The stored tag index in document order. Keys use the spelling of the first carrier.</summary>
    public IReadOnlyList<TagEntry> TagIndex { get; }

    public IReadOnlyList<EventEdge> EventEdges { get; }

    public IReadOnlyList<MissionNode> MissionTree { get; }

    /// <summary>Every distinct asset path the properties refer to, in order of first use.</summary>
    public IReadOnlyList<string> AssetPaths { get; }

    /// <summary>1.1 <c>animations</c> in document order (empty when the document has none).</summary>
    public IReadOnlyList<AnimationSet> Animations => (IReadOnlyList<AnimationSet>?)_animations ?? Array.Empty<AnimationSet>();

    /// <summary>True when the document stores an <c>animations</c> member (even an empty one).</summary>
    public bool HasAnimations => _animations is not null;

    /// <summary>The animation set of a skeletal mesh by asset path (ignoring ASCII case), or null.</summary>
    public AnimationSet? FindAnimationSet(string meshPath) => _animationByMeshCaseless.GetValueOrDefault(meshPath);

    /// <summary>The actor with <paramref name="id"/> (ignoring ASCII case), or null.</summary>
    public ActorRecord? FindActor(string id) => _index.Find(id);

    /// <summary>Actors carrying <paramref name="tag"/> (ignoring ASCII case), in level order, from the stored tag index.</summary>
    public IReadOnlyList<ActorRecord> ActorsWithTag(string tag) =>
        _tagLookup.TryGetValue(tag, out int i) ? _tagActors[i] : Array.Empty<ActorRecord>();

    /// <summary>Settings of one game mode (ignoring ASCII case), including the legacy numeric MercDeathmatch alias, or null.</summary>
    public PropBag? GetModeSettings(string mode) => _modeLookup.TryGetValue(mode, out int i) ? MapSettings[i].Value : null;

    // ---- load ----------------------------------------------------------------------------------------------

    public static GameplayDoc Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static GameplayDoc Parse(ReadOnlySpan<byte> utf8, string file = RelativePath) => FromJson(SvJson.Parse(utf8, file), file);

    /// <summary>A copy of the document with the derived indexes rebuilt from the actors (<see cref="ToJson(bool)"/>).</summary>
    public GameplayDoc WithDerived() => FromJson(ToJson(rebuildDerived: true));

    public static GameplayDoc FromJson(SvJsonNode node, string file = RelativePath)
    {
        ArgumentNullException.ThrowIfNull(node);
        var j = new JsonRead(file);
        SvJsonObject root = j.Object(node, "");
        string version = j.FormatVersion(root, "");
        string map = MapId(j, root);

        var index = new ActorIndex();
        var all = new List<ActorRecord>();
        all.Add(ReadActor(j, j.ObjectMember(root, "levelInfo", ""), "/levelInfo", 0, index));
        if (!all[0].ClassName.EndsWith("LevelInfo", StringComparison.Ordinal))
        {
            throw j.Error("/levelInfo/class", $"'{all[0].Class}' is not a LevelInfo class");
        }

        if (!all[0].Props.TryGetValue("GEActivationMask", out PropValue mask) || !mask.TryGetInt(out int maskValue) ||
            maskValue is < 0 or > 255)
        {
            throw j.Error("/levelInfo/props", "GEActivationMask must be present as a byte");
        }

        SvJsonArray actors = j.ArrayMember(root, "actors", "");
        for (int i = 0; i < actors.Count; i++)
        {
            all.Add(ReadActor(j, j.Object(actors[i], $"/actors/{i}"), $"/actors/{i}", i + 1, index));
        }

        var mapSettings = new List<KeyValuePair<string, PropBag>>();
        foreach (KeyValuePair<string, SvJsonNode> mode in j.ObjectMember(root, "mapSettings", ""))
        {
            string at = $"/mapSettings/{mode.Key}";
            PropBag props = PropJson.BagFromJson(j, j.Object(mode.Value, at), at, index);
            // Legacy PrisD packs store "7" instead of MercDeathmatch. Keep the key for
            // canonical round trips, sim hashes and the matching ui/mapinfo.json text; only the lookup aliases it.
            bool legacyMercDeathmatch = mode.Key == LegacyMercDeathmatchKey &&
                props.TryGetValue("GameModeId", out PropValue id) && id.TryGetInt(out int gameMode) && gameMode == 7;
            if (!legacyMercDeathmatch && (mode.Key.Length == 0 || !char.IsAsciiLetter(mode.Key[0]) || !PropJson.IsIdentifier(mode.Key)))
            {
                throw j.Error(at, "is not a game mode name");
            }

            mapSettings.Add(new KeyValuePair<string, PropBag>(mode.Key, props));
        }

        var tagIndex = new List<TagEntry>();
        var tagActors = new List<ActorRecord[]>();
        foreach (KeyValuePair<string, SvJsonNode> tag in j.ObjectMember(root, "tagIndex", ""))
        {
            string at = $"/tagIndex/{tag.Key}";
            List<string> ids = IdList(j, j.Array(tag.Value, at), at);
            var records = new ActorRecord[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                records[i] = index.Find(ids[i]) ?? throw j.Error($"{at}/{i}", $"'{ids[i]}' is not an actor id");
            }

            tagIndex.Add(new TagEntry(tag.Key, ids));
            tagActors.Add(records);
        }

        var edges = new List<EventEdge>();
        SvJsonArray edgeArray = j.ArrayMember(root, "eventEdges", "");
        for (int i = 0; i < edgeArray.Count; i++)
        {
            string at = $"/eventEdges/{i}";
            SvJsonObject e = j.Object(edgeArray[i], at);
            edges.Add(new EventEdge(j.StringMember(e, "from", at), j.StringMember(e, "via", at), j.StringMember(e, "event", at),
                IdList(j, j.ArrayMember(e, "to", at), $"{at}/to"))
            {
                Kind = j.OptionalString(e, "kind", at),
                Seq = j.OptionalString(e, "seq", at),
                Time = e.TryGetValue("time", out SvJsonNode time) ? j.Number(time, $"{at}/time") : null,
                Notify = j.OptionalString(e, "notify", at),
            });
        }

        var missions = new List<MissionNode>();
        SvJsonArray missionArray = j.ArrayMember(root, "missionTree", "");
        for (int i = 0; i < missionArray.Count; i++)
        {
            string at = $"/missionTree/{i}";
            SvJsonObject m = j.Object(missionArray[i], at);
            var objectives = new List<ObjectiveNode>();
            SvJsonArray objArray = j.ArrayMember(m, "objectives", at);
            for (int k = 0; k < objArray.Count; k++)
            {
                string oat = $"{at}/objectives/{k}";
                SvJsonObject o = j.Object(objArray[k], oat);
                objectives.Add(new ObjectiveNode(j.StringMember(o, "objective", oat), IdList(j, j.ArrayMember(o, "triggers", oat), $"{oat}/triggers")));
            }

            missions.Add(new MissionNode(j.StringMember(m, "mission", at), (int?)j.OptionalInteger(m, "gameMode", at, 0, 255),
                (int?)j.OptionalInteger(m, "minimumObjectives", at, 0, int.MaxValue), objectives)
            {
                Triggers = m.TryGetValue("triggers", out SvJsonNode own) ? IdList(j, j.Array(own, $"{at}/triggers"), $"{at}/triggers") : null,
            });
        }

        List<AnimationSet>? animations = null;
        if (root.TryGetValue("animations", out SvJsonNode animNode))
        {
            animations = new List<AnimationSet>();
            foreach (KeyValuePair<string, SvJsonNode> set in j.Object(animNode, "/animations"))
            {
                string at = $"/animations/{set.Key}";
                animations.Add(ReadAnimationSet(j, set.Key, j.Object(set.Value, at), at, index));
            }
        }

        return new GameplayDoc(version, map, index, all, mapSettings, tagIndex, edges, missions, tagActors.ToArray(), animations);
    }

    private static AnimationSet ReadAnimationSet(JsonRead j, string mesh, SvJsonObject obj, string path, ActorIndex index)
    {
        if (!AssetRef.IsValidPath(mesh))
        {
            throw j.Error(path, $"'{mesh}' is not an asset path");
        }

        var sequences = new List<AnimSequenceInfo>();
        foreach (KeyValuePair<string, SvJsonNode> seq in j.ObjectMember(obj, "sequences", path))
        {
            string at = $"{path}/sequences/{seq.Key}";
            SvJsonObject s = j.Object(seq.Value, at);
            var notifies = new List<AnimNotifyInfo>();
            SvJsonArray array = j.ArrayMember(s, "notifies", at);
            for (int i = 0; i < array.Count; i++)
            {
                string nat = $"{at}/notifies/{i}";
                SvJsonObject n = j.Object(array[i], nat);
                PropBag props = n.TryGetValue("props", out SvJsonNode propsNode)
                    ? PropJson.BagFromJson(j, j.Object(propsNode, $"{nat}/props"), $"{nat}/props", index)
                    : PropBag.Empty;
                notifies.Add(new AnimNotifyInfo(j.Number(j.Member(n, "time", nat), $"{nat}/time"), props)
                {
                    Function = j.OptionalString(n, "function", nat),
                    Object = j.OptionalString(n, "object", nat),
                    Class = j.OptionalString(n, "class", nat),
                    HasProps = n.ContainsKey("props"),
                });
            }

            List<string>? groups = s.TryGetValue("groups", out SvJsonNode groupNode)
                ? IdList(j, j.Array(groupNode, $"{at}/groups"), $"{at}/groups")
                : null;
            sequences.Add(new AnimSequenceInfo(seq.Key, (int)j.IntegerMember(s, "frames", at, 0, int.MaxValue),
                j.Number(j.Member(s, "rate", at), $"{at}/rate"), notifies)
            {
                StartFrame = (int?)j.OptionalInteger(s, "startFrame", at, int.MinValue, int.MaxValue),
                Groups = groups,
            });
        }

        return new AnimationSet(mesh, j.StringMember(obj, "animation", path), sequences);
    }

    private static ActorRecord ReadActor(JsonRead j, SvJsonObject obj, string path, int position, ActorIndex index)
    {
        string id = j.StringMember(obj, "id", path);
        if (!PropJson.IsIdentifier(id))
        {
            throw j.Error($"{path}/id", $"'{id}' is not an actor id");
        }

        string cls = j.StringMember(obj, "class", path);
        if (!PropJson.IsClassPath(cls))
        {
            throw j.Error($"{path}/class", $"'{cls}' is not Package.Class");
        }

        PropBag props = PropJson.BagFromJson(j, j.ObjectMember(obj, "props", path), $"{path}/props", index);
        int? export = null;
        if (obj.TryGetValue("src", out SvJsonNode src))
        {
            export = (int)j.IntegerMember(j.Object(src, $"{path}/src"), "export", $"{path}/src", 1, int.MaxValue);
        }

        var record = new ActorRecord(position, id, cls, props, export);
        if (!index.TryAdd(record))
        {
            throw j.Error($"{path}/id", $"duplicate actor id '{id}' (ids ignore ASCII case)");
        }

        return record;
    }

    private static string MapId(JsonRead j, SvJsonObject root)
    {
        string map = j.StringMember(root, "map", "");
        return PropJson.IsIdentifier(map) ? map : throw j.Error("/map", $"'{map}' is not a map id");
    }

    private static List<string> IdList(JsonRead j, SvJsonArray array, string path)
    {
        var ids = new List<string>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            ids.Add(j.String(array[i], $"{path}/{i}"));
        }

        return ids;
    }

    private static void CollectAssets(PropBag bag, List<string> assets, HashSet<string> seen)
    {
        foreach (KeyValuePair<string, PropValue> item in bag)
        {
            CollectAssets(item.Value, assets, seen);
        }
    }

    private static void CollectAssets(PropValue value, List<string> assets, HashSet<string> seen)
    {
        switch (value.Kind)
        {
            case PropKind.Asset:
                if (seen.Add(value.AsAsset().Path))
                {
                    assets.Add(value.AsAsset().Path);
                }

                break;
            case PropKind.Struct:
                CollectAssets(value.AsStruct(), assets, seen);
                break;
            case PropKind.Array:
                foreach (PropValue item in value.AsArray())
                {
                    CollectAssets(item, assets, seen);
                }

                break;
            case PropKind.StaticArray:
                foreach (KeyValuePair<int, PropValue> e in value.AsStaticArray())
                {
                    CollectAssets(e.Value, assets, seen);
                }

                break;
        }
    }

    // ---- write ---------------------------------------------------------------------------------------------

    /// <summary>The document as JSON; <see cref="CanonicalJson"/> turns it into file or canonical bytes.</summary>
    public SvJsonObject ToJson() => ToJson(rebuildDerived: false);

    /// <summary>
    /// The document as JSON, with the derived indexes as stored or, with <paramref name="rebuildDerived"/>, rebuilt
    /// from the actors by the rules of its minor version (what the pipeline's <c>with_derived</c> writes).
    /// </summary>
    public SvJsonObject ToJson(bool rebuildDerived)
    {
        IReadOnlyList<TagEntry> tagIndex = rebuildDerived ? ComputeTagIndex() : TagIndex;
        IReadOnlyList<EventEdge> eventEdges = rebuildDerived ? ComputeEventEdges() : EventEdges;
        IReadOnlyList<MissionNode> missionTree = rebuildDerived ? ComputeMissionTree() : MissionTree;
        var root = new SvJsonObject();
        root.Add("formatVersion", SvJsonNode.String(FormatVersion));
        root.Add("map", SvJsonNode.String(Map));
        root.Add("levelInfo", ActorToJson(LevelInfo));
        var settings = new SvJsonObject();
        foreach (KeyValuePair<string, PropBag> mode in MapSettings)
        {
            settings.Add(mode.Key, PropJson.BagToJson(mode.Value));
        }

        root.Add("mapSettings", settings);
        var actors = new SvJsonArray();
        foreach (ActorRecord a in Actors)
        {
            actors.Add(ActorToJson(a));
        }

        root.Add("actors", actors);
        var tags = new SvJsonObject();
        foreach (TagEntry t in tagIndex)
        {
            tags.Add(t.Tag, Strings(t.Ids));
        }

        root.Add("tagIndex", tags);
        var edges = new SvJsonArray();
        foreach (EventEdge e in eventEdges)
        {
            var o = new SvJsonObject();
            o.Add("from", SvJsonNode.String(e.From));
            o.Add("via", SvJsonNode.String(e.Via));
            o.Add("event", SvJsonNode.String(e.Event));
            o.Add("to", Strings(e.To));
            AddOptional(o, "kind", e.Kind);
            AddOptional(o, "seq", e.Seq);
            if (e.Time is float time)
            {
                o.Add("time", SvJsonNode.Float(time));
            }

            AddOptional(o, "notify", e.Notify);
            edges.Add(o);
        }

        root.Add("eventEdges", edges);
        var missions = new SvJsonArray();
        foreach (MissionNode m in missionTree)
        {
            var o = new SvJsonObject();
            o.Add("mission", SvJsonNode.String(m.Mission));
            if (m.GameMode is int gameMode)
            {
                o.Add("gameMode", SvJsonNode.Integer(gameMode));
            }

            if (m.MinimumObjectives is int minimum)
            {
                o.Add("minimumObjectives", SvJsonNode.Integer(minimum));
            }

            if (m.Triggers is not null)
            {
                o.Add("triggers", Strings(m.Triggers));
            }

            var objectives = new SvJsonArray();
            foreach (ObjectiveNode obj in m.Objectives)
            {
                var oo = new SvJsonObject();
                oo.Add("objective", SvJsonNode.String(obj.Objective));
                oo.Add("triggers", Strings(obj.Triggers));
                objectives.Add(oo);
            }

            o.Add("objectives", objectives);
            missions.Add(o);
        }

        root.Add("missionTree", missions);
        if (_animations is not null)
        {
            var animations = new SvJsonObject();
            foreach (AnimationSet set in _animations)
            {
                animations.Add(set.Mesh, AnimationSetToJson(set));
            }

            root.Add("animations", animations);
        }

        return root;
    }

    private static SvJsonObject AnimationSetToJson(AnimationSet set)
    {
        var sequences = new SvJsonObject();
        foreach (AnimSequenceInfo seq in set.Sequences)
        {
            var s = new SvJsonObject();
            s.Add("frames", SvJsonNode.Integer(seq.Frames));
            s.Add("rate", SvJsonNode.Float(seq.Rate));
            if (seq.StartFrame is int start)
            {
                s.Add("startFrame", SvJsonNode.Integer(start));
            }

            if (seq.Groups is not null)
            {
                s.Add("groups", Strings(seq.Groups));
            }

            var notifies = new SvJsonArray();
            foreach (AnimNotifyInfo n in seq.Notifies)
            {
                var o = new SvJsonObject();
                o.Add("time", SvJsonNode.Float(n.Time));
                AddOptional(o, "function", n.Function);
                AddOptional(o, "object", n.Object);
                AddOptional(o, "class", n.Class);
                if (n.HasProps)
                {
                    o.Add("props", PropJson.BagToJson(n.Props));
                }

                notifies.Add(o);
            }

            s.Add("notifies", notifies);
            sequences.Add(seq.Name, s);
        }

        var obj = new SvJsonObject();
        obj.Add("animation", SvJsonNode.String(set.Animation));
        obj.Add("sequences", sequences);
        return obj;
    }

    private static void AddOptional(SvJsonObject obj, string key, string? value)
    {
        if (value is not null)
        {
            obj.Add(key, SvJsonNode.String(value));
        }
    }

    private static SvJsonObject ActorToJson(ActorRecord a)
    {
        var o = new SvJsonObject();
        o.Add("id", SvJsonNode.String(a.Id));
        o.Add("class", SvJsonNode.String(a.Class));
        o.Add("props", PropJson.BagToJson(a.Props));
        if (a.SourceExport is int export)
        {
            var src = new SvJsonObject();
            src.Add("export", SvJsonNode.Integer(export));
            o.Add("src", src);
        }

        return o;
    }

    private static SvJsonArray Strings(IReadOnlyList<string> items)
    {
        var array = new SvJsonArray();
        foreach (string s in items)
        {
            array.Add(SvJsonNode.String(s));
        }

        return array;
    }

    // ---- derived indexes (the pipeline's rules, svmbuild/svmap.py) -----------------------------------------

    /// <summary>
    /// The tag index rebuilt from the actors' <c>Tag</c> properties: actors grouped ignoring ASCII case, keyed by
    /// the first carrier's spelling, in level order. Tags that are empty or None are not indexed.
    /// </summary>
    public List<TagEntry> ComputeTagIndex()
    {
        var order = new List<(string Tag, List<string> Ids)>();
        var lookup = new Dictionary<string, int>(AsciiCaseComparer.Instance);
        foreach (ActorRecord a in _all)
        {
            if (!a.Props.TryGetExact("Tag", out PropValue v) || !v.TryGetString(out string tag) || !IsName(tag))
            {
                continue;
            }

            if (!lookup.TryGetValue(tag, out int at))
            {
                at = order.Count;
                lookup[tag] = at;
                order.Add((tag, new List<string>()));
            }

            order[at].Ids.Add(a.Id);
        }

        var result = new List<TagEntry>(order.Count);
        foreach ((string tag, List<string> ids) in order)
        {
            result.Add(new TagEntry(tag, ids));
        }

        return result;
    }

    /// <summary>
    /// Event edges rebuilt from the actors and the rebuilt tag index, with the rules of the document's minor
    /// version: 1.0 has a fire edge per <c>Event</c> property; 1.1 walks every property at any depth for fire and
    /// accept edges and adds the notify edges of the sequences an actor plays (spec/svmap/1.1/README.md).
    /// </summary>
    public List<EventEdge> ComputeEventEdges() => FormatMinor >= 1 ? ComputeEventEdges11() : ComputeEventEdges10();

    private Dictionary<string, IReadOnlyList<string>> TagLookup()
    {
        var tags = new Dictionary<string, IReadOnlyList<string>>(AsciiCaseComparer.Instance);
        foreach (TagEntry t in ComputeTagIndex())
        {
            tags.TryAdd(t.Tag, t.Ids);
        }

        return tags;
    }

    private static List<string> Receivers(Dictionary<string, IReadOnlyList<string>> tags, string ev) =>
        tags.TryGetValue(ev, out IReadOnlyList<string>? to) ? new List<string>(to) : new List<string>();

    private List<EventEdge> ComputeEventEdges10()
    {
        Dictionary<string, IReadOnlyList<string>> tags = TagLookup();
        var edges = new List<EventEdge>();
        foreach (ActorRecord a in _all)
        {
            foreach (string prop in EventProperties)
            {
                if (a.Props.TryGetExact(prop, out PropValue v) && v.TryGetString(out string ev) && IsName(ev))
                {
                    edges.Add(new EventEdge(a.Id, prop, ev, Receivers(tags, ev)));
                }
            }
        }

        return edges;
    }

    private List<EventEdge> ComputeEventEdges11()
    {
        Dictionary<string, IReadOnlyList<string>> tags = TagLookup();
        var edges = new List<EventEdge>();
        foreach (ActorRecord a in _all)
        {
            foreach (KeyValuePair<string, PropValue> prop in SortedMembers(a.Props))
            {
                WalkEvents(a, prop.Value, prop.Key, prop.Key, tags, edges);
            }

            AddNotifyEdges(a, tags, edges);
        }

        return edges;
    }

    /// <summary>Fire and accept edges inside one property value, in document order (struct members by ASCII bytes).</summary>
    private static void WalkEvents(ActorRecord owner, PropValue value, string path, string key,
        Dictionary<string, IReadOnlyList<string>> tags, List<EventEdge> edges)
    {
        switch (value.Kind)
        {
            case PropKind.Actor:
                if (Contains(AcceptKeys, key))
                {
                    string tag = owner.Props.TryGetExact("Tag", out PropValue t) && t.TryGetString(out string s) && IsName(s) ? s : "None";
                    edges.Add(new EventEdge(owner.Id, path, tag, [value.AsActor().Id]) { Kind = EventEdge.AcceptKind });
                }

                return;
            case PropKind.StaticArray:
                foreach (KeyValuePair<int, PropValue> e in value.AsStaticArray())
                {
                    WalkEvents(owner, e.Value, $"{path}[{e.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)}]", key, tags, edges);
                }

                return;
            case PropKind.Struct:
                foreach (KeyValuePair<string, PropValue> member in SortedMembers(value.AsStruct()))
                {
                    WalkEvents(owner, member.Value, path + "." + member.Key, member.Key, tags, edges);
                }

                return;
            case PropKind.Array:
            {
                IReadOnlyList<PropValue> items = value.AsArray();
                for (int i = 0; i < items.Count; i++)
                {
                    WalkEvents(owner, items[i], $"{path}[{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}]", key, tags, edges);
                }

                return;
            }

            case PropKind.String:
            {
                string ev = value.AsString();
                if (IsEventKey(key) && IsName(ev))
                {
                    edges.Add(new EventEdge(owner.Id, path, ev, Receivers(tags, ev)));
                }

                return;
            }
        }
    }

    /// <summary>Notify edges of the sequences an actor plays (its Animations or AnimsFish names) on its Mesh's set.</summary>
    private void AddNotifyEdges(ActorRecord a, Dictionary<string, IReadOnlyList<string>> tags, List<EventEdge> edges)
    {
        if (!a.Props.TryGetExact("Mesh", out PropValue mesh) || mesh.Kind != PropKind.Asset ||
            !_animationByMesh.TryGetValue(mesh.AsAsset().Path, out AnimationSet? set))
        {
            return;
        }

        foreach (string sk in SequenceKeys)
        {
            if (!a.Props.TryGetExact(sk, out PropValue names) || names.Kind != PropKind.Array)
            {
                continue;
            }

            IReadOnlyList<PropValue> list = names.AsArray();
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].TryGetString(out string seqName) || set.FindSequenceExact(seqName) is not { } seq)
                {
                    continue;
                }

                foreach (AnimNotifyInfo n in seq.Notifies)
                {
                    if (!n.Props.TryGetExact("EventName", out PropValue en) || !en.TryGetString(out string ev) || !IsName(ev))
                    {
                        continue;
                    }

                    edges.Add(new EventEdge(a.Id, $"{sk}[{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}]", ev, Receivers(tags, ev))
                    {
                        Kind = EventEdge.NotifyKind,
                        Seq = seqName,
                        Time = n.SecondsInto(seq.Frames, seq.Rate),
                        Notify = n.Object ?? string.Empty,
                    });
                }
            }
        }
    }

    /// <summary>1.1 fire-edge keys: <c>Event</c>, <c>Events</c>, or a longer name ending in <c>Event</c> (exact spelling).</summary>
    public static bool IsEventKey(string key) =>
        key is "Event" or "Events" || (key.Length > 5 && key.EndsWith("Event", StringComparison.Ordinal));

    private static bool Contains(IReadOnlyList<string> list, string key)
    {
        foreach (string s in list)
        {
            if (string.Equals(s, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<KeyValuePair<string, PropValue>> SortedMembers(PropBag bag)
    {
        var members = new List<KeyValuePair<string, PropValue>>(bag);
        members.Sort(static (x, y) => string.CompareOrdinal(x.Key, y.Key));
        return members;
    }

    /// <summary>
    /// The mission tree rebuilt from each SMission's <c>Objectives</c> and each SObjective's <c>Triggers</c>; in 1.1
    /// also the mission's own <c>Triggers</c> when it has any.
    /// </summary>
    public List<MissionNode> ComputeMissionTree()
    {
        var missions = new List<MissionNode>();
        foreach (ActorRecord a in Actors)
        {
            if (a.ClassName != "SMission")
            {
                continue;
            }

            int? gameMode = a.Props.TryGetExact("GameMode", out PropValue gm) && gm.TryGetInt(out int g) ? g : null;
            int? minimum = a.Props.TryGetExact("MinimumObjectives", out PropValue mo) && mo.TryGetInt(out int m) ? m : null;
            var objectives = new List<ObjectiveNode>();
            foreach (string oid in RefList(a.Props, "Objectives"))
            {
                ActorRecord? obj = _index.FindExact(oid);
                objectives.Add(new ObjectiveNode(oid, obj is null ? new List<string>() : RefList(obj.Props, "Triggers")));
            }

            List<string>? own = null;
            if (FormatMinor >= 1)
            {
                own = RefList(a.Props, "Triggers");
                if (own.Count == 0)
                {
                    own = null;
                }
            }

            missions.Add(new MissionNode(a.Id, gameMode, minimum, objectives) { Triggers = own });
        }

        return missions;
    }

    /// <summary>
    /// Problems with the stored derived indexes, found by rebuilding them from the actors with the rules of the
    /// document's minor version (empty when they match). Mirrors the pipeline validator; each problem names the
    /// first difference.
    /// </summary>
    public List<string> CheckDerived()
    {
        var problems = new List<string>();
        List<TagEntry> tags = ComputeTagIndex();
        if (!SameTagIndex(tags, TagIndex))
        {
            problems.Add("tagIndex does not match the actors' Tag properties");
        }

        List<EventEdge> edges = ComputeEventEdges();
        int edgeDiff = FirstDifference(edges, EventEdges, static (a, b) => a.ContentEquals(b));
        if (edgeDiff >= 0)
        {
            problems.Add("eventEdges do not match the actors' event properties: " +
                Difference("eventEdges", edgeDiff, EventEdges, edges));
        }

        List<MissionNode> missions = ComputeMissionTree();
        int missionDiff = FirstDifference(missions, MissionTree, static (a, b) => a.ContentEquals(b));
        if (missionDiff >= 0)
        {
            problems.Add("missionTree does not match the SMission and SObjective properties: " +
                Difference("missionTree", missionDiff, MissionTree, missions));
        }

        return problems;
    }

    private static int FirstDifference<T>(IReadOnlyList<T> derived, IReadOnlyList<T> stored, Func<T, T, bool> same)
    {
        int n = Math.Min(derived.Count, stored.Count);
        for (int i = 0; i < n; i++)
        {
            if (!same(derived[i], stored[i]))
            {
                return i;
            }
        }

        return derived.Count == stored.Count ? -1 : n;
    }

    private static string Difference<T>(string what, int at, IReadOnlyList<T> stored, IReadOnlyList<T> derived)
        where T : class =>
        $"first difference at {what}[{at}]: stored {(at < stored.Count ? stored[at]!.ToString() : "nothing")}, " +
        $"derived {(at < derived.Count ? derived[at]!.ToString() : "nothing")} ({stored.Count} stored, {derived.Count} derived)";

    /// <summary>The minor version of a <c>1.x.y</c> version string (0 when it is not one).</summary>
    public static int MinorOf(string formatVersion)
    {
        ArgumentNullException.ThrowIfNull(formatVersion);
        string[] parts = formatVersion.Split('.');
        return parts.Length == 3 && parts[0] == "1" &&
            int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int minor)
            ? minor
            : 0;
    }

    /// <summary>Actor references (in properties and in the derived indexes) whose id names no actor, matched exactly.</summary>
    public List<string> CheckReferences()
    {
        var problems = new List<string>();
        foreach (ActorRecord a in _all)
        {
            foreach (KeyValuePair<string, PropValue> item in a.Props)
            {
                CheckRefs(a.Id, item.Value, problems);
            }
        }

        foreach (EventEdge e in EventEdges)
        {
            CheckId("eventEdges", e.From, problems);
            foreach (string to in e.To)
            {
                CheckId("eventEdges", to, problems);
            }
        }

        foreach (MissionNode m in MissionTree)
        {
            CheckId("missionTree", m.Mission, problems);
            foreach (ObjectiveNode o in m.Objectives)
            {
                CheckId("missionTree", o.Objective, problems);
                foreach (string t in o.Triggers)
                {
                    CheckId("missionTree", t, problems);
                }
            }
        }

        return problems;
    }

    private void CheckRefs(string owner, PropValue value, List<string> problems)
    {
        switch (value.Kind)
        {
            case PropKind.Actor:
                CheckId(owner, value.AsActor().Id, problems);
                break;
            case PropKind.Struct:
                foreach (KeyValuePair<string, PropValue> item in value.AsStruct())
                {
                    CheckRefs(owner, item.Value, problems);
                }

                break;
            case PropKind.Array:
                foreach (PropValue item in value.AsArray())
                {
                    CheckRefs(owner, item, problems);
                }

                break;
            case PropKind.StaticArray:
                foreach (KeyValuePair<int, PropValue> e in value.AsStaticArray())
                {
                    CheckRefs(owner, e.Value, problems);
                }

                break;
        }
    }

    private void CheckId(string where, string id, List<string> problems)
    {
        if (_index.FindExact(id) is null)
        {
            problems.Add($"{where} refers to unknown actor {id}");
        }
    }

    private static List<string> RefList(PropBag props, string name)
    {
        var ids = new List<string>();
        if (!props.TryGetExact(name, out PropValue v))
        {
            return ids;
        }

        if (v.Kind == PropKind.Array)
        {
            foreach (PropValue item in v.AsArray())
            {
                if (item.Kind == PropKind.Actor)
                {
                    ids.Add(item.AsActor().Id);
                }
            }
        }
        else if (v.Kind == PropKind.StaticArray)
        {
            foreach (KeyValuePair<int, PropValue> e in v.AsStaticArray())
            {
                if (e.Value.Kind == PropKind.Actor)
                {
                    ids.Add(e.Value.AsActor().Id);
                }
            }
        }

        return ids;
    }

    private static bool SameTagIndex(IReadOnlyList<TagEntry> a, IReadOnlyList<TagEntry> b)
    {
        // Object member order carries no meaning, so compare as maps keyed by exact spelling.
        if (a.Count != b.Count)
        {
            return false;
        }

        var byTag = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (TagEntry t in b)
        {
            byTag[t.Tag] = t.Ids;
        }

        foreach (TagEntry t in a)
        {
            if (!byTag.TryGetValue(t.Tag, out IReadOnlyList<string>? ids) || !SameIds(t.Ids, ids))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool SameIds(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A name value that is set: not empty and not None (ignoring ASCII case).</summary>
    public static bool IsName(string value) => value.Length != 0 && !AsciiCaseComparer.AreEqual(value, "None");
}
