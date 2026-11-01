using System;
using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

public sealed partial class CollisionWorld
{
    private const ETraceFlags GeometryBits = ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Volumes |
        ETraceFlags.TRACE_Others | ETraceFlags.TRACE_Movers;

    private const ETraceFlags ActorBits = ETraceFlags.TRACE_Pawns | ETraceFlags.TRACE_Others;

    // ULevel::MultiLineCheck sorts by the reported (pulled-back) times: a mesh floor and a BSP floor at the same height
    // report the mesh first, whose pull-back is longer.
    private static readonly Comparison<MultiEntry> s_multiOrder = (a, b) =>
    {
        int c = a.Pulled.CompareTo(b.Pulled);
        if (c != 0)
        {
            return c;
        }

        c = a.RawTime.CompareTo(b.RawTime);
        return c != 0 ? c : a.Key.CompareTo(b.Key);
    };

    private readonly int[] _stackNode = new int[256];
    private readonly float[] _stackT = new float[256];
    private readonly int[] _filterStamp;
    private readonly bool[] _filterValue;
    private readonly int[] _multiStamp;
    private readonly int[] _multiSlot;
    private readonly List<MultiEntry> _multi = new(16);
    private int _stamp;

    private struct MultiEntry
    {
        public float RawTime;
        public float Pulled;
        public long Key;
        public FCheckResult Result;
    }

    /// <summary>One query's input and running best result.</summary>
    private struct Query
    {
        public FVector S;
        public FVector D;
        public FVector E;
        public FVector NodeE;
        public FVector InvD;
        public bool ZX;
        public bool ZY;
        public bool ZZ;
        public bool Zero;
        public bool IsOverlap;

        /// <summary>An overlap test of the shape the sweeps move (world cylinder on static meshes), not PointCheck's.</summary>
        public bool MoveShape;
        public ETraceFlags Flags;
        public Actor? Source;
        public Actor? Ignore;
        public int OnlyCollider;
        public bool StopAtFirst;
        public bool Collect;
        public bool Done;

        public bool HasHit;
        public float BestT;

        /// <summary>The best hit's reported time (pulled back by its primitive's rule): hits are ranked by it.</summary>
        public float BestPulled;

        /// <summary>|D|, for the pull-backs.</summary>
        public float Len;

        /// <summary>The largest pull-back any primitive can apply at this length: candidates within it of the best still count.</summary>
        public float MaxPull;
        public long BestKey;
        public FVector BestN;
        public bool BestPen;
        public int BestCollider;
        public int BestItem;
        public int BestMaterial;
        public Actor? BestActor;

        public readonly float Limit => !Collect && !StopAtFirst && HasHit ? SvmMath.Min(1f, BestPulled + MaxPull) : 1f;
    }

    // ---- public queries -----------------------------------------------------------------------------------

    [UcNative("ULevel::SingleLineCheck")]
    [PortOf("nat:ULevel::SingleLineCheck", Spec = "docs/PHYSICS.md#queries")]
    public bool LineCheck(out FCheckResult Hit, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags) =>
        Trace(out Hit, SourceActor, null, -1, End, Start, Extent, TraceFlags, bruteForce: false);

    [UcNative("ULevel::MultiLineCheck")]
    [PortOf("nat:ULevel::MultiLineCheck", Spec = "docs/PHYSICS.md#queries")]
    public int MultiLineCheck(List<FCheckResult> Hits, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags)
    {
        ArgumentNullException.ThrowIfNull(Hits);
        Hits.Clear();
        FVector d = End - Start;
        if (d.IsZero())
        {
            return 0;
        }

        _multi.Clear();
        Query q = NewQuery(Start, d, Extent, TraceFlags, SourceActor, null, -1);
        q.Collect = true;
        q.StopAtFirst = false;
        RunSweep(ref q, bruteForce: false);
        _multi.Sort(s_multiOrder);
        float len = d.Size();
        for (int i = 0; i < _multi.Count; i++)
        {
            MultiEntry m = _multi[i];
            FCheckResult r = m.Result;
            if (q.Zero && m.RawTime >= 1f)
            {
                continue;
            }

            float time = m.Pulled;

            r.Time = time;
            r.Location = Start + d * time;
            Hits.Add(r);
            if (r.Collider >= 0 && _colliders[r.Collider].Kind == ColliderKind.WorldBsp)
            {
                break;
            }
        }

        return Hits.Count;
    }

    [UcNative("ULevel::SinglePointCheck")]
    [PortOf("nat:ULevel::SinglePointCheck", Spec = "docs/PHYSICS.md#queries")]
    public bool PointCheck(out FCheckResult Hit, Actor? SourceActor, FVector Location, FVector Extent, ETraceFlags TraceFlags) =>
        Overlap(out Hit, SourceActor, null, -1, Location, Extent, TraceFlags, bruteForce: false);

    [UcNative("AActor::execFastTrace", Exe = 0x10a27750)]
    [PortOf("nat:Engine.Actor.FastTrace", Spec = "docs/PHYSICS.md#queries")]
    public bool FastTrace(FVector End, FVector Start) =>
        !Trace(out _, null, null, -1, End, Start, default, ETraceFlags.TRACE_World | ETraceFlags.TRACE_StopAtFirstHit, bruteForce: false);

    // ---- internal entry points ----------------------------------------------------------------------------

    /// <summary>
    /// A line or box trace. <paramref name="ignore"/> is skipped like the source (an encroacher pushing an actor);
    /// <paramref name="onlyCollider"/> limits the trace to one collider; <paramref name="bruteForce"/> tests every
    /// primitive instead of walking the BVHs (tests compare the two).
    /// </summary>
    internal bool Trace(out FCheckResult hit, Actor? source, Actor? ignore, int onlyCollider, FVector end, FVector start, FVector extent,
        ETraceFlags flags, bool bruteForce)
    {
        hit = FCheckResult.NoHit(end);
        FVector d = end - start;
        if (d.IsZero())
        {
            return false;
        }

        Query q = NewQuery(start, d, extent, flags, source, ignore, onlyCollider);
        RunSweep(ref q, bruteForce);
        if (!q.HasHit)
        {
            return false;
        }

        if (q.Zero && q.BestT >= 1f)
        {
            return false;
        }

        if (SkipsBackdrop(in q))
        {
            return false;
        }

        float time = q.BestPulled;
        hit = MakeResult(ref q, time, start + d * time);
        return true;
    }

    /// <summary>
    /// SingleLineCheck rule 3 (spec/native/collision-traces.md): with TRACE_BackdropSkip or TRACE_ShadowCast, a world
    /// BSP hit on a surface with a poly flag in ((Flags &amp; 0x100) | 0x8000) &gt;&gt; 8 (PF_FakeBackdrop, and PF_Invisible
    /// for shadow traces) is dropped. MultiLineCheck keeps one BSP hit and pulls the end to it, so nothing behind the
    /// surface is reported either: the trace hits an actor in front of it or nothing.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SingleLineCheck editor 0x110be450 / game 0x10a1d200: Flags & 0x80100, poly flag mask ((Flags & 0x100) | 0x8000) >> 8")]
    private bool SkipsBackdrop(in Query q)
    {
        if ((q.Flags & (ETraceFlags.TRACE_BackdropSkip | ETraceFlags.TRACE_ShadowCast)) == 0 || q.BestCollider < 0 ||
            _colliders[q.BestCollider].Kind != ColliderKind.WorldBsp || (uint)q.BestMaterial >= (uint)_materials.Length)
        {
            return false;
        }

        uint mask = (((uint)q.Flags & 0x100u) | 0x8000u) >> 8;
        return (_materialPolyFlags[q.BestMaterial] & mask) != 0;
    }

    /// <summary>
    /// True when the shape the sweeps move (the box against BSP and brushes, the world cylinder of radius e.X and half
    /// height e.Z against static meshes) overlaps something at <paramref name="p"/>. PointCheck answers differently for
    /// rotated meshes (it tests the box's bounds in the mesh's frame); this is the check of where movement can end.
    /// </summary>
    public bool OverlapMoveShape(out FCheckResult hit, Actor? source, FVector p, FVector e, ETraceFlags flags) =>
        Overlap(out hit, source, null, -1, p, e, flags, bruteForce: false, moveShape: true);

    /// <summary>An overlap test of a box (or point) against everything the flags select.</summary>
    internal bool Overlap(out FCheckResult hit, Actor? source, Actor? ignore, int onlyCollider, FVector p, FVector e, ETraceFlags flags,
        bool bruteForce, bool moveShape = false)
    {
        hit = FCheckResult.NoHit(p);
        Query q = NewQuery(p, default, e, flags, source, ignore, onlyCollider);
        q.IsOverlap = true;
        q.MoveShape = moveShape;
        if (!q.Zero)
        {
            // Static meshes test the box's bounds in their own frame, which reach further than the box for a rotated
            // mesh: widen the tree culling to cover them.
            float reach = OverlapCullScale * q.E.Size() + NodePad;
            q.NodeE = new FVector(reach, reach, reach);
        }
        _stamp++;
        if ((flags & GeometryBits) != 0)
        {
            OverlapSet(_static, ref q, bruteForce);
            foreach (int di in _dynamicColliders)
            {
                ColliderState c = _colliders[di];
                if (!c.Enabled || (onlyCollider >= 0 && di != onlyCollider))
                {
                    continue;
                }

                if (!bruteForce && !SyncColliderForQuery(c, p, p, q.NodeE)) continue;
                if (bruteForce) SyncCollider(c);
                if (c.Set is null || !CollisionMath.BoxesTouch(c.Min - q.NodeE, c.Max + q.NodeE, p, p) || !ColliderPasses(di, ref q))
                {
                    continue;
                }

                OverlapSet(c.Set, ref q, bruteForce);
            }
        }

        if ((flags & ActorBits) != 0 && onlyCollider < 0)
        {
            OverlapActors(ref q);
        }

        if (!q.HasHit)
        {
            return false;
        }

        hit = MakeResult(ref q, 0f, p);
        hit.bStartPenetrating = true;
        return true;
    }

    /// <summary>
    /// The reported time of a hit at contact time <paramref name="contact"/> of a trace of length
    /// <paramref name="len"/>: pulled back along the trace by the rule of the primitive hit, clamped to [0, 1].
    /// Actor cylinders lose 0.001 of the time; BSP (world and brushes) half a unit for lines and
    /// min(0.1 / |D|, 0.01) of the time for boxes; static meshes (and the mesh actors: movers, glass, gameplay
    /// objects, attached collision) min(1 / |D|, 0.1) for both. Below 1 uu the BSP and mesh rules pull back 0.01 / |D|
    /// and 0.1 / |D|.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UPrimitive / UModel / UStaticMesh LineCheck pull-backs (spec/native/collision-primitives.md)")]
    private float PullBack(float contact, float len, bool zeroExtent, int collider)
    {
        return SvmMath.Clamp(contact - PullFraction(len, zeroExtent, collider < 0 ? null : _colliders[collider].Kind), 0f, 1f);
    }

    /// <summary>The largest pull-back of any primitive for a trace of length <paramref name="len"/>.</summary>
    private static float MaxPullBack(float len, bool zeroExtent) =>
        SvmMath.Max(PullFraction(len, zeroExtent, null),
            SvmMath.Max(PullFraction(len, zeroExtent, ColliderKind.WorldBsp), PullFraction(len, zeroExtent, ColliderKind.MeshHull)));

    /// <summary>The pull-back fraction of a primitive kind (null: an actor cylinder).</summary>
    private static float PullFraction(float len, bool zeroExtent, ColliderKind? kind)
    {
        if (kind is null)
        {
            return K.Pullback.Cylinder;
        }

        if (kind is ColliderKind.WorldBsp or ColliderKind.Brush)
        {
            if (zeroExtent)
            {
                return K.Pullback.BspLine / len;
            }

            return K.Pullback.BspBoxCap / len <= K.Pullback.BspBoxCap
                ? SvmMath.Min(K.Pullback.BspBox / len, K.Pullback.BspBoxCap)
                : K.Pullback.BspBoxCap / len;
        }

        return K.Pullback.MeshCap / len <= K.Pullback.MeshCap
            ? SvmMath.Min(K.Pullback.Mesh / len, K.Pullback.MeshCap)
            : K.Pullback.MeshCap / len;
    }

    // ---- sweep core ---------------------------------------------------------------------------------------

    private Query NewQuery(FVector s, FVector d, FVector e, ETraceFlags flags, Actor? source, Actor? ignore, int onlyCollider)
    {
        var q = new Query
        {
            S = s,
            D = d,
            E = new FVector(SvmMath.Abs(e.X), SvmMath.Abs(e.Y), SvmMath.Abs(e.Z)),
            Flags = flags,
            Source = source,
            Ignore = ignore,
            OnlyCollider = onlyCollider,
            StopAtFirst = (flags & ETraceFlags.TRACE_StopAtFirstHit) != 0,
            BestT = 1f,
            BestCollider = -1,
            BestItem = -1,
            BestMaterial = -1,
        };
        q.Zero = q.E.IsZero();
        q.NodeE = q.E + new FVector(NodePad, NodePad, NodePad);
        q.Len = d.Size();
        q.MaxPull = q.Len > 0f ? MaxPullBack(q.Len, q.Zero) : 0f;
        q.ZX = d.X == 0f;
        q.ZY = d.Y == 0f;
        q.ZZ = d.Z == 0f;
        q.InvD = new FVector(q.ZX ? 0f : 1f / d.X, q.ZY ? 0f : 1f / d.Y, q.ZZ ? 0f : 1f / d.Z);
        return q;
    }

    private void RunSweep(ref Query q, bool bruteForce)
    {
        _stamp++;
        if ((q.Flags & GeometryBits) != 0)
        {
            if (q.Zero)
            {
                BspLineCheck(ref q);
                if (q.Done)
                {
                    return;
                }
            }

            SweepSet(_static, ref q, bruteForce);
            if (q.Done)
            {
                return;
            }

            FVector smin = CollisionMath.MinV(q.S, q.S + q.D) - q.NodeE;
            FVector smax = CollisionMath.MaxV(q.S, q.S + q.D) + q.NodeE;
            foreach (int di in _dynamicColliders)
            {
                ColliderState c = _colliders[di];
                if (!c.Enabled || (q.OnlyCollider >= 0 && di != q.OnlyCollider))
                {
                    continue;
                }

                if (!bruteForce && !SyncColliderForQuery(c, smin, smax)) continue;
                if (bruteForce) SyncCollider(c);
                if (c.Set is null || !CollisionMath.BoxesTouch(c.Min, c.Max, smin, smax) || !ColliderPasses(di, ref q))
                {
                    continue;
                }

                SweepSet(c.Set, ref q, bruteForce);
                if (q.Done)
                {
                    return;
                }
            }
        }

        if ((q.Flags & ActorBits) != 0 && q.OnlyCollider < 0)
        {
            SweepActors(ref q);
        }
    }

    private void SweepSet(PrimitiveSet set, ref Query q, bool bruteForce)
    {
        if (set.Tris.Length > 0)
        {
            if (bruteForce)
            {
                for (int i = 0; i < set.Tris.Length && !q.Done; i++)
                {
                    TestTri(set, i, ref q);
                }
            }
            else
            {
                TraverseTris(set, ref q);
            }
        }

        if (set.Hulls.Length > 0 && !q.Done)
        {
            if (bruteForce)
            {
                for (int i = 0; i < set.Hulls.Length && !q.Done; i++)
                {
                    TestHull(set, i, ref q);
                }
            }
            else
            {
                TraverseHulls(set, ref q);
            }
        }
    }

    private bool NodeEntry(in BvhNode node, ref Query q, out float entry) =>
        CollisionMath.SegmentBox(node.Min - q.NodeE, node.Max + q.NodeE, q.S, q.InvD, q.ZX, q.ZY, q.ZZ, q.Limit, out entry);

    private void TraverseTris(PrimitiveSet set, ref Query q)
    {
        BvhNode[] nodes = set.TriNodes;
        if (!NodeEntry(nodes[0], ref q, out float t0))
        {
            return;
        }

        int sp = 0;
        _stackNode[sp] = 0;
        _stackT[sp++] = t0;
        while (sp > 0)
        {
            sp--;
            int ni = _stackNode[sp];
            if (_stackT[sp] > q.Limit)
            {
                continue;
            }

            ref BvhNode node = ref nodes[ni];
            if (node.IsLeaf)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    TestTri(set, i, ref q);
                    if (q.Done)
                    {
                        return;
                    }
                }

                continue;
            }

            Push(nodes, node.Start, ref q, ref sp);
        }
    }

    private void TraverseHulls(PrimitiveSet set, ref Query q)
    {
        BvhNode[] nodes = set.HullNodes;
        if (!NodeEntry(nodes[0], ref q, out float t0))
        {
            return;
        }

        int sp = 0;
        _stackNode[sp] = 0;
        _stackT[sp++] = t0;
        while (sp > 0)
        {
            sp--;
            int ni = _stackNode[sp];
            if (_stackT[sp] > q.Limit)
            {
                continue;
            }

            ref BvhNode node = ref nodes[ni];
            if (node.IsLeaf)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    TestHull(set, i, ref q);
                    if (q.Done)
                    {
                        return;
                    }
                }

                continue;
            }

            Push(nodes, node.Start, ref q, ref sp);
        }
    }

    /// <summary>Pushes the children of an inner node that the sweep reaches, the nearer one on top.</summary>
    private void Push(BvhNode[] nodes, int left, ref Query q, ref int sp)
    {
        bool hl = NodeEntry(nodes[left], ref q, out float tl);
        bool hr = NodeEntry(nodes[left + 1], ref q, out float tr);
        if (hl && hr)
        {
            if (tl <= tr)
            {
                _stackNode[sp] = left + 1;
                _stackT[sp++] = tr;
                _stackNode[sp] = left;
                _stackT[sp++] = tl;
            }
            else
            {
                _stackNode[sp] = left;
                _stackT[sp++] = tl;
                _stackNode[sp] = left + 1;
                _stackT[sp++] = tr;
            }
        }
        else if (hl)
        {
            _stackNode[sp] = left;
            _stackT[sp++] = tl;
        }
        else if (hr)
        {
            _stackNode[sp] = left + 1;
            _stackT[sp++] = tr;
        }
    }

    private void TestTri(PrimitiveSet set, int i, ref Query q)
    {
        ref TriPrim t = ref set.Tris[i];
        if (t.Degenerate || (q.Zero && t.Collider == _bspCollider) || !ColliderPasses(t.Collider, ref q))
        {
            return;
        }

        if (q.Zero && IsStaticMeshKind(_colliders[t.Collider].Kind))
        {
            // UStaticMesh::LineCheck tests a line against both faces of a collision triangle.
            if (StaticMeshSweep.LineTriangle(t, q.S, q.D, q.Limit, out float lt, out FVector ln))
            {
                Consider(ref q, lt, Key(t.Collider, t.Item), ln, false, t.Collider, t.Item, t.Material, null);
            }

            return;
        }

        if (!q.Zero && IsStaticMeshKind(_colliders[t.Collider].Kind))
        {
            // UStaticMesh::LineCheck sweeps the pawn's cylinder, not the box, against mesh triangles.
            if (StaticMeshSweep.SweepTriangle(t, q.S, q.D, q.E, q.Limit, out float mt, out FVector mn))
            {
                Consider(ref q, mt, Key(t.Collider, t.Item), mn, false, t.Collider, t.Item, t.Material, null);
            }

            return;
        }

        if (CollisionMath.SweepTriangle(t, q.S, q.D, q.E, q.Zero, q.Limit, out float time, out FVector n, out bool pen) &&
            (!q.Zero || time < 1f) &&
            (q.Zero || CollisionMath.SweepTriangle(t, q.S, q.D, Skinned(q), false, q.Limit, out _, out _, out _)))
        {
            Consider(ref q, time, Key(t.Collider, t.Item), n, pen, t.Collider, t.Item, t.Material, null);
        }
    }

    /// <summary>Colliders whose triangles are a UStaticMesh's (the same split as the pull-backs): all but BSP and brushes.</summary>
    private static bool IsStaticMeshKind(ColliderKind kind) => kind is not (ColliderKind.WorldBsp or ColliderKind.Brush);

    private void TestHull(PrimitiveSet set, int i, ref Query q)
    {
        ref HullPrim h = ref set.Hulls[i];
        if (!ColliderPasses(h.Collider, ref q))
        {
            return;
        }

        ReadOnlySpan<HullPlane> planes = set.Planes.AsSpan(h.PlaneStart, h.PlaneCount);
        if (CollisionMath.SweepHull(planes, h.Min, h.Max, q.S, q.D, q.E, q.Limit, out float time, out FVector n, out bool pen) &&
            (!q.Zero || time < 1f) &&
            (q.Zero || CollisionMath.SweepHull(planes, h.Min, h.Max, q.S, q.D, Skinned(q), q.Limit, out _, out _, out _)))
        {
            Consider(ref q, time, Key(h.Collider, h.Item), n, pen, h.Collider, h.Item, h.Material, null);
        }
    }

    /// <summary>
    /// The query's box shrunk by <see cref="K.BoxContactSkin"/>: a BSP, brush or hull hit of a box counts only when the
    /// shrunk box hits too (the time and normal stay the full box's), and an overlap only when the shrunk box overlaps.
    /// </summary>
    private static FVector Skinned(in Query q) =>
        q.E.IsZero()
            ? q.E
            : new FVector(SvmMath.Max(0f, q.E.X - K.BoxContactSkin), SvmMath.Max(0f, q.E.Y - K.BoxContactSkin), SvmMath.Max(0f, q.E.Z - K.BoxContactSkin));

    private static long Key(int collider, int item) => ((long)collider << 32) | (uint)item;

    private long ActorKey(Actor a) => (long)(_colliders.Length + a.ActorId) << 32;

    private void Consider(ref Query q, float time, long key, FVector normal, bool pen, int collider, int item, int material, Actor? actor)
    {
        if (q.Collect)
        {
            AddMulti(ref q, time, key, normal, pen, collider, item, material, actor);
            return;
        }

        float pulled = q.Len > 0f ? PullBack(time, q.Len, q.Zero, collider) : time;
        if (!q.HasHit || pulled < q.BestPulled || (pulled == q.BestPulled && (time < q.BestT || (time == q.BestT && key < q.BestKey))))
        {
            q.HasHit = true;
            q.BestT = time;
            q.BestPulled = pulled;
            q.BestKey = key;
            q.BestN = normal;
            q.BestPen = pen;
            q.BestCollider = collider;
            q.BestItem = item;
            q.BestMaterial = material;
            q.BestActor = actor;
        }

        if (q.StopAtFirst)
        {
            q.Done = true;
        }
    }

    private void AddMulti(ref Query q, float time, long key, FVector normal, bool pen, int collider, int item, int material, Actor? actor)
    {
        q.HasHit = true;
        var entry = new MultiEntry { RawTime = time, Pulled = PullBack(time, q.Len, q.Zero, collider), Key = key };
        FillResult(ref entry.Result, normal, pen, collider, item, material, actor);
        if (collider >= 0)
        {
            if (_multiStamp[collider] == _stamp)
            {
                int slot = _multiSlot[collider];
                MultiEntry old = _multi[slot];
                if (entry.Pulled < old.Pulled || (entry.Pulled == old.Pulled && (time < old.RawTime || (time == old.RawTime && key < old.Key))))
                {
                    _multi[slot] = entry;
                }

                return;
            }

            _multiStamp[collider] = _stamp;
            _multiSlot[collider] = _multi.Count;
        }

        _multi.Add(entry);
    }

    private FCheckResult MakeResult(ref Query q, float time, FVector location)
    {
        var r = default(FCheckResult);
        FillResult(ref r, q.BestN, q.BestPen, q.BestCollider, q.BestItem, q.BestMaterial, q.BestActor);
        r.Time = time;
        r.Location = location;
        return r;
    }

    private void FillResult(ref FCheckResult r, FVector normal, bool pen, int collider, int item, int material, Actor? actor)
    {
        r.Normal = normal;
        r.bStartPenetrating = pen;
        r.Collider = collider;
        r.Item = item;
        r.Material = material;
        if (collider >= 0)
        {
            r.Actor = _colliders[collider].Actor ?? _level?.Info;
        }
        else
        {
            r.Actor = actor;
        }

        if ((uint)material < (uint)_materials.Length)
        {
            CollisionMaterial m = _materials[material];
            r.SurfaceType = m.SurfaceType;
            r.MaterialFlags = m.Flags;
            if (collider >= 0 && _colliders[collider].Kind == ColliderKind.WorldBsp)
            {
                r.PolyFlags = _materialPolyFlags[material];
            }
        }
    }

    // ---- collider filter ----------------------------------------------------------------------------------

    private bool ColliderPasses(int ci, ref Query q)
    {
        if (_filterStamp[ci] == _stamp)
        {
            return _filterValue[ci];
        }

        bool ok = EvaluateCollider(_colliders[ci], ref q);
        _filterStamp[ci] = _stamp;
        _filterValue[ci] = ok;
        return ok;
    }

    /// <summary>
    /// The trace filter of one collider (UE2 ShouldTrace with the hash candidate rules): not the source, an owner of
    /// the source or the ignored actor; of a class the flags select; colliding and blocking this extent; the static
    /// mesh tree the trace uses; then the actor gates (ShadowCast, bOnlyAffectPawns, camera, projectors, bullets) and
    /// the class rules: world BSP always; world static meshes with LevelGeometry, for a pawn source only when they
    /// block players; others by OnlyProjActor and Blocking; brushes and movers block only sources that collide with
    /// the world.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "AActor::ShouldTrace 0x110ac870, FCollisionHash::ActorLineCheck 0x1115f5a0 (spec/native/collision-traces.md)")]
    [Provenance(ProvenanceKind.Estimated, "Movers and volumes answer to TRACE_Movers / TRACE_Volumes as in UE2 (their ShouldTrace overrides are not decoded)")]
    private bool EvaluateCollider(ColliderState c, ref Query q)
    {
        if (!c.Enabled || (q.OnlyCollider >= 0 && c.Index != q.OnlyCollider))
        {
            return false;
        }

        Actor? owner = c.Actor;
        Actor? src = q.Source;
        if (owner is not null && (owner.bDeleteMe || ReferenceEquals(owner, q.Ignore) || IsSourceOrOwnerOf(owner, src)))
        {
            return false;
        }

        if ((q.Flags & c.TraceBits) == 0)
        {
            return false;
        }

        GetColliderFlags(c, out bool collide, out bool blockActors, out bool blockPlayers);
        if (c.Kind != ColliderKind.WorldBsp && !collide)
        {
            return false;
        }

        if (!q.IsOverlap)
        {
            ColliderFlags need = q.Zero ? ColliderFlags.BlockZeroExtent : ColliderFlags.BlockNonZeroExtent;
            if ((c.Flags & need) == 0)
            {
                return false;
            }
        }

        // Static meshes: the simple collision set unless the trace asks for shadows, else the per-poly set.
        bool shadow = (q.Flags & ETraceFlags.TRACE_ShadowCast) != 0;
        if (c.SimpleTwin >= 0 && !shadow && _colliders[c.SimpleTwin].Enabled)
        {
            return false;
        }

        if (c.PerPolyTwin >= 0 && shadow && _colliders[c.PerPolyTwin].Enabled)
        {
            return false;
        }

        if (shadow && owner is not null && !owner.bShadowCast)
        {
            return false;
        }

        if (c.Kind != ColliderKind.WorldBsp && src is not null && src.bOnlyAffectPawns)
        {
            return false;
        }

        if (owner is not null && owner.bOnlyAffectPawns && (src is null || !src.bIsAPawn))
        {
            return false;
        }

        if ((q.Flags & ETraceFlags.TRACE_Camera) != 0 && (c.Flags & ColliderFlags.BlockCamera) == 0)
        {
            return false;
        }

        if ((q.Flags & ETraceFlags.TRACE_AcceptProjectors) != 0 && owner is not null && !owner.bAcceptsProjectors)
        {
            return false;
        }

        if ((q.Flags & ETraceFlags.TRACE_Bullet) != 0 && (c.Flags & ColliderFlags.BulletGoesThrough) != 0)
        {
            return false;
        }

        bool blocking = (q.Flags & ETraceFlags.TRACE_Blocking) != 0;
        switch (c.Kind)
        {
            case ColliderKind.WorldBsp:
                return true;
            case ColliderKind.MeshPoly or ColliderKind.MeshHull:
                // bWorldGeometry static meshes: a pawn source sees them only when they block players.
                if (src is not null && src.bIsAPawn)
                {
                    return blockPlayers;
                }

                return true;
            case ColliderKind.Brush when owner is Volume vol && !VolumeShouldTrace(vol, q.Flags, blockActors, blockPlayers):
                return false;
            case ColliderKind.Brush or ColliderKind.Mover:
                if (!blocking)
                {
                    return true;
                }

                if (src is null)
                {
                    return blockActors || blockPlayers;
                }

                return src.bCollideWorld && (src.bIsAPawn ? blockPlayers : blockActors);
            default:
                if ((q.Flags & ETraceFlags.TRACE_OnlyProjActor) != 0 && !(owner?.bProjTarget ?? false) && !(blockActors && blockPlayers))
                {
                    return false;
                }

                if (!blocking)
                {
                    return true;
                }

                if (src is null)
                {
                    return blockActors || blockPlayers;
                }

                return src.bIsAPawn ? blockPlayers : blockActors;
        }
    }

    /// <summary>
    /// AVolume::ShouldTrace past the shared gates (spec/native/collision-traces.md, ShouldTrace, AVolume): a world
    /// geometry volume is traced by LevelGeometry; otherwise the trace needs Volumes, and with OnlyProjActor only a
    /// bProjTarget volume or one blocking both actors and players is traced. So a script Trace with actors and the
    /// sticky cam line (both OnlyProjActor) pass through zone, sound and physics volumes. The Blocking rule stays in
    /// the brush branch of <see cref="EvaluateCollider"/>.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "AVolume::ShouldTrace game 0x10991de0 (Volume / BlockingVolume vtable +0xac; AActor's is 0x10991c40)")]
    internal static bool VolumeShouldTrace(Volume V, ETraceFlags Flags, bool BlockActors, bool BlockPlayers)
    {
        if (V.bWorldGeometry && (Flags & ETraceFlags.TRACE_LevelGeometry) != 0)
        {
            return true;
        }

        if ((Flags & ETraceFlags.TRACE_Volumes) == 0)
        {
            return false;
        }

        return (Flags & ETraceFlags.TRACE_OnlyProjActor) == 0 || V.bProjTarget || (BlockActors && BlockPlayers);
    }

    /// <summary>True when <paramref name="actor"/> is <paramref name="source"/> or one of its owners.</summary>
    internal static bool IsSourceOrOwnerOf(Actor actor, Actor? source)
    {
        for (Actor? a = source; a is not null; a = a.Owner)
        {
            if (ReferenceEquals(a, actor))
            {
                return true;
            }

            if (ReferenceEquals(a.Owner, source))
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// A collider's collide and block flags: its own (from the map) while its owner keeps the flags it had when the
    /// level was bound, the owner's live ones once script has changed them (SetCollision).
    /// </summary>
    internal static void GetColliderFlags(ColliderState c, out bool collide, out bool blockActors, out bool blockPlayers)
    {
        Actor? a = c.Actor;
        if (a is not null && (a.bCollideActors != c.BaseCollideActors || a.bBlockActors != c.BaseBlockActors ||
                              a.bBlockPlayers != c.BaseBlockPlayers))
        {
            collide = a.bCollideActors;
            blockActors = a.bBlockActors;
            blockPlayers = a.bBlockPlayers;
            return;
        }

        collide = (c.Flags & ColliderFlags.CollideActors) != 0;
        blockActors = (c.Flags & ColliderFlags.BlockActors) != 0;
        blockPlayers = (c.Flags & ColliderFlags.BlockPlayers) != 0;
    }

    // ---- overlaps -----------------------------------------------------------------------------------------

    private void OverlapSet(PrimitiveSet set, ref Query q, bool bruteForce)
    {
        if (set.Tris.Length > 0 && !q.Zero)
        {
            if (bruteForce)
            {
                for (int i = 0; i < set.Tris.Length; i++)
                {
                    OverlapTri(set, i, ref q);
                }
            }
            else
            {
                OverlapWalk(set, set.TriNodes, hulls: false, ref q);
            }
        }

        if (set.Hulls.Length > 0)
        {
            if (bruteForce)
            {
                for (int i = 0; i < set.Hulls.Length; i++)
                {
                    OverlapHullAt(set, i, ref q);
                }
            }
            else
            {
                OverlapWalk(set, set.HullNodes, hulls: true, ref q);
            }
        }
    }

    private void OverlapWalk(PrimitiveSet set, BvhNode[] nodes, bool hulls, ref Query q)
    {
        int sp = 0;
        _stackNode[sp++] = 0;
        while (sp > 0)
        {
            int ni = _stackNode[--sp];
            ref BvhNode node = ref nodes[ni];
            if (!CollisionMath.BoxesTouch(node.Min - q.NodeE, node.Max + q.NodeE, q.S, q.S))
            {
                continue;
            }

            if (node.IsLeaf)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    if (hulls)
                    {
                        OverlapHullAt(set, i, ref q);
                    }
                    else
                    {
                        OverlapTri(set, i, ref q);
                    }
                }

                continue;
            }

            _stackNode[sp++] = node.Start + 1;
            _stackNode[sp++] = node.Start;
        }
    }

    private void OverlapTri(PrimitiveSet set, int i, ref Query q)
    {
        ref TriPrim t = ref set.Tris[i];
        if (t.Degenerate || !ColliderPasses(t.Collider, ref q))
        {
            return;
        }

        bool mesh = !q.E.IsZero() && IsStaticMeshKind(_colliders[t.Collider].Kind);
        FVector n = default;
        bool overlaps = mesh
            ? q.MoveShape
                ? StaticMeshSweep.OverlapsSweptCylinder(t, q.S, q.E.X, q.E.Z)
                : StaticMeshSweep.OverlapTriangle(t, _colliders[t.Collider], q.S, q.E, out n)
            : CollisionMath.OverlapTriangle(t, q.S, Skinned(q), out n);
        if (overlaps)
        {
            ConsiderOverlap(ref q, Key(t.Collider, t.Item), n, t.Collider, t.Item, t.Material, null);
        }
    }

    private void OverlapHullAt(PrimitiveSet set, int i, ref Query q)
    {
        ref HullPrim h = ref set.Hulls[i];
        if (!ColliderPasses(h.Collider, ref q))
        {
            return;
        }

        if (CollisionMath.OverlapHull(set.Planes.AsSpan(h.PlaneStart, h.PlaneCount), h.Min, h.Max, q.S, Skinned(q), out FVector n))
        {
            ConsiderOverlap(ref q, Key(h.Collider, h.Item), n, h.Collider, h.Item, h.Material, null);
        }
    }

    private static void ConsiderOverlap(ref Query q, long key, FVector normal, int collider, int item, int material, Actor? actor)
    {
        if (q.HasHit && key >= q.BestKey)
        {
            return;
        }

        q.HasHit = true;
        q.BestT = 0f;
        q.BestKey = key;
        q.BestN = normal;
        q.BestPen = true;
        q.BestCollider = collider;
        q.BestItem = item;
        q.BestMaterial = material;
        q.BestActor = actor;
    }
}
