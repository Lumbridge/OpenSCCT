using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Online;

namespace OpenScct.Integration.Tests.Online;

/// <summary>Monotonic wall clock for the real-time online tests.</summary>
internal sealed class WallClock : IClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public long NowMicroseconds => (long)_watch.Elapsed.TotalMicroseconds;
}

/// <summary>
/// An online session under test: one server and its clients, stepped together on the test's thread. With a virtual
/// clock every frame is exactly 1/60 s (SimLink); with the wall clock frames run in real time (UDP on localhost).
/// </summary>
internal sealed class OnlineRig : IDisposable
{
    private readonly List<OnlineClient> _clients = new();
    private readonly IClock _clock;
    private long _frame;
    private long _start;

    /// <summary>A rig on <paramref name="clock"/>: a <see cref="VirtualClock"/> the rig advances, or a <see cref="WallClock"/>.</summary>
    public OnlineRig(OnlineServer server, IClock clock)
    {
        Server = server;
        _clock = clock;
        _start = clock.NowMicroseconds;
    }

    public OnlineServer Server { get; }

    public IReadOnlyList<OnlineClient> Clients => _clients;

    public long Now => _clock.NowMicroseconds;

    public List<string> Log { get; } = new();

    public OnlineClient Add(OnlineClient client)
    {
        _clients.Add(client);
        return client;
    }

    /// <summary>One frame: the server, then every client.</summary>
    public void Step()
    {
        _frame++;
        if (_clock is VirtualClock virtualClock)
        {
            virtualClock.AdvanceTo(_start + VirtualClock.TickToMicroseconds(_frame, Sim.Hz));
        }
        else
        {
            long due = _start + VirtualClock.TickToMicroseconds(_frame, Sim.Hz);
            while (_clock.NowMicroseconds < due)
            {
                Thread.Sleep(1);
            }

            // A long tick (a map load) drops the backlog instead of rushing through it.
            if (_clock.NowMicroseconds - due > 100_000)
            {
                _start = _clock.NowMicroseconds - VirtualClock.TickToMicroseconds(_frame, Sim.Hz);
            }
        }

        long now = Now;
        Server.Tick(now);
        AfterServerTick?.Invoke(now);
        foreach (OnlineClient c in _clients)
        {
            if (ShouldTickClient?.Invoke(c) != false) c.Tick(now);
        }
    }

    /// <summary>Runs after each server tick, before the clients (bots, terminal unblocking).</summary>
    public Action<long>? AfterServerTick { get; set; }

    /// <summary>Models a renderer blocked loading its scene while the other processes keep ticking.</summary>
    public Func<OnlineClient, bool>? ShouldTickClient { get; set; }

    /// <summary>Steps until <paramref name="done"/> holds or <paramref name="seconds"/> of clock time pass; false on timeout.</summary>
    public bool RunUntil(Func<bool> done, double seconds)
    {
        long end = Now + (long)(seconds * 1_000_000);
        while (Now < end)
        {
            Step();
            if (done())
            {
                return true;
            }
        }

        return done();
    }

    public void Dispose()
    {
        foreach (OnlineClient c in _clients)
        {
            c.Dispose();
        }

        Server.Dispose();
    }

    /// <summary>The settings of a test lobby on <paramref name="map"/>: short countdowns and end screens.</summary>
    public static OnlineServerSettings Settings(string map, int timeLimit = 0) => new()
    {
        Name = "Test server",
        Dedicated = true,
        MaxPlayers = 4,
        Setup = LobbySetup.ForMap(map, OpenScct.Core.EGameMode.GM_Multi, WalkerRig.Maps.GetMapSettings(map), 4) with { TimeLimit = timeLimit },
        Maps = WalkerRig.Maps,
        Classes = WalkerRig.Classes,
        MinPlayersToLaunch = 2,
        LaunchCountdownSeconds = 1f,
        EndScreenSeconds = 1f,
        ResultsSeconds = 0f,
    };

    public static OnlineClientSettings ClientSettings(string name, int team) => new()
    {
        PlayerName = name,
        InstallId = "test-" + name,
        PreferredTeam = team,
        Maps = WalkerRig.Maps,
        Classes = WalkerRig.Classes,
        LoopbackOnly = true,
    };

    /// <summary>
    /// A session factory that plays each round's map with a bot brain (OpenScct.Bots) and the lobby with a plain session.
    /// BotClient logs in with its own name and team options, so the visit's login options ride after the name.
    /// </summary>
    public static Func<ClientSessionRequest, ClientSession> BotSessions(BotTeam team, Func<BotBrain> brain, NavCache nav)
    {
        return request =>
        {
            if (request.Map is null || string.Equals(request.Map, MapCatalog.Lobby, StringComparison.OrdinalIgnoreCase))
            {
                return new ClientSession(request.Transport, request.Address, request.NowMicros, request.Settings);
            }

            var bot = new BotClient(request.Transport, request.Address, request.NowMicros,
                request.Settings.PlayerName + request.Settings.LoginOptions, team, brain(), nav, request.Settings, ownsTransport: false);
            return bot.Session;
        };
    }

    /// <summary>Turns player blocking off on every terminal (the converted terminal collision; docs/NETCODE.md "Bot matches").</summary>
    public static void UnblockTerminals(World world)
    {
        foreach (SComputerObjectiveTrigger t in world.Level.AllActors<SComputerObjectiveTrigger>())
        {
            if (t.bBlockPlayers)
            {
                t.SetCollision(t.bCollideActors, t.bBlockActors, false);
            }
        }
    }

    public static string Port(int port) => port.ToString(CultureInfo.InvariantCulture);
}
