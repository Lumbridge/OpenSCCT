using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

/// <summary>
/// The original's per-leaf light lists (SVMap <c>render/leaflights.json</c>): which static lights may light an actor,
/// decided at Build time for each BSP leaf. FEditorVisibility::ActorVisibility (editor 0x1101b050) floods each light
/// from its own leaf through the leaf portals within its radius (a sun joins every leaf of its own zone), and
/// TestLights (UnVisi.cpp, editor 0x1101dcb7) writes a None-terminated run per leaf into UModel.Lights, its start in
/// FLeaf.iPermeating. At run time the actor light gather (game 0x10a37610, editor 0x111a7300) walks the leaves the
/// actor touches: the static runs (pass 0), then a per-leaf runtime list for non-static and spawned lights (pass 1).
/// No static light is taken by distance alone, so a light behind a solid wall does not light the next room's actors.
/// The file's <c>nodeLeaf</c> gives each world BSP node's back and front leaf (BSPN keeps none), aligned with the world
/// collider's nodes in sim/collision.bin.
/// </summary>
[PortOf("eng:Render.ActorLighting", Spec = "docs/RENDERING.md#actor-lighting")]
public sealed class LeafLights
{
    public const string FileName = "render/leaflights.json";

    private readonly CollisionWorld _world;
    private readonly int[][] _leafLights;
    private readonly int[] _nodeLeaf;
    private readonly Dictionary<string, int> _lightIds;
    private readonly int[] _leafZone;
    private readonly List<string?> _zoneNames;

    private LeafLights(CollisionWorld world, int[][] leafLights, int[] nodeLeaf, Dictionary<string, int> lightIds, int[] leafZone,
        List<string?> zoneNames)
    {
        _world = world;
        _leafLights = leafLights;
        _nodeLeaf = nodeLeaf;
        _lightIds = lightIds;
        _leafZone = leafZone;
        _zoneNames = zoneNames;
    }

    /// <summary>Distinct lights named by some leaf's list.</summary>
    public int LightCount => _lightIds.Count;

    /// <summary>Distinct zones named by the leaves (a leaf with no zone actor counts as one more, named null).</summary>
    public int ZoneCount => _zoneNames.Count;

    /// <summary>The zone actor name of a zone id (<see cref="ZoneOf"/>), null for leaves with no zone actor.</summary>
    public string? ZoneName(int zone) => (uint)zone < (uint)_zoneNames.Count ? _zoneNames[zone] : null;

    /// <summary>The zone id of a leaf (an index for <see cref="ZoneName"/>), -1 for an unknown leaf.</summary>
    public int ZoneOf(int leaf) => (uint)leaf < (uint)_leafZone.Length ? _leafZone[leaf] : -1;

    /// <summary>
    /// A zone mask for <see cref="LeafLightSet.Gather(in ActorLeafBounds, bool[])"/>: true for each zone id whose zone
    /// actor name (null for leaves with no zone actor) passes <paramref name="keep"/>.
    /// </summary>
    public bool[] ZoneMask(Func<string?, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        var mask = new bool[_zoneNames.Count];
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = keep(_zoneNames[i]);
        }

        return mask;
    }

    /// <summary>
    /// The lists of a map folder's <see cref="FileName"/> over <paramref name="world"/>'s BSP, or null when the file is
    /// absent, unreadable or not for this BSP (its nodeLeaf count is not the world collider's node count; the callers
    /// then keep choosing lights by distance).
    /// </summary>
    public static LeafLights? Load(string? mapFolder, CollisionWorld? world)
    {
        if (mapFolder is null || world is null)
        {
            return null;
        }

        string path = Path.Combine(mapFolder, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
            return Parse(json.RootElement, world);
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The lists of a parsed leaflights document (version 1), or null when it does not fit: its nodeLeaf must have one
    /// entry per node of <paramref name="world"/>'s world BSP, in the same order (a file left from an older
    /// sim/collision.bin would map leaves to the wrong nodes).
    /// </summary>
    public static LeafLights? Parse(JsonElement root, CollisionWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1 ||
            !root.TryGetProperty("leaves", out JsonElement leaves) || leaves.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("nodeLeaf", out JsonElement nodes) || nodes.ValueKind != JsonValueKind.Array ||
            world.WorldBspNodeCount == 0 || nodes.GetArrayLength() != world.WorldBspNodeCount)
        {
            return null;
        }

        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var leafLights = new int[leaves.GetArrayLength()][];
        var leafZone = new int[leafLights.Length];
        var zoneIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var zoneNames = new List<string?>();
        int noZone = -1;
        int li = 0;
        var run = new List<int>();
        foreach (JsonElement leaf in leaves.EnumerateArray())
        {
            run.Clear();
            string? zoneName = leaf.ValueKind == JsonValueKind.Object && leaf.TryGetProperty("zone", out JsonElement z) &&
                z.ValueKind == JsonValueKind.String && z.GetString() is { Length: > 0 } zn ? zn : null;
            int zoneId;
            if (zoneName is null)
            {
                if (noZone < 0)
                {
                    noZone = zoneNames.Count;
                    zoneNames.Add(null);
                }

                zoneId = noZone;
            }
            else if (!zoneIds.TryGetValue(zoneName, out zoneId))
            {
                zoneId = zoneNames.Count;
                zoneIds.Add(zoneName, zoneId);
                zoneNames.Add(zoneName);
            }

            leafZone[li] = zoneId;
            if (leaf.ValueKind == JsonValueKind.Object)
            {
                if (leaf.TryGetProperty("lights", out JsonElement names) && names.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement n in names.EnumerateArray())
                    {
                        if (n.ValueKind != JsonValueKind.String || n.GetString() is not { Length: > 0 } name) continue;
                        if (!ids.TryGetValue(name, out int id))
                        {
                            id = ids.Count;
                            ids.Add(name, id);
                        }

                        run.Add(id);
                    }
                }
            }

            leafLights[li++] = run.ToArray();
        }

        var nodeLeaf = new int[nodes.GetArrayLength() * 2];
        int ni = 0;
        foreach (JsonElement pair in nodes.EnumerateArray())
        {
            int back = -1, front = -1;
            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() >= 2)
            {
                back = pair[0].ValueKind == JsonValueKind.Number ? pair[0].GetInt32() : -1;
                front = pair[1].ValueKind == JsonValueKind.Number ? pair[1].GetInt32() : -1;
            }

            nodeLeaf[ni++] = back < leafLights.Length ? back : -1;
            nodeLeaf[ni++] = front < leafLights.Length ? front : -1;
        }

        return new LeafLights(world, leafLights, nodeLeaf, ids, leafZone, zoneNames);
    }

    /// <summary>The id of a light named by some leaf's list, or -1 when no leaf lists it.</summary>
    public int IdOf(string name) => _lightIds.TryGetValue(name, out int id) ? id : -1;

    /// <summary>The light ids a leaf lists (empty for an unknown leaf).</summary>
    public ReadOnlySpan<int> LightsOf(int leaf) => (uint)leaf < (uint)_leafLights.Length ? _leafLights[leaf] : [];

    /// <summary>
    /// The leaves a sphere touches (each once): the leaves of an actor that has no bounds box here (pawns, the bots' light
    /// field, emitters), a stand-in for the original's actor leaf array, which is built from the actor's bounds box
    /// (<see cref="BoxLeaves"/>).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "a bounds sphere standing in for the actor's bounds box (actor+0x1c4 in the original)")]
    public void SphereLeaves(FVector center, float radius, List<int> into) => SphereLeaves(center, radius, into, strict: false);

    private void SphereLeaves(FVector center, float radius, List<int> into, bool strict)
    {
        ArgumentNullException.ThrowIfNull(into);
        List<(int Node, bool Front)> sides = t_sides ??= [];
        sides.Clear();
        _world.SphereLeafNodes(center, radius, sides, strict);
        foreach ((int node, bool front) in sides)
        {
            int leaf = LeafOf(node, front);
            if (leaf >= 0 && !into.Contains(leaf)) into.Add(leaf);
        }
    }

    /// <summary>
    /// The leaves an actor whose (expanded, <see cref="TouchExtent"/>) bounds box is (<paramref name="center"/>, half size
    /// <paramref name="extent"/>) touches, each once, by the original's shape test: a chunky box (twice its smallest half
    /// size above its largest) is taken as the sphere of radius |extent| at its centre, an elongated one as the box.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "actor leaf walk, game 0x10a08320: sphere of radius |extent| when 2 min(extent) > max(extent), else the box test")]
    public void BoxLeaves(FVector center, FVector extent, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        float ex = MathF.Abs(extent.X), ey = MathF.Abs(extent.Y), ez = MathF.Abs(extent.Z);
        float min = MathF.Min(ex, MathF.Min(ey, ez));
        float max = MathF.Max(ex, MathF.Max(ey, ez));
        if (2f * min > max)
        {
            SphereLeaves(center, extent.Size(), into, strict: true);
            return;
        }

        List<(int Node, bool Front)> sides = t_sides ??= [];
        sides.Clear();
        _world.BoxLeafNodes(center, extent, sides);
        foreach ((int node, bool front) in sides)
        {
            int leaf = LeafOf(node, front);
            if (leaf >= 0 && !into.Contains(leaf)) into.Add(leaf);
        }
    }

    /// <summary>
    /// The half size of the box an actor's touched leaves are found with, from its render bounds' half size
    /// <paramref name="extent"/> and its Physics: PHYS_None keeps the bounds, PHYS_Rotating takes a cube of half size
    /// 1.2 |extent| (it may turn any way), any other Physics grows each axis by 128 uu (pawns, movers, projectiles).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "actor bounds for the leaf walk, game 0x10990e60: Physics (+0x2d8) 0 as is, 3 a cube of 1.2 |extent| (0x10c10cb0), else extent + 128 (0x10bff1b4)")]
    public static FVector TouchExtent(FVector extent, EPhysics physics) => physics switch
    {
        EPhysics.PHYS_None => extent,
        EPhysics.PHYS_Rotating => new FVector(1f, 1f, 1f) * (RotatingBoundsScale * extent.Size()),
        _ => new FVector(MathF.Abs(extent.X) + MovingBoundsGrowth, MathF.Abs(extent.Y) + MovingBoundsGrowth, MathF.Abs(extent.Z) + MovingBoundsGrowth),
    };

    /// <summary>A PHYS_Rotating actor's leaf box: a cube of this many times its bounds' |extent|.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10990e60, constant 0x10c10cb0 = 1.2")]
    public const float RotatingBoundsScale = 1.2f;

    /// <summary>uu added to each half size of a moving (not PHYS_None or PHYS_Rotating) actor's leaf box.</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10990e60, constant 0x10bff1b4 = 128.0")]
    public const float MovingBoundsGrowth = 128f;

    private int LeafOf(int node, bool front)
    {
        int i = (node * 2) + (front ? 1 : 0);
        return (uint)i < (uint)_nodeLeaf.Length ? _nodeLeaf[i] : -1;
    }

    [ThreadStatic]
    private static List<(int Node, bool Front)>? t_sides;

    /// <summary>A reusable set of the lights listed by the leaves an actor touches (one per caller, not shared between threads).</summary>
    public LeafLightSet NewSet() => new(this);

    /// <summary>The union of the leaf lists of the leaves one actor touches, refilled by <see cref="Gather"/>.</summary>
    public sealed class LeafLightSet
    {
        private readonly LeafLights _lists;
        private readonly int[] _mark;
        private readonly List<int> _leaves = [];
        private readonly List<int> _zones = [];
        private int _stamp;

        internal LeafLightSet(LeafLights lists)
        {
            _lists = lists;
            _mark = new int[lists.LightCount];
        }

        /// <summary>Collects the lights listed by every leaf the sphere (<paramref name="center"/>, <paramref name="radius"/>) touches.</summary>
        public void Gather(FVector center, float radius)
        {
            Begin();
            _lists.SphereLeaves(center, radius, _leaves);
            Mark();
        }

        /// <summary>
        /// Collects the lights listed by every leaf an actor's leaf box (<paramref name="center"/>, half size
        /// <paramref name="extent"/>, already expanded by <see cref="TouchExtent"/>) touches (<see cref="BoxLeaves"/>).
        /// </summary>
        public void Gather(FVector center, FVector extent)
        {
            Begin();
            _lists.BoxLeaves(center, extent, _leaves);
            Mark();
        }

        /// <summary>
        /// Collects the lights listed by every leaf an actor with these render bounds touches. With
        /// <paramref name="keepZone"/> (indexed by zone id, <see cref="ZoneOf"/>) the touched leaves of the other zones
        /// are dropped first: the original keeps only the leaves whose zone's Tag is the actor's ForcedVisibilityZoneTag
        /// when that is set (<see cref="ZoneMask"/>).
        /// </summary>
        [Provenance(ProvenanceKind.Decoded, "actor leaf array, game 0x10990e60 at 0x109910d0-0x10991152: with ForcedVisibilityZoneTag (+0x2b0) set, a touched leaf whose zone actor's Tag (+0x2bc; the LevelInfo when the leaf has none) differs is removed")]
        public void Gather(in ActorLeafBounds bounds, bool[]? keepZone = null)
        {
            Begin();
            _lists.BoxLeaves(bounds.Center, TouchExtent(bounds.Extent, bounds.Physics), _leaves);
            if (keepZone is not null)
            {
                int kept = 0;
                for (int i = 0; i < _leaves.Count; i++)
                {
                    int zone = _lists.ZoneOf(_leaves[i]);
                    if ((uint)zone < (uint)keepZone.Length && keepZone[zone])
                    {
                        _leaves[kept++] = _leaves[i];
                    }
                }

                _leaves.RemoveRange(kept, _leaves.Count - kept);
            }

            Mark();
        }

        /// <summary>The zone ids (<see cref="ZoneOf"/>) of the leaves the last Gather kept, each once.</summary>
        public IReadOnlyList<int> Zones => _zones;

        /// <summary>The number of leaves the last Gather kept.</summary>
        public int LeafCount => _leaves.Count;

        private void Begin()
        {
            if (++_stamp == int.MaxValue)
            {
                Array.Clear(_mark);
                _stamp = 1;
            }

            _leaves.Clear();
            _zones.Clear();
        }

        private void Mark()
        {
            foreach (int leaf in _leaves)
            {
                int zone = _lists.ZoneOf(leaf);
                if (zone >= 0 && !_zones.Contains(zone)) _zones.Add(zone);

                foreach (int id in _lists.LightsOf(leaf))
                {
                    _mark[id] = _stamp;
                }
            }
        }

        /// <summary>True when a touched leaf lists the light <paramref name="id"/> (<see cref="IdOf"/>).</summary>
        public bool Contains(int id) => (uint)id < (uint)_mark.Length && _mark[id] == _stamp;
    }
}

/// <summary>
/// An actor's render bounds for the leaf walk (<see cref="LeafLights.BoxLeaves"/>): the box's centre and half size in uu
/// (axis aligned, in world space) and the actor's Physics, which picks how the box is grown (<see cref="LeafLights.TouchExtent"/>).
/// <paramref name="Exact"/> is false when the box only approximates the original's render bounds (a skeletal mesh's rest
/// pose box): its leaves are then not trusted to keep a sun to its zone. <paramref name="ZoneTag"/> is the actor's
/// ForcedVisibilityZoneTag (null when unset): only the touched leaves of zones with that Tag are kept.
/// </summary>
public readonly record struct ActorLeafBounds(FVector Center, FVector Extent, EPhysics Physics, bool Exact = true, string? ZoneTag = null);
