using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Physics.Collision;

namespace OpenScct.Bots;

/// <summary>How a <see cref="NavGrid"/> is sampled and which moves it allows.</summary>
public sealed record NavGridSettings
{
    /// <summary>Grid spacing in uu.</summary>
    public float Spacing { get; init; } = 32f;

    /// <summary>The walker's collision cylinder (the standing pawn: 35 x 87.5 uu).</summary>
    public float Radius { get; init; } = 35f;

    public float Height { get; init; } = 87.5f;

    /// <summary>Highest step up a walk edge may take (MAXSTEPHEIGHT).</summary>
    public float StepUp { get; init; } = 34f;

    /// <summary>Longest drop a drop edge may take (below the fall damage height of the walker's side).</summary>
    public float MaxDrop { get; init; } = 420f;

    /// <summary>Floors steeper than this normal Z are not walkable (MINFLOORZ).</summary>
    public float MinFloorNormalZ { get; init; } = 0.7f;
}

/// <summary>
/// A walk graph built from a map's collision at run time (nothing about a map is stored): floors found by tracing
/// down each column of a grid, a node where the walker's cylinder fits, walk edges to neighbouring nodes a swept
/// cylinder reaches with at most a step up, and one-way drop edges off ledges. Bots plan with A* over it and pull the
/// path straight where the collision allows.
/// </summary>
public sealed class NavGrid
{
    // What a walking pawn collides with, without other pawns and movers (doors open as they are reached).
    private const ETraceFlags WorldFlags = ETraceFlags.TRACE_Blocking | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry |
        ETraceFlags.TRACE_Volumes | ETraceFlags.TRACE_Others;

    // The floor search sweeps a small box, as the walking pawn's own floor checks do (a zero-extent line misses some
    // static mesh collision).
    private static readonly FVector FloorProbe = new(4f, 4f, 2f);

    private readonly ICollisionWorld _world;
    private readonly List<FVector> _nodes = new();
    private readonly List<int> _edgeStart = new();
    private readonly List<int> _edges = new();
    private readonly List<float> _edgeCost = new();
    private readonly Dictionary<long, List<int>> _columns = new();
    private readonly FVector _min;
    private readonly int _nx;
    private readonly int _ny;

    private NavGrid(ICollisionWorld world, NavGridSettings settings, FVector min, int nx, int ny)
    {
        _world = world;
        Settings = settings;
        _min = min;
        _nx = nx;
        _ny = ny;
    }

    public NavGridSettings Settings { get; }

    /// <summary>Walkable spots: the floor point under the walker's centre.</summary>
    public IReadOnlyList<FVector> Nodes => _nodes;

    public int EdgeCount => _edges.Count;

    /// <summary>The extent of the walker's cylinder as a box.</summary>
    public FVector Extent => new(Settings.Radius, Settings.Radius, Settings.Height);

    /// <summary>
    /// The box the grid tests with: the walker's cylinder from half a step above its feet to its head, so a stair
    /// riser or a kerb under a step high (which walking steps over) does not block it.
    /// </summary>
    public FVector Body => new(Settings.Radius, Settings.Radius, Settings.Height - Settings.StepUp * 0.25f);

    /// <summary>Height of the <see cref="Body"/> box's centre above the floor.</summary>
    public float BodyLift => Settings.Height + Settings.StepUp * 0.25f + 2f;

    /// <summary>Samples <paramref name="world"/> between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static NavGrid Build(ICollisionWorld world, FVector min, FVector max, NavGridSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        settings ??= new NavGridSettings();
        float s = settings.Spacing;
        int nx = Math.Max(1, (int)((max.X - min.X) / s) + 1);
        int ny = Math.Max(1, (int)((max.Y - min.Y) / s) + 1);
        var grid = new NavGrid(world, settings, min, nx, ny);
        grid.SampleFloors(min.Z, max.Z);
        grid.Connect();
        return grid;
    }

    private long Key(int ix, int iy) => ((long)ix << 32) | (uint)iy;

    private FVector ColumnXY(int ix, int iy) => new(_min.X + ix * Settings.Spacing, _min.Y + iy * Settings.Spacing, 0f);

    private void SampleFloors(float bottom, float top)
    {
        FVector body = Body;
        for (int ix = 0; ix < _nx; ix++)
        {
            for (int iy = 0; iy < _ny; iy++)
            {
                FVector c = ColumnXY(ix, iy);
                float z = top;
                List<int>? column = null;
                for (int guard = 0; guard < 32 && z > bottom; guard++)
                {
                    var start = new FVector(c.X, c.Y, z);
                    var end = new FVector(c.X, c.Y, bottom);
                    if (!_world.LineCheck(out FCheckResult hit, null, end, start, FloorProbe, WorldFlags))
                    {
                        break;
                    }

                    float floorZ = hit.Location.Z - FloorProbe.Z;
                    if (hit.Normal.Z >= Settings.MinFloorNormalZ)
                    {
                        var stand = new FVector(c.X, c.Y, floorZ + BodyLift);
                        if (!_world.PointCheck(out _, null, stand, body, WorldFlags))
                        {
                            column ??= new List<int>(2);
                            column.Add(_nodes.Count);
                            _nodes.Add(new FVector(c.X, c.Y, floorZ));
                        }
                    }

                    // Continue below this surface: step down out of the solid it belongs to.
                    z = floorZ - 2f;
                    for (int dig = 0; dig < 64 && z > bottom && _world.PointCheck(out _, null, new FVector(c.X, c.Y, z), new FVector(1f, 1f, 1f), WorldFlags); dig++)
                    {
                        z -= 8f;
                    }
                }

                if (column is not null)
                {
                    _columns[Key(ix, iy)] = column;
                }
            }
        }
    }

    private void Connect()
    {
        FVector body = Body;
        float s = Settings.Spacing;
        Span<(int dx, int dy)> dirs = stackalloc (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };
        for (int n = 0; n < _nodes.Count; n++)
        {
            _edgeStart.Add(_edges.Count);
            FVector a = _nodes[n];
            int ix = (int)MathF.Round((a.X - _min.X) / s);
            int iy = (int)MathF.Round((a.Y - _min.Y) / s);
            foreach ((int dx, int dy) in dirs)
            {
                // Up to three cells out: the farther ones only over a gap or an edge (no floor at this level in
                // between), where the walker's box has to clear the edge before it can drop or reach the far side.
                for (int k = 1; k <= MaxReach; k++)
                {
                    if (k > 1 && HasFloorNear(ix + dx * (k - 1), iy + dy * (k - 1), a.Z))
                    {
                        break;
                    }

                    if (!_columns.TryGetValue(Key(ix + dx * k, iy + dy * k), out List<int>? column))
                    {
                        continue;
                    }

                    foreach (int m in column)
                    {
                        TryEdge(a, m, k, dx != 0 && dy != 0, body);
                    }
                }
            }
        }

        _edgeStart.Add(_edges.Count);
    }

    private const int MaxReach = 3;

    private bool HasFloorNear(int ix, int iy, float z)
    {
        if (_columns.TryGetValue(Key(ix, iy), out List<int>? column))
        {
            foreach (int m in column)
            {
                if (MathF.Abs(_nodes[m].Z - z) <= Settings.StepUp)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void TryEdge(FVector a, int m, int k, bool diagonal, FVector body)
    {
        FVector b = _nodes[m];
        float dz = b.Z - a.Z;
        if (dz > Settings.StepUp || -dz > Settings.MaxDrop)
        {
            return;
        }

        // Across at the higher of the two floors (a step up is taken before the move, as walking does).
        float z = MathF.Max(a.Z, b.Z) + BodyLift;
        var from = new FVector(a.X, a.Y, z);
        var to = new FVector(b.X, b.Y, z);
        if (_world.LineCheck(out _, null, to, from, body, WorldFlags))
        {
            return;
        }

        // Down to the other floor: a drop must be clear all the way.
        var land = new FVector(b.X, b.Y, b.Z + BodyLift);
        if (dz < -Settings.StepUp && _world.LineCheck(out _, null, land, to, body, WorldFlags))
        {
            return;
        }

        float horizontal = k * Settings.Spacing * (diagonal ? 1.4142f : 1f);
        _edges.Add(m);
        _edgeCost.Add(horizontal + (dz < -Settings.StepUp ? -dz * 0.5f : 0f) + (k > 1 ? Settings.Spacing : 0f));
    }

    /// <summary>Weakly connected component of every node (drop edges counted both ways), for diagnostics.</summary>
    public int[] Components(out int count)
    {
        var parent = new int[_nodes.Count];
        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        for (int n = 0; n < _nodes.Count; n++)
        {
            for (int e = _edgeStart[n]; e < _edgeStart[n + 1]; e++)
            {
                int a = Find(n);
                int b = Find(_edges[e]);
                if (a != b)
                {
                    parent[a] = b;
                }
            }
        }

        var ids = new Dictionary<int, int>();
        var result = new int[_nodes.Count];
        for (int n = 0; n < _nodes.Count; n++)
        {
            int r = Find(n);
            if (!ids.TryGetValue(r, out int id))
            {
                id = ids.Count;
                ids[r] = id;
            }

            result[n] = id;
        }

        count = ids.Count;
        return result;
    }

    /// <summary>The node nearest <paramref name="p"/> (a floor within 200 uu below its feet, horizontally closest), or -1.</summary>
    public int NearestNode(FVector p, float footOffset)
    {
        float s = Settings.Spacing;
        int cx = (int)MathF.Round((p.X - _min.X) / s);
        int cy = (int)MathF.Round((p.Y - _min.Y) / s);
        float feet = p.Z - footOffset;
        int best = -1;
        float bestScore = float.MaxValue;
        for (int r = 0; r <= 5 && best < 0; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !_columns.TryGetValue(Key(cx + dx, cy + dy), out List<int>? column))
                    {
                        continue;
                    }

                    foreach (int n in column)
                    {
                        FVector q = _nodes[n];
                        float dz = feet - q.Z;
                        if (dz < -40f || dz > 200f)
                        {
                            continue;
                        }

                        float score = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y) + dz * dz * 0.25f;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = n;
                        }
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// A* from node <paramref name="from"/> to node <paramref name="to"/>; fills <paramref name="path"/> with the floor
    /// points (start excluded). Nodes in <paramref name="avoid"/> (where a walker got stuck) are not entered. False
    /// when the target cannot be reached.
    /// </summary>
    public bool FindPath(int from, int to, List<FVector> path, IReadOnlySet<int>? avoid = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        if (from < 0 || to < 0)
        {
            return false;
        }

        int count = _nodes.Count;
        var g = new float[count];
        var came = new int[count];
        var closed = new bool[count];
        Array.Fill(g, float.MaxValue);
        Array.Fill(came, -1);
        var open = new PriorityQueue<int, float>();
        g[from] = 0f;
        open.Enqueue(from, Heuristic(from, to));
        while (open.TryDequeue(out int n, out _))
        {
            if (closed[n])
            {
                continue;
            }

            if (n == to)
            {
                break;
            }

            closed[n] = true;
            for (int e = _edgeStart[n]; e < _edgeStart[n + 1]; e++)
            {
                int m = _edges[e];
                if (avoid is not null && m != to && avoid.Contains(m))
                {
                    continue;
                }

                float cost = g[n] + _edgeCost[e];
                if (cost < g[m])
                {
                    g[m] = cost;
                    came[m] = n;
                    open.Enqueue(m, cost + Heuristic(m, to));
                }
            }
        }

        if (came[to] < 0 && to != from)
        {
            return false;
        }

        for (int n = to; n != from; n = came[n])
        {
            path.Add(_nodes[n]);
        }

        path.Reverse();
        return true;
    }

    private float Heuristic(int a, int b)
    {
        FVector d = _nodes[b] - _nodes[a];
        return MathF.Sqrt(d.X * d.X + d.Y * d.Y);
    }

    /// <summary>
    /// Pulls a path straight: from each kept point, the farthest later point on the same floor (within a step) that a
    /// swept cylinder reaches is kept next.
    /// </summary>
    public void Smooth(FVector start, List<FVector> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 3)
        {
            return;
        }

        var kept = new List<FVector>(path.Count);
        FVector body = Body;
        FVector at = start;
        int i = 0;
        while (i < path.Count)
        {
            int best = i;
            for (int j = path.Count - 1; j > i; j--)
            {
                if (MathF.Abs(path[j].Z - at.Z) > Settings.StepUp * 0.5f || !SameLevel(path, i, j))
                {
                    continue;
                }

                var from = new FVector(at.X, at.Y, MathF.Max(at.Z, path[j].Z) + BodyLift);
                var to = new FVector(path[j].X, path[j].Y, from.Z);
                if (!_world.LineCheck(out _, null, to, from, body, WorldFlags) && GroundAlong(from, to, at.Z))
                {
                    best = j;
                    break;
                }
            }

            kept.Add(path[best]);
            at = path[best];
            i = best + 1;
        }

        path.Clear();
        path.AddRange(kept);
    }

    /// <summary>
    /// Whether a walker standing on <paramref name="fromFloor"/> can walk straight to <paramref name="toFloor"/>: the
    /// swept body clears the geometry and there is ground all along. Points more than half a step apart in height
    /// (stairs, drops) count as clear: the graph's own edge decides those.
    /// </summary>
    public bool Clear(FVector fromFloor, FVector toFloor)
    {
        if (MathF.Abs(toFloor.Z - fromFloor.Z) > Settings.StepUp * 0.5f)
        {
            return true;
        }

        var from = new FVector(fromFloor.X, fromFloor.Y, MathF.Max(fromFloor.Z, toFloor.Z) + BodyLift);
        var to = new FVector(toFloor.X, toFloor.Y, from.Z);
        return !_world.LineCheck(out _, null, to, from, Body, WorldFlags) && GroundAlong(from, to, fromFloor.Z);
    }

    private bool SameLevel(List<FVector> path, int i, int j)
    {
        float z = path[i].Z;
        for (int k = i; k <= j; k++)
        {
            if (MathF.Abs(path[k].Z - z) > Settings.StepUp * 0.5f)
            {
                return false;
            }
        }

        return true;
    }

    // Ground under the line every half spacing (no shortcut over a hole).
    private bool GroundAlong(FVector from, FVector to, float floorZ)
    {
        FVector d = to - from;
        float len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        int steps = Math.Max(1, (int)(len / (Settings.Spacing * 0.5f)));
        for (int k = 1; k < steps; k++)
        {
            FVector p = from + d * (k / (float)steps);
            var top = new FVector(p.X, p.Y, floorZ + 8f);
            var bottom = new FVector(p.X, p.Y, floorZ - Settings.StepUp - 8f);
            if (!_world.LineCheck(out _, null, bottom, top, default, WorldFlags))
            {
                return false;
            }
        }

        return true;
    }
}
