using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>One bot of a match: its name, side and brain.</summary>
public sealed record BotSpec(string Name, BotTeam Team, Func<BotBrain> Brain);

/// <summary>A bot match: map, game options, the bots and the network settings.</summary>
public sealed record BotMatchSettings
{
    public string Map { get; init; } = "SteeE";

    /// <summary>SteeE Story as the map's own settings give it: 10 minutes, 4 spy lives, 3 merc lives, 10 s respawn.</summary>
    public string GameOptions { get; init; } = StoryOptions;

    public const string StoryOptions = "?GameMode=5?TimeLimit=10?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3";

    public IReadOnlyList<BotSpec> Bots { get; init; } = [];

    public ulong Seed { get; init; } = 1;

    public ENetMode NetMode { get; init; } = ENetMode.NM_DedicatedServer;

    /// <summary>Server driver settings; null keeps the server default.</summary>
    public NetDriverSettings? ServerNet { get; init; }

    public NetClassRegistry? Classes { get; init; }

    public MapCatalog? Maps { get; init; }

    public NavCache? Nav { get; init; }

    /// <summary>
    /// Terminals block players as the map says (default). False turns their player blocking off on every machine, for
    /// the converted terminal collision that keeps a spy's walk to the use spot 4 uu short of it (docs/NETCODE.md,
    /// "Bot matches"); a test that needs the hack to finish sets it.
    /// </summary>
    public bool TerminalsBlockPlayers { get; init; } = true;

    /// <summary>The 2v2 scripts: <paramref name="spies"/> and <paramref name="mercs"/> brains, two of each.</summary>
    public static IReadOnlyList<BotSpec> TwoVersusTwo(Func<int, BotBrain> spies, Func<int, BotBrain> mercs) =>
    [
        new BotSpec("Spy1", BotTeam.Spy, () => spies(0)),
        new BotSpec("Spy2", BotTeam.Spy, () => spies(1)),
        new BotSpec("Merc1", BotTeam.Merc, () => mercs(0)),
        new BotSpec("Merc2", BotTeam.Merc, () => mercs(1)),
    ];
}

/// <summary>What one bot's connection measured over the match.</summary>
public sealed record BotConnectionReport(
    string Name,
    double ServerToClientBytesPerSecond,
    double ClientToServerBytesPerSecond,
    long MovesApplied,
    long Corrections,
    long CorrectionsApplied,
    long Reinits,
    float MaxRestError,
    float RestErrorP95,
    int ClientWinningTeam,
    int ClientWinningReason,
    bool ClientSawGameEnded,
    bool ResultsMatch);

/// <summary>The outcome of a bot match.</summary>
public sealed record BotMatchReport(
    bool Started,
    bool Ended,
    int WinningTeam,
    int WinningReason,
    float MatchSeconds,
    int MissionObjectivesTaken,
    int MissionObjectivesNeeded,
    IReadOnlyList<BotConnectionReport> Connections)
{
    public override string ToString()
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"started {Started} ended {Ended} winner {WinningTeam} reason {WinningReason} after {MatchSeconds:F1} s, {MissionObjectivesTaken} of {MissionObjectivesNeeded} objectives taken"),
        };
        foreach (BotConnectionReport c in Connections)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {c.Name}: down {c.ServerToClientBytesPerSecond:F0} B/s up {c.ClientToServerBytesPerSecond:F0} B/s, moves {c.MovesApplied}, corrections sent {c.Corrections} applied {c.CorrectionsApplied}, reinits {c.Reinits}, rest error p95 {c.RestErrorP95:F2} max {c.MaxRestError:F2} uu, client result {c.ClientWinningTeam}/{c.ClientWinningReason} game ended {c.ClientSawGameEnded} results match {c.ResultsMatch}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// A server running the SBase game rules and a bot client per <see cref="BotMatchSettings.Bots"/> entry, stepped
/// together on one thread at 60 Hz from a clock the caller advances (a virtual clock for SimLink and loopback, the
/// wall clock for UDP). Measures each connection's bandwidth, the move protocol's corrections and reinits, the
/// error between a resting pawn's client and server positions, and the end of the round as each client sees it.
/// </summary>
public sealed class BotMatch : IDisposable
{
    private readonly List<ITransport> _transports = new();
    private readonly List<BotClient> _bots = new();
    private readonly float[] _restError;
    private readonly int[] _restFrames;
    private readonly List<float>[] _restSamples;
    private readonly (FVector Client, FVector Server)[] _restAt;
    private readonly bool _ownsServer;
    private bool _disposed;

    /// <summary>
    /// Creates the server (bound with <paramref name="bind"/>(<c>"server"</c>)) and the bots (bound with their names,
    /// connecting to <paramref name="serverAddress"/>).
    /// </summary>
    public BotMatch(BotMatchSettings settings, Func<string, ITransport> bind, string serverAddress, long nowMicros)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(bind);
        Settings = settings;
        NetClassRegistry classes = settings.Classes ?? NetClasses.Create();
        MapCatalog maps = settings.Maps ?? MapCatalog.CreateDefault();
        Nav = settings.Nav ?? new NavCache();
        ITransport serverTransport = bind("server");
        _transports.Add(serverTransport);
        var serverSettings = new ServerSettings
        {
            Map = settings.Map,
            GameOptions = settings.GameOptions,
            PlayersToStart = settings.Bots.Count,
            MaxPlayers = Math.Max(4, settings.Bots.Count),
            MatchSeed = settings.Seed,
            NetMode = settings.NetMode,
            Classes = classes,
            Maps = maps,
        };
        if (settings.ServerNet is { } net)
        {
            serverSettings = serverSettings with { Net = net };
        }

        Server = new ServerHost(serverTransport, serverSettings);
        _ownsServer = true;
        AddBots(bind, serverAddress, nowMicros, classes, maps);
        _restError = new float[_bots.Count];
        _restFrames = new int[_bots.Count];
        _restSamples = NewSamples(_bots.Count);
        _restAt = new (FVector, FVector)[_bots.Count];
    }

    private static List<float>[] NewSamples(int count)
    {
        var samples = new List<float>[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = new List<float>();
        }

        return samples;
    }

    /// <summary>Bots for a server that already runs (<paramref name="server"/> is not disposed with the match).</summary>
    public BotMatch(ServerHost server, BotMatchSettings settings, Func<string, ITransport> bind, string serverAddress, long nowMicros)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(bind);
        Settings = settings;
        Server = server;
        Nav = settings.Nav ?? new NavCache();
        AddBots(bind, serverAddress, nowMicros, settings.Classes ?? server.Driver.Classes, settings.Maps ?? MapCatalog.CreateDefault());
        _restError = new float[_bots.Count];
        _restFrames = new int[_bots.Count];
        _restSamples = NewSamples(_bots.Count);
        _restAt = new (FVector, FVector)[_bots.Count];
    }

    private void AddBots(Func<string, ITransport> bind, string serverAddress, long nowMicros, NetClassRegistry classes, MapCatalog maps)
    {
        foreach (BotSpec spec in Settings.Bots)
        {
            ITransport transport = bind(spec.Name);
            _transports.Add(transport);
            _bots.Add(new BotClient(transport, serverAddress, nowMicros, spec.Name, spec.Team, spec.Brain(), Nav,
                new ClientSettings { Classes = classes, Maps = maps }));
        }
    }

    public BotMatchSettings Settings { get; }

    public ServerHost Server { get; }

    public IReadOnlyList<BotClient> Bots => _bots;

    public NavCache Nav { get; }

    public long Frame { get; private set; }

    public GameReplicationInfo? ServerGri => Server.Game?.GameReplicationInfo;

    /// <summary>True once the server's round has a winner.</summary>
    public bool Ended => ServerGri is { WinningTeam: not 255 } && Server.Game is { bGameEnded: true };

    /// <summary>One frame: the server ticks, then every bot.</summary>
    public void Step(long nowMicros)
    {
        Server.Tick(nowMicros);
        StepBots(nowMicros);
    }

    /// <summary>The bots' half of a frame, for a server ticked elsewhere.</summary>
    public void StepBots(long nowMicros)
    {
        Frame++;
        if (!Settings.TerminalsBlockPlayers)
        {
            UnblockTerminals(Server.World);
        }

        for (int i = 0; i < _bots.Count; i++)
        {
            _bots[i].Tick(nowMicros);
            MeasureRest(i);
        }
    }

    private void UnblockTerminals(World world)
    {
        UnblockTerminalsOf(world);
        foreach (BotClient bot in _bots)
        {
            UnblockTerminalsOf(bot.Session.World);
        }
    }

    /// <summary>Turns player blocking off on every terminal of <paramref name="world"/> (see <see cref="BotMatchSettings.TerminalsBlockPlayers"/>).</summary>
    internal static void UnblockTerminalsOf(World world)
    {
        foreach (SComputerObjectiveTrigger t in world.Level.AllActors<SComputerObjectiveTrigger>())
        {
            if (t.bBlockPlayers)
            {
                t.SetCollision(t.bCollideActors, t.bBlockActors, false);
            }
        }
    }

    // The client and server copies of a resting pawn must agree: both copies alive and still (no velocity, and
    // neither has moved, a stance change included, for 30 frames), while the round runs (in GameEnded, or dead, the
    // client sends no moves, so nothing corrects its copy) and while neither copy rests against another pawn
    // (PressedAgainstPawn).
    private void MeasureRest(int i)
    {
        BotClient bot = _bots[i];
        if (Ended || bot.Controller?.Pawn is not { } clientPawn || ServerController(bot)?.Pawn is not { } serverPawn ||
            !clientPawn.Velocity.IsZero() || !serverPawn.Velocity.IsZero() || clientPawn.Physics != EPhysics.PHYS_Walking ||
            serverPawn.Physics != EPhysics.PHYS_Walking || clientPawn.Health <= 0 || serverPawn.Health <= 0)
        {
            _restFrames[i] = 0;
            return;
        }

        (FVector lastClient, FVector lastServer) = _restAt[i];
        _restAt[i] = (clientPawn.Location, serverPawn.Location);
        if (!clientPawn.Location.Equals(lastClient) || !serverPawn.Location.Equals(lastServer) ||
            PressedAgainstPawn(clientPawn, bot.Session.World) || PressedAgainstPawn(serverPawn, Server.World))
        {
            _restFrames[i] = 0;
            return;
        }

        if (++_restFrames[i] < 30)
        {
            return;
        }

        float error = (clientPawn.Location - serverPawn.Location).Size();
        if (error > _restError[i])
        {
            _restError[i] = error;
        }

        // One sample per second of rest, from the second second: a resting client the server disagrees with is only
        // sure of a correction after the forced one (more than 1 s after the last, net-moves.md rule 12) and the link's
        // round trip, and an unreliable correction is dropped while the connection is saturated, as after a stalled
        // frame (IsNetReady); a rest that the protocol has not yet had the chance to correct does not measure it.
        if (_restFrames[i] % Sim.Hz == 0 && _restFrames[i] >= RestSampleFrom)
        {
            _restSamples[i].Add(error);
        }
    }

    /// <summary>Frames of rest before the rest error is sampled (2 s).</summary>
    private const int RestSampleFrom = 2 * Sim.Hz;

    /// <summary>Contact band of two pawn cylinders (uu): a pawn closer than the sum of the radii plus this touches.</summary>
    private const float PawnContactBand = 1f;

    /// <summary>
    /// True when <paramref name="pawn"/> rests against another blocking pawn of <paramref name="world"/>. Such a rest is
    /// not sampled: when the two copies touch the other pawn at different points of its cylinder, the correction is a
    /// chord into that cylinder, and MovePawn (a single sweep, net-moves.md rule 16) is refused by the cylinder rule
    /// for a start inside the radius moving inward (collision-primitives.md), in the original as here. Nothing can
    /// make such copies agree until one of the pawns moves, so they do not measure the move protocol.
    /// </summary>
    private static bool PressedAgainstPawn(Pawn pawn, World world)
    {
        foreach (Pawn other in world.Level.AllActors<Pawn>())
        {
            if (ReferenceEquals(other, pawn) || other.bDeleteMe || !other.bCollideActors || !other.bBlockPlayers)
            {
                continue;
            }

            FVector d = other.Location - pawn.Location;
            float reach = pawn.CollisionRadius + other.CollisionRadius + PawnContactBand;
            if (MathF.Abs(d.Z) < pawn.CollisionHeight + other.CollisionHeight && d.X * d.X + d.Y * d.Y < reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The server's controller of <paramref name="bot"/>, or null.</summary>
    public SPlayerController? ServerController(BotClient bot)
    {
        foreach (PlayerSlot slot in Server.Players)
        {
            if (slot.Name == bot.Name)
            {
                return slot.Controller as SPlayerController;
            }
        }

        return null;
    }

    /// <summary>The report after the run: <paramref name="seconds"/> is the wall or virtual time the bandwidth is averaged over.</summary>
    public BotMatchReport Report(double seconds)
    {
        var connections = new List<BotConnectionReport>();
        for (int i = 0; i < _bots.Count; i++)
        {
            BotClient bot = _bots[i];
            NetConnection? up = bot.Session.Connection;
            NetConnection? down = null;
            foreach (PlayerSlot slot in Server.Players)
            {
                if (slot.Name == bot.Name)
                {
                    down = slot.Connection;
                }
            }

            double downRate = down is null ? 0 : (down.Stats.OutBytes + down.Stats.OutPackets * NetConnection.PacketOverheadBytes) / seconds;
            double upRate = up is null ? 0 : (up.Stats.OutBytes + up.Stats.OutPackets * NetConnection.PacketOverheadBytes) / seconds;
            SPlayerController? spc = ServerController(bot);
            ServerMoves? moves = spc is null ? null : Server.Movement.EndpointOf(spc)?.Server;
            GameReplicationInfo? gri = ClientGri(bot);
            ClientMoves? clientMoves = bot.Controller is { } cpc ? bot.Session.Movement.EndpointOf(cpc)?.Client : null;
            connections.Add(new BotConnectionReport(bot.Name, downRate, upRate, moves?.MovesApplied ?? 0,
                (moves?.Sent.Corrections ?? 0) + (moves?.Sent.ShortCorrections ?? 0), clientMoves?.CorrectionsApplied ?? 0,
                moves?.Sent.Reinits ?? 0, _restError[i], Percentile(_restSamples[i], 0.95f),
                gri?.WinningTeam ?? -1, gri?.WinningReason ?? -1, bot.Controller?.IsInState(new Name("GameEnded")) ?? false,
                ResultsMatch(bot)));
        }

        GameReplicationInfo? sg = ServerGri;
        int taken = 0;
        int needed = 0;
        EGameMode mode = Server.Game?.MatchInfo?.GameMode ?? EGameMode.GM_Undefined;
        foreach (SMission m in Server.World.Level.AllActors<SMission>())
        {
            if (m.GameMode != mode)
            {
                continue;
            }

            needed = m.MinimumObjectives;
            foreach (SObjective? o in m.Objectives)
            {
                if (o is { ObjectiveTaken: true })
                {
                    taken++;
                }
            }
        }

        float matchSeconds = Server.StartGate is { StartedAt: >= 0f } gate ? Server.World.TimeSeconds - gate.StartedAt : 0f;
        return new BotMatchReport(Server.StartGate?.Started ?? false, Ended, sg?.WinningTeam ?? -1, sg?.WinningReason ?? -1, matchSeconds,
            taken, needed, connections);
    }

    private static float Percentile(List<float> samples, float p)
    {
        if (samples.Count == 0)
        {
            return 0f;
        }

        var sorted = new List<float>(samples);
        sorted.Sort();
        int index = Math.Clamp((int)MathF.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    /// <summary>The bot's copy of the game state.</summary>
    public static GameReplicationInfo? ClientGri(BotClient bot)
    {
        foreach (GameReplicationInfo gri in bot.Session.World.Level.AllActors<GameReplicationInfo>())
        {
            return gri;
        }

        return null;
    }

    /// <summary>
    /// The results the bot's client holds equal the server's: the winner and reason, and every player's lives,
    /// deaths and score as replicated on its PRI.
    /// </summary>
    public bool ResultsMatch(BotClient bot)
    {
        GameReplicationInfo? client = ClientGri(bot);
        GameReplicationInfo? server = ServerGri;
        if (client is null || server is null || client.WinningTeam != server.WinningTeam || client.WinningReason != server.WinningReason)
        {
            return false;
        }

        foreach (PlayerReplicationInfo s in Server.World.Level.AllActors<PlayerReplicationInfo>())
        {
            PlayerReplicationInfo? c = null;
            foreach (PlayerReplicationInfo p in bot.Session.World.Level.AllActors<PlayerReplicationInfo>())
            {
                if (p.PlayerName == s.PlayerName)
                {
                    c = p;
                }
            }

            if (c is null || c.NumLives != s.NumLives || c.Deaths != s.Deaths || c.Score != s.Score)
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (BotClient bot in _bots)
        {
            bot.Dispose();
        }

        if (_ownsServer)
        {
            Server.Dispose();
        }

        foreach (ITransport t in _transports)
        {
            t.Dispose();
        }
    }
}
