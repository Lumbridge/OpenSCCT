using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Core;

namespace OpenScct.Game.AI;

/// <summary>How an edge of the navigation graph is travelled.</summary>
public enum NavEdgeKind : byte
{
    /// <summary>Walk (or run) standing.</summary>
    Walk = 0,

    /// <summary>Walk crouched: the gap only fits a crouched pawn (vents, crawl spaces).</summary>
    Crouch = 1,

    /// <summary>Walk off a ledge and fall (one way).</summary>
    Drop = 2,

    /// <summary>Jump at a wall, catch its ledge (LG) and pull up.</summary>
    LedgeClimb = 3,

    /// <summary>Walk into a vertical pipe (PIPE), climb it and step off at the top.</summary>
    PipeClimb = 4,

    /// <summary>Jump at a zip line (ZL) under its high end and ride it down.</summary>
    ZipLine = 5,

    /// <summary>Walk into a ladder (LADDER) at its foot, climb it and step off at the top (both sides).</summary>
    LadderUp = 6,

    /// <summary>Walk onto a ladder (LADDER) from its top and climb down it (both sides).</summary>
    LadderDown = 7,

    /// <summary>Jump at a horizontal pipe (HOH) from under it, go hand over hand to its far end and drop.</summary>
    HandOverHand = 8,

    /// <summary>Run into a climbable fence under its top (FENCE), climb it, cross the top and climb down the far side.</summary>
    FenceClimb = 9,

    /// <summary>A collision-checked jump between supported floor points.</summary>
    Jump = 10,

    /// <summary>Jump at a low rail and vault to the floor beyond it (spies).</summary>
    BarrierHop = 11,

    /// <summary>Climb a vertical pipe, transfer to a bar and drop onto the destination floor.</summary>
    PipeToBar = 12,

    /// <summary>Catch a bar, continue onto a zip line and ride to the destination floor.</summary>
    BarToZip = 13,

    /// <summary>Climb a pipe, cross a bar and continue onto a zip line without touching floor.</summary>
    PipeToBarToZip = 14,
}

/// <summary>A node: a floor point where the walker fits, standing or only crouched.</summary>
public readonly record struct NavNode(FVector Floor, bool Standing);

/// <summary>
/// A traversal link: where the move starts (a floor point and the yaw to face), where it ends, and the GE it uses.
/// Chains also store the following GEs, the bar exit at hand height (Transfer), and its travel heading.
/// </summary>
public readonly record struct NavLink(NavEdgeKind Kind, int From, int To, FVector Start, int Yaw, FVector End, int GE, float Height,
    int NextGE = -1, int ExitGE = -1, FVector Transfer = default, int TravelYaw = 0);

/// <summary>One step of a path: the node reached and the edge taken to it (Link -1 for plain moves).</summary>
public readonly record struct NavStep(int Node, NavEdgeKind Kind, int Link, FVector Point);

/// <summary>What a pawn may use when the graph is built (one graph per side).</summary>
public sealed record NavProfile
{
    /// <summary>Spies climb ledges and pipes and ride zip lines; mercs do not.</summary>
    public bool Traversal { get; init; } = true;

    public float Radius { get; init; } = AiK.PawnRadius;

    public float StandingHeight { get; init; } = AiK.StandingHeight;

    public float CrouchHeight { get; init; } = AiK.SpyCrouchHeight;

    /// <summary>Highest step (MAXSTEPHEIGHT) and steepest floor (MINFLOORZ).</summary>
    public float StepUp { get; init; } = OpenScct.Physics.K.MAXSTEPHEIGHT;

    public float MinFloorNormalZ { get; init; } = OpenScct.Physics.K.MINFLOORZ;

    /// <summary>Longest planned drop (a spy takes up to 700 uu, a few points of fall damage; a merc stays under its own).</summary>
    public float MaxDrop { get; init; } = AiK.SpyMaxDrop;

    /// <summary>Drops deeper than this cost extra (the side's fall damage height).</summary>
    public float SafeDrop { get; init; } = AiK.SpyFallDamageHeight;

    public float Spacing { get; init; } = AiK.NavSpacing;

    /// <summary>Jump impulse and running speed used to validate a conservative airborne arc.</summary>
    public float JumpZ { get; init; } = 610f;

    public float JumpSpeed { get; init; } = 385f;

    public static NavProfile Spy { get; } = new();

    public static NavProfile Merc { get; } = new()
    {
        Traversal = false,
        CrouchHeight = AiK.MercCrouchHeight,
        MaxDrop = AiK.MercFallDamageHeight - 50f,
        SafeDrop = AiK.MercFallDamageHeight,
        JumpZ = 495f,
    };

    /// <summary>A short key of the profile's values for cache file names.</summary>
    public string Key => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{(Traversal ? "t" : "w")}{Radius:F0}-{StandingHeight:F1}-{CrouchHeight:F0}-{StepUp:F0}-{MaxDrop:F0}-{Spacing:F0}-j{JumpZ:F0}-{JumpSpeed:F0}");
}

/// <summary>
/// A navigation graph built from a map's collision and GEs (<see cref="NavBuilder"/>): floor nodes on a grid, walk and
/// crouch edges both ways, drops one way, and the traversal links of the GEs a spy can use. A* plans over it with a
/// cost that can weigh light (the stealth of a route) and threats, and leave out links that failed.
/// </summary>
public sealed class NavGraph
{
    /// <summary>Format of <see cref="Write"/>; a cache with another version is rebuilt.</summary>
    public const int FormatVersion = 15;

    private readonly NavNode[] _nodes;
    private readonly int[] _edgeStart;
    private readonly int[] _edgeTo;
    private readonly NavEdgeKind[] _edgeKind;
    private readonly float[] _edgeCost;
    private readonly int[] _edgeLink;
    private readonly NavLink[] _links;
    private readonly Dictionary<(int X, int Y), int[]> _columns = new();
    private readonly float[] _light;

    // A* scratch, reused between plans (one planner at a time per graph: plans run on the host's thread).
    private float[]? _g;
    private int[]? _came;
    private int[]? _cameEdge;
    private int[]? _stamp;
    private int _search;
    private readonly PriorityQueue<int, float> _open = new();
    // Separate from A*: its penalty callback may ask for a reachability field while a path is being planned.
    private readonly PriorityQueue<int, float> _costOpen = new();

    internal NavGraph(NavProfile profile, FVector origin, NavNode[] nodes, int[] edgeStart, int[] edgeTo, NavEdgeKind[] edgeKind,
        float[] edgeCost, int[] edgeLink, NavLink[] links)
    {
        Profile = profile;
        Origin = origin;
        _nodes = nodes;
        _edgeStart = edgeStart;
        _edgeTo = edgeTo;
        _edgeKind = edgeKind;
        _edgeCost = edgeCost;
        _edgeLink = edgeLink;
        _links = links;
        _light = new float[nodes.Length];
        Array.Fill(_light, -1f);
        var lists = new Dictionary<(int X, int Y), List<int>>();
        for (int i = 0; i < nodes.Length; i++)
        {
            var key = Cell(nodes[i].Floor);
            if (!lists.TryGetValue(key, out List<int>? list))
            {
                list = new List<int>(2);
                lists[key] = list;
            }

            list.Add(i);
        }

        foreach (var (cell, list) in lists)
        {
            _columns[cell] = list.ToArray();
        }
    }

    public NavProfile Profile { get; }

    /// <summary>An empty graph for read-only perception contexts that do not navigate or build map navigation.</summary>
    public static NavGraph Empty(NavProfile profile) => new(profile ?? throw new ArgumentNullException(nameof(profile)),
        default, [], [0], [], [], [], [], []);

    /// <summary>The grid origin (the sampled box's minimum corner).</summary>
    public FVector Origin { get; }

    public int NodeCount => _nodes.Length;

    public int EdgeCount => _edgeTo.Length;

    public IReadOnlyList<NavLink> Links => _links;

    public NavNode Node(int i) => _nodes[i];

    /// <summary>The edges of node <paramref name="n"/>: (to, kind, cost, link).</summary>
    public IEnumerable<(int To, NavEdgeKind Kind, float Cost, int Link)> EdgesOf(int n)
    {
        for (int e = _edgeStart[n]; e < _edgeStart[n + 1]; e++)
        {
            yield return (_edgeTo[e], _edgeKind[e], _edgeCost[e], _edgeLink[e]);
        }
    }

    /// <summary>Number of links of each kind.</summary>
    public int CountLinks(NavEdgeKind kind)
    {
        int n = 0;
        foreach (NavLink l in _links)
        {
            if (l.Kind == kind)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Number of edges of each kind.</summary>
    public int CountEdges(NavEdgeKind kind)
    {
        int n = 0;
        foreach (NavEdgeKind k in _edgeKind)
        {
            if (k == kind)
            {
                n++;
            }
        }

        return n;
    }

    // ---- light -----------------------------------------------------------------------------------------------

    /// <summary>The static light level of every node (for routes that keep to the dark); -1 until set.</summary>
    public float LightOf(int node) => _light[node];

    public bool HasLight { get; private set; }

    /// <summary>Reads each node's light level (at a crouched spy's centre) from <paramref name="field"/>.</summary>
    public void ApplyLight(AiLightField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!field.HasData)
        {
            return;
        }

        for (int i = 0; i < _nodes.Length; i++)
        {
            _light[i] = field.LevelAround(_nodes[i].Floor + new FVector(0f, 0f, Profile.CrouchHeight), Profile.Radius);
        }

        HasLight = true;
    }

    // ---- lookup ----------------------------------------------------------------------------------------------

    private (int, int) Cell(FVector p) =>
        ((int)MathF.Round((p.X - Origin.X) / Profile.Spacing), (int)MathF.Round((p.Y - Origin.Y) / Profile.Spacing));

    /// <summary>
    /// The node nearest <paramref name="p"/> whose floor is within <paramref name="below"/> under and 40 above the feet
    /// at <paramref name="p"/> minus <paramref name="footOffset"/>, horizontally closest (5 cells out), or -1.
    /// </summary>
    public int NearestNode(FVector p, float footOffset = 0f, float below = 200f)
    {
        (int cx, int cy) = Cell(p);
        float feet = p.Z - footOffset;
        int best = -1;
        float bestScore = float.MaxValue;
        for (int r = 0; r <= 5 && best < 0; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !_columns.TryGetValue((cx + dx, cy + dy), out int[]? column))
                    {
                        continue;
                    }

                    foreach (int n in column)
                    {
                        FVector q = _nodes[n].Floor;
                        float dz = feet - q.Z;
                        if (dz < -40f || dz > below)
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

    /// <summary>Nodes within <paramref name="radius"/> (horizontally) of <paramref name="p"/> and 120 uu in height.</summary>
    public void NodesNear(FVector p, float radius, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        int cells = (int)MathF.Ceiling(radius / Profile.Spacing);
        (int cx, int cy) = Cell(p);
        for (int dx = -cells; dx <= cells; dx++)
        {
            for (int dy = -cells; dy <= cells; dy++)
            {
                if (!_columns.TryGetValue((cx + dx, cy + dy), out int[]? column))
                {
                    continue;
                }

                foreach (int n in column)
                {
                    FVector q = _nodes[n].Floor;
                    float h2 = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y);
                    if (h2 <= radius * radius && MathF.Abs(q.Z - p.Z) <= 120f)
                    {
                        into.Add(n);
                    }
                }
            }
        }

        into.Sort();
    }

    // ---- planning --------------------------------------------------------------------------------------------

    /// <summary>
    /// A* from <paramref name="from"/> to <paramref name="to"/>. The cost of an edge is its length (or time for
    /// traversal) times <c>1 + stealth x light</c> of the node it reaches (when the graph has light), plus
    /// <paramref name="penalty"/>(node) when given. Links in <paramref name="badLinks"/> and edges (by index) in
    /// <paramref name="badEdges"/> are not taken. Fills <paramref name="path"/> with the steps (start excluded); false
    /// when unreachable. With <paramref name="allowPartial"/>, return a route toward an unreachable goal as far
    /// as the connected graph permits. The caller must check the endpoint before treating it as arrival.
    /// <paramref name="walkScale"/>(from node, to node), when given, scales the cost of plain walks (conveyors under
    /// them: <see cref="NavConveyors"/>).
    /// </summary>
    public bool FindPath(int from, int to, List<NavStep> path, float stealth = 0f, Func<int, float>? penalty = null,
        IReadOnlySet<int>? badLinks = null, float maxCost = float.MaxValue, IReadOnlySet<int>? badEdges = null, bool allowPartial = false,
        Func<int, int, float>? walkScale = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        if (from < 0 || to < 0 || from >= _nodes.Length || to >= _nodes.Length)
        {
            return false;
        }

        int count = _nodes.Length;
        if (_g is null || _g.Length != count)
        {
            _g = new float[count];
            _came = new int[count];
            _cameEdge = new int[count];
            _stamp = new int[count];
        }

        float[] g = _g;
        int[] came = _came!;
        int[] cameEdge = _cameEdge!;
        int[] stamp = _stamp!;
        int search = ++_search;
        _open.Clear();
        g[from] = 0f;
        came[from] = -1;
        cameEdge[from] = -1;
        stamp[from] = search;
        _open.Enqueue(from, Heuristic(from, to));
        bool found = from == to;
        int closest = from;
        while (!found && _open.TryDequeue(out int n, out float priority))
        {
            if (n == to)
            {
                found = true;
                break;
            }

            if (priority - Heuristic(n, to) > g[n] + 0.01f)
            {
                continue;
            }

            if (allowPartial && Heuristic(n, to) < Heuristic(closest, to))
            {
                closest = n;
            }

            for (int e = _edgeStart[n]; e < _edgeStart[n + 1]; e++)
            {
                int link = _edgeLink[e];
                if ((link >= 0 && badLinks is not null && badLinks.Contains(link)) || (badEdges is not null && badEdges.Contains(e)))
                {
                    continue;
                }

                int m = _edgeTo[e];
                float cost = _edgeCost[e];
                if (walkScale is not null && link < 0)
                {
                    cost *= walkScale(n, m);
                }

                if (stealth > 0f && HasLight)
                {
                    cost *= 1f + stealth * 3f * SvmMath.Max(0f, _light[m]);
                }

                if (penalty is not null)
                {
                    cost += penalty(m);
                }

                float total = g[n] + cost;
                if (total > maxCost)
                {
                    continue;
                }

                if (stamp[m] != search || total < g[m])
                {
                    stamp[m] = search;
                    g[m] = total;
                    came[m] = n;
                    cameEdge[m] = e;
                    _open.Enqueue(m, total + Heuristic(m, to));
                }
            }
        }

        if (!found)
        {
            if (!allowPartial || closest == from)
            {
                return false;
            }
            to = closest;
        }

        for (int n = to; n != from; n = came[n])
        {
            int e = cameEdge[n];
            path.Add(new NavStep(n, _edgeKind[e], _edgeLink[e], _nodes[n].Floor));
        }

        path.Reverse();
        LastCost = g[to];
        return true;
    }

    /// <summary>The cost of the last path found.</summary>
    public float LastCost { get; private set; }

    /// <summary>
    /// Costs from <paramref name="from"/> to every node (Dijkstra, plain lengths), up to <paramref name="maxCost"/>;
    /// unreached nodes read infinity.
    /// </summary>
    public float[] CostsFrom(int from, float maxCost = float.MaxValue, IReadOnlySet<int>? badLinks = null, IReadOnlySet<int>? badEdges = null)
    {
        var costs = new float[_nodes.Length];
        CostsFrom(from, costs.AsSpan(), maxCost, badLinks, badEdges);
        return costs;
    }

    /// <summary>
    /// Recomputes reachability into caller-owned storage, with exactly <see cref="NodeCount"/> entries. Reusing
    /// this storage avoids a graph-sized allocation on every bot decision; costs and blocked routes are still
    /// evaluated afresh. Like <see cref="FindPath"/>, searches run one at a time on the host's thread.
    /// </summary>
    public void CostsFrom(int from, Span<float> costs, float maxCost = float.MaxValue,
        IReadOnlySet<int>? badLinks = null, IReadOnlySet<int>? badEdges = null)
    {
        if (costs.Length != _nodes.Length)
            throw new ArgumentException("The cost buffer must have one entry per navigation node.", nameof(costs));

        costs.Fill(float.PositiveInfinity);
        PriorityQueue<int, float> open = _costOpen;
        open.Clear();
        if (from < 0)
        {
            return;
        }

        costs[from] = 0f;
        open.Enqueue(from, 0f);
        while (open.TryDequeue(out int n, out float c))
        {
            if (c > costs[n])
            {
                continue;
            }

            for (int e = _edgeStart[n]; e < _edgeStart[n + 1]; e++)
            {
                if ((_edgeLink[e] >= 0 && badLinks is not null && badLinks.Contains(_edgeLink[e])) || (badEdges is not null && badEdges.Contains(e)))
                {
                    continue;
                }

                int m = _edgeTo[e];
                float total = c + _edgeCost[e];
                if (total < costs[m] && total <= maxCost)
                {
                    costs[m] = total;
                    open.Enqueue(m, total);
                }
            }
        }

    }

    /// <summary>
    /// The walk, crouch and drop edges (by index, both ways) whose line crosses the line <paramref name="a"/> to
    /// <paramref name="b"/> seen from above, with an end within <paramref name="zRange"/> of <paramref name="floorZ"/>.
    /// Links are left out (they fail on their own). Fills <paramref name="into"/> (cleared first, sorted).
    /// </summary>
    public void EdgesAcross(FVector a, FVector b, float floorZ, float zRange, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        FVector mid = (a + b) * 0.5f;
        float half = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) * 0.5f;
        var near = new List<int>();
        NodesNear(new FVector(mid.X, mid.Y, floorZ), half + Profile.Spacing * 4f, near);
        foreach (int n in near)
        {
            FVector p = _nodes[n].Floor;
            for (int e = _edgeStart[n]; e < _edgeStart[n + 1]; e++)
            {
                if (_edgeLink[e] >= 0)
                {
                    continue;
                }

                FVector q = _nodes[_edgeTo[e]].Floor;
                if ((MathF.Abs(p.Z - floorZ) <= zRange || MathF.Abs(q.Z - floorZ) <= zRange) && Crosses(p, q, a, b))
                {
                    into.Add(e);
                }
            }
        }

        into.Sort();
    }

    /// <summary>True when the segments p-q and a-b cross in the horizontal plane.</summary>
    private static bool Crosses(FVector p, FVector q, FVector a, FVector b)
    {
        float d1 = Side(a, b, p);
        float d2 = Side(a, b, q);
        float d3 = Side(p, q, a);
        float d4 = Side(p, q, b);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    private static float Side(FVector a, FVector b, FVector p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private float Heuristic(int a, int b)
    {
        FVector d = _nodes[b].Floor - _nodes[a].Floor;
        return MathF.Sqrt(d.X * d.X + d.Y * d.Y);
    }

    // ---- cache file ------------------------------------------------------------------------------------------

    /// <summary>Writes the graph (nodes, edges, links) for the per-map cache.</summary>
    public void Write(Stream stream, string mapKey)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mapKey);
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write("SVMNAV");
        w.Write(FormatVersion);
        w.Write(mapKey);
        w.Write(Profile.Key);
        WriteVector(w, Origin);
        w.Write(_nodes.Length);
        foreach (NavNode n in _nodes)
        {
            WriteVector(w, n.Floor);
            w.Write(n.Standing);
        }

        w.Write(_edgeStart.Length);
        foreach (int s in _edgeStart)
        {
            w.Write(s);
        }

        w.Write(_edgeTo.Length);
        for (int e = 0; e < _edgeTo.Length; e++)
        {
            w.Write(_edgeTo[e]);
            w.Write((byte)_edgeKind[e]);
            w.Write(_edgeCost[e]);
            w.Write(_edgeLink[e]);
        }

        w.Write(_links.Length);
        foreach (NavLink l in _links)
        {
            w.Write((byte)l.Kind);
            w.Write(l.From);
            w.Write(l.To);
            WriteVector(w, l.Start);
            w.Write(l.Yaw);
            WriteVector(w, l.End);
            w.Write(l.GE);
            w.Write(l.Height);
            w.Write(l.NextGE);
            w.Write(l.ExitGE);
            WriteVector(w, l.Transfer);
            w.Write(l.TravelYaw);
        }
    }

    /// <summary>Reads a graph written by <see cref="Write"/>; null when the file is for another map, profile or version.</summary>
    public static NavGraph? Read(Stream stream, string mapKey, NavProfile profile)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(profile);
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        try
        {
            if (!MatchesHeader(r, mapKey, profile))
            {
                return null;
            }

            FVector origin = ReadVector(r);
            var nodes = new NavNode[ReadCount(r, 13)];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new NavNode(ReadVector(r), r.ReadBoolean());
            }

            int startCount = ReadCount(r, sizeof(int));
            if (startCount != nodes.Length + 1)
            {
                return null;
            }

            var starts = new int[startCount];
            for (int i = 0; i < starts.Length; i++)
            {
                starts[i] = r.ReadInt32();
                if (i == 0 ? starts[i] != 0 : starts[i] < starts[i - 1])
                {
                    return null;
                }
            }

            int edges = ReadCount(r, 13);
            if (starts[^1] != edges)
            {
                return null;
            }

            var to = new int[edges];
            var kinds = new NavEdgeKind[edges];
            var costs = new float[edges];
            var linkOf = new int[edges];
            for (int e = 0; e < edges; e++)
            {
                to[e] = r.ReadInt32();
                kinds[e] = (NavEdgeKind)r.ReadByte();
                costs[e] = r.ReadSingle();
                linkOf[e] = r.ReadInt32();
                if ((uint)to[e] >= (uint)nodes.Length || kinds[e] > NavEdgeKind.PipeToBarToZip ||
                    !float.IsFinite(costs[e]) || costs[e] < 0f || linkOf[e] < -1)
                {
                    return null;
                }
            }

            var links = new NavLink[ReadCount(r, 69)];
            for (int i = 0; i < links.Length; i++)
            {
                links[i] = new NavLink((NavEdgeKind)r.ReadByte(), r.ReadInt32(), r.ReadInt32(), ReadVector(r), r.ReadInt32(), ReadVector(r),
                    r.ReadInt32(), r.ReadSingle(), r.ReadInt32(), r.ReadInt32(), ReadVector(r), r.ReadInt32());
                if (links[i].Kind is < NavEdgeKind.LedgeClimb or > NavEdgeKind.PipeToBarToZip ||
                    (uint)links[i].From >= (uint)nodes.Length || (uint)links[i].To >= (uint)nodes.Length)
                {
                    return null;
                }
            }

            foreach (int link in linkOf)
            {
                if (link >= links.Length) return null;
            }

            return new NavGraph(profile, origin, nodes, starts, to, kinds, costs, linkOf, links);
        }
        catch (Exception error) when (error is EndOfStreamException or InvalidDataException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Checks a cache identity without loading its nodes and edges.</summary>
    public static bool MatchesHeader(Stream stream, string mapKey, NavProfile profile)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        return MatchesHeader(reader, mapKey, profile);
    }

    private static bool MatchesHeader(BinaryReader reader, string mapKey, NavProfile profile) =>
        MatchString(reader, "SVMNAV") && reader.ReadInt32() == FormatVersion &&
        MatchString(reader, mapKey) && MatchString(reader, profile.Key);

    private static bool MatchString(BinaryReader reader, string expected)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return reader.Read7BitEncodedInt() == bytes.Length && reader.ReadBytes(bytes.Length).AsSpan().SequenceEqual(bytes);
    }

    private static int ReadCount(BinaryReader reader, int recordSize)
    {
        int count = reader.ReadInt32();
        Stream stream = reader.BaseStream;
        if (count < 0 || stream.CanSeek && count > (stream.Length - stream.Position) / recordSize)
        {
            throw new InvalidDataException("Invalid navigation record count.");
        }

        return count;
    }

    /// <summary>A digest of the graph's content (nodes, edges, links), equal for equal builds.</summary>
    public ulong ContentDigest()
    {
        using var ms = new MemoryStream();
        Write(ms, "digest");
        ulong h = 14695981039346656037UL;
        foreach (byte b in ms.GetBuffer().AsSpan(0, (int)ms.Length))
        {
            h = (h ^ b) * 1099511628211UL;
        }

        return h;
    }

    private static void WriteVector(BinaryWriter w, FVector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static FVector ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
