using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
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
    private readonly List<int> _moving = [];
    private readonly Dictionary<int, (FVector At, FRotator Rotation, GERecord Record)> _movingCache = [];
    private Func<string, Actor?>? _resolveActor;

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
            if (ge.AttachedTo is not null) _moving.Add(i);
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

    public bool HasMovingLedges => _moving.Count != 0;

    /// <summary>Match an existing traversal payload to a nearby authored carrier, allowing for network phase delay.</summary>
    public GERecord? MovingLedgeNear(FVector a, FVector b, float radius)
    {
        GERecord? best = null;
        float limit = radius * radius * 2;
        foreach (int index in _moving)
        {
            if (Resolve(index) is not { Kind: GEKind.LG } ge) continue;
            float error = Math.Min((ge.P0 - a).SizeSquared() + (ge.P1 - b).SizeSquared(),
                (ge.P1 - a).SizeSquared() + (ge.P0 - b).SizeSquared());
            if (error >= limit) continue;
            limit = error; best = ge;
        }
        return best;
    }

    public GERecord this[int index] => Resolve(index) ?? _ges[index];

    public void BindActors(Func<string, Actor?> resolver) => _resolveActor = resolver;

    public Actor? OwnerOf(GERecord ge) => ge.AttachedTo is { } owner ? _resolveActor?.Invoke(owner) : null;

    private GERecord? Resolve(int index)
    {
        var ge = _ges[index];
        if (ge.AttachedTo is null) return ge;
        if (OwnerOf(ge) is not { bDeleteMe: false } actor) return null;
        if (_movingCache.TryGetValue(index, out var cached) && cached.At == actor.Location && cached.Rotation == actor.Rotation) return cached.Record;
        var axes = FCoords.FromRotator(actor.Rotation);
        var points = new FVector[ge.Points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var p = ge.Points[i];
            points[i] = actor.Location + axes.XAxis * p.X + axes.YAxis * p.Y + axes.ZAxis * p.Z;
        }
        var world = new GERecord(index, ge.Id, ge.Kind, points, ge.Mask, ge.Platform, ge.Source, ge.AttachedTo);
        _movingCache[index] = (actor.Location, actor.Rotation, world);
        return world;
    }

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
                    if (_ges[_order[i]].AttachedTo is null) Test(_order[i], center, radius, kindMask, activationMask, results);
                }

                continue;
            }

            _stack[sp++] = node.Start + 1;
            _stack[sp++] = node.Start;
        }

        foreach (int index in _moving) Test(index, center, radius, kindMask, activationMask, results);
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
        GERecord? ge = Resolve(i);
        if (ge is null) return;
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
