using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>Where a pad's match is.</summary>
public enum HubMatchStatus
{
    /// <summary>The session is open; its players are joining its lobby.</summary>
    Lobby = 0,

    /// <summary>The round is loading or being played.</summary>
    Playing = 1,

    /// <summary>The round ended (or everyone left) and the session closed.</summary>
    Ended = 2,

    /// <summary>The session could not run (nobody came, it stopped on an error).</summary>
    Failed = 3,
}

/// <summary>A pad match's score as the hub shows it on the pad's sign (written by the match's thread).</summary>
public sealed record HubMatchLive(int SpyScore, int MercScore, int SecondsLeft, int Players, int Watchers, bool Playing)
{
    public static HubMatchLive None { get; } = new(0, 0, 0, 0, 0, false);
}

/// <summary>What a pad asks a match for: the pad's config, the people and the side each plays, the join token.</summary>
public sealed record HubMatchRequest(HubPadConfig Pad, IReadOnlyList<(string Name, int Team)> Players, string Password);

/// <summary>A pad's match running somewhere (a thread of the hub's process, or another process).</summary>
public interface IHubMatch : IDisposable
{
    HubMatchRequest Request { get; }

    /// <summary>The UDP port players join it on.</summary>
    int Port { get; }

    /// <summary>Read from the hub's thread; the match may change it from its own.</summary>
    HubMatchStatus Status { get; }

    /// <summary>Why it failed, or null.</summary>
    string? Failure { get; }

    /// <summary>The score, time left and watchers now (the match's thread replaces it as it plays).</summary>
    HubMatchLive Live => HubMatchLive.None;

    /// <summary>The match's own (unlisted) master entry once the master answered, or null: players join it at its address.</summary>
    string? MasterId => null;

    /// <summary>Takes one line the match logged since the last call (the hub logs it on its own thread).</summary>
    bool TryTakeLog(out string line);
}

/// <summary>Starts pads' matches. Throws <see cref="InvalidOperationException"/> with a reason players can read.</summary>
public interface IHubMatchLauncher
{
    IHubMatch Start(HubMatchRequest request);
}

/// <summary>
/// The default pad match launcher (spec/additions/hub.md "Starting the match"): a dedicated
/// <see cref="OnlineServer"/> per match on its own thread of the hub's process, on the first free UDP port of the
/// config's range (28890 to 28897 by default, clear of the hub's 28886 and the game's 28888). The session is a normal online
/// session with a lobby that launches by itself once the expected people are in (or 20 s after the first came), the
/// pad's match as its setup, bots filling the empty places when the pad says so, and the pad's password, so only the
/// players the hub sent get in. Once the round's results are out and its players left (or 45 s after the round
/// ended), it closes.
/// </summary>
public sealed class HubMatchLauncher : IHubMatchLauncher
{
    private readonly MapCatalog _maps;
    private readonly NetClassRegistry? _classes;
    private readonly OnlineBotFactory? _bots;
    private readonly HubConfig _config;
    private readonly bool _loopbackOnly;
    private readonly Func<IClock> _clock;
    private readonly List<Match> _live = new();

    /// <param name="clock">Makes the wall clock each match runs on (the host's: the server library keeps no wall clock of its own).</param>
    public HubMatchLauncher(MapCatalog maps, NetClassRegistry? classes, OnlineBotFactory? bots, HubConfig config, Func<IClock> clock, bool loopbackOnly = false)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
        _classes = classes;
        _bots = bots;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _loopbackOnly = loopbackOnly;
    }

    /// <summary>Seconds after the first player joined before the lobby launches with those who came.</summary>
    public float LaunchAfterSeconds { get; init; } = 20f;

    /// <summary>Seconds the session waits for its first player before it gives up.</summary>
    public float NobodyCameSeconds { get; init; } = 45f;

    /// <summary>
    /// The hub's master: each match registers there unlisted, so players reach it as they reach the hub (direct, NAT
    /// punch or the master's relay) and signed-in players bring their tickets. Null: matches are joined at the hub's
    /// address only.
    /// </summary>
    public MasterLinkSettings? Master { get; init; }

    /// <summary>Seconds after the round ended before the session closes whoever is still there.</summary>
    public float CloseAfterEndSeconds { get; init; } = 45f;

    public IHubMatch Start(HubMatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _live.RemoveAll(m => !m.IsRunning);
        HubPadConfig pad = request.Pad;
        if (_maps.FindMap(pad.Map) is null)
        {
            throw new InvalidOperationException($"This hub does not have the map {pad.Map}.");
        }

        LiteNetLibTransport? udp = null;
        string? lastError = null;
        for (int port = _config.MatchPortFirst; port <= _config.MatchPortLast && udp is null; port++)
        {
            if (_live.Exists(m => m.Port == port))
            {
                continue;
            }

            try
            {
                udp = LiteNetLibTransport.Listen(port, pad.Slots + 6, _loopbackOnly);
            }
            catch (InvalidOperationException e)
            {
                lastError = e.Message;
            }
        }

        if (udp is null)
        {
            throw new InvalidOperationException(lastError is null
                ? "Every match port of this hub is in use; try again when a match ended."
                : "No free match port: " + lastError);
        }

        int humans = request.Players.Count;
        int maxPlayers = pad.Slots;
        var maps = new MapCatalog([.. _maps.Roots]);
        // The laptop's settings when the pad has them, else the map's defaults.
        LobbySetup setup = (pad.Setup ?? LobbySetup.ForMap(pad.Map, pad.Mode, maps.GetMapSettings(pad.Map), maxPlayers)) with
        {
            Map = pad.Map,
            Mode = pad.Mode,
            MaxPlayers = maxPlayers,
            Bots = pad.Bots,
            Revenge = false,
            GamesInARow = 1,
            RandomMap = false,
        };
        OnlineServer server;
        try
        {
            server = new OnlineServer(udp, new OnlineServerSettings
            {
                Name = $"Hub {pad.Id}: {pad.Title} {pad.Format}",
                Dedicated = true,
                MaxPlayers = maxPlayers,
                MaxSpectators = _config.Spectators,
                Password = request.Password,
                Setup = setup,
                Maps = maps,
                Classes = _classes,
                Bots = _bots,
                AutoLaunch = true,
                MinPlayersToLaunch = Math.Max(1, humans),
                LaunchAfterSeconds = LaunchAfterSeconds,
                ReadyOnJoin = true,
                LaunchCountdownSeconds = 2f,
                ResultsSeconds = 15f,
                OpenMapCommands = false,
                LanPort = 0,
                Voice = true,
                Profile = NetProfile.Enhanced,
                Master = Master is { } master ? master with { Listed = false, Kind = ServerKind.Game } : null,
            }, 0, udp);
        }
        catch (Exception e) when (e is InvalidOperationException or System.IO.IOException or ArgumentException)
        {
            udp.Dispose();
            throw new InvalidOperationException("The match could not open: " + e.Message, e);
        }

        var match = new Match(this, request, server, udp);
        _live.Add(match);
        match.Begin();
        return match;
    }

    private sealed class Match : IHubMatch
    {
        private readonly HubMatchLauncher _launcher;
        private readonly OnlineServer _server;
        private readonly LiteNetLibTransport _udp;
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<string> _log = new();
        private readonly Thread _thread;
        private volatile int _status;
        private volatile string? _failure;
        private volatile HubMatchLive _live = HubMatchLive.None;
        private long _opened = -1;
        private long _ended = -1;
        private bool _anyoneCame;
        private bool _disposed;

        public Match(HubMatchLauncher launcher, HubMatchRequest request, OnlineServer server, LiteNetLibTransport udp)
        {
            _launcher = launcher;
            Request = request;
            _server = server;
            _udp = udp;
            Port = udp.LocalPort;
            server.Logged += line => _log.Enqueue(line);
            _thread = new Thread(Run) { IsBackground = true, Name = "OpenSCCT hub match " + request.Pad.Id };
        }

        public HubMatchRequest Request { get; }

        public int Port { get; }

        public HubMatchStatus Status => (HubMatchStatus)_status;

        public string? Failure => _failure;

        public HubMatchLive Live => _live;

        public string? MasterId => _server.Master?.ServerId;

        public bool IsRunning => _thread.IsAlive;

        public bool TryTakeLog(out string line)
        {
            if (_log.TryDequeue(out string? l))
            {
                line = l;
                return true;
            }

            line = string.Empty;
            return false;
        }

        public void Begin() => _thread.Start();

        private void Run()
        {
            IClock clock = _launcher._clock();
            try
            {
                _server.RunRealtime(clock, _stop.Token, s => Watch(s, clock.NowMicroseconds));
                if (Status != HubMatchStatus.Failed)
                {
                    _status = (int)HubMatchStatus.Ended;
                }
            }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or ArgumentException or System.IO.IOException)
            {
                _failure = "The match stopped: " + e.Message;
                _status = (int)HubMatchStatus.Failed;
            }
            finally
            {
                _server.Dispose();
            }
        }

        private void Watch(OnlineServer s, long now)
        {
            if (_opened < 0)
            {
                _opened = now;
            }

            // Watchers do not keep a match open: only its players count.
            int humans = 0;
            int watchers = 0;
            foreach (SessionPlayer p in s.Players)
            {
                if (p.IsSpectator)
                {
                    watchers += p.IsBot ? 0 : 1;
                }
                else
                {
                    humans += p.IsBot ? 0 : 1;
                }
            }

            bool playing = s.Phase is SessionPhase.Loading or SessionPhase.Match;
            ServerInfo info = s.Info();
            var teams = s.Host.Game?.GameReplicationInfo?.Teams;
            _live = new HubMatchLive((int)(teams?[0]?.Score ?? 0f), (int)(teams?[1]?.Score ?? 0f), info.SecondsLeft, humans, watchers, playing);

            _anyoneCame |= humans > 0;
            if (s.Phase is SessionPhase.Loading or SessionPhase.Match)
            {
                _status = (int)HubMatchStatus.Playing;
            }

            if (!_anyoneCame && now - _opened > (long)(_launcher.NobodyCameSeconds * 1_000_000))
            {
                Fail("Nobody joined the match.");
                return;
            }

            if (_ended < 0 && (s.LastResults is not null || (s.RoundsStarted > 0 && s.Phase == SessionPhase.Lobby)))
            {
                _ended = now;
            }

            bool everyoneGone = _anyoneCame && humans == 0;
            if ((_ended >= 0 && (everyoneGone || now - _ended > (long)(_launcher.CloseAfterEndSeconds * 1_000_000))) ||
                (everyoneGone && s.RoundsStarted == 0 && s.Phase == SessionPhase.Lobby))
            {
                _status = (int)(s.RoundsStarted > 0 ? HubMatchStatus.Ended : HubMatchStatus.Failed);
                if (s.RoundsStarted == 0)
                {
                    _failure = "Everyone left before the match started.";
                }

                _stop.Cancel();
            }
        }

        private void Fail(string reason)
        {
            _failure = reason;
            _status = (int)HubMatchStatus.Failed;
            _stop.Cancel();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stop.Cancel();
            if (!_thread.Join(TimeSpan.FromSeconds(3)))
            {
                _log.Enqueue("the match thread did not stop in time");
            }

            _udp.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>A join token: 12 characters of the session password alphabet.</summary>
    public static string NewPassword()
    {
        Span<byte> bytes = stackalloc byte[6];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string Describe(HubMatchRequest r) =>
        string.Create(CultureInfo.InvariantCulture, $"{r.Pad.Id} {r.Pad.Map} {r.Pad.Format} with {r.Players.Count} player(s)");
}
