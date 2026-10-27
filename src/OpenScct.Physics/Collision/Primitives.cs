using System.Runtime.CompilerServices;
using OpenScct.Core;

namespace OpenScct.Physics.Collision;

[InlineArray(9)]
internal struct Vector9
{
    private FVector _element;
}

[InlineArray(9)]
internal struct Float9
{
    private float _element;
}

/// <summary>
/// A world-space collision triangle prepared for separating-axis tests against an axis-aligned box: its unit
/// normal, its bounds (the box face axes) and the nine edge-cross-box axes, each with the triangle's projection
/// interval. The Minkowski sum of the triangle and any axis-aligned box is the intersection of these slabs widened
/// by the box's support on each axis, so sweeping the box centre through them is exact.
/// </summary>
internal struct TriPrim
{
    public FVector V0;
    public FVector V1;
    public FVector V2;
    public FVector Min;
    public FVector Max;

    /// <summary>Unit front-face normal, (v1 - v0) x (v2 - v0) normalised.</summary>
    public FVector N;
    public float NLo;
    public float NHi;
    public Vector9 Axes;
    public Float9 Lo;
    public Float9 Hi;
    public int Collider;
    public int Material;

    /// <summary>Source TRIS index.</summary>
    public int Item;

    /// <summary>Zero area (after a transform): never collides.</summary>
    public bool Degenerate;

    /// <summary>Fills the derived data from the three corners. False for a degenerate (zero area) triangle.</summary>
    public bool Set(FVector v0, FVector v1, FVector v2)
    {
        V0 = v0;
        V1 = v1;
        V2 = v2;
        Min = new FVector(SvmMath.Min(v0.X, SvmMath.Min(v1.X, v2.X)), SvmMath.Min(v0.Y, SvmMath.Min(v1.Y, v2.Y)),
            SvmMath.Min(v0.Z, SvmMath.Min(v1.Z, v2.Z)));
        Max = new FVector(SvmMath.Max(v0.X, SvmMath.Max(v1.X, v2.X)), SvmMath.Max(v0.Y, SvmMath.Max(v1.Y, v2.Y)),
            SvmMath.Max(v0.Z, SvmMath.Max(v1.Z, v2.Z)));
        FVector e0 = v1 - v0;
        FVector e1 = v2 - v1;
        FVector e2 = v0 - v2;
        FVector n = e0 ^ (v2 - v0);
        float lenSq = n.SizeSquared();
        bool ok = lenSq > 1e-12f;
        Degenerate = !ok;
        N = ok ? n * (1f / SvmMath.Sqrt(lenSq)) : default;
        Project(N, out NLo, out NHi);
        SetCrossAxes(0, e0);
        SetCrossAxes(3, e1);
        SetCrossAxes(6, e2);
        return ok;
    }

    private void SetCrossAxes(int first, FVector e)
    {
        SetAxis(first, new FVector(0f, e.Z, -e.Y));
        SetAxis(first + 1, new FVector(-e.Z, 0f, e.X));
        SetAxis(first + 2, new FVector(e.Y, -e.X, 0f));
    }

    private void SetAxis(int k, FVector a)
    {
        float lenSq = a.SizeSquared();
        a = lenSq > 1e-12f ? a * (1f / SvmMath.Sqrt(lenSq)) : default;
        Axes[k] = a;
        Project(a, out Lo[k], out Hi[k]);
    }

    private readonly void Project(FVector a, out float lo, out float hi)
    {
        float p0 = a | V0;
        float p1 = a | V1;
        float p2 = a | V2;
        lo = SvmMath.Min(p0, SvmMath.Min(p1, p2));
        hi = SvmMath.Max(p0, SvmMath.Max(p1, p2));
    }
}

/// <summary>A hull plane: unit outward normal and <c>w = n . p</c> (inside where <c>n . p - w &lt;= 0</c>).</summary>
internal struct HullPlane
{
    public FVector N;
    public float W;

    public HullPlane(FVector n, float w)
    {
        N = n;
        W = w;
    }
}

/// <summary>A world-space convex hull: a range of planes plus the axis-aligned bounds used as bevel planes.</summary>
internal struct HullPrim
{
    public int PlaneStart;
    public int PlaneCount;
    public FVector Min;
    public FVector Max;
    public int Collider;
    public int Material;

    /// <summary>Source HULL index.</summary>
    public int Item;
}
