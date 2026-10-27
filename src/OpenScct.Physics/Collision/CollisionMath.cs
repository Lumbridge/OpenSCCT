using System;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

/// <summary>
/// Clips the segment <c>S + t D</c>, t in [0, limit], against a set of half-spaces <c>n . x &lt;= b</c>, each given
/// as its start distance <c>n . S - b</c> and its rate <c>n . D</c>. Keeps the latest entry (the hit time and
/// normal), the earliest exit, and the boundary nearest the start for the start-inside rule.
/// </summary>
internal struct ClipState
{
    public float TIn;
    public FVector NIn;
    public float TOut;
    public float MaxDist;
    public FVector NMax;
    public float TLimit;

    public void Reset(float limit)
    {
        TIn = float.NegativeInfinity;
        NIn = default;
        TOut = float.PositiveInfinity;
        MaxDist = float.NegativeInfinity;
        NMax = default;
        TLimit = limit;
    }

    /// <summary>Adds one half-space. False as soon as the segment provably misses the volume.</summary>
    public bool Add(FVector n, float dist, float denom)
    {
        if (dist > MaxDist)
        {
            MaxDist = dist;
            NMax = n;
        }

        if (denom < 0f)
        {
            float t = -dist / denom;
            if (t > TIn)
            {
                TIn = t;
                NIn = n;
            }
        }
        else if (denom > 0f)
        {
            float t = -dist / denom;
            if (t < TOut)
            {
                TOut = t;
            }
        }
        else if (dist > 0f)
        {
            return false;
        }

        return TIn <= TOut && TIn <= TLimit && TOut >= 0f;
    }

    /// <summary>
    /// Decides the hit once every half-space is in. Starting outside, the hit is the latest entry. Starting inside
    /// (or exactly on the boundary), the volume blocks only a move that goes deeper through its nearest face: the
    /// hit is then at time 0 with that face's normal; a move out of or along it is free.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 lets an actor that starts inside geometry move out of it; the exact native rule is not decoded")]
    public readonly bool Finish(FVector D, out float time, out FVector normal, out bool startPenetrating)
    {
        if (MaxDist > 0f)
        {
            time = TIn;
            normal = NIn;
            startPenetrating = false;
            return true;
        }

        if ((D | NMax) < 0f)
        {
            time = 0f;
            normal = NMax;
            startPenetrating = MaxDist < 0f;
            return true;
        }

        time = 1f;
        normal = default;
        startPenetrating = false;
        return false;
    }
}

/// <summary>Sweep and overlap kernels for axis-aligned boxes against triangles, hulls and actor cylinders.</summary>
internal static class CollisionMath
{
    /// <summary>Support radius of a box with half sizes <paramref name="e"/> along unit axis <paramref name="a"/>.</summary>
    public static float Support(FVector a, FVector e) => SvmMath.Abs(a.X) * e.X + SvmMath.Abs(a.Y) * e.Y + SvmMath.Abs(a.Z) * e.Z;

    private static bool Slab(ref ClipState st, FVector a, float lo, float hi, FVector s, FVector d, FVector e)
    {
        float c = a | s;
        float v = a | d;
        float r = Support(a, e);
        if (!st.Add(a, c - (hi + r), v))
        {
            return false;
        }

        return st.Add(-a, (lo - r) - c, -v);
    }

    private static bool AxisSlabs(ref ClipState st, FVector min, FVector max, FVector s, FVector d, FVector e)
    {
        return st.Add(new FVector(1f, 0f, 0f), s.X - (max.X + e.X), d.X)
            && st.Add(new FVector(-1f, 0f, 0f), (min.X - e.X) - s.X, -d.X)
            && st.Add(new FVector(0f, 1f, 0f), s.Y - (max.Y + e.Y), d.Y)
            && st.Add(new FVector(0f, -1f, 0f), (min.Y - e.Y) - s.Y, -d.Y)
            && st.Add(new FVector(0f, 0f, 1f), s.Z - (max.Z + e.Z), d.Z)
            && st.Add(new FVector(0f, 0f, -1f), (min.Z - e.Z) - s.Z, -d.Z);
    }

    /// <summary>
    /// Sweeps a box (half sizes <paramref name="e"/>, centre from <paramref name="s"/> along <paramref name="d"/>)
    /// against a triangle. A zero extent traces a line, which only hits the triangle's front face.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Exact separating-axis sweep; UE2 static mesh and BSP box checks may differ at edges (bevels)")]
    public static bool SweepTriangle(in TriPrim t, FVector s, FVector d, FVector e, bool zeroExtent, float limit,
        out float time, out FVector normal, out bool startPenetrating)
    {
        var st = default(ClipState);
        st.Reset(limit);
        time = 1f;
        normal = default;
        startPenetrating = false;
        // Bounds first: the cheapest slabs reject most candidates.
        if (!AxisSlabs(ref st, t.Min, t.Max, s, d, e) || !Slab(ref st, t.N, t.NLo, t.NHi, s, d, e))
        {
            return false;
        }

        for (int k = 0; k < 9; k++)
        {
            FVector a = t.Axes[k];
            if (a.IsZero())
            {
                continue;
            }

            if (!Slab(ref st, a, t.Lo[k], t.Hi[k], s, d, e))
            {
                return false;
            }
        }

        if (!st.Finish(d, out time, out normal, out startPenetrating))
        {
            return false;
        }

        if (zeroExtent && (d | t.N) >= 0f)
        {
            time = 1f;
            normal = default;
            startPenetrating = false;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sweeps a box against a convex hull: each face plane pushed out by the box's support on its normal, plus the
    /// hull's bounds widened by the box as bevel planes.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 convex and BSP box checks: planes expanded by the extent plus axis-aligned bevels")]
    public static bool SweepHull(ReadOnlySpan<HullPlane> planes, FVector min, FVector max, FVector s, FVector d, FVector e,
        float limit, out float time, out FVector normal, out bool startPenetrating)
    {
        var st = default(ClipState);
        st.Reset(limit);
        time = 1f;
        normal = default;
        startPenetrating = false;
        for (int i = 0; i < planes.Length; i++)
        {
            HullPlane p = planes[i];
            float r = Support(p.N, e);
            if (!st.Add(p.N, (p.N | s) - (p.W + r), p.N | d))
            {
                return false;
            }
        }

        if (!AxisSlabs(ref st, min, max, s, d, e))
        {
            return false;
        }

        return st.Finish(d, out time, out normal, out startPenetrating);
    }

    /// <summary>
    /// A line or box sweep against an actor cylinder (UPrimitive::LineCheck): the box widens the cylinder to radius
    /// R + Ex and half height H + Ez. Entering through a cap gives the cap normal, through the side the horizontal
    /// normal at the entry point. A sweep that starts inside the radius and between the caps is blocked only when it
    /// moves inward (rate below -0.1), at time 0. Returns the raw entry time; the caller applies the pull-back.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UPrimitive::LineCheck editor 0x110c2400 (spec/native/collision-primitives.md)")]
    public static bool CylinderCheck(FVector center, float radius, float halfHeight, FVector s, FVector d, FVector e, float limit,
        out float time, out FVector normal, out bool startInside)
    {
        time = 1f;
        normal = default;
        startInside = false;
        float r = radius + e.X;
        float hz = halfHeight + e.Z;
        FVector end = s + d;

        // Both ends beyond the same face of the widened box.
        float maxX = center.X + r;
        float minX = center.X - r;
        float maxY = center.Y + radius + e.Y;
        float minY = center.Y - radius - e.Y;
        float top = center.Z + hz;
        float bottom = center.Z - hz;
        if ((s.X > maxX && end.X > maxX) || (s.X < minX && end.X < minX) || (s.Y > maxY && end.Y > maxY) ||
            (s.Y < minY && end.Y < minY) || (s.Z > top && end.Z > top) || (s.Z < bottom && end.Z < bottom))
        {
            return false;
        }

        float tIn = 0f;
        float tOut = 1f;
        if (s.Z > top && end.Z < top)
        {
            tIn = SvmMath.Max(tIn, (top - s.Z) / d.Z);
            normal = new FVector(0f, 0f, 1f);
        }
        else if (s.Z < bottom && end.Z > bottom)
        {
            tIn = SvmMath.Max(tIn, (bottom - s.Z) / d.Z);
            normal = new FVector(0f, 0f, -1f);
        }

        if (end.Z > top && s.Z < top)
        {
            tOut = SvmMath.Min(tOut, (top - s.Z) / d.Z);
        }
        else if (end.Z < bottom && s.Z > bottom)
        {
            tOut = SvmMath.Min(tOut, (bottom - s.Z) / d.Z);
        }

        if (tIn >= tOut)
        {
            return false;
        }

        float kx = s.X - center.X;
        float ky = s.Y - center.Y;
        float a = d.X * d.X + d.Y * d.Y;
        float b = 2f * (kx * d.X + ky * d.Y);
        float c = kx * kx + ky * ky - r * r;
        if (c < K.Pullback.CylinderInsideC && s.Z > bottom && s.Z < top)
        {
            if (d.X * kx + d.Y * ky < K.Pullback.CylinderInward)
            {
                time = 0f;
                normal = new FVector(kx, ky, 0f).SafeNormal();
                startInside = true;
                return true;
            }

            return false;
        }

        float disc = b * b - 4f * a * c;
        if (disc < 0f)
        {
            return false;
        }

        if (a >= 1e-8f)
        {
            float sq = SvmMath.Sqrt(disc);
            float inv = 0.5f / a;
            tOut = SvmMath.Min(tOut, (sq - b) * inv);
            float t = -(sq + b) * inv;
            if (t > tIn)
            {
                tIn = t;
                FVector p = s + d * t;
                normal = new FVector(p.X - center.X, p.Y - center.Y, 0f).SafeNormal();
            }

            if (tIn >= tOut)
            {
                return false;
            }
        }
        else if (c > 0f)
        {
            return false;
        }

        if (tIn > limit)
        {
            return false;
        }

        time = tIn;
        return true;
    }

    /// <summary>Box against triangle overlap (strict: touching is not overlapping). Zero extent never overlaps a triangle.</summary>
    public static bool OverlapTriangle(in TriPrim t, FVector p, FVector e, out FVector normal)
    {
        normal = default;
        if (e.IsZero())
        {
            return false;
        }

        float best = float.NegativeInfinity;
        if (!OverlapSlab(t.N, t.NLo, t.NHi, p, e, ref best, ref normal)
            || !OverlapAxes(t.Min, t.Max, p, e, ref best, ref normal))
        {
            return false;
        }

        for (int k = 0; k < 9; k++)
        {
            FVector a = t.Axes[k];
            if (a.IsZero())
            {
                continue;
            }

            if (!OverlapSlab(a, t.Lo[k], t.Hi[k], p, e, ref best, ref normal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Box against convex hull overlap (the hull is solid: a box wholly inside overlaps).</summary>
    public static bool OverlapHull(ReadOnlySpan<HullPlane> planes, FVector min, FVector max, FVector p, FVector e, out FVector normal)
    {
        normal = default;
        float best = float.NegativeInfinity;
        for (int i = 0; i < planes.Length; i++)
        {
            HullPlane h = planes[i];
            float dist = (h.N | p) - (h.W + Support(h.N, e));
            if (dist >= 0f)
            {
                return false;
            }

            if (dist > best)
            {
                best = dist;
                normal = h.N;
            }
        }

        return OverlapAxes(min, max, p, e, ref best, ref normal);
    }

    /// <summary>Box against axis-aligned box overlap (strict).</summary>
    public static bool OverlapBox(FVector center, FVector half, FVector p, FVector e, out FVector normal)
    {
        normal = default;
        float best = float.NegativeInfinity;
        return OverlapAxes(center - half, center + half, p, e, ref best, ref normal);
    }

    private static bool OverlapSlab(FVector a, float lo, float hi, FVector p, FVector e, ref float best, ref FVector normal)
    {
        float c = a | p;
        float r = Support(a, e);
        float up = c - (hi + r);
        float down = (lo - r) - c;
        if (up >= 0f || down >= 0f)
        {
            return false;
        }

        if (up > best)
        {
            best = up;
            normal = a;
        }

        if (down > best)
        {
            best = down;
            normal = -a;
        }

        return true;
    }

    private static bool OverlapAxes(FVector min, FVector max, FVector p, FVector e, ref float best, ref FVector normal)
    {
        return OverlapAxis(p.X, min.X, max.X, e.X, new FVector(1f, 0f, 0f), ref best, ref normal)
            && OverlapAxis(p.Y, min.Y, max.Y, e.Y, new FVector(0f, 1f, 0f), ref best, ref normal)
            && OverlapAxis(p.Z, min.Z, max.Z, e.Z, new FVector(0f, 0f, 1f), ref best, ref normal);
    }

    private static bool OverlapAxis(float c, float lo, float hi, float r, FVector axis, ref float best, ref FVector normal)
    {
        float up = c - (hi + r);
        float down = (lo - r) - c;
        if (up >= 0f || down >= 0f)
        {
            return false;
        }

        if (up > best)
        {
            best = up;
            normal = axis;
        }

        if (down > best)
        {
            best = down;
            normal = -axis;
        }

        return true;
    }

    /// <summary>Ray against an axis-aligned box: the entry time within [0, limit], or false.</summary>
    public static bool SegmentBox(FVector min, FVector max, FVector s, FVector invD, bool zx, bool zy, bool zz, float limit, out float entry)
    {
        float tmin = 0f;
        float tmax = limit;
        entry = 0f;
        if (zx)
        {
            if (s.X < min.X || s.X > max.X)
            {
                return false;
            }
        }
        else
        {
            float t1 = (min.X - s.X) * invD.X;
            float t2 = (max.X - s.X) * invD.X;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tmin = SvmMath.Max(tmin, t1);
            tmax = SvmMath.Min(tmax, t2);
            if (tmin > tmax)
            {
                return false;
            }
        }

        if (zy)
        {
            if (s.Y < min.Y || s.Y > max.Y)
            {
                return false;
            }
        }
        else
        {
            float t1 = (min.Y - s.Y) * invD.Y;
            float t2 = (max.Y - s.Y) * invD.Y;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tmin = SvmMath.Max(tmin, t1);
            tmax = SvmMath.Min(tmax, t2);
            if (tmin > tmax)
            {
                return false;
            }
        }

        if (zz)
        {
            if (s.Z < min.Z || s.Z > max.Z)
            {
                return false;
            }
        }
        else
        {
            float t1 = (min.Z - s.Z) * invD.Z;
            float t2 = (max.Z - s.Z) * invD.Z;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tmin = SvmMath.Max(tmin, t1);
            tmax = SvmMath.Min(tmax, t2);
            if (tmin > tmax)
            {
                return false;
            }
        }

        entry = tmin;
        return true;
    }

    /// <summary>True when two boxes overlap or touch.</summary>
    public static bool BoxesTouch(FVector aMin, FVector aMax, FVector bMin, FVector bMax) =>
        aMin.X <= bMax.X && aMax.X >= bMin.X && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;

    public static FVector MinV(FVector a, FVector b) => new(SvmMath.Min(a.X, b.X), SvmMath.Min(a.Y, b.Y), SvmMath.Min(a.Z, b.Z));

    public static FVector MaxV(FVector a, FVector b) => new(SvmMath.Max(a.X, b.X), SvmMath.Max(a.Y, b.Y), SvmMath.Max(a.Z, b.Z));
}
