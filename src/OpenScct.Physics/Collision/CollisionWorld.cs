using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenScct.Content.Collision;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

/// <summary>A read-only view of one collider of a <see cref="CollisionWorld"/>.</summary>
public readonly record struct ColliderInfo(
    int Index,
    ColliderKind Kind,
    ColliderFlags Flags,
    string? ActorId,
    Actor? Actor,
    bool Enabled,
    bool IsDynamic,
    FVector Location,
    FRotator Rotation,
    FVector Min,
    FVector Max);

/// <summary>Runtime state of one collider.</summary>
internal sealed class ColliderState
{
    public int Index;
    public ColliderKind Kind;
    public ColliderFlags Flags;

    /// <summary>The trace class bits this collider answers to (see CollisionWorld.KindBits).</summary>
    public ETraceFlags TraceBits;
    public string? ActorId;
    public Actor? Actor;
    public bool Enabled;
    public bool IsDynamic;

    /// <summary>For a static mesh's per-poly collider, its simple-collision collider (traces use that one), else -1.</summary>
    public int SimpleTwin = -1;

    /// <summary>For a static mesh's simple-collision collider, its per-poly collider (shadow traces use that one), else -1.</summary>
    public int PerPolyTwin = -1;
    public PrimitiveSet? Set;
    public FVector Location;
    public FRotator Rotation;
    public FVector Scale;
    public FVector PrePivot;
    public FVector Min;
    public FVector Max;

    // Cheap transformed local box, independent of the lazily updated primitive/BVH transform. Live actor
    // coordinates key this cache, not the tick: teleports, movement substeps and prediction rewind are visible.
    public bool QueryBoundsValid;
    public FVector QueryLocation;
    public FRotator QueryRotation;
    public FVector QueryMin;
    public FVector QueryMax;

    // Owner flags when the level was bound: while the owner still has them, the collider's own flags apply.
    public bool BaseCollideActors;
    public bool BaseBlockActors;
    public bool BaseBlockPlayers;
}

/// <summary>Per-actor collision data, indexed by Actor.ActorId.</summary>
internal struct ActorSlot
{
    public int FirstCollider;
    public bool HasCylinder;
    public float Radius;
    public float Height;
    public ColliderFlags CylinderFlags;
    public float BaseRadius;
    public float BaseHeight;
    public bool BaseCollideActors;
    public bool BaseBlockActors;
    public bool BaseBlockPlayers;
}

/// <summary>
/// The collision world of one map, built from <c>sim/collision.bin</c> (<see cref="CollisionData"/>): a static BVH
/// over every collider that never moves (world BSP polygons, brushes, static mesh triangles and hulls), one BVH per
/// dynamic collider (movers, glass, gameplay objects, attached collision) that follows its actor, and the
/// collision cylinders of the level's actors. Not thread-safe: queries share scratch buffers.
/// </summary>
[PortOf("fmt:svmap.collision", Spec = "spec/svmap/1.0/collision.md#coll-colliders-stride-128")]
public sealed partial class CollisionWorld : ICollisionWorld
{
    /// <summary>BVH node bounds are widened by this much so rounding never prunes a real hit.</summary>
    private const float NodePad = 0.05f;

    /// <summary>Overlap tests cull the tree with this many box diagonals (the static mesh point check's local bounds).</summary>
    private const float OverlapCullScale = 2f;

    private readonly ColliderState[] _colliders;
    private readonly int[] _dynamicColliders;
    private readonly PrimitiveSet _static;
    private readonly CollisionMaterial[] _materials;

    /// <summary>The original UE2 PolyFlags of each collision material's BSP surface (SVMap 1.1 TFLX; 0 without it).</summary>
    private readonly uint[] _materialPolyFlags;

    /// <summary>Each collision material's own Material.OcclusionType (EOcclusionType; SVMap 1.1 TFLX; 0 without it).</summary>
    private readonly byte[] _materialOcclusion;
    private readonly CollisionZone[] _zones;
    private readonly string?[] _zoneActorIds;
    private readonly ZoneInfo?[] _zoneActors;
    private readonly CollisionBspNode[] _bsp;
    private readonly int _bspRoot = -1;
    private readonly CollisionCylinder[] _cylinders;
    private readonly string?[] _cylinderActorIds;
    private readonly int[] _nextColliderOfActor;

    private readonly List<Actor> _touchOwners = new();
    private Level? _level;
    private ActorSlot[] _slots = [];

    private CollisionWorld(CollisionData data)
    {
        int nc = data.Colliders.Length;
        _colliders = new ColliderState[nc];
        _nextColliderOfActor = new int[nc];
        var staticBuilder = new PrimitiveSetBuilder(dynamic: false);
        var dynamic = new List<int>();
        var localPlanes = new List<HullPlane>();
        bool hasWorld = false;
        FVector worldMin = default;
        FVector worldMax = default;
        int degenerate = 0;
        int triangles = 0;
        int hulls = 0;

        for (int ci = 0; ci < nc; ci++)
        {
            Collider c = data.Colliders[ci];
            var state = new ColliderState
            {
                Index = ci,
                Kind = c.Kind,
                Flags = c.Flags,
                TraceBits = KindBits(c.Kind, c.Flags),
                ActorId = data.GetString(c.Actor),
                Enabled = (c.Flags & ColliderFlags.StartsDisabled) == 0,
                IsDynamic = IsDynamicKind(c.Kind),
                Location = c.Location,
                Rotation = c.Rotation,
                Scale = c.Scale,
                PrePivot = c.PrePivot,
            };
            _nextColliderOfActor[ci] = -1;

            var xf = new ColliderTransform(c.Location, c.Rotation, c.Scale, c.PrePivot);
            PrimitiveSetBuilder target = state.IsDynamic ? new PrimitiveSetBuilder(dynamic: true) : staticBuilder;
            FVector cmin = default;
            FVector cmax = default;
            bool has = false;
            if (xf.Valid)
            {
                if (c.HullCount > 0)
                {
                    for (int h = (int)c.HullStart; h < (int)(c.HullStart + c.HullCount); h++)
                    {
                        CollisionHull hull = data.Hulls[h];
                        localPlanes.Clear();
                        foreach (CollisionPlane p in data.PlanesOf(hull))
                        {
                            localPlanes.Add(new HullPlane(p.Normal, p.W));
                        }

                        target.AddHull(ci, hull.Material, h, CollectionsMarshal.AsSpan(localPlanes), data.CornersOf(hull), hull.Min,
                            hull.Max, xf, ref cmin, ref cmax, ref has);
                        hulls++;
                    }
                }
                else
                {
                    // Colliders with BSP nodes collide by the triangles of their polygons (BSPN is provisional).
                    for (int t = (int)c.TriStart; t < (int)(c.TriStart + c.TriCount); t++)
                    {
                        CollisionTriangle tri = data.Triangles[t];
                        target.AddTriangle(ci, tri.Material, t, data.Vertices[(int)tri.I0], data.Vertices[(int)tri.I1],
                            data.Vertices[(int)tri.I2], xf, ref cmin, ref cmax, ref has);
                        triangles++;
                    }
                }
            }
            else
            {
                state.Enabled = false;
            }

            if (state.IsDynamic)
            {
                state.Set = target.Build();
                degenerate += target.DegenerateTriangles;
                dynamic.Add(ci);
            }

            state.Min = has ? cmin : c.AabbMin;
            state.Max = has ? cmax : c.AabbMax;
            if (has)
            {
                worldMin = hasWorld ? CollisionMath.MinV(worldMin, cmin) : cmin;
                worldMax = hasWorld ? CollisionMath.MaxV(worldMax, cmax) : cmax;
                hasWorld = true;
            }

            if (_bspRoot < 0 && c.Kind == ColliderKind.WorldBsp && c.BspCount > 0)
            {
                _bspRoot = (int)c.BspStart;

                // The level model is untransformed; its tree answers zero-extent traces (UModel::LineCheck).
                if (!state.IsDynamic && c.Location.IsZero() && c.Rotation == default && c.PrePivot.IsZero() &&
                    c.Scale == new FVector(1f, 1f, 1f))
                {
                    _bspCollider = ci;
                }
            }

            _colliders[ci] = state;
        }

        PairMeshSets(data);
        _static = staticBuilder.Build();
        degenerate += staticBuilder.DegenerateTriangles;
        _dynamicColliders = dynamic.ToArray();
        _materials = data.Materials.AsSpan().ToArray();
        _materialPolyFlags = ReadPolyFlags(data, _materials.Length);
        _materialOcclusion = ReadOcclusionTypes(data, _materials.Length);
        _bsp =data.BspNodes.AsSpan().ToArray();
        if (_bspCollider >= 0)
        {
            _bspTris = data.Triangles.AsSpan().ToArray();
            _bspVerts = data.Vertices.AsSpan().ToArray();
        }
        _zones = data.Zones.AsSpan().ToArray();
        _zoneActorIds = new string?[_zones.Length];
        _zoneActors = new ZoneInfo?[_zones.Length];
        for (int i = 0; i < _zones.Length; i++)
        {
            _zoneActorIds[i] = data.GetString(_zones[i].Actor);
        }

        _cylinders = data.Cylinders.AsSpan().ToArray();
        _cylinderActorIds = new string?[_cylinders.Length];
        for (int i = 0; i < _cylinders.Length; i++)
        {
            _cylinderActorIds[i] = data.GetString(_cylinders[i].Actor);
        }

        _filterStamp = new int[nc];
        _filterValue = new bool[nc];
        _multiStamp = new int[nc];
        _multiSlot = new int[nc];
        WorldMin = worldMin;
        WorldMax = worldMax;
        KillZ = hasWorld ? worldMin.Z - K.KillZBelowWorld : -K.KillZBelowWorld;
        DegenerateTriangles = degenerate;
        TriangleCount = triangles - degenerate;
        HullCount = hulls;
    }

    /// <summary>
    /// Pairs each static mesh's per-poly collider with its simple-collision collider. Traces use the simple set when a
    /// mesh has one and the per-poly set only with TRACE_ShadowCast (UStaticMesh::LineCheck tree selection). The
    /// converter writes the two sets of one mesh one after the other (per-poly then simple) with the same owner and
    /// transform. Plain meshes tell them apart by kind (meshPoly, meshHull); movers, glass and gameplay objects use one
    /// kind for both, so their sets come from the SVMap 1.1 CSRC chunk (set 2 per-poly, 3 simple, same source, asset
    /// and variants). Maps without CSRC keep both sets of those actors colliding.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UStaticMesh::LineCheck tree selector editor 0x111b47a0 (spec/native/collision-primitives.md)")]
    [Provenance(ProvenanceKind.Decoded, "Terminal mode_neutralisation: per-poly reaches X 33.2, simple hulls 28.2; the 64 uu use spot only fits a 35 uu spy against the simple set (spec/objectives/terminal-hack.md)")]
    private void PairMeshSets(CollisionData data)
    {
        ReadOnlySpan<byte> sources = default;
        foreach (CollisionUnknownChunk chunk in data.UnknownChunks)
        {
            if (chunk.FourCC == "CSRC" && chunk.Count == (uint)_colliders.Length && chunk.Payload.Length >= _colliders.Length * CsrcStride)
            {
                sources = chunk.Payload;
                break;
            }
        }

        for (int ci = 0; ci + 1 < _colliders.Length; ci++)
        {
            Collider a = data.Colliders[ci];
            Collider b = data.Colliders[ci + 1];
            if (a.Actor != b.Actor || a.Location != b.Location || a.Rotation != b.Rotation || a.Scale != b.Scale ||
                a.PrePivot != b.PrePivot)
            {
                continue;
            }

            bool pair = a.Kind == ColliderKind.MeshPoly && b.Kind == ColliderKind.MeshHull;
            if (!pair && !sources.IsEmpty && a.Kind == b.Kind)
            {
                ReadOnlySpan<byte> sa = sources.Slice(ci * CsrcStride, CsrcStride);
                ReadOnlySpan<byte> sb = sources.Slice((ci + 1) * CsrcStride, CsrcStride);
                pair = sa[8] == CsrcSetPerPoly && sb[8] == CsrcSetSimple && sa[..8].SequenceEqual(sb[..8]) &&
                       sa.Slice(10, 2).SequenceEqual(sb.Slice(10, 2));
            }

            if (!pair)
            {
                continue;
            }

            _colliders[ci].SimpleTwin = ci + 1;
            _colliders[ci + 1].PerPolyTwin = ci;
        }
    }

    /// <summary>The poly flags of the SVMap 1.1 TFLX rows (u32 polyFlags, u16 materialBits, u8 occlusion, u8), one per material.</summary>
    private static uint[] ReadPolyFlags(CollisionData data, int count)
    {
        var flags = new uint[count];
        foreach (CollisionUnknownChunk chunk in data.UnknownChunks)
        {
            if (chunk.FourCC == "TFLX" && chunk.Count == (uint)count && chunk.Payload.Length >= count * TflxStride)
            {
                ReadOnlySpan<byte> rows = chunk.Payload;
                for (int i = 0; i < count; i++)
                {
                    flags[i] = BinaryPrimitives.ReadUInt32LittleEndian(rows.Slice(i * TflxStride, 4));
                }

                break;
            }
        }

        return flags;
    }

    /// <summary>The occlusion type byte of the TFLX rows, one per material (the sound system's occlusion, spec/native/audio.md rule 18).</summary>
    private static byte[] ReadOcclusionTypes(CollisionData data, int count)
    {
        var types = new byte[count];
        foreach (CollisionUnknownChunk chunk in data.UnknownChunks)
        {
            if (chunk.FourCC == "TFLX" && chunk.Count == (uint)count && chunk.Payload.Length >= count * TflxStride)
            {
                for (int i = 0; i < count; i++)
                {
                    types[i] = chunk.Payload[(i * TflxStride) + TflxOcclusionOffset];
                }

                break;
            }
        }

        return types;
    }

    /// <summary>The hit material's own OcclusionType (0, Generic, for -1 or a map without TFLX).</summary>
    public byte OcclusionTypeOf(int material) =>
        material >= 0 && material < _materialOcclusion.Length ? _materialOcclusion[material] : (byte)0;

    private const int TflxStride = 8;
    private const int TflxOcclusionOffset = 6;

    // SVMap 1.1 CSRC rows (spec/svmap/1.1/collision.md): u32 source, u32 asset, u8 set, u8, u16 variants, u16 traceUse, u16.
    private const int CsrcStride = 16;
    private const byte CsrcSetPerPoly = 2;
    private const byte CsrcSetSimple = 3;

    /// <summary>Builds the collision world of a map.</summary>
    public static CollisionWorld Build(CollisionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new CollisionWorld(data);
    }

    /// <summary>Bounds of all collision geometry at its initial transforms.</summary>
    public FVector WorldMin { get; }

    public FVector WorldMax { get; }

    /// <summary>Actors falling below this Z fall out of the world (FellOutOfWorld). Defaults to below the geometry.</summary>
    [Provenance(ProvenanceKind.Estimated, "No KillZ decoded for SCCT")]
    public float KillZ { get; set; }

    /// <summary>Collision triangles (degenerate ones excluded).</summary>
    public int TriangleCount { get; }

    /// <summary>Convex hulls.</summary>
    public int HullCount { get; }

    /// <summary>Triangles dropped at build because they have no area.</summary>
    public int DegenerateTriangles { get; }

    public int ColliderCount => _colliders.Length;

    /// <summary>The bound level, or null.</summary>
    public Level? Level => _level;

    /// <summary>
    /// Binds the world to a level: collider owners, zone actors and actor cylinders are resolved by actor id with
    /// <paramref name="resolveActor"/> (none when null), world BSP hits report the LevelInfo, and actor cylinders
    /// come from the level's colliding actors.
    /// </summary>
    public void BindLevel(Level level, Func<string, Actor?>? resolveActor = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        _level = level;
        _slots = new ActorSlot[64];
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i].FirstCollider = -1;
        }

        for (int ci = 0; ci < _colliders.Length; ci++)
        {
            ColliderState c = _colliders[ci];
            c.Actor = null;
            _nextColliderOfActor[ci] = -1;
            if (c.ActorId is null || resolveActor is null || resolveActor(c.ActorId) is not { } actor)
            {
                continue;
            }

            c.Actor = actor;
            c.BaseCollideActors = actor.bCollideActors;
            c.BaseBlockActors = actor.bBlockActors;
            c.BaseBlockPlayers = actor.bBlockPlayers;
            ref ActorSlot slot = ref Slot(actor);
            _nextColliderOfActor[ci] = slot.FirstCollider;
            slot.FirstCollider = ci;
            SyncCollider(c);
        }

        _touchOwners.Clear();
        foreach (ColliderState c in _colliders)
        {
            if (c.Actor is { } owner && (c.Flags & ColliderFlags.CollideActors) != 0 &&
                (c.Flags & (ColliderFlags.BlockActors | ColliderFlags.BlockPlayers)) == 0 && !_touchOwners.Contains(owner))
            {
                _touchOwners.Add(owner);
            }
        }

        for (int i = 0; i < _zones.Length; i++)
        {
            _zoneActors[i] = _zoneActorIds[i] is { } id && resolveActor is not null ? resolveActor(id) as ZoneInfo : null;
        }

        if (_zoneActors.Length > 0 && _zoneActors[0] is null)
        {
            _zoneActors[0] = level.Info;
        }

        for (int i = 0; i < _cylinders.Length; i++)
        {
            if (_cylinderActorIds[i] is not { } id || resolveActor is null || resolveActor(id) is not { } actor || HasColliders(actor))
            {
                continue;
            }

            CollisionCylinder cyl = _cylinders[i];
            ref ActorSlot slot = ref Slot(actor);
            slot.HasCylinder = true;
            slot.Radius = cyl.Radius;
            slot.Height = cyl.Height;
            slot.CylinderFlags = cyl.Flags;
            slot.BaseRadius = actor.CollisionRadius;
            slot.BaseHeight = actor.CollisionHeight;
            slot.BaseCollideActors = actor.bCollideActors;
            slot.BaseBlockActors = actor.bBlockActors;
            slot.BaseBlockPlayers = actor.bBlockPlayers;
        }

        _cylinderDirty = true;
    }

    /// <summary>Binds to the level a map was loaded into, resolving actor ids through the load result.</summary>
    public void BindLevel(MapLoadResult loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        BindLevel(loaded.Level, loaded.FindActor);
    }

    // ---- colliders -------------------------------------------------------------------------------------------

    public ColliderInfo GetCollider(int index)
    {
        ColliderState c = _colliders[index];
        SyncCollider(c);
        return new ColliderInfo(c.Index, c.Kind, c.Flags, c.ActorId, c.Actor, c.Enabled, c.IsDynamic, c.Location, c.Rotation, c.Min,
            c.Max);
    }

    /// <summary>Turns one collider on or off (glass breaking, gameplay object mesh swaps).</summary>
    public void SetColliderEnabled(int index, bool enabled) => _colliders[index].Enabled = enabled;

    /// <summary>Turns every collider of <paramref name="actor"/> on or off.</summary>
    public void SetActorCollidersEnabled(Actor actor, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(actor);
        for (int ci = FirstColliderOf(actor); ci >= 0; ci = _nextColliderOfActor[ci])
        {
            _colliders[ci].Enabled = enabled;
        }
    }

    /// <summary>
    /// Moves a dynamic collider. Colliders bound to an actor follow that actor's Location and Rotation by
    /// themselves; this is for unbound ones.
    /// </summary>
    public void SetColliderTransform(int index, FVector location, FRotator rotation)
    {
        ColliderState c = _colliders[index];
        if (!c.IsDynamic)
        {
            throw new InvalidOperationException($"Collider {index} ({c.Kind}) is static and cannot move.");
        }

        ApplyTransform(c, location, rotation);
    }

    /// <summary>True when <paramref name="actor"/> collides by mesh or brush colliders rather than its cylinder.</summary>
    public bool HasColliders(Actor actor) => FirstColliderOf(actor) >= 0;

    /// <summary>Adds the indexes of <paramref name="actor"/>'s colliders to <paramref name="output"/>.</summary>
    public int GetActorColliders(Actor actor, List<int> output)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(output);
        int n = 0;
        for (int ci = FirstColliderOf(actor); ci >= 0; ci = _nextColliderOfActor[ci])
        {
            output.Add(ci);
            n++;
        }

        return n;
    }

    /// <summary>Call after changing an actor's collision flags or size outside the physics (SetCollision).</summary>
    public void NotifyCollisionChanged() => _cylinderDirty = true;

    internal int FirstColliderOf(Actor actor)
    {
        int id = actor.ActorId;
        return (uint)id < (uint)_slots.Length ? _slots[id].FirstCollider : -1;
    }

    internal int NextColliderOf(int collider) => _nextColliderOfActor[collider];

    internal ColliderState ColliderAt(int index) => _colliders[index];

    /// <summary>Actors with non-blocking colliders (volumes): touched, never blocking.</summary>
    internal List<Actor> TouchColliderOwners => _touchOwners;

    /// <summary>Brings a dynamic collider bound to an actor to that actor's current Location and Rotation.</summary>
    internal void SyncCollider(ColliderState c)
    {
        if (!c.IsDynamic || c.Actor is not { } actor || c.Set is null)
        {
            return;
        }

        if (actor.Location == c.Location && actor.Rotation == c.Rotation)
        {
            return;
        }

        ApplyTransform(c, actor.Location, actor.Rotation);
    }

    /// <summary>
    /// A moved mesh outside a query needs only its eight transformed local box corners, not a rewrite of every
    /// triangle, hull plane and BVH node. Candidate order and the subsequent exact world-bounds test are unchanged.
    /// </summary>
    private bool SyncColliderForQuery(ColliderState c, FVector queryMin, FVector queryMax, FVector expansion = default)
    {
        if (!c.IsDynamic || c.Actor is not { } actor || c.Set is not { } set) return true;
        FVector location = actor.Location;
        FRotator rotation = actor.Rotation;
        if (location == c.Location && rotation == c.Rotation) return true;

        if (!c.QueryBoundsValid || location != c.QueryLocation || rotation != c.QueryRotation)
        {
            var xf = new ColliderTransform(location, rotation, c.Scale, c.PrePivot);
            PrimitiveSetBuilder.HullBounds([], set.LocalMin, set.LocalMax, xf, out FVector min, out FVector max);
            var pad = new FVector(NodePad, NodePad, NodePad);
            c.QueryMin = min - pad;
            c.QueryMax = max + pad;
            c.QueryLocation = location;
            c.QueryRotation = rotation;
            c.QueryBoundsValid = float.IsFinite(min.X) && float.IsFinite(min.Y) && float.IsFinite(min.Z) &&
                float.IsFinite(max.X) && float.IsFinite(max.Y) && float.IsFinite(max.Z);
        }

        // Nonfinite or incomplete content falls back to the existing transform and query path.
        if (c.QueryBoundsValid && !CollisionMath.BoxesTouch(c.QueryMin - expansion, c.QueryMax + expansion, queryMin, queryMax)) return false;
        ApplyTransform(c, location, rotation);
        return true;
    }

    private static void ApplyTransform(ColliderState c, FVector location, FRotator rotation)
    {
        c.Location = location;
        c.Rotation = rotation;
        if (c.Set is null || c.Set.IsEmpty)
        {
            return;
        }

        var xf = new ColliderTransform(location, rotation, c.Scale, c.PrePivot);
        if (!xf.Valid)
        {
            return;
        }

        c.Set.ApplyTransform(xf);
        c.Min = c.Set.Min;
        c.Max = c.Set.Max;
    }

    private ref ActorSlot Slot(Actor actor)
    {
        int id = actor.ActorId;
        if (id < 0)
        {
            throw new ArgumentException($"{actor} has not been spawned.", nameof(actor));
        }

        if (id >= _slots.Length)
        {
            int old = _slots.Length;
            Array.Resize(ref _slots, Math.Max(id + 1, old * 2));
            for (int i = old; i < _slots.Length; i++)
            {
                _slots[i].FirstCollider = -1;
            }
        }

        return ref _slots[id];
    }

    private static bool IsDynamicKind(ColliderKind kind) =>
        kind is ColliderKind.Mover or ColliderKind.Glass or ColliderKind.Gpo or ColliderKind.Attached;

    /// <summary>
    /// The trace class bits a collider answers to. A brush that blocks (BlockingVolume) is level geometry and a
    /// volume; one that does not block (a plain or damage volume) is only seen by traces asking for volumes.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2: volumes need TRACE_Volumes, BlockingVolumes are world geometry")]
    internal static ETraceFlags KindBits(ColliderKind kind, ColliderFlags flags) => kind switch
    {
        ColliderKind.WorldBsp => ETraceFlags.TRACE_Level,
        ColliderKind.Brush => (flags & (ColliderFlags.BlockActors | ColliderFlags.BlockPlayers)) != 0
            ? ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Volumes
            : ETraceFlags.TRACE_Volumes,
        ColliderKind.MeshPoly or ColliderKind.MeshHull => ETraceFlags.TRACE_LevelGeometry,
        ColliderKind.Mover => ETraceFlags.TRACE_Movers,
        _ => ETraceFlags.TRACE_Others,
    };

    /// <summary>World geometry, brushes and encroachers only block actors that collide with the world.</summary>
    internal static bool NeedsCollideWorld(ColliderKind kind) => kind != ColliderKind.Glass && kind != ColliderKind.Gpo &&
        kind != ColliderKind.Attached;
}
