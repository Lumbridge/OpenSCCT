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
    // Per GE: the actor it is attached to (looked up once per binding) and its record at the pose last asked for.
    private readonly Actor?[] _owners;
    private readonly bool[] _ownerKnown;
    private readonly (FVector At, FRotator Rotation, GERecord? Record)[] _posed;
    private Func<string, Actor?>? _resolveActor;

    public GEIndex(IReadOnlyList<GERecord> ges)
    {
        ArgumentNullException.ThrowIfNull(ges);
        _ges = new GERecord[ges.Count];
        _min = new FVector[ges.Count];
        _max = new FVector[ges.Count];
        _owners = new Actor?[ges.Count];
        _ownerKnown = new bool[ges.Count];
        _posed = new (FVector, FRotator, GERecord?)[ges.Count];
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

    /// <summary>True when some GE is attached to an actor (and so can move).</summary>
    public bool HasMoving => _moving.Count != 0;

    /// <summary>
    /// OpenSCCT: ties GE <paramref name="index"/>, stored in world space, to <paramref name="owner"/> where it stands
    /// now. Its points become offsets along the actor's axes, so every later query finds it at the actor's pose.
    /// </summary>
    public void Attach(int index, string ownerId, Actor owner)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(owner);
        GERecord ge = _ges[index];
        if (ge.AttachedTo is not null) return;
        FCoords axes = FCoords.FromRotator(owner.Rotation);
        var points = new FVector[ge.Points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            FVector d = ge.Points[i] - owner.Location;
            points[i] = new FVector(d | axes.XAxis, d | axes.YAxis, d | axes.ZAxis);
        }

        _ges[index] = new GERecord(ge.Index, ge.Id, ge.Kind, points, ge.Mask, ge.Platform, ge.Source, ownerId);
        _owners[index] = owner;
        _ownerKnown[index] = true;
        _posed[index] = default;
        _moving.Add(index);
    }

    /// <summary>The stored record of GE <paramref name="index"/>: world points, or offsets from its actor when attached.</summary>
    public GERecord Stored(int index) => _ges[index];

    /// <summary>
    /// The attached GE whose two points, where its actor stands now, are nearest to <paramref name="a"/> and
    /// <paramref name="b"/> (in either order) with each within about <paramref name="radius"/>. Used on the machine
    /// that chose the GE, a tick or so after it did.
    /// </summary>
    public GERecord? MovingNear(FVector a, FVector b, float radius)
    {
        GERecord? best = null;
        float limit = radius * radius * 2;
        foreach (int index in _moving)
        {
            if (Resolve(index) is not { } ge) continue;
            float error = Math.Min((ge.P0 - a).SizeSquared() + (ge.P1 - b).SizeSquared(),
                (ge.P1 - a).SizeSquared() + (ge.P0 - b).SizeSquared());
            if (error >= limit) continue;
            limit = error; best = ge;
        }
        return best;
    }

    /// <summary>
    /// The attached GE that the segment <paramref name="a"/>-<paramref name="b"/> is a copy of, told by another
    /// machine whose copy of the carrying actor stands elsewhere: the same length (within 4 uu) and direction (within
    /// about 30 degrees, either way round) and no further than <paramref name="reach"/> away. With
    /// <paramref name="owner"/>, only that actor's GEs count and the one nearest to <paramref name="near"/> wins (a
    /// pawn placed on this machine's copy of the actor); without, the one that is the shortest move away.
    /// </summary>
    public GERecord? MovingMatch(FVector a, FVector b, Actor? owner, FVector near, float reach)
    {
        FVector along = b - a;
        float length = along.Size();
        if (length <= 0f) return null;
        FVector mid = (a + b) * 0.5f;
        GERecord? best = null;
        float bestScore = float.MaxValue;
        foreach (int index in _moving)
        {
            if (owner is not null && !ReferenceEquals(OwnerAt(index), owner)) continue;
            if (Resolve(index) is not { } ge) continue;
            FVector geAlong = ge.P1 - ge.P0;
            float geLength = geAlong.Size();
            if (Math.Abs(geLength - length) > 4f || Math.Abs(geAlong | along) < 0.85f * geLength * length) continue;
            float moved = ((ge.P0 + ge.P1) * 0.5f - mid).Size();
            if (moved > reach) continue;
            float score = owner is null ? moved : (ClosestPointOnSegment(ge.P0, ge.P1, near, out _) - near).Size();
            if (score >= bestScore) continue;
            bestScore = score; best = ge;
        }
        return best;
    }

    /// <summary>True when a GE that is not attached to an actor has its two points within <paramref name="tolerance"/> of <paramref name="a"/> and <paramref name="b"/> on every axis.</summary>
    public bool IsStaticGE(FVector a, FVector b, float tolerance)
    {
        for (int i = 0; i < _ges.Length; i++)
        {
            GERecord ge = _ges[i];
            if (ge.AttachedTo is null && ((Near(ge.P0, a, tolerance) && Near(ge.P1, b, tolerance)) || (Near(ge.P1, a, tolerance) && Near(ge.P0, b, tolerance))))
                return true;
        }
        return false;
    }

    private static bool Near(FVector p, FVector q, float tolerance) =>
        Math.Abs(p.X - q.X) <= tolerance && Math.Abs(p.Y - q.Y) <= tolerance && Math.Abs(p.Z - q.Z) <= tolerance;

    public GERecord this[int index] => Resolve(index) ?? _ges[index];

    public void BindActors(Func<string, Actor?> resolver)
    {
        _resolveActor = resolver;
        Array.Clear(_ownerKnown);
        Array.Clear(_owners);
    }

    public Actor? OwnerOf(GERecord ge) => ge.AttachedTo is { } owner ? _resolveActor?.Invoke(owner) : null;

    private Actor? OwnerAt(int index)
    {
        if (!_ownerKnown[index])
        {
            _owners[index] = _ges[index].AttachedTo is { } owner ? _resolveActor?.Invoke(owner) : null;
            _ownerKnown[index] = true;
        }

        return _owners[index];
    }

    /// <summary>GE <paramref name="index"/> in world space: itself, or for an attached GE its points where its actor stands now (null without the actor).</summary>
    private GERecord? Resolve(int index)
    {
        var ge = _ges[index];
        if (ge.AttachedTo is null) return ge;
        if (OwnerAt(index) is not { bDeleteMe: false } actor) return null;
        ref var posed = ref _posed[index];
        if (posed.Record is not null && posed.At == actor.Location && posed.Rotation == actor.Rotation) return posed.Record;
        var axes = FCoords.FromRotator(actor.Rotation);
        var points = new FVector[ge.Points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var p = ge.Points[i];
            points[i] = actor.Location + axes.XAxis * p.X + axes.YAxis * p.Y + axes.ZAxis * p.Z;
        }
        var world = new GERecord(index, ge.Id, ge.Kind, points, ge.Mask, ge.Platform, ge.Source, ge.AttachedTo);
        posed = (actor.Location, actor.Rotation, world);
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
