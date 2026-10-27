using System;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

public sealed partial class CollisionWorld
{
    /// <summary>The world collider whose BSP tree answers zero-extent traces, or -1 (no tree: its triangles do).</summary>
    private readonly int _bspCollider = -1;

    private readonly CollisionTriangle[] _bspTris = [];
    private readonly FVector[] _bspVerts = [];

    /// <summary>
    /// UModel::LineCheck with a zero extent on the world BSP: the line is split at every node plane it crosses and
    /// followed front half first; a CSG node (a polygon, NodeFlags without 0x01 NotCsg or 0x20 IsNew) makes its
    /// front side empty and its back side solid, other nodes pass the state on. The first solid leaf is the hit:
    /// the point where the line entered it, on the last plane crossed. The hit polygon is the first node of that
    /// plane's coplanar chain whose polygon holds the point. Polygon winding plays no part, so the converted floor
    /// polygons wound the other way are hit like the rest.
    /// </summary>
    [UcNative("UModel::LineCheck", Exe = 0x10a42f30, Editor = 0x111943e0)]
    [PortOf("eng:UModel::LineCheck", Spec = "spec/native/collision-primitives.md#umodellinecheck-bsp")]
    [Provenance(ProvenanceKind.Decoded, "Line walk editor 0x111925a0 (planes within 0.001 count as front, CSG test NumVertices and NodeFlags & 0x21, start in solid hits unless ExtraNodeFlags & 0x10), coplanar hit search 0x11192a30 (spec/native/ANSWERS-moveset.md W1)")]
    [Provenance(ProvenanceKind.Estimated, "svmap BSPN has no NumVertices: a node with triangles stands for one with a polygon (on SteeE every node has 3 or more vertices and every non-solid surface node carries NF_NotCsg)")]
    private void BspLineCheck(ref Query q)
    {
        if (_bspCollider < 0 || _bspRoot < 0 || !ColliderPasses(_bspCollider, ref q))
        {
            return;
        }

        // ExtraNodeFlags are 0 (ULevel::MultiLineCheck): the 0x10 bit that lets a line start in solid (the native's
        // "was outside" flag) is never set, so a line that starts in solid hits at its start.
        FVector end = q.S + q.D;
        if (BspWalk(_bspRoot, _bspRoot, q.S, end, K.Bsp.LevelRootOutside, out FVector hitAt, out int hitNode))
        {
            return;
        }

        float dd = q.D | q.D;
        float time = ((hitAt - q.S) | q.D) / dd;
        int node = BspPolygonAt(hitNode, hitAt);
        if (node < 0)
        {
            node = hitNode;
        }

        CollisionBspNode n = _bsp[node];
        int material = n.TriCount > 0 ? _bspTris[(int)n.TriStart].Material : -1;
        Consider(ref q, SvmMath.Clamp(time, 0f, 1f), Key(_bspCollider, node), n.Plane.Normal, false, _bspCollider, node, material, null);
    }

    /// <summary>
    /// One call of the line walk (editor 0x111925a0) from <paramref name="start"/> to <paramref name="end"/> at
    /// <paramref name="node"/>, <paramref name="prev"/> being the last plane crossed. True when the segment ends in
    /// empty space (no hit); false with the entry point and the plane's node at the first solid leaf.
    /// </summary>
    private bool BspWalk(int prev, int node, FVector start, FVector end, bool outside, out FVector hitAt, out int hitNode)
    {
        while (node >= 0)
        {
            CollisionBspNode n = _bsp[node];
            float ds = n.Plane.PlaneDot(start);
            float de = n.Plane.PlaneDot(end);
            bool csg = n.TriCount > 0 && (n.NodeFlags & K.Bsp.NotCsgFlags) == 0;
            if (ds > -K.Bsp.PlaneSlop && de > -K.Bsp.PlaneSlop)
            {
                outside |= csg;
                node = n.Front;
                continue;
            }

            if (ds < K.Bsp.PlaneSlop && de < K.Bsp.PlaneSlop)
            {
                outside &= !csg;
                node = n.Back;
                continue;
            }

            // Crossing: the near half first (the last plane crossed is still prev), then the far half from the
            // crossing point with this node as the plane crossed.
            FVector middle = start + ((start - end) * (ds / (de - ds)));
            bool startFront = ds > 0f;
            bool near = startFront ? outside || csg : outside && !csg;
            if (!BspWalk(prev, startFront ? n.Front : n.Back, start, middle, near, out hitAt, out hitNode))
            {
                return false;
            }

            outside = startFront ? outside && !csg : outside || csg;
            prev = node;
            node = startFront ? n.Back : n.Front;
            start = middle;
        }

        hitAt = start;
        hitNode = prev;
        return outside;
    }

    /// <summary>
    /// The node of <paramref name="node"/>'s coplanar chain whose polygon holds <paramref name="p"/> (editor
    /// 0x11192a30: every edge's side test against the point agrees in sign, zero allowed), or -1.
    /// </summary>
    private int BspPolygonAt(int node, FVector p)
    {
        for (int guard = 0; node >= 0 && guard <= _bsp.Length; guard++)
        {
            CollisionBspNode n = _bsp[node];
            FVector nn = n.Plane.Normal;
            for (int t = (int)n.TriStart; t < (int)(n.TriStart + n.TriCount); t++)
            {
                CollisionTriangle tri = _bspTris[t];
                if (InsidePolygon(nn, p, _bspVerts[(int)tri.I0], _bspVerts[(int)tri.I1], _bspVerts[(int)tri.I2]))
                {
                    return node;
                }
            }

            node = n.Coplanar;
        }

        return -1;
    }

    private static bool InsidePolygon(FVector normal, FVector p, FVector a, FVector b, FVector c)
    {
        float last = 0f;
        FVector prev = c;
        foreach (FVector v in (ReadOnlySpan<FVector>)[a, b, c])
        {
            FVector side = (v - prev) ^ normal;
            float s = (side | p) - (side | v);
            if ((s < 0f && last > 0f) || (s > 0f && last < 0f))
            {
                return false;
            }

            last = s;
            prev = v;
        }

        return true;
    }
}
