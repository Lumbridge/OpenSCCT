using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Physics;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using GameFlowK = OpenScct.Game.GameFlow.K;
using PhysK = OpenScct.Physics.K;

namespace OpenScct.MapSweep;

/// <summary>What one sweep of a map plays.</summary>
public sealed record MapSweepOptions
{
    public string Map { get; init; } = "SteeE";

    /// <summary>Story, Deathmatch... or null for the mode the host menu preselects on the map (the catalog's DefaultMode).</summary>
    public string? Mode { get; init; }

    /// <summary>Simulated seconds of the match.</summary>
    public float Seconds { get; init; } = 240f;

    public ulong Seed { get; init; } = 1;

    /// <summary>Server bots next to the local player (3: a 2v2).</summary>
    public int Bots { get; init; } = 3;

    public string Difficulty { get; init; } = "normal";

    /// <summary>The local player's side (the wanderer).</summary>
    public PlaySide Side { get; init; } = PlaySide.Spy;

    /// <summary>Extra SVMap roots searched before the defaults.</summary>
    public string[] MapRoots { get; init; } = [];

    /// <summary>
    /// The mode a map is swept in when none is given: the one the host menu preselects (Story when it plays, else the
    /// first that plays). The last letter of a map code is the version (E Enhanced, D the default map), not the mode.
    /// </summary>
    public static string ModeOf(string map) =>
        MapCatalog.ModeKey(DefaultCatalog.Value.GetCatalogEntry(map)?.DefaultMode ?? EGameMode.GM_Multi);

    private static readonly Lazy<MapCatalog> DefaultCatalog = new(MapCatalog.CreateDefault);
}

/// <summary>One problem a sweep found: its area, what it is about, the first sighting and how often it was seen.</summary>
public sealed class MapIssue
{
    public MapIssue(string area, string subject, string detail, float at)
    {
        Area = area;
        Subject = subject;
        Detail = detail;
        At = at;
    }

    /// <summary>load, class, spawn, world, fall, embedded, stuck, nav, objective, traversal, exception, frame, net.</summary>
    public string Area { get; }

    public string Subject { get; }

    public string Detail { get; }

    /// <summary>Simulated seconds of the first sighting (0 for load-time checks).</summary>
    public float At { get; }

    public int Count { get; set; } = 1;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"[{Area}] {Subject}: {Detail}{(Count > 1 ? $" (x{Count})" : string.Empty)}{(At > 0f ? $" at {At:F1} s" : string.Empty)}");
}

/// <summary>What one bot (or the local player) did over a sweep.</summary>
public sealed record SweepPawnReport(string Name, int Team, float Travelled, float FarthestFromStart, int Deaths, int Kills,
    int LinksDone, int LinksFailed, string Physics, string Status);

/// <summary>The outcome of a sweep of one map.</summary>
public sealed class MapSweepReport
{
    public string Map { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string? LoadError { get; set; }

    public double LoadSeconds { get; set; }

    public int Actors { get; set; }

    public int KnownActors { get; set; }

    public int UnknownActors { get; set; }

    public List<KeyValuePair<string, int>> UnknownClasses { get; } = new();

    public int FailedSpawns { get; set; }

    public int UnappliedProps { get; set; }

    /// <summary>Props the loader could not apply: Class.Prop (reason) x actors.</summary>
    public List<string> UnappliedDetails { get; } = new();

    public int PlayerStarts { get; set; }

    public int GEs { get; set; }

    public string GEKinds { get; set; } = string.Empty;

    public int SpyNavNodes { get; set; }

    public int MercNavNodes { get; set; }

    public string SpyNavLinks { get; set; } = string.Empty;

    public int Objectives { get; set; }

    public bool Started { get; set; }

    public bool Ended { get; set; }

    public int WinningTeam { get; set; } = -1;

    public string Reason { get; set; } = string.Empty;

    public float SimSeconds { get; set; }

    public double WallSeconds { get; set; }

    public int ObjectivesTaken { get; set; }

    public int ObjectivesNeeded { get; set; }

    public double FrameMsMean { get; set; }

    public double FrameMsP99 { get; set; }

    public double FrameMsMax { get; set; }

    public int SlowFrames { get; set; }

    public long CorrectionsSent { get; set; }

    public long Reinits { get; set; }

    public List<SweepPawnReport> Pawns { get; } = new();

    public List<MapIssue> Issues { get; } = new();

    /// <summary>Ok when nothing was found, else the worst area.</summary>
    public string Status => LoadError is not null ? "load fails"
        : Issues.Any(i => i.Area == "exception") ? "exception"
        : Issues.Count == 0 ? "ok" : "issues";

    public void Add(string area, string subject, string detail, float at = 0f)
    {
        foreach (MapIssue i in Issues)
        {
            if (i.Area == area && i.Subject == subject)
            {
                i.Count++;
                return;
            }
        }

        Issues.Add(new MapIssue(area, subject, detail, at));
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{Map} ({Mode}): {Status}");
        if (LoadError is not null)
        {
            sb.Append(": ").Append(LoadError);
            return sb.ToString();
        }

        sb.AppendLine();
        sb.Append(CultureInfo.InvariantCulture,
            $"  load {LoadSeconds:F1} s, actors {Actors} ({KnownActors} ported, {UnknownActors} markers, {FailedSpawns} failed), unapplied props {UnappliedProps}, starts {PlayerStarts}, GEs {GEs} [{GEKinds}], nav spy {SpyNavNodes} merc {MercNavNodes} links [{SpyNavLinks}], objectives {Objectives}");
        sb.AppendLine();
        sb.Append(CultureInfo.InvariantCulture,
            $"  match started {Started} ended {Ended} winner {WinningTeam} {Reason} after {SimSeconds:F0} s ({WallSeconds:F0} s wall), objectives {ObjectivesTaken}/{ObjectivesNeeded}, frame ms mean {FrameMsMean:F2} p99 {FrameMsP99:F2} max {FrameMsMax:F1} slow {SlowFrames}, corrections {CorrectionsSent} reinits {Reinits}");
        foreach (SweepPawnReport p in Pawns)
        {
            sb.AppendLine();
            sb.Append(CultureInfo.InvariantCulture,
                $"  {p.Name} ({(p.Team == 0 ? "spy" : "merc")}): travelled {p.Travelled:F0} uu, farthest {p.FarthestFromStart:F0}, deaths {p.Deaths}, kills {p.Kills}, links {p.LinksDone}/{p.LinksFailed} failed, physics {p.Physics}; {p.Status}");
        }

        if (UnknownClasses.Count > 0)
        {
            sb.AppendLine();
            sb.Append("  unknown classes: ").Append(string.Join(", ", UnknownClasses.Select(c => c.Key + " x" + c.Value.ToString(CultureInfo.InvariantCulture))));
        }

        foreach (MapIssue i in Issues)
        {
            sb.AppendLine();
            sb.Append("  ").Append(i);
        }

        return sb.ToString();
    }
}

/// <summary>
/// Sweeps one map: the offline play session (inline host, loopback, the bots of <see cref="MapSweepOptions.Bots"/>
/// next to a local spy who wanders with the keys), stepped as fast as it runs on a step clock. Before the match it
/// checks the load (marker classes, failed spawns), the player starts (inside geometry, no floor) and the objectives
/// (on the spies' walk graph from a spy start); while it runs, every half second, each pawn on the server (out of the
/// world, falling for long, inside geometry, stuck), and every tick the frame time and exceptions.
/// </summary>
public sealed class MapSweeper
{
    /// <summary>A frame slower than this (ms, server and client tick together) counts as slow.</summary>
    public const double SlowFrameMs = 1000.0 / Sim.Hz;

    /// <summary>Continuous falling that counts as a fall through the floor (s).</summary>
    public const float LongFallSeconds = 6f;

    /// <summary>A pawn that moves less than this over <see cref="StuckWindowSeconds"/> while alive counts as stuck (uu).</summary>
    public const float StuckDistance = 64f;

    public const float StuckWindowSeconds = 45f;

    /// <summary>How far under a start the sweep looks for a floor (uu).</summary>
    public const float StartFloorProbe = 400f;

    private const float SamplePeriod = 0.5f;

    private readonly MapSweepOptions _options;

    public MapSweeper(MapSweepOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MapSweepReport Run(Action<string>? log = null)
    {
        string mode = _options.Mode ?? MapSweepOptions.ModeOf(_options.Map);
        var report = new MapSweepReport { Map = _options.Map, Mode = mode };
        var clock = new SweepClock();
        // A tutorial is played alone, as the side of its map (Tuto_MapsList: TSP maps as a spy, TME maps as a merc).
        bool training = MapCatalog.IsTrainingMap(_options.Map);
        PlaySide side = training
            ? _options.Map.StartsWith("TME", StringComparison.OrdinalIgnoreCase) ? PlaySide.Merc : PlaySide.Spy
            : _options.Side;
        var settings = new PlaySettings
        {
            Map = _options.Map,
            Side = side,
            Mode = mode,
            HostMode = PlayHostMode.Inline,
            MatchSeed = _options.Seed,
            Bots = training ? 0 : _options.Bots,
            Difficulty = _options.Difficulty,
            MapRoots = _options.MapRoots,
        };

        var watch = Stopwatch.StartNew();
        if (MapCatalog.IsMenuMap(_options.Map))
        {
            LoadOnly(report, watch);
            return report;
        }

        PlaySession session;
        try
        {
            session = new PlaySession(settings, clock);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            report.LoadError = Describe(e);
            report.LoadSeconds = watch.Elapsed.TotalSeconds;
            return report;
        }

        using (session)
        {
            report.LoadSeconds = watch.Elapsed.TotalSeconds;
            log?.Invoke(string.Create(CultureInfo.InvariantCulture, $"{_options.Map}: loaded in {report.LoadSeconds:F1} s"));
            ServerHost host = session.Host;
            CheckLoad(host, report);
            CheckStarts(host, report);
            CheckNavigation(host, report);
            Play(session, clock, report, log);
        }

        return report;
    }

    /// <summary>
    /// A front end map (Entry, Lobby) is loaded behind the menus and never played: it is loaded as a server and as a
    /// client would load it, with its physics, and checked like a played map's load.
    /// </summary>
    private void LoadOnly(MapSweepReport report, Stopwatch watch)
    {
        MapCatalog catalog = MapCatalog.CreateDefault();
        string? folder = catalog.FindFolder(_options.Map);
        if (folder is null)
        {
            report.LoadError = "not installed";
            return;
        }

        try
        {
            OpenScct.Content.SvMap map = OpenScct.Content.SvMap.Load(folder);
            foreach (bool client in new[] { false, true })
            {
                var world = new World(0, client ? ENetMode.NM_Client : ENetMode.NM_DedicatedServer);
                MapInfo info = MapCatalog.LoadSvMap(world, map, client);
                if (!client)
                {
                    CheckLoad(info, report);
                    report.PlayerStarts = info.Starts.Count;
                    if (info.Physics is null)
                    {
                        report.Add("load", "physics", "the map has no collision");
                    }
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            report.LoadError = Describe(e);
        }

        report.LoadSeconds = watch.Elapsed.TotalSeconds;
        report.Reason = "load only (front end map)";
    }

    // ---- load ---------------------------------------------------------------------------------------------

    private static void CheckLoad(ServerHost host, MapSweepReport report) => CheckLoad(host.Map, report);

    private static void CheckLoad(MapInfo map, MapSweepReport report)
    {
        if (map.Loaded is not { } loaded)
        {
            report.Add("load", "map", "no loader result");
            return;
        }

        report.Actors = loaded.Actors.Count;
        report.KnownActors = loaded.KnownCount;
        report.UnknownActors = loaded.UnknownCount;
        report.UnknownClasses.AddRange(loaded.UnknownClasses);
        report.FailedSpawns = loaded.FailedSpawns;
        report.UnappliedProps = loaded.UnappliedProps.Sum(p => p.Value);
        foreach (UnappliedProp p in loaded.Unapplied)
        {
            report.UnappliedDetails.Add(string.Create(CultureInfo.InvariantCulture, $"{p.ClassPath}.{p.Prop} ({p.Reason}) x{p.Count}"));
        }

        foreach (KeyValuePair<string, int> c in loaded.UnknownClasses)
        {
            report.Add("class", c.Key, string.Create(CultureInfo.InvariantCulture, $"{c.Value} actors have no C# class (created as markers)"));
        }

        if (loaded.FailedSpawns > 0)
        {
            report.Add("load", "spawns", string.Create(CultureInfo.InvariantCulture, $"{loaded.FailedSpawns} map actors failed to spawn"));
        }

        foreach (UnappliedProp p in loaded.Unapplied)
        {
            if (p.Reason == UnappliedReason.MissingActor)
            {
                report.Add("load", p.ClassPath + "." + p.Prop, string.Create(CultureInfo.InvariantCulture, $"reference to an actor that did not spawn ({p.Count} actors)"));
            }
        }
    }

    // ---- starts -------------------------------------------------------------------------------------------

    private static void CheckStarts(ServerHost host, MapSweepReport report)
    {
        if (host.Map.Physics is not { } physics)
        {
            report.Add("load", "physics", "the map has no collision");
            return;
        }

        CollisionWorld collision = physics.Collision;
        var spy = new SPawnAttaque();
        var merc = new SPAWNDEFENSE();
        int starts = 0;
        foreach (PlayerStart start in host.World.Level.AllActors<PlayerStart>())
        {
            starts++;
            Pawn shape = start.TeamNumber == 1 ? merc : spy;
            var extent = new FVector(shape.CollisionRadius, shape.CollisionRadius, shape.CollisionHeight);
            string name = (host.Map.Loaded?.RecordOf(start)?.Id ?? start.ToString() ?? "start") + (start.bMercDeathMatchStart ? " (merc deathmatch start)" : string.Empty);
            // The spawn places the pawn there without a placement check (no bCollideWhenPlacing on pawns); the spy then
            // crouches at once (its walking state starts crouched), so a spy start is tested at the crouched size.
            FVector spot = SpawnPoint(collision, start);
            FVector body = extent;
            FVector at = spot;
            if (shape is SPawnAttaque)
            {
                body = extent with { Z = shape.CrouchHeight };
                at = spot - new FVector(0f, 0f, shape.CollisionHeight - shape.CrouchHeight);
            }

            if (Embedded(collision, null, at, body - new FVector(2f, 2f, 2f), out FCheckResult inside))
            {
                report.Add("spawn", name, string.Create(CultureInfo.InvariantCulture,
                    $"team {start.TeamNumber} start at {Fmt(start.Location)}: a {shape.GetType().Name} spawned there is inside collider {inside.Collider} {inside.Actor}"));
                continue;
            }

            FVector below = spot - new FVector(0f, 0f, shape.CollisionHeight + StartFloorProbe);
            if (!collision.LineCheck(out _, null, below, spot, extent with { Z = 2f }, PhysK.Flags.Floor))
            {
                report.Add("spawn", name, string.Create(CultureInfo.InvariantCulture,
                    $"team {start.TeamNumber} start at {Fmt(start.Location)} has no floor within {StartFloorProbe:F0} uu"));
            }
        }

        report.PlayerStarts = starts;
        if (starts == 0)
        {
            report.Add("spawn", "map", "no PlayerStart");
        }
    }

    /// <summary>Where RestartPlayer spawns a pawn for a start: 79 uu over the floor a 200 uu box probe finds under it, else at it.</summary>
    private static FVector SpawnPoint(CollisionWorld collision, PlayerStart start)
    {
        FVector spot = start.Location;
        if (collision.LineCheck(out FCheckResult probe, null, spot - new FVector(0f, 0f, GameFlowK.FloorProbeLength), spot,
            GameFlowK.FloorProbeExtent, ETraceFlags.TRACE_ProjTargets))
        {
            spot.Z = probe.Location.Z + GameFlowK.SpawnHeightAboveHit;
        }

        return spot;
    }

    /// <summary>
    /// True when a box of <paramref name="extent"/> at <paramref name="p"/> starts inside world geometry, as the moves
    /// see it: a 2 uu sweep down through the point that starts penetrating (PointCheck tests a rotated mesh's bounds,
    /// which reach further than the mesh).
    /// </summary>
    private static bool Embedded(CollisionWorld collision, Actor? source, FVector p, FVector extent, out FCheckResult hit) =>
        collision.LineCheck(out hit, source, p - new FVector(0f, 0f, 1f), p + new FVector(0f, 0f, 1f), extent, PhysK.Flags.MoveWorld) &&
        hit.bStartPenetrating;

    // ---- navigation and objectives ------------------------------------------------------------------------

    private static void CheckNavigation(ServerHost host, MapSweepReport report)
    {
        if (host.Map.Physics is not { } physics)
        {
            return;
        }

        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < physics.GEs.Count; i++)
        {
            string k = physics.GEs[i].Kind.ToString();
            kinds[k] = kinds.GetValueOrDefault(k) + 1;
        }

        report.GEs = physics.GEs.Count;
        report.GEKinds = string.Join(" ", kinds.Select(k => k.Key + "=" + k.Value.ToString(CultureInfo.InvariantCulture)));

        NavGraph spyGraph;
        NavGraph mercGraph;
        try
        {
            var nav = new AiNavCache();
            byte mask = host.World.Level.Info.GEActivationMask;
            spyGraph = nav.Get(host.Map, spy: true, mask);
            mercGraph = nav.Get(host.Map, spy: false, mask);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            report.Add("nav", "graph", "the walk graph did not build: " + Describe(e));
            return;
        }

        report.SpyNavNodes = spyGraph.NodeCount;
        report.MercNavNodes = mercGraph.NodeCount;
        report.SpyNavLinks = string.Join(" ", Enum.GetValues<NavEdgeKind>()
            .Select(k => (k, n: spyGraph.CountLinks(k))).Where(x => x.n > 0)
            .Select(x => x.k + "=" + x.n.ToString(CultureInfo.InvariantCulture)));

        var spyStarts = new List<int>();
        var mercStarts = new List<int>();
        foreach (PlayerStart start in host.World.Level.AllActors<PlayerStart>())
        {
            if (start.bMercDeathMatchStart && report.Mode != "MercDeathMatch")
            {
                continue;
            }

            NavGraph g = start.TeamNumber == 1 ? mercGraph : spyGraph;
            float height = start.TeamNumber == 1 ? new SPAWNDEFENSE().CollisionHeight : new SPawnAttaque().CollisionHeight;
            FVector spawn = SpawnPoint(physics.Collision, start);
            int node = g.NearestNode(spawn, height);
            string name = host.Map.Loaded?.RecordOf(start)?.Id ?? "start";
            if (node < 0)
            {
                report.Add("nav", name, string.Create(CultureInfo.InvariantCulture, $"team {start.TeamNumber} start at {Fmt(start.Location)} (pawn at {Fmt(spawn)}) is on no walk graph node"));
                continue;
            }

            (start.TeamNumber == 1 ? mercStarts : spyStarts).Add(node);
        }

        // Starts no bot of their side can leave: a walk graph island of a few nodes.
        CheckIslands(spyGraph, spyStarts, "spy", report);
        CheckIslands(mercGraph, mercStarts, "merc", report);

        // The objectives of the swept mode's missions, from the first spy start that has a node.
        SGameInfo? game = host.Game ?? FirstGame(host);
        EGameMode mode = report.Mode switch
        {
            "Story" => EGameMode.GM_Multi,
            "DiskHunt" => EGameMode.GM_DiskHunt,
            "Deathmatch" => EGameMode.GM_DeathMatch,
            _ => EGameMode.GM_MercDeathMatch,
        };
        _ = game;
        var triggers = new List<SObjectiveTrigger>();
        foreach (SMission m in host.World.Level.AllActors<SMission>())
        {
            if (m.GameMode != mode)
            {
                continue;
            }

            Collect(m, triggers, 0);
        }

        report.Objectives = triggers.Count;
        if (spyStarts.Count == 0 || triggers.Count == 0)
        {
            return;
        }

        // The cheapest cost from any spy start.
        float[] costs = spyGraph.CostsFrom(spyStarts[0]);
        foreach (int s in spyStarts.Distinct().Skip(1))
        {
            float[] more = spyGraph.CostsFrom(s);
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = Math.Min(costs[i], more[i]);
            }
        }
        foreach (SObjectiveTrigger t in triggers)
        {
            string name = host.Map.Loaded?.RecordOf(t)?.Id ?? t.ToString() ?? "objective";
            var probe = new SPawnAttaque();
            FVector spot = t.Location;
            if (t is SComputerObjectiveTrigger computer)
            {
                computer.GetUseSpot(probe, out spot, out _);
                spot.Z = t.Location.Z;
            }

            int node = spyGraph.NearestNode(spot, probe.CollisionHeight, 260f);
            if (node < 0)
            {
                report.Add("objective", name, string.Create(CultureInfo.InvariantCulture,
                    $"{t.GetType().Name} at {Fmt(t.Location)} has no spy walk graph node near its use spot {Fmt(spot)}"));
            }
            else if (float.IsPositiveInfinity(costs[node]) || costs[node] == float.MaxValue)
            {
                report.Add("objective", name, string.Create(CultureInfo.InvariantCulture,
                    $"{t.GetType().Name} at {Fmt(t.Location)} cannot be reached on the spy walk graph from any spy start"));
            }
        }
    }

    private static void Collect(SObjective o, List<SObjectiveTrigger> into, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        if (o is SMission m)
        {
            foreach (SObjective? child in m.Objectives)
            {
                if (child is not null)
                {
                    Collect(child, into, depth + 1);
                }
            }

            // A Disk Hunt mission holds its disks itself, with no objectives.
            foreach (SObjectiveTrigger? t in m.Triggers)
            {
                if (t is not null && !into.Contains(t))
                {
                    into.Add(t);
                }
            }

            return;
        }

        foreach (SObjectiveTrigger? t in o.Triggers)
        {
            if (t is not null && !into.Contains(t))
            {
                into.Add(t);
            }
        }
    }

    private static void CheckIslands(NavGraph graph, List<int> starts, string side, MapSweepReport report)
    {
        foreach (int s in starts.Distinct())
        {
            float[] costs = graph.CostsFrom(s);
            int reached = 0;
            foreach (float c in costs)
            {
                if (c < float.MaxValue && !float.IsPositiveInfinity(c))
                {
                    reached++;
                }
            }

            if (reached < Math.Max(50, graph.NodeCount / 50))
            {
                report.Add("nav", side + " start node " + s.ToString(CultureInfo.InvariantCulture),
                    string.Create(CultureInfo.InvariantCulture, $"a {side} start at {Fmt(graph.Node(s).Floor)} reaches only {reached} of {graph.NodeCount} walk graph nodes"));
            }
        }
    }

    // ---- the match ----------------------------------------------------------------------------------------

    private sealed class Track
    {
        public string Name = string.Empty;
        public int Team;
        public SBasePawn? Pawn;
        public AiController? Bot;
        public PlayerReplicationInfo? Pri;
        public int Lives;
        public FVector Start;
        public bool HasStart;
        public FVector Last;
        public float Travelled;
        public float Farthest;
        public float FallingSince = -1f;
        public int EmbeddedSamples;
        public readonly Queue<(float T, FVector P)> Window = new();
        public readonly SortedDictionary<string, int> Physics = new(StringComparer.Ordinal);
        public bool StuckReported;
    }

    private void Play(PlaySession session, SweepClock clock, MapSweepReport report, Action<string>? log)
    {
        ServerHost host = session.Host;
        CollisionWorld? collision = host.Map.Physics?.Collision;
        var tracks = new Dictionary<PlayerController, Track>(ReferenceEqualityComparer.Instance);
        var frameMs = new List<double>((int)(_options.Seconds * Sim.Hz) + 1);
        var pilot = new WanderPilot();
        long maxTicks = (long)(_options.Seconds * Sim.Hz);
        long tick = 0;
        float nextSample = 0f;
        float nextLog = 30f;
        var wall = Stopwatch.StartNew();
        var frame = new Stopwatch();
        while (tick < maxTicks)
        {
            tick++;
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            pilot.Fill(session, tick);
            frame.Restart();
            try
            {
                session.Step(clock.NowMicroseconds);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                report.Add("exception", e.GetType().Name, Describe(e), (float)tick / Sim.Hz);
                break;
            }

            frameMs.Add(frame.Elapsed.TotalMilliseconds);
            session.Input.EndTick();
            if (session.LoginError is { } login)
            {
                report.Add("load", "login", "the local player's login was refused: " + login);
                break;
            }

            float now = host.World.TimeSeconds;
            if (now >= nextSample)
            {
                nextSample = now + SamplePeriod;
                Sample(session, collision, tracks, report, (float)tick / Sim.Hz);
            }

            if (log is not null && tick / (float)Sim.Hz >= nextLog)
            {
                nextLog += 30f;
                log(string.Create(CultureInfo.InvariantCulture, $"{_options.Map}: {tick / Sim.Hz} s simulated in {wall.Elapsed.TotalSeconds:F0} s"));
            }

            if (Ended(host) && tick > 5 * Sim.Hz)
            {
                // A round that ends (a side out of lives, the objectives) is enough; the restart would only repeat it.
                break;
            }
        }

        report.WallSeconds = wall.Elapsed.TotalSeconds;
        report.SimSeconds = (float)tick / Sim.Hz;
        Summarise(session, report, tracks, frameMs);
    }

    private static SGameInfo? FirstGame(ServerHost host)
    {
        foreach (SGameInfo g in host.World.Level.AllActors<SGameInfo>())
        {
            return g;
        }

        return null;
    }

    private static bool Ended(ServerHost host) =>
        FirstGame(host) is { bGameEnded: true } game && game.GameReplicationInfo is { WinningTeam: not 255 };

    private static void Sample(PlaySession session, CollisionWorld? collision, Dictionary<PlayerController, Track> tracks, MapSweepReport report, float t)
    {
        ServerHost host = session.Host;
        foreach (SBasePawn pawn in host.World.Level.AllActors<SBasePawn>())
        {
            if (pawn.bDeleteMe || pawn.Controller is not PlayerController pc)
            {
                continue;
            }

            if (!tracks.TryGetValue(pc, out Track? track))
            {
                track = new Track
                {
                    Name = pc.PlayerReplicationInfo?.PlayerName ?? "?",
                    Team = pawn is SPAWNDEFENSE ? 1 : 0,
                    Bot = session.Bots?.Of(pc),
                    Pri = pc.PlayerReplicationInfo,
                };
                tracks[pc] = track;
            }

            FVector p = pawn.Location;
            if (!ReferenceEquals(track.Pawn, pawn))
            {
                // A new life: distances count from its own start.
                track.Pawn = pawn;
                track.Lives++;
                track.Start = p;
                track.Last = p;
                track.HasStart = true;
                track.FallingSince = -1f;
                track.EmbeddedSamples = 0;
                track.Window.Clear();
            }

            track.Travelled += (p - track.Last).Size();
            track.Last = p;
            track.Farthest = Math.Max(track.Farthest, (p - track.Start).Size());
            bool alive = pawn.Health > 0 && !pc.IsInState("s_Dead");
            string phys = pawn.Physics.ToString();
            track.Physics[phys] = track.Physics.GetValueOrDefault(phys) + 1;
            string where = string.Create(CultureInfo.InvariantCulture, $"{Fmt(p)} state {pc.GetStateName()} physics {pawn.Physics}");

            // Out of the world: under the kill height, or the controller's out-of-world fall.
            if (collision is not null && p.Z < collision.KillZ)
            {
                report.Add("world", track.Name, "fell out of the world at " + where, t);
            }
            else if (pc.IsInState("s_PlayerFalling_OutOfWorld"))
            {
                report.Add("world", track.Name, "out-of-world fall at " + where, t);
            }

            if (pawn.Physics == EPhysics.PHYS_Falling && alive && !pc.IsInState("PlayerFallingInerte"))
            {
                if (track.FallingSince < 0f)
                {
                    track.FallingSince = t;
                }
                else if (t - track.FallingSince >= LongFallSeconds)
                {
                    report.Add("fall", track.Name, string.Create(CultureInfo.InvariantCulture,
                        $"falling for {t - track.FallingSince:F0} s, now at {where}"), t);
                    track.FallingSince = t;
                }
            }
            else
            {
                track.FallingSince = -1f;
            }

            if (collision is not null && alive && pawn.Physics is EPhysics.PHYS_Walking or EPhysics.PHYS_Falling)
            {
                var extent = new FVector(pawn.CollisionRadius - 2f, pawn.CollisionRadius - 2f, pawn.CollisionHeight - 2f);
                if (Embedded(collision, pawn, p, extent, out FCheckResult hit))
                {
                    if (++track.EmbeddedSamples == 2)
                    {
                        report.Add("embedded", track.Name, "inside " + (hit.Actor?.ToString() ?? "level geometry") + " at " + where, t);
                    }
                }
                else
                {
                    track.EmbeddedSamples = 0;
                }
            }

            // Stuck: alive and moved less than StuckDistance across the window.
            if (!alive)
            {
                track.Window.Clear();
                continue;
            }

            track.Window.Enqueue((t, p));
            while (track.Window.Count > 0 && t - track.Window.Peek().T > StuckWindowSeconds)
            {
                track.Window.Dequeue();
            }

            if (!track.StuckReported && track.Window.Count > 0 && t - track.Window.Peek().T >= StuckWindowSeconds - SamplePeriod)
            {
                float most = 0f;
                FVector first = track.Window.Peek().P;
                foreach ((float _, FVector q) in track.Window)
                {
                    most = Math.Max(most, (q - first).Size());
                }

                if (most < StuckDistance)
                {
                    report.Add("stuck", track.Name, string.Create(CultureInfo.InvariantCulture,
                        $"moved under {StuckDistance:F0} uu in {StuckWindowSeconds:F0} s at {where}"), t);
                    track.StuckReported = true;
                }
            }
        }
    }

    private static void Summarise(PlaySession session, MapSweepReport report, Dictionary<PlayerController, Track> tracks, List<double> frameMs)
    {
        ServerHost host = session.Host;
        SGameInfo? game = FirstGame(host);
        GameReplicationInfo? gri = game?.GameReplicationInfo;
        report.Started = gri?.bMatchHasBegun ?? false;
        report.Ended = Ended(host);
        report.WinningTeam = gri is { WinningTeam: not 255 } ? gri.WinningTeam : -1;
        report.Reason = gri is { WinningTeam: not 255 } ? ((EEndReason)gri.WinningReason).ToString() : string.Empty;
        if (!report.Started)
        {
            report.Add("match", "start", "the match never started");
        }

        EGameMode mode = game?.MatchInfo?.GameMode ?? EGameMode.GM_Undefined;
        var seen = new HashSet<SObjective>(ReferenceEqualityComparer.Instance);
        foreach (SMission m in host.World.Level.AllActors<SMission>())
        {
            if (m.GameMode != mode)
            {
                continue;
            }

            report.ObjectivesNeeded = m.MinimumObjectives;
            foreach (SObjective? o in m.Objectives)
            {
                if (o is { ObjectiveTaken: true } && seen.Add(o))
                {
                    report.ObjectivesTaken++;
                }
            }
        }

        if (frameMs.Count > 0)
        {
            double[] sorted = [.. frameMs];
            Array.Sort(sorted);
            report.FrameMsMean = frameMs.Average();
            report.FrameMsP99 = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))];
            report.FrameMsMax = sorted[^1];

            // The first second loads graphs and spawns everyone: leave it out of the slow frame count.
            report.SlowFrames = frameMs.Skip(Sim.Hz).Count(ms => ms > SlowFrameMs);
            if (report.SlowFrames > frameMs.Count / 200)
            {
                report.Add("frame", "slow", string.Create(CultureInfo.InvariantCulture,
                    $"{report.SlowFrames} of {frameMs.Count} frames over {SlowFrameMs:F1} ms (p99 {report.FrameMsP99:F1} ms, max {report.FrameMsMax:F0} ms)"));
            }
        }

        foreach (Track track in tracks.Values)
        {
            PlayerReplicationInfo? pri = track.Pri;
            AiController? bot = track.Bot;
            string phys = string.Join(" ", track.Physics.Select(k => k.Key.Replace("PHYS_", string.Empty, StringComparison.Ordinal) + "=" + k.Value.ToString(CultureInfo.InvariantCulture)));
            report.Pawns.Add(new SweepPawnReport(track.Name, track.Team, track.Travelled, track.Farthest, pri?.Deaths ?? 0, pri?.Kills ?? 0,
                bot?.Brain?.Follower.LinksDone ?? 0, bot?.Brain?.Follower.LinksFailed ?? 0, phys, bot is null ? "local player" : bot.Status));
        }

        // Bots that never got far from where they were first seen: they could not plan or follow a route.
        var byName = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (SweepPawnReport p in report.Pawns)
        {
            byName[p.Name] = Math.Max(byName.GetValueOrDefault(p.Name), p.FarthestFromStart);
        }

        foreach ((string name, float farthest) in byName)
        {
            if (farthest < 600f && report.SimSeconds >= 60f && name != session.Settings.PlayerName)
            {
                report.Add("nav", name, string.Create(CultureInfo.InvariantCulture, $"bot never got more than {farthest:F0} uu from its spawn"));
            }

            SweepPawnReport last = report.Pawns.Last(p => p.Name == name);
            if (last.LinksFailed > Math.Max(2, last.LinksDone))
            {
                report.Add("traversal", name, string.Create(CultureInfo.InvariantCulture, $"{last.LinksFailed} traversal links failed, {last.LinksDone} done"));
            }
        }

        if (session.ServerController is { } server && host.World.Level.GetService<OpenScct.Game.Movement.IMovementNet>() is NetMovementBridge bridge &&
            bridge.EndpointOf(server)?.Server is { } moves)
        {
            report.CorrectionsSent = moves.Sent.Corrections + moves.Sent.ShortCorrections;
            report.Reinits = moves.Sent.Reinits;
            if (report.Reinits > 0)
            {
                report.Add("net", "reinit", string.Create(CultureInfo.InvariantCulture, $"the local player's pawn was reinitialised {report.Reinits} times"));
            }
        }
    }

    internal static string Describe(Exception e)
    {
        string[] lines = (e.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string top = string.Join(" | ", lines.Take(4).Select(l => l.Trim()));
        string inner = e.InnerException is { } i ? " (inner " + i.GetType().Name + ": " + i.Message + ")" : string.Empty;
        return e.GetType().Name + ": " + e.Message + inner + (top.Length > 0 ? " @ " + top : string.Empty);
    }

    internal static string Fmt(FVector v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:F0}, {v.Y:F0}, {v.Z:F0})");
}

/// <summary>A clock the sweep moves by hand, one tick at a time.</summary>
internal sealed class SweepClock : IClock
{
    public long NowMicroseconds { get; set; }
}

/// <summary>
/// The local spy's keys during a sweep: stand up, then walk forward at the fast speed, turning away (with the mouse)
/// whenever the pawn made no headway for two seconds, so the client's moves cover the map's floors and walls.
/// </summary>
internal sealed class WanderPilot
{
    private FVector _last;
    private long _lastCheck;
    private int _turnTicks;
    private float _turnRate;
    private bool _stood;
    private int _hung;
    private int _drop;
    private uint _seed = 2463534242u;

    public void Fill(PlaySession session, long tick)
    {
        LocalInput input = session.Input;
        input.MouseX = 0f;
        if (session.LocalPawn is not { } pawn || session.LocalController is not { } pc || pc.IsInState("WaitingForPawn"))
        {
            return;
        }

        if (!_stood && pawn is SPAWNDEFENSE)
        {
            _stood = true;
            _lastCheck = tick;
            _last = pawn.Location;
            return;
        }

        if (!_stood)
        {
            // The spy spawns crouched; one tap stands him up, one more speed tap runs.
            _stood = true;
            input.Set("bCrouch", true);
            _lastCheck = tick;
            _last = pawn.Location;
            return;
        }

        // The crouch key lets go of a pipe, ladder or bar (s_OnPipe: bCrouchWithRoll drops the spy; jump does nothing there).
        input.Set("bCrouch", _drop > 0);
        if (_drop > 0)
        {
            _drop--;
        }

        input.Set("BT_Up", true);
        if (_turnTicks > 0)
        {
            _turnTicks--;
            input.MouseX = _turnRate;
        }

        if (tick - _lastCheck >= 2 * Sim.Hz)
        {
            if ((pawn.Location - _last).Size2D() < 80f)
            {
                _turnTicks = 20 + (int)(Next() % 30u);
                _turnRate = (Next() & 1u) == 0 ? 8f : -8f;
            }

            // Hanging still at the end of a pipe, ladder, fence or bar with the key held: let go with the crouch key, as
            // a player does (turning the view alone never leaves it).
            _hung = pawn.Physics != EPhysics.PHYS_Walking && (pawn.Location - _last).Size() < 80f ? _hung + 1 : 0;
            if (_hung >= 2)
            {
                _drop = 2;
                _hung = 0;
            }

            _last = pawn.Location;
            _lastCheck = tick;
        }
    }

    private uint Next()
    {
        _seed ^= _seed << 13;
        _seed ^= _seed >> 17;
        _seed ^= _seed << 5;
        return _seed;
    }
}
