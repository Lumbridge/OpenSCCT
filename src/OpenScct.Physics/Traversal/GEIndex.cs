using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Physics.Collision;

namespace OpenScct.Physics.Traversal;

/// <summary>A GE found by a query: the record, its distance from the query centre and the closest point on it.</summary>
public readonly record struct GEHit(int Index, GERecord GE, float Distance, FVector ClosestPoint, float Alpha);

/// <summary>
/// Spatial index over a map's GEs (traversal.json). A GE is the segment from its first to its second point
/// (bottom to top for PIPE and LADDER, whose third point is the side point). Queries take a kind mask (GEKind
/// bits) and the level's activation mask: a GE takes part only while its ID byte is 0 or shares a bit with the
/// mask. Results come in GE list order, so they do not depend on the index's internal layout.
/// </summary>
public sealed class GEIndex
{
    private static readonly Comparison<GEHit> s_byIndex = (a, b) => a.Index.CompareTo(b.Index);

    private readonly GERecord[] _ges;
    private readonly BvhNode[] _nodes;
    private readonly int[] _order;
    private readonly FVector[] _min;
    private readonly FVector[] _max;
    private readonly int[] _stack = new int[128];

    public GEIndex(IReadOnlyList<GERecord> ges)
    {
        ArgumentNullException.ThrowIfNull(ges);
        _ges = new GERecord[ges.Count];
        _min = new FVector[ges.Count];
        _max = new FVector[ges.Count];
        for (int i = 0; i < ges.Count; i++)
        {
            GERecord ge = ges[i];
            _ges[i] = ge;
            _min[i] = CollisionMath.MinV(ge.P0, ge.P1);
            _max[i] = CollisionMath.MaxV(ge.P0, ge.P1);
        }

        _nodes = Bvh.Build(_min, _max, out _order);
    }

    public GEIndex(TraversalDoc doc)
        : this((doc ?? throw new ArgumentNullException(nameof(doc))).Ges)
    {
    }

    public int Count => _ges.Length;

    public GERecord this[int index] => _ges[index];

    /// <summary>
    /// GEs within <paramref name="radius"/> of <paramref name="center"/>, of the kinds in
    /// <paramref name="kindMask"/>, active under <paramref name="activationMask"/>. Fills
    /// <paramref name="results"/> (cleared first) in GE list order and returns the count.
    /// </summary>
    public int QuerySphere(FVector center, float radius, int kindMask, byte activationMask, List<GEHit> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        results.Clear();
        if (_ges.Length == 0)
        {
            return 0;
        }

        var r = new FVector(radius, radius, radius);
        int sp = 0;
        _stack[sp++] = 0;
        while (sp > 0)
        {
            ref BvhNode node = ref _nodes[_stack[--sp]];
            if (!CollisionMath.BoxesTouch(node.Min - r, node.Max + r, center, center))
            {
                continue;
            }

            if (node.IsLeaf)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    Test(_order[i], center, radius, kindMask, activationMask, results);
                }

                continue;
            }

            _stack[sp++] = node.Start + 1;
            _stack[sp++] = node.Start;
        }

        results.Sort(s_byIndex);
        return results.Count;
    }

    /// <summary>The same query by testing every GE (tests compare the two).</summary>
    internal int QuerySphereBruteForce(FVector center, float radius, int kindMask, byte activationMask, List<GEHit> results)
    {
        results.Clear();
        for (int i = 0; i < _ges.Length; i++)
        {
            Test(i, center, radius, kindMask, activationMask, results);
        }

        return results.Count;
    }

    private void Test(int i, FVector center, float radius, int kindMask, byte activationMask, List<GEHit> results)
    {
        GERecord ge = _ges[i];
        if (((int)ge.Kind & kindMask) == 0 || !ge.IsActive(activationMask))
        {
            return;
        }

        FVector p = ClosestPointOnSegment(ge.P0, ge.P1, center, out float alpha);
        float dist = (p - center).Size();
        if (dist <= radius)
        {
            results.Add(new GEHit(i, ge, dist, p, alpha));
        }
    }

    /// <summary>Closest point to <paramref name="p"/> on segment a-b; <paramref name="alpha"/> is its position (0 at a, 1 at b).</summary>
    public static FVector ClosestPointOnSegment(FVector a, FVector b, FVector p, out float alpha)
    {
        FVector ab = b - a;
        float lenSq = ab.SizeSquared();
        if (lenSq <= 0f)
        {
            alpha = 0f;
            return a;
        }

        alpha = SvmMath.Clamp(((p - a) | ab) / lenSq, 0f, 1f);
        return a + ab * alpha;
    }
}
