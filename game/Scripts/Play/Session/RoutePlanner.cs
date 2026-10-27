using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;

namespace OpenScctGame.Play.Session;

/// <summary>
/// Finds a walking route through a map's collision for scripted runs (smoke tests, demos): a breadth-first search
/// over a grid of floor spots reachable by a pawn's cylinder, stepping up at most a step, dropping down at most
/// <see cref="MaxDrop"/>, then the route pulled straight where the cylinder can sweep. A test tool, not part of the
/// game: the game has no navigation in this mode.
/// </summary>
public sealed class RoutePlanner
{
    /// <summary>The walking pre-probe and floor masks the walking physics uses (docs/PHYSICS.md, trace flags).</summary>
    private const ETraceFlags MoveFlags = (ETraceFlags)0xde;

    private readonly ICollisionWorld _collision;
    private readonly Actor _source;
    private readonly FVector _extent;

    public RoutePlanner(ICollisionWorld collision, Actor source, float radius, float halfHeight)
    {
        _collision = collision ?? throw new ArgumentNullException(nameof(collision));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _extent = new FVector(radius, radius, halfHeight);
    }

    /// <summary>Grid spacing in uu.</summary>
    public float Step { get; init; } = 24f;

    /// <summary>Highest step climbed without a jump.</summary>
    public float MaxStepUp { get; init; } = 30f;

    /// <summary>Largest drop taken (below the spy's fall damage height).</summary>
    public float MaxDrop { get; init; } = 280f;

    /// <summary>Search budget in grid spots.</summary>
    public int MaxNodes { get; init; } = 60000;

    /// <summary>
    /// A route from <paramref name="start"/> (the pawn's location) to within <paramref name="reach"/> of
    /// <paramref name="goal"/> (horizontally, and within <paramref name="reachZ"/> vertically), as pawn locations;
    /// null when none is found within the budget.
    /// </summary>
    public List<FVector>? Find(FVector start, FVector goal, float reach, float reachZ = 120f)
    {
        if (!Floor(start, out FVector s))
        {
            return null;
        }

        var nodes = new List<FVector> { s };
        var parent = new List<int> { -1 };
        var seen = new HashSet<(int, int, int)> { Key(s) };
        var queue = new Queue<int>();
        queue.Enqueue(0);
        int found = -1;
        Span<(int X, int Y)> dirs = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];
        while (queue.Count > 0 && nodes.Count < MaxNodes)
        {
            int i = queue.Dequeue();
            FVector at = nodes[i];
            FVector d = goal - at;
            if (d.X * d.X + d.Y * d.Y <= reach * reach && MathF.Abs(d.Z) <= reachZ)
            {
                found = i;
                break;
            }

            foreach ((int dx, int dy) in dirs)
            {
                var next = new FVector(at.X + dx * Step, at.Y + dy * Step, at.Z);
                if (!TryMove(at, next, out FVector landed))
                {
                    continue;
                }

                if (!seen.Add(Key(landed)))
                {
                    continue;
                }

                nodes.Add(landed);
                parent.Add(i);
                queue.Enqueue(nodes.Count - 1);
            }
        }

        Explored = nodes.Count;
        LastNodes = nodes;
        if (found < 0)
        {
            return null;
        }

        var path = new List<FVector>();
        for (int i = found; i >= 0; i = parent[i])
        {
            path.Add(nodes[i]);
        }

        path.Reverse();
        return Simplify(path);
    }

    /// <summary>The number of grid spots the last search could reach (for reports).</summary>
    public int Explored { get; private set; }

    /// <summary>The grid spots of the last search, in visit order.</summary>
    public IReadOnlyList<FVector> LastNodes { get; private set; } = [];

    /// <summary>One grid move: sweep across at step height, then find the floor below within the drop limit.</summary>
    private bool TryMove(FVector from, FVector to, out FVector landed)
    {
        landed = default;
        var lift = new FVector(0f, 0f, 2f);
        if (_collision.LineCheck(out _, _source, to + lift, from + lift, _extent, MoveFlags))
        {
            lift = new FVector(0f, 0f, MaxStepUp);
            if (_collision.LineCheck(out _, _source, to + lift, from + lift, _extent, MoveFlags))
            {
                return false;
            }
        }

        return Floor(to + lift, out landed) && from.Z - landed.Z <= MaxDrop && landed.Z - from.Z <= MaxStepUp + 1f;
    }

    /// <summary>The pawn location standing on the walkable floor below <paramref name="at"/>.</summary>
    private bool Floor(FVector at, out FVector standing)
    {
        FVector down = at - new FVector(0f, 0f, MaxDrop + MaxStepUp + 4f);
        if (!_collision.LineCheck(out FCheckResult hit, _source, down, at, _extent, MoveFlags) || hit.Normal.Z < 0.7f)
        {
            standing = default;
            return false;
        }

        standing = hit.Location + new FVector(0f, 0f, 1.5f);
        return true;
    }

    private (int, int, int) Key(FVector p) =>
        ((int)MathF.Round(p.X / Step), (int)MathF.Round(p.Y / Step), (int)MathF.Round(p.Z / 16f));

    /// <summary>Drops waypoints the cylinder can skip: a straight sweep at step height over spots of one floor height.</summary>
    private List<FVector> Simplify(List<FVector> path)
    {
        if (path.Count <= 2)
        {
            return path;
        }

        var result = new List<FVector> { path[0] };
        int anchor = 0;
        int i = 1;
        while (i < path.Count)
        {
            if (CanSkip(path, anchor, i))
            {
                i++;
                continue;
            }

            anchor = i - 1 > anchor ? i - 1 : i;
            result.Add(path[anchor]);
            i = anchor + 1;
        }

        if (result[^1] != path[^1])
        {
            result.Add(path[^1]);
        }

        return result;
    }

    private bool CanSkip(List<FVector> path, int from, int to)
    {
        float lo = path[from].Z;
        float hi = lo;
        for (int k = from + 1; k <= to; k++)
        {
            lo = MathF.Min(lo, path[k].Z);
            hi = MathF.Max(hi, path[k].Z);
        }

        if (hi - lo >= 2f)
        {
            return false;
        }

        var lift = new FVector(0f, 0f, MaxStepUp);
        return !_collision.LineCheck(out _, _source, path[to] + lift, path[from] + lift, _extent, MoveFlags);
    }
}
