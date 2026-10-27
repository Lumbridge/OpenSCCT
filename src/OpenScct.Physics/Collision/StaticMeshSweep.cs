using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

/// <summary>
/// The non-zero-extent sweep of UStaticMesh::LineCheck against one collision triangle (spec/native/ANSWERS-fidelity.md,
/// F1). The swept shape is not the trace's box: it is a vertical cylinder of radius Extent.X and half height
/// Extent.Z, in world space, tested on a fixed set of one-sided separating axes:
/// <list type="number">
/// <item>the triangle's bounds on X, Y and Z, each on the side the start lies (the start above the bounds centre
/// tests the bounds' max face);</item>
/// <item>the triangle's plane, both ways;</item>
/// <item>for every edge that is not vertical: edge x Z, edge x X and Y x edge, each turned away from the third
/// corner;</item>
/// <item>for every corner: the horizontal direction from the corner to the start, when it points away from both
/// edges at that corner.</item>
/// </list>
/// The cylinder's reach on an axis n is H for |n.Z| of 1 (0.99999), else |n.Z| H + R |n.XY|. The entry time is the
/// latest approach over all axes; a sweep that starts touching or inside the triangle is not blocked, so a pawn
/// can always move out. A cylinder misses a triangle corner that the box's corner would reach: a falling pawn
/// passes a mesh's corner the box would catch.
/// </summary>
internal static class StaticMeshSweep
{
    private static readonly FVector AxisX = new(1f, 0f, 0f);
    private static readonly FVector AxisY = new(0f, 1f, 0f);
    private static readonly FVector AxisZ = new(0f, 0f, 1f);

    /// <summary>The cylinder's reach along unit axis <paramref name="n"/> (editor 0x111b4bc0).</summary>
    [UcNative("UStaticMesh::LineCheck cylinder support", Editor = 0x111b4bc0)]
    [Provenance(ProvenanceKind.Decoded, "editor 0x111b4bc0: 0 below |n|^2 0.001, H for |n.Z| >= 0.99999, else |n.Z| H + R (x^2 + y^2) / |n.XY|")]
    public static float CylinderReach(FVector n, float radius, float halfHeight)
    {
        float x2 = n.X * n.X;
        float y2 = n.Y * n.Y;
        if (n.Z * n.Z + x2 + y2 < K.MeshSweep.MinAxisSquared)
        {
            return 0f;
        }

        float az = SvmMath.Abs(n.Z);
        if (az >= K.MeshSweep.VerticalAxis)
        {
            return halfHeight;
        }

        float k = 1f / SvmMath.Sqrt(y2 + x2) * radius;
        return az * halfHeight + k * x2 + k * y2;
    }

    /// <summary>
    /// Sweeps the cylinder (radius <c>e.X</c>, half height <c>e.Z</c>) centred at <paramref name="s"/> along
    /// <paramref name="d"/> against the triangle. True with the raw entry time (in (0, <paramref name="limit"/>)) and
    /// the normal of the axis that set it; the caller applies the mesh pull-back.
    /// </summary>
    [UcNative("UStaticMesh::LineCheck box triangle test", Editor = 0x111b5d80)]
    [PortOf("eng:UStaticMesh::LineCheck", Spec = "spec/native/ANSWERS-fidelity.md#f1-static-mesh-sweeps")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x111b5d80 (axes), 0x111b5c30 (bounds axes), 0x111b5ae0 (one axis), 0x111b7590 (tree walk), 0x111b72b0 (setup)")]
    public static bool SweepTriangle(in TriPrim t, FVector s, FVector d, FVector e, float limit, out float time, out FVector normal)
    {
        time = 1f;
        normal = default;
        float r = e.X;
        float h = e.Z;
        float minT = 0f;
        float maxT = limit;
        bool hit = false;
        FVector hitN = default;

        // The bounds' faces, on the start's side of the bounds centre.
        FVector c = (t.Min + t.Max) * 0.5f;
        FVector sc = s - c;
        if (!Axis(sc.X > 0f ? AxisX : -AxisX, sc.X > 0f ? t.Max.X : -t.Min.X, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit)
            || !Axis(sc.Y > 0f ? AxisY : -AxisY, sc.Y > 0f ? t.Max.Y : -t.Min.Y, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit)
            || !Axis(sc.Z > 0f ? AxisZ : -AxisZ, sc.Z > 0f ? t.Max.Z : -t.Min.Z, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit))
        {
            return false;
        }

        // The plane, both ways. The walk's normal is (v2 - v1) x (v0 - v1), the same sense as TriPrim.N.
        FVector n = t.N;
        float w = n | t.V0;
        if (!Axis(n, w, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit) || !Axis(-n, -w, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit))
        {
            return false;
        }

        if (!Corner(t.V0, t.V1, t.V2, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit)
            || !Corner(t.V1, t.V2, t.V0, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit)
            || !Corner(t.V2, t.V0, t.V1, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit))
        {
            return false;
        }

        if (!hit)
        {
            return false;
        }

        time = minT;
        normal = hitN.SafeNormal();
        return true;
    }

    /// <summary>The axes of the edge <paramref name="a"/> to <paramref name="b"/> (third corner <paramref name="o"/>) and of the corner <paramref name="a"/>.</summary>
    private static bool Corner(FVector a, FVector b, FVector o, FVector s, FVector d, float r, float h, ref float minT, ref float maxT,
        ref FVector hitN, ref bool hit)
    {
        FVector u = Unit(b - a);
        FVector w = Unit(o - a);
        if (u.X * u.X + u.Y * u.Y > K.MeshSweep.MinEdgeXYSquared)
        {
            if (!EdgeAxis(Unit(u ^ AxisZ), a, w, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit)
                || !EdgeAxis(Unit(u ^ AxisX), a, w, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit)
                || !EdgeAxis(Unit(AxisY ^ u), a, w, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit))
            {
                return false;
            }
        }

        var toStart = new FVector(s.X - a.X, s.Y - a.Y, 0f);
        if (toStart.X * toStart.X + toStart.Y * toStart.Y > K.MeshSweep.MinEdgeXYSquared)
        {
            FVector v = Unit(toStart);
            if ((u | v) < K.MeshSweep.CornerOutside && (w | v) < K.MeshSweep.CornerOutside &&
                !Axis(v, v | a, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EdgeAxis(FVector n, FVector a, FVector w, FVector s, FVector d, float r, float h, ref float minT, ref float maxT,
        ref FVector hitN, ref bool hit)
    {
        if ((n | w) > 0f)
        {
            n = -n;
        }

        return Axis(n, n | a, s, d, r, h, ref minT, ref maxT, ref hitN, ref hit);
    }

    /// <summary>
    /// One axis (editor 0x111b5ae0): the plane <c>n . x = w</c> with the triangle behind it. Approaching, the entry
    /// time raises the minimum; leaving, the exit time lowers the maximum; parallel and clear of the cylinder's
    /// reach at both ends, the sweep misses. A start that touches the plane within the reach while approaching
    /// counts as entering at time 0. False once the interval is empty.
    /// </summary>
    private static bool Axis(FVector n, float w, FVector s, FVector d, float r, float h, ref float minT, ref float maxT,
        ref FVector hitN, ref bool hit)
    {
        float reach = CylinderReach(n, r, h);
        float d0 = (s.X * n.X + s.Y * n.Y + s.Z * n.Z) - w;
        float d1 = ((s.X + d.X) * n.X + (d.Y + s.Y) * n.Y + (d.Z + s.Z) * n.Z) - w;
        float gap = d0 - reach;
        if (d1 < d0 && -reach <= gap && gap < 0f)
        {
            gap = 0f;
        }

        float rate = d0 - d1;
        if (rate > K.MeshSweep.ParallelRate)
        {
            float t = gap / rate;
            if (minT < t)
            {
                minT = t;
                hitN = n;
                hit = true;
            }
        }
        else if (rate < -K.MeshSweep.ParallelRate)
        {
            float t = gap / rate;
            if (t < maxT)
            {
                maxT = t;
            }
        }
        else if (reach < d0 && reach < d1)
        {
            return false;
        }

        return !(maxT <= minT);
    }

    /// <summary>
    /// UStaticMesh::PointCheck against one triangle (FCylinderPointCheck, editor 0x111b6520; walk 0x111b6680; test
    /// 0x111b4d60). The check runs in the mesh's own frame: the point is taken into it, and the box
    /// <paramref name="e"/> becomes the axis-aligned bounds of the rotated box there, whose X and Z half sizes are the
    /// cylinder's radius and half height (a yawed mesh therefore sees a wider cylinder than a sweep does). The walk
    /// first drops the point when it lies outside a tree node's box grown by those bounds on each axis; the
    /// triangle then overlaps when the point is within the cylinder's reach of its plane and no edge axis (edge x
    /// Z, edge x X, Y x edge, pushed out by the reach) and no corner (outside the radius and beyond both edges)
    /// separates them. The test has no axis of its own along the mesh's X, Y and Z: only the node box covers
    /// them.
    /// </summary>
    [UcNative("UStaticMesh::PointCheck", Editor = 0x111b80c0)]
    [PortOf("eng:UStaticMesh::PointCheck", Spec = "spec/native/ANSWERS-fidelity.md#f1-static-mesh-sweeps")]
    [Provenance(ProvenanceKind.Decoded, "FCylinderPointCheck 0x111b6520 (local frame, local bounds of the extent), walk 0x111b6680 (node box grown by the bounds, plane reach), test 0x111b4d60 (edge and corner axes)")]
    [Provenance(ProvenanceKind.Estimated, "the node box: the original's nodes are quantized boxes (5 bits per side of the parent, 0x111722d0) around a node's triangle and its subtree; the converted tree differs, so the triangle's own bounds stand in for it (stricter by up to one node cell)")]
    public static bool OverlapTriangle(in TriPrim t, ColliderState c, FVector p, FVector e, out FVector normal)
    {
        normal = default;
        FCoords axes = FCoords.FromRotator(c.Rotation);
        FVector s = c.Scale;
        FVector ax = axes.XAxis;
        FVector ay = axes.YAxis;
        FVector az = axes.ZAxis;
        float ex = (SvmMath.Abs(ax.X) * e.X + SvmMath.Abs(ax.Y) * e.Y + SvmMath.Abs(ax.Z) * e.Z) / SvmMath.Abs(s.X);
        float ey = (SvmMath.Abs(ay.X) * e.X + SvmMath.Abs(ay.Y) * e.Y + SvmMath.Abs(ay.Z) * e.Z) / SvmMath.Abs(s.Y);
        float ez = (SvmMath.Abs(az.X) * e.X + SvmMath.Abs(az.Y) * e.Y + SvmMath.Abs(az.Z) * e.Z) / SvmMath.Abs(s.Z);
        FVector lp = ToLocal(p, c, ax, ay, az);
        FVector v0 = ToLocal(t.V0, c, ax, ay, az);
        FVector v1 = ToLocal(t.V1, c, ax, ay, az);
        FVector v2 = ToLocal(t.V2, c, ax, ay, az);
        if (!InBounds(lp.X, v0.X, v1.X, v2.X, ex) || !InBounds(lp.Y, v0.Y, v1.Y, v2.Y, ey) ||
            !InBounds(lp.Z, v0.Z, v1.Z, v2.Z, ez))
        {
            return false;
        }

        FVector n = ((v2 - v1) ^ (v0 - v1)).SafeNormal();
        float dist = SvmMath.Abs((n | lp) - (n | v0));
        float reach = CylinderReach(n, ex, ez);
        if (!(dist < reach))
        {
            return false;
        }

        if (!Inside(v0, v1, v2, lp, ex, ez) || !Inside(v1, v2, v0, lp, ex, ez) || !Inside(v2, v0, v1, lp, ex, ez))
        {
            return false;
        }

        // The normal in world space, facing the point.
        FVector wn = (ax * (n.X / s.X) + ay * (n.Y / s.Y) + az * (n.Z / s.Z)).SafeNormal();
        normal = ((wn | (p - t.V0)) < 0f) ? -wn : wn;
        return true;
    }

    /// <summary>
    /// True when the world-space cylinder the sweeps move (radius <paramref name="r"/>, half height
    /// <paramref name="h"/>) at <paramref name="p"/> overlaps the triangle on the sweep's axes. Not a native: a
    /// check of the shape the movement collides with, for tests and diagnostics.
    /// </summary>
    public static bool OverlapsSweptCylinder(in TriPrim t, FVector p, float r, float h)
    {
        FVector c = (t.Min + t.Max) * 0.5f;
        FVector half = (t.Max - t.Min) * 0.5f;
        if (!(SvmMath.Abs(p.X - c.X) < half.X + r) || !(SvmMath.Abs(p.Y - c.Y) < half.Y + r) || !(SvmMath.Abs(p.Z - c.Z) < half.Z + h))
        {
            return false;
        }

        float dist = SvmMath.Abs((t.N | p) - (t.N | t.V0));
        return dist < CylinderReach(t.N, r, h) && Inside(t.V0, t.V1, t.V2, p, r, h) && Inside(t.V1, t.V2, t.V0, p, r, h) &&
               Inside(t.V2, t.V0, t.V1, p, r, h);
    }

    /// <summary>One edge's axes and its first corner (0x111b4d60): false when they separate the point.</summary>
    private static bool Inside(FVector a, FVector b, FVector o, FVector p, float r, float h)
    {
        FVector u = Unit(b - a);
        FVector w = Unit(o - a);
        if (u.X * u.X + u.Y * u.Y > K.MeshSweep.MinEdgeXYSquared)
        {
            if (Separates(Unit(u ^ AxisZ), a, w, p, r, h) || Separates(Unit(u ^ AxisX), a, w, p, r, h) ||
                Separates(Unit(AxisY ^ u), a, w, p, r, h))
            {
                return false;
            }
        }

        float dx = p.X - a.X;
        float dy = p.Y - a.Y;
        return !(r * r < dx * dx + dy * dy && dx * u.X + dy * u.Y < K.MeshSweep.CornerOutside &&
                 dx * w.X + dy * w.Y < K.MeshSweep.CornerOutside);
    }

    private static bool Separates(FVector n, FVector a, FVector w, FVector p, float r, float h)
    {
        if ((n | w) > 0f)
        {
            n = -n;
        }

        return (n | p) - ((n | a) + CylinderReach(n, r, h)) > 0f;
    }

    /// <summary>The point within the node box grown by the extent on one axis (the walk's box test, 0x111b6680).</summary>
    private static bool InBounds(float p, float a, float b, float c, float e) =>
        p > SvmMath.Min(a, SvmMath.Min(b, c)) - e && p < SvmMath.Max(a, SvmMath.Max(b, c)) + e;

    private static FVector ToLocal(FVector world, ColliderState c, FVector ax, FVector ay, FVector az)
    {
        FVector d = world - c.Location;
        return new FVector((d | ax) / c.Scale.X + c.PrePivot.X, (d | ay) / c.Scale.Y + c.PrePivot.Y, (d | az) / c.Scale.Z + c.PrePivot.Z);
    }

    /// <summary>A unit vector without a length check (a zero vector gives NaN, which no axis test accepts).</summary>
    private static FVector Unit(FVector v)
    {
        float k = 1f / SvmMath.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return new FVector(v.X * k, v.Y * k, v.Z * k);
    }

    /// <summary>
    /// The zero-extent test of UStaticMesh::LineCheck against one collision triangle (the line walk editor
    /// 0x111b6c50): the segment must cross the triangle's plane (one end at least 0.001 in front and one 0.001
    /// behind), the crossing point lie inside the three edges, and 0 &lt;= t &lt; 1. Both faces are hit; the
    /// normal is the triangle's own (outward) plane normal whichever side the line came from.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UStaticMesh::LineCheck zero-extent walk editor 0x111b6c50: plane span 0.001, edge planes (strict there, inclusive here), no facing test (spec/native/ANSWERS-moveset.md W2)")]
    public static bool LineTriangle(in TriPrim t, FVector s, FVector d, float limit, out float time, out FVector normal)
    {
        time = 1f;
        normal = default;
        float w = t.N | t.V0;
        float ds = (t.N | s) - w;
        float de = (t.N | (s + d)) - w;
        if (!((ds <= -K.Bsp.PlaneSlop || de <= -K.Bsp.PlaneSlop) && (ds >= K.Bsp.PlaneSlop || de >= K.Bsp.PlaneSlop)))
        {
            return false;
        }

        float f = ds / (ds - de);
        FVector p = s + (d * f);
        float tt = ((p - s) | d) / (d | d);
        if (!(tt >= 0f && tt < 1f) || !(tt < limit))
        {
            return false;
        }

        if (!InsideEdge(t.N, t.V0, t.V1, p) || !InsideEdge(t.N, t.V1, t.V2, p) || !InsideEdge(t.N, t.V2, t.V0, p))
        {
            return false;
        }

        time = tt;
        normal = t.N;
        return true;
    }

    /// <summary>
    /// The point lies on the inner side of the edge a to b (in the triangle's plane). The native test is strict; a
    /// point exactly on an edge (a shared diagonal, in practice only in synthetic geometry) then misses both
    /// triangles depending on rounding, so here it counts as inside.
    /// </summary>
    private static bool InsideEdge(FVector n, FVector a, FVector b, FVector p)
    {
        FVector outward = ((b - a) ^ n).SafeNormal();
        return ((outward | p) - (outward | a)) <= 0f;
    }
}
