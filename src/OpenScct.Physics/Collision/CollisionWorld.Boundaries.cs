using System;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Physics.Collision;

/// <summary>A unit surface plane, with distance measured from the query box center.</summary>
public readonly record struct CollisionBoundaryPlane(FVector Normal, float Distance);

public sealed partial class CollisionWorld
{
    /// <summary>
    /// Tests an actual world-aligned box and collects the exact geometry planes cutting it. Unlike native
    /// PointCheck, rotated mesh bounds do not enlarge the box. Overflow returns -1 planes and remains blocked.
    /// Surface planes are deliberately conservative within the small query box; they are not a replacement
    /// for full triangle collision. No pulled-back line-hit positions enter the result.
    /// </summary>
    public bool BoxBoundaryPlanes(Actor? source, FVector center, FVector extent, ETraceFlags flags,
        Span<CollisionBoundaryPlane> planes, out int count)
    {
        Query q = NewQuery(center, default, extent, flags, source, null, -1);
        q.IsOverlap = true;
        _stamp++;
        count = 0;
        bool blocked = BoundarySet(_static, ref q, planes, ref count);
        foreach (int index in _dynamicColliders)
        {
            ColliderState collider = _colliders[index];
            if (!collider.Enabled || !SyncColliderForQuery(collider, center, center, extent) ||
                collider.Set is null || !ColliderPasses(index, ref q)) continue;
            blocked |= BoundarySet(collider.Set, ref q, planes, ref count);
        }
        return blocked;
    }

    private bool BoundarySet(PrimitiveSet set, ref Query q, Span<CollisionBoundaryPlane> planes, ref int count)
    {
        bool blocked = false;
        if (set.Tris.Length > 0) blocked |= BoundaryWalk(set, set.TriNodes, false, ref q, planes, ref count);
        if (set.Hulls.Length > 0) blocked |= BoundaryWalk(set, set.HullNodes, true, ref q, planes, ref count);
        return blocked;
    }

    private bool BoundaryWalk(PrimitiveSet set, BvhNode[] nodes, bool hulls, ref Query q,
        Span<CollisionBoundaryPlane> planes, ref int count)
    {
        int sp = 0; _stackNode[sp++] = 0;
        bool blocked = false;
        while (sp > 0)
        {
            ref BvhNode node = ref nodes[_stackNode[--sp]];
            if (!CollisionMath.BoxesTouch(node.Min - q.E, node.Max + q.E, q.S, q.S)) continue;
            if (!node.IsLeaf) { _stackNode[sp++] = node.Start + 1; _stackNode[sp++] = node.Start; continue; }
            for (int i = node.Start; i < node.Start + node.Count; i++)
            {
                if (!hulls)
                {
                    ref TriPrim tri = ref set.Tris[i];
                    if (tri.Degenerate || !ColliderPasses(tri.Collider, ref q) ||
                        !CollisionMath.OverlapTriangle(tri, q.S, q.E, out _)) continue;
                    blocked = true;
                    AddBoundary(planes, ref count, tri.N, tri.N | (tri.V0 - q.S));
                }
                else
                {
                    ref HullPrim hull = ref set.Hulls[i];
                    if (!ColliderPasses(hull.Collider, ref q) ||
                        !CollisionMath.OverlapHull(set.Planes.AsSpan(hull.PlaneStart, hull.PlaneCount),
                            hull.Min, hull.Max, q.S, q.E, out _)) continue;
                    blocked = true;
                    for (int p = hull.PlaneStart; p < hull.PlaneStart + hull.PlaneCount; p++)
                    {
                        HullPlane plane = set.Planes[p];
                        float distance = plane.W - (plane.N | q.S);
                        float support = MathF.Abs(plane.N.X) * q.E.X + MathF.Abs(plane.N.Y) * q.E.Y + MathF.Abs(plane.N.Z) * q.E.Z;
                        if (MathF.Abs(distance) <= support) AddBoundary(planes, ref count, plane.N, distance);
                    }
                }
            }
        }
        return blocked;
    }

    private static void AddBoundary(Span<CollisionBoundaryPlane> planes, ref int count, FVector normal, float distance)
    {
        if (count < 0) return;
        if (normal.X < -.00001f || MathF.Abs(normal.X) <= .00001f &&
            (normal.Y < -.00001f || MathF.Abs(normal.Y) <= .00001f && normal.Z < 0f))
        { normal = -normal; distance = -distance; }
        for (int i = 0; i < count; i++)
            if ((planes[i].Normal | normal) > .99999f && MathF.Abs(planes[i].Distance - distance) < .01f) return;
        if (count == planes.Length) { count = -1; return; }
        planes[count++] = new(normal, distance);
    }
}
