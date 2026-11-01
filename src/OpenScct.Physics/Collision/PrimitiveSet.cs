using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Physics.Collision;

/// <summary>Local-space source of one hull of a dynamic set.</summary>
internal struct LocalHull
{
    public int CornerStart;
    public int CornerCount;
    public FVector Min;
    public FVector Max;
}

/// <summary>Local-space corners of one triangle of a dynamic set.</summary>
internal struct LocalTri
{
    public FVector V0;
    public FVector V1;
    public FVector V2;
}

/// <summary>
/// World-space triangles and hulls with one BVH each. The static set holds every collider that never moves; each
/// dynamic collider (movers, glass, gameplay objects, attached collision) has its own set that keeps its
/// local-space source, so a new transform rewrites the world data in place and refits the BVH.
/// </summary>
internal sealed class PrimitiveSet
{
    public TriPrim[] Tris = [];
    public BvhNode[] TriNodes = [];
    public HullPrim[] Hulls = [];
    public HullPlane[] Planes = [];
    public BvhNode[] HullNodes = [];

    // Dynamic sets only, in the same (BVH) order as Tris and Hulls.
    public LocalTri[]? LocalTris;
    public LocalHull[]? LocalHulls;
    public HullPlane[]? LocalPlanes;
    public FVector[]? LocalCorners;
    public FVector LocalMin;
    public FVector LocalMax;

    public FVector Min;
    public FVector Max;

    public bool IsEmpty => Tris.Length == 0 && Hulls.Length == 0;

    /// <summary>Rewrites the world data from the local source and refits the BVHs (dynamic sets only).</summary>
    public void ApplyTransform(in ColliderTransform xf)
    {
        if (LocalTris is null || LocalHulls is null || LocalPlanes is null || LocalCorners is null)
        {
            throw new InvalidOperationException("Only dynamic primitive sets can be moved.");
        }

        for (int i = 0; i < Tris.Length; i++)
        {
            ref TriPrim t = ref Tris[i];
            LocalTri l = LocalTris[i];
            FVector w0 = xf.TransformPoint(l.V0);
            FVector w1 = xf.TransformPoint(l.V1);
            FVector w2 = xf.TransformPoint(l.V2);
            if (xf.Mirrored)
            {
                (w1, w2) = (w2, w1);
            }

            t.Set(w0, w1, w2);
        }

        for (int i = 0; i < Hulls.Length; i++)
        {
            ref HullPrim h = ref Hulls[i];
            for (int k = h.PlaneStart; k < h.PlaneStart + h.PlaneCount; k++)
            {
                Planes[k] = xf.TransformPlane(LocalPlanes[k].N, LocalPlanes[k].W);
            }

            LocalHull lh = LocalHulls[i];
            PrimitiveSetBuilder.HullBounds(LocalCorners.AsSpan(lh.CornerStart, lh.CornerCount), lh.Min, lh.Max, xf, out h.Min, out h.Max);
        }

        RefitTris();
        RefitHulls();
        UpdateBounds();
    }

    public void UpdateBounds()
    {
        bool any = false;
        Min = default;
        Max = default;
        if (Tris.Length > 0)
        {
            Min = TriNodes[0].Min;
            Max = TriNodes[0].Max;
            any = true;
        }

        if (Hulls.Length > 0)
        {
            Min = any ? CollisionMath.MinV(Min, HullNodes[0].Min) : HullNodes[0].Min;
            Max = any ? CollisionMath.MaxV(Max, HullNodes[0].Max) : HullNodes[0].Max;
        }
    }

    private void RefitTris()
    {
        BvhNode[] nodes = TriNodes;
        for (int i = nodes.Length - 1; i >= 0; i--)
        {
            ref BvhNode n = ref nodes[i];
            if (n.IsLeaf)
            {
                FVector min = Tris[n.Start].Min;
                FVector max = Tris[n.Start].Max;
                for (int k = 1; k < n.Count; k++)
                {
                    min = CollisionMath.MinV(min, Tris[n.Start + k].Min);
                    max = CollisionMath.MaxV(max, Tris[n.Start + k].Max);
                }

                n.Min = min;
                n.Max = max;
            }
            else if (n.Start > 0)
            {
                n.Min = CollisionMath.MinV(nodes[n.Start].Min, nodes[n.Start + 1].Min);
                n.Max = CollisionMath.MaxV(nodes[n.Start].Max, nodes[n.Start + 1].Max);
            }
        }
    }

    private void RefitHulls()
    {
        BvhNode[] nodes = HullNodes;
        for (int i = nodes.Length - 1; i >= 0; i--)
        {
            ref BvhNode n = ref nodes[i];
            if (n.IsLeaf)
            {
                FVector min = Hulls[n.Start].Min;
                FVector max = Hulls[n.Start].Max;
                for (int k = 1; k < n.Count; k++)
                {
                    min = CollisionMath.MinV(min, Hulls[n.Start + k].Min);
                    max = CollisionMath.MaxV(max, Hulls[n.Start + k].Max);
                }

                n.Min = min;
                n.Max = max;
            }
            else if (n.Start > 0)
            {
                n.Min = CollisionMath.MinV(nodes[n.Start].Min, nodes[n.Start + 1].Min);
                n.Max = CollisionMath.MaxV(nodes[n.Start].Max, nodes[n.Start + 1].Max);
            }
        }
    }
}

/// <summary>Collects primitives (already in world space, or local plus a transform) and builds a set.</summary>
internal sealed class PrimitiveSetBuilder
{
    private readonly bool _dynamic;
    private readonly List<TriPrim> _tris = new();
    private readonly List<LocalTri> _localTris = new();
    private readonly List<HullPrim> _hulls = new();
    private readonly List<HullPlane> _planes = new();
    private readonly List<HullPlane> _localPlanes = new();
    private readonly List<LocalHull> _localHulls = new();
    private readonly List<FVector> _localCorners = new();
    private readonly List<FVector> _scratchCorners = new();

    public PrimitiveSetBuilder(bool dynamic)
    {
        _dynamic = dynamic;
    }

    /// <summary>Triangles dropped because they have no area.</summary>
    public int DegenerateTriangles { get; private set; }

    /// <summary>Adds a triangle given in local space; grows <paramref name="min"/>/<paramref name="max"/> by its world bounds.</summary>
    public void AddTriangle(int collider, int material, int item, FVector l0, FVector l1, FVector l2, in ColliderTransform xf,
        ref FVector min, ref FVector max, ref bool hasBounds)
    {
        FVector w0 = xf.TransformPoint(l0);
        FVector w1 = xf.TransformPoint(l1);
        FVector w2 = xf.TransformPoint(l2);
        if (xf.Mirrored)
        {
            (w1, w2) = (w2, w1);
        }

        var t = new TriPrim { Collider = collider, Material = material, Item = item };
        if (!t.Set(w0, w1, w2))
        {
            DegenerateTriangles++;
            return;
        }

        _tris.Add(t);
        if (_dynamic)
        {
            _localTris.Add(new LocalTri { V0 = l0, V1 = l1, V2 = l2 });
        }

        Grow(t.Min, t.Max, ref min, ref max, ref hasBounds);
    }

    /// <summary>Adds a hull given by local planes, optional local corners and local bounds.</summary>
    public void AddHull(int collider, int material, int item, ReadOnlySpan<HullPlane> localPlanes, ReadOnlySpan<FVector> localCorners,
        FVector localMin, FVector localMax, in ColliderTransform xf, ref FVector min, ref FVector max, ref bool hasBounds)
    {
        if (localPlanes.Length == 0)
        {
            return;
        }

        ReadOnlySpan<FVector> corners = localCorners;
        if (corners.Length == 0)
        {
            _scratchCorners.Clear();
            if (HullCorners.Compute(localPlanes, _scratchCorners))
            {
                corners = _scratchCorners.ToArray();
            }
        }

        int planeStart = _planes.Count;
        for (int i = 0; i < localPlanes.Length; i++)
        {
            _planes.Add(xf.TransformPlane(localPlanes[i].N, localPlanes[i].W));
            if (_dynamic)
            {
                _localPlanes.Add(localPlanes[i]);
            }
        }

        HullBounds(corners, localMin, localMax, xf, out FVector wmin, out FVector wmax);
        _hulls.Add(new HullPrim
        {
            PlaneStart = planeStart,
            PlaneCount = localPlanes.Length,
            Min = wmin,
            Max = wmax,
            Collider = collider,
            Material = material,
            Item = item,
        });
        if (_dynamic)
        {
            int cornerStart = _localCorners.Count;
            for (int i = 0; i < corners.Length; i++)
            {
                _localCorners.Add(corners[i]);
            }

            _localHulls.Add(new LocalHull { CornerStart = cornerStart, CornerCount = corners.Length, Min = localMin, Max = localMax });
        }

        Grow(wmin, wmax, ref min, ref max, ref hasBounds);
    }

    /// <summary>World bounds of a hull: its transformed corners, or its transformed local box when it has none.</summary>
    public static void HullBounds(ReadOnlySpan<FVector> corners, FVector localMin, FVector localMax, in ColliderTransform xf,
        out FVector min, out FVector max)
    {
        if (corners.Length > 0)
        {
            min = xf.TransformPoint(corners[0]);
            max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                FVector p = xf.TransformPoint(corners[i]);
                min = CollisionMath.MinV(min, p);
                max = CollisionMath.MaxV(max, p);
            }

            return;
        }

        min = xf.TransformPoint(localMin);
        max = min;
        for (int i = 1; i < 8; i++)
        {
            var c = new FVector((i & 1) != 0 ? localMax.X : localMin.X, (i & 2) != 0 ? localMax.Y : localMin.Y,
                (i & 4) != 0 ? localMax.Z : localMin.Z);
            FVector p = xf.TransformPoint(c);
            min = CollisionMath.MinV(min, p);
            max = CollisionMath.MaxV(max, p);
        }
    }

    private static void Grow(FVector a, FVector b, ref FVector min, ref FVector max, ref bool hasBounds)
    {
        if (!hasBounds)
        {
            min = a;
            max = b;
            hasBounds = true;
        }
        else
        {
            min = CollisionMath.MinV(min, a);
            max = CollisionMath.MaxV(max, b);
        }
    }

    public PrimitiveSet Build()
    {
        var set = new PrimitiveSet();
        int nt = _tris.Count;
        var mins = new FVector[nt];
        var maxs = new FVector[nt];
        for (int i = 0; i < nt; i++)
        {
            mins[i] = _tris[i].Min;
            maxs[i] = _tris[i].Max;
        }

        set.TriNodes = Bvh.Build(mins, maxs, out int[] triOrder);
        set.Tris = new TriPrim[nt];
        for (int i = 0; i < nt; i++)
        {
            set.Tris[i] = _tris[triOrder[i]];
        }

        int nh = _hulls.Count;
        mins = new FVector[nh];
        maxs = new FVector[nh];
        for (int i = 0; i < nh; i++)
        {
            mins[i] = _hulls[i].Min;
            maxs[i] = _hulls[i].Max;
        }

        set.HullNodes = Bvh.Build(mins, maxs, out int[] hullOrder);
        set.Hulls = new HullPrim[nh];
        for (int i = 0; i < nh; i++)
        {
            set.Hulls[i] = _hulls[hullOrder[i]];
        }

        set.Planes = _planes.ToArray();
        if (_dynamic)
        {
            set.LocalTris = new LocalTri[nt];
            for (int i = 0; i < nt; i++)
            {
                set.LocalTris[i] = _localTris[triOrder[i]];
            }

            set.LocalHulls = new LocalHull[nh];
            for (int i = 0; i < nh; i++)
            {
                set.LocalHulls[i] = _localHulls[hullOrder[i]];
            }

            set.LocalPlanes = _localPlanes.ToArray();
            set.LocalCorners = _localCorners.ToArray();
            bool hasLocalBounds = false;
            foreach (LocalTri tri in set.LocalTris)
            {
                Grow(tri.V0, tri.V0, ref set.LocalMin, ref set.LocalMax, ref hasLocalBounds);
                Grow(tri.V1, tri.V1, ref set.LocalMin, ref set.LocalMax, ref hasLocalBounds);
                Grow(tri.V2, tri.V2, ref set.LocalMin, ref set.LocalMax, ref hasLocalBounds);
            }

            foreach (FVector corner in set.LocalCorners)
            {
                Grow(corner, corner, ref set.LocalMin, ref set.LocalMax, ref hasLocalBounds);
            }

            foreach (LocalHull hull in set.LocalHulls)
            {
                if (hull.CornerCount == 0)
                    Grow(hull.Min, hull.Max, ref set.LocalMin, ref set.LocalMax, ref hasLocalBounds);
            }
        }

        set.UpdateBounds();
        return set;
    }
}
