using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScct.Net.Voice;
using OpenScct.Provenance;

namespace OpenScct.Server.Online;

/// <summary>What an online session serves: its name, player limits, the match it starts with, and its rules.</summary>
public sealed record OnlineServerSettings
{
    public string Name { get; init; } = "OpenSCCT server";

    /// <summary>A dedicated server (no host player: the lobby launches itself when everyone is ready).</summary>
    public bool Dedicated { get; init; } = true;

    /// <summary>Players on the two sides together.</summary>
    public int MaxPlayers { get; init; } = 8;

    /// <summary>Spectators on top of the players (late joiners watch until the next round).</summary>
    public int MaxSpectators { get; init; } = 2;

    /// <summary>A password players must give, or null.</summary>
    public string? Password { get; init; }

    /// <summary>The match the lobby starts with.</summary>
    public LobbySetup Setup { get; init; } = new();

    /// <summary>Dedicated servers: the matches the lobby moves through after each series (empty keeps the setup).</summary>
    public IReadOnlyList<LobbySetup> Rotation { get; init; } = [];

    /// <summary>Replication rate of each round's server, 1 to 60; 0 (the default) takes the rate of <see cref="Profile"/>.</summary>
    public int NetRate { get; init; }

    /// <summary>
    /// Network profile of each round's server (<see cref="ServerSettings.Profile"/>): Stock by default; the dedicated
    /// server, the hub's matches and the game's own host choose Enhanced.
    /// </summary>
    public NetProfile Profile { get; init; } = NetProfile.Stock;

    public ulong MatchSeed { get; init; } = 1;

    public MapCatalog? Maps { get; init; }

    /// <summary>
    /// Where a map came from when it was downloaded from the SCCT-Maps repository (null: not downloaded), told to
    /// players with the setup so those without the map can fetch it. Null: never told.
    /// </summary>
    public Func<string, MapSourceInfo?>? MapSource { get; init; }

    public NetClassRegistry? Classes { get; init; }

    /// <summary>Driver settings of each round's server; null keeps the server default.</summary>
    public NetDriverSettings? Net { get; init; }

    /// <summary>Launch on its own when every player is ready (dedicated servers); a listen host launches by hand.</summary>
    public bool? AutoLaunch { get; init; }

    /// <summary>Players needed before a dedicated lobby launches on its own.</summary>
    public int MinPlayersToLaunch { get; init; } = 2;

    /// <summary>
    /// Dedicated servers: launch with the players there once this many seconds have passed since the session opened,
    /// even when fewer than <see cref="MinPlayersToLaunch"/> came (0: wait). An OpenSCCT addition for the hub's
    /// matches (spec/additions/hub.md), whose players are told to come and may not all make it.
    /// </summary>
    public float LaunchAfterSeconds { get; init; }

    /// <summary>Players are ready as soon as they join the lobby (the hub's matches: the pad was the ready check).</summary>
    public bool ReadyOnJoin { get; init; }

    /// <summary>Seconds between the launch and the travel (the lobby's countdown, launchMatchNow).</summary>
    public float LaunchCountdownSeconds { get; init; } = 0f;

    /// <summary>Seconds the end screen shows before the results (the original opens its end menu 10 s after the winner).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/game/end-of-round.md: the end menu opens 10 s after the winner is known")]
    public float EndScreenSeconds { get; init; } = 10f;

    /// <summary>Dedicated servers: seconds the results stay before the next round goes on without everyone ready (0: wait).</summary>
    public float ResultsSeconds { get; init; } = 30f;

    /// <summary>Seconds a round waits for its players to load before it starts with those who did.</summary>
    public float LoadTimeoutSeconds { get; init; } = 60f;

    /// <summary>
    /// Seconds the old map's connections may still send after the travel line (clients wait longer than this before
    /// they connect again, see <see cref="SessionTransport"/>).
    /// </summary>
    public float TravelGraceSeconds { get; init; } = 1.5f;

    /// <summary>Dedicated servers: every player may change the map and launch unless an admin password is set.</summary>
    public bool OpenMapCommands { get; init; } = true;

    /// <summary>The password of <c>#svm admin</c> and <c>!login</c>; null has no remote admins.</summary>
    public string? AdminPassword { get; init; }

    /// <summary>A listen host's own player proves it is the host with this key (<c>?SvmHost=</c>).</summary>
    public string? HostKey { get; init; }

    public BanList? Bans { get; init; }

    /// <summary>A line every player sees on joining.</summary>
    public string? Motd { get; init; }

    /// <summary>The port LAN searches reach (0: no LAN beacon).</summary>
    public int LanPort { get; init; }

    /// <summary>Bind the LAN beacon to loopback only (tests).</summary>
    public bool LanLoopbackOnly { get; init; }

    /// <summary>List the server on a master, or null.</summary>
    public MasterLinkSettings? Master { get; init; }

    /// <summary>
    /// Only signed-in players (with a ticket from <see cref="Master"/>) may join; a listen host's own player always
    /// may. Without a master nobody could have a ticket, so the server runs open.
    /// </summary>
    public bool RequireAccounts { get; init; }

    /// <summary>
    /// A listen server's own signed-in account (its id and claimed name), or null: the host's player (the one with
    /// <see cref="HostKey"/>) has no master ticket for a server that is only now registering, so it goes by this
    /// account instead of a guest's <c>Name#tag</c>. The server runs in the host's own game, which knows the sign-in.
    /// </summary>
    public HostAccount? HostAccount { get; init; }

    /// <summary>The wall clock that player tickets' expiry is checked against (the simulation never reads it).</summary>
    public TimeProvider WallClock { get; init; } = TimeProvider.System;

    /// <summary>
    /// Lag compensation of the players' shots on every round (<see cref="ServerSettings.LagCompensation"/>); on by
    /// default, as in Reloaded. The console's <c>lagcomp</c> and the chat command <c>!lagcomp</c> change it while the session runs.
    /// </summary>
    public bool LagCompensation { get; init; } = true;

    /// <summary>The longest rewind of <see cref="LagCompensation"/>, in milliseconds.</summary>
    public int LagCompensationCeilingMs { get; init; } = OpenScct.Server.LagCompensation.DefaultCeilingMs;

    /// <summary>
    /// Relay voice chat (docs/VOICE.md) on every map of the session, lobby included, routed by the setup's voice mode,
    /// "Voice in Lobby" and the players' "Restrict Mic to Team".
    /// </summary>
    public bool Voice { get; init; } = true;

    /// <summary>
    /// Makes the server bots of each round (Create Session's "Bots"; OpenScct.Bots' <c>OnlineServerBots.Factory</c>);
    /// null: the session has no bots whatever the setup says.
    /// </summary>
    public OnlineBotFactory? Bots { get; init; }

    /// <summary>
    /// Records each round as a demo (docs/DEMOS.md) into this folder; null (the default) records nothing.
    /// <see cref="OnlineServer.DemoRecorded"/> reports each finished file.
    /// </summary>
    public string? DemoFolder { get; init; }

    /// <summary>Writes a JSON line for every finished round (rl:qol.matchlog); null (the default) logs nothing.</summary>
    public MatchLog? MatchLog { get; init; }
}

/// <summary>A player of the session on the server, kept across the maps of the visit.</summary>
public sealed class SessionPlayer
{
    internal SessionPlayer(int id, string name, ulong token)
    {
        Id = id;
        Name = name;
        Token = token;
    }

    public int Id { get; }

    public string Name { get; internal set; }

    /// <summary>The client's session token (its identity across map changes).</summary>
    public ulong Token { get; }

    public string? InstallId { get; internal set; }

    /// <summary>0 spies, 1 mercs, 255 spectator.</summary>
    public byte Team { get; internal set; }

    public bool Ready { get; internal set; }

    public bool IsHost { get; internal set; }

    public bool IsAdmin { get; internal set; }

    public MapState Map { get; internal set; }

    public int PingMs { get; internal set; }

    public string? Address { get; internal set; }

    /// <summary>The player's slot on the current map's server, or null while it changes map.</summary>
    public PlayerSlot? Slot { get; internal set; }

    public bool IsSpectator => Team == SessionProtocol.SpectatorTeam;

    /// <summary>
    /// A server bot: a lobby row with no connection, always ready, that the round's map logs in and plays with the
    /// game's AI. Bots come and go with the setup's "Bots" and the people on each side, and never count as people
    /// (voice, the launch's ready check, the server's player count).
    /// </summary>
    public bool IsBot { get; internal set; }

    /// <summary>Waiting for this player to come back after a travel.</summary>
    internal bool Expected { get; set; }

    /// <summary>The player's endpoint on the socket (kept across travels, dropped when the player leaves for good).</summary>
    internal EndpointId Endpoint { get; set; }

    internal int SentPing { get; set; } = -1;

    /// <summary>The lobby's "Restrict Mic to Team" (SPlayerController.ServerSetTeamVoiceOnly), kept across the maps.</summary>
    public bool TeamVoiceOnly { get; internal set; }

    /// <summary>
    /// Banned from voice by the host or an admin (PlayerReplicationInfo.bVoiceBanned, which the original set from Xbox
    /// Live), kept across the maps: nobody hears him and he hears nobody.
    /// </summary>
    public bool VoiceBanned { get; internal set; }

    /// <summary>The signed-in player's master account (from his ticket), or null for a guest or a bot.</summary>
    public long? AccountId { get; internal set; }

    /// <summary>
    /// The player's proven key (<see cref="PlayerKey"/>: he signed the server's join challenge), or null for a client
    /// without one, a bot, or the listen host's own player.
    /// </summary>
    public string? PublicKey { get; internal set; }

    /// <summary>The player joined with a valid master ticket: his name is his account's claimed name.</summary>
    public bool Verified { get; internal set; }

    /// <summary>What the player's ticket (or the listen host's account) unlocks; none for guests and bots. A look changed in the lobby is checked against it.</summary>
    public CosmeticUnlocks Unlocks { get; internal set; } = CosmeticUnlocks.None;

    /// <summary>The checked cosmetic looks (only what the ticket unlocks; retail for guests and bots).</summary>
    public CosmeticLook SpyLook { get; internal set; }

    public CosmeticLook MercLook { get; internal set; }

    /// <summary>The rank with the checked title (PlayerRank packed; 0 for guests and bots).</summary>
    public int Rank { get; internal set; }

    /// <summary>The round place this player came back to (he dropped out of it), until he is in again.</summary>
    internal DroppedPlayer? Rejoin { get; set; }

    /// <summary>Kicked or banned: he gets no place to rejoin.</summary>
    internal bool Kicked { get; set; }

    internal SessionPlayerFlags Flags(SessionPhase phase) =>
        (Ready ? SessionPlayerFlags.Ready : 0) | (IsHost ? SessionPlayerFlags.Host : 0) | (IsAdmin ? SessionPlayerFlags.Admin : 0) |
        (IsSpectator ? SessionPlayerFlags.Spectator : 0) |
        (Slot is { Connection.bJoined: true } && phase is SessionPhase.Match or SessionPhase.Results ? SessionPlayerFlags.InGame : 0) |
        (Expected ? SessionPlayerFlags.Loading : 0) | (TeamVoiceOnly ? SessionPlayerFlags.TeamVoice : 0) |
        (Verified ? SessionPlayerFlags.Verified : 0) | (VoiceBanned ? SessionPlayerFlags.VoiceBanned : 0) |
        (IsBot ? SessionPlayerFlags.Bot | (phase is SessionPhase.Match or SessionPhase.Results ? SessionPlayerFlags.InGame : 0) : 0);
}

/// <summary>
/// An online session (docs/ONLINE.md): the lobby, where players pick sides, get ready and the host (or, on a
/// dedicated server, the lobby itself) sets up and launches the match; the travel to the match map, where the round is
/// the game's own SGameInfo started by the start gate once everyone loaded; the end screen and the results; then the
/// next game of the series, a rematch or the lobby (spec/game/round-travel.md). Each map runs its own
/// <see cref="ServerHost"/> on a <see cref="SessionTransport"/> generation of one socket, and clients reconnect on
/// travel as UE2 clients do. The session's own commands travel as <see cref="SessionProtocol"/> lines.
/// <para>Single-threaded: call <see cref="Tick"/> (or <see cref="RunRealtime"/>) from one thread.</para>
/// </summary>
public sealed partial class OnlineServer : IDisposable
{
    private const long Second = 1_000_000;

    private readonly SessionTransport _transport;
    private readonly LiteNetLibTransport? _udp;
    private readonly List<SessionPlayer> _players = new();
    private readonly Dictionary<NetConnection, SessionPlayer> _byConnection = new();
    private readonly List<(SessionPlayer Player, long At)> _kicks = new();
    private readonly NetClassRegistry _classes;
    private readonly byte[] _infoBuffer = new byte[DiscoveryPackets.MaxBytes];
    private readonly byte[] _probeBuffer = new byte[DiscoveryPackets.HeaderBytes];
    private readonly AnswerLimiter _answers = new();
    private readonly Dictionary<ulong, DateTimeOffset> _usedTickets = new();
    private LanBeacon? _lan;
    private int _nextPlayerId = 1;
    private int _rotationIndex;
    private long _now;

    /// <summary>When the round being played started (its duration goes to the master with the results).</summary>
    private long _matchStart;
    private DateTimeOffset _matchStartUtc;

    /// <summary>When a "restart" launches the round again once everyone is back in the lobby (-1: none).</summary>
    private long _relaunchAt = -1;
    private long _phaseStart;
    private long _countdownEnd = -1;
    private long _endedAt = -1;
    private long _resultsEnd = -1;
    private long _nextPing;
    private Travel? _travel;
    private VoiceTransport? _voice;
    private VoiceRelay? _relay;
    private IOnlineBotRunner? _bots;
    private readonly List<string> _randomPool = new();
    private ulong _random;
    private long _closeAt = -1;
    private string? _notice;
    private bool _closing;
    private bool _disposed;
    private bool _lagCompensation;
    private int _lagCompensationCeilingMs;

    /// <summary>A change of map in progress.</summary>
    private sealed record Travel(string Map, string? GameOptions, SessionPhase Next, long Deadline, ServerHost Old);

    /// <summary>The session's time as the voice relay reads it (the time of the current tick).</summary>
    private sealed class SessionClock(OnlineServer server) : IClock
    {
        public long NowMicroseconds => server._now;
    }

    /// <summary>
    /// Who talks in the lobby: the session's players on the lobby level (which has no game rules, so no controllers),
    /// talker id = session player id. The lobby rules of spec/game/voice-chat.md apply: everyone hears everyone, but
    /// only with "Voice in Lobby" on, and a player who restricted his mic to his team is not heard by the other team.
    /// </summary>
    private sealed class LobbyVoiceDirectory(OnlineServer server) : IVoiceDirectory
    {
        public void Fill(VoiceRoster roster)
        {
            roster.Clear();
            roster.Rules = new VoiceRules(server.Setup.VoiceMode, InLobby: true, Enabled: server.Setup.VoiceInLobby);
            foreach (SessionPlayer p in server._players)
            {
                if (p.IsBot || p.Slot is not { } slot || p.Id is <= 0 or > ushort.MaxValue)
                {
                    continue;
                }

                VoiceMember member = VoiceMember.Create((ushort)p.Id, p.IsSpectator ? -1 : p.Team);
                member.Endpoint = slot.Connection.RemoteEndpoint;
                member.TeamVoiceOnly = p.TeamVoiceOnly;
                member.VoiceBanned = p.VoiceBanned;
                roster.Add(member);
            }

            roster.Commit();
        }
    }

    /// <summary>
    /// A session on <paramref name="transport"/> (the server's socket, or a composite of a listen host's loopback and
    /// UDP); <paramref name="udp"/> is the UDP part when there is one (server info answers, master probes, master
    /// registration). The session takes ownership of the transport.
    /// </summary>
    public OnlineServer(ITransport transport, OnlineServerSettings settings, long nowMicros, LiteNetLibTransport? udp = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Maps = settings.Maps ?? MapCatalog.CreateDefault();
        _classes = settings.Classes ?? NetClasses.Create();
        _transport = new SessionTransport(transport, ownsInner: true);
        _udp = udp;
        _now = nowMicros;
        _random = settings.MatchSeed * 0x9E3779B97F4A7C15UL + 1;
        _lagCompensation = settings.LagCompensation;
        _lagCompensationCeilingMs = settings.LagCompensationCeilingMs;
        Setup = settings.Setup.Clamped() with { MaxPlayers = Math.Max(settings.Setup.MaxPlayers, settings.MaxPlayers) };
        if (Maps.FindMap(Setup.Map) is null)
        {
            throw new InvalidOperationException($"Unknown map '{Setup.Map}'.");
        }

        if (udp is not null)
        {
            udp.Unconnected = OnUnconnected;
        }

        if (settings.LanPort > 0)
        {
            _lan = LanBeacon.TryStart(settings.LanPort, Info, settings.LanLoopbackOnly, out string? lanError);
            if (lanError is not null)
            {
                Log(lanError);
            }
        }

        if (settings.Master is { Url.Length: > 0 } master && udp is not null)
        {
            Master = new MasterLink(master with { RequireAccounts = settings.RequireAccounts }, udp);
            Master.Notice += Announce;
            Master.AccountIds = SignedInAccounts;
            Master.KickAccounts += OnMasterKicks;
            Master.PlayerDetails = PlayerDetailsForMaster;
            Master.RoundSecondsLeft = () => Phase == SessionPhase.Match ? Info().SecondsLeft : -1;
            Master.RejoinAccounts = RejoinAccounts;
            Master.Commands += OnMasterCommands;
        }
        else if (settings.RequireAccounts)
        {
            Log("RequireAccounts needs a master server (players get their tickets there): the server runs open.");
        }

        Host = CreateHost(MapCatalog.Lobby, null);
        Phase = SessionPhase.Lobby;
        _phaseStart = nowMicros;
        UpdateBots();
    }

    public OnlineServerSettings Settings { get; }

    public MapCatalog Maps { get; }

    /// <summary>The server of the current map (the lobby level, or the round's map).</summary>
    public ServerHost Host { get; private set; }

    public SessionPhase Phase { get; private set; }

    /// <summary>The match the lobby sets up (and the running round's).</summary>
    public LobbySetup Setup { get; private set; }

    public IReadOnlyList<SessionPlayer> Players => _players;

    /// <summary>The results of the last round, once it ended.</summary>
    public MatchResults? LastResults { get; private set; }

    /// <summary>Rounds started so far.</summary>
    public int RoundsStarted { get; private set; }

    /// <summary>The registration with the master, or null.</summary>
    public MasterLink? Master { get; }

    /// <summary>The current map's voice relay, or null when voice is off (tests read its counters).</summary>
    public VoiceRelay? VoiceRelay => _relay;

    /// <summary>The current round's bots, or null (the lobby, no bots, or no bot factory).</summary>
    public IOnlineBotRunner? RoundBots => _bots;

    /// <summary>Session ids of the players the lobby routes voice for (people on the current map; never a bot).</summary>
    public IReadOnlyList<int> LobbyVoiceMembers()
    {
        var ids = new List<int>();
        foreach (SessionPlayer p in _players)
        {
            if (!p.IsBot && p.Slot is not null && p.Id is > 0 and <= ushort.MaxValue)
            {
                ids.Add(p.Id);
            }
        }

        return ids;
    }

    /// <summary>The LAN beacon, or null.</summary>
    public LanBeacon? Lan => _lan;

    /// <summary>The socket's port when the session runs on UDP, else 0.</summary>
    public int GamePort => _udp?.LocalPort ?? 0;

    /// <summary>True once <see cref="Close"/> was asked for (the host process should stop).</summary>
    public bool CloseRequested { get; private set; }

    /// <summary>Lines for the server log (joins, leaves, travels, admin actions, master notices).</summary>
    public event Action<string>? Logged;

    /// <summary>Raised after each map's server was created, before players join it (tests adjust the world here).</summary>
    public event Action<ServerHost>? HostStarted;

    /// <summary>Raised when a round ended and its results are known.</summary>
    public event Action<MatchResults>? RoundEnded;

    /// <summary>
    /// Raised when a round's demo is complete on disk (<see cref="OnlineServerSettings.DemoFolder"/>): the file, and
    /// the round's results (null when the round stopped early: everyone left, a map change).
    /// </summary>
    public event Action<string, MatchResults?>? DemoRecorded;

    private void Log(string line) => Logged?.Invoke(line);

    // ---- ticking ------------------------------------------------------------------------------------------------

    /// <summary>One 60 Hz step: the current map's server, the travel, the lobby, round and results rules, discovery.</summary>
    public void Tick(long nowMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _now = nowMicros;
        if (_travel is { } travel)
        {
            Host.Tick(nowMicros);
            if (nowMicros >= travel.Deadline || AllOldConnectionsClosed())
            {
                FinishTravel(travel);
            }
        }
        else
        {
            Host.Tick(nowMicros);
            switch (Phase)
            {
                case SessionPhase.Lobby:
                    TickLobby();
                    TickRelaunch();
                    break;
                case SessionPhase.Loading:
                    TickLoading();
                    break;
                case SessionPhase.Match:
                    TickMatch();
                    break;
                case SessionPhase.Results:
                    TickResults();
                    break;
            }

            TickKicks();
            TickPings();
        }

        _lan?.Poll(nowMicros);
        Master?.Tick(nowMicros, Info);
        while (Master is { } master && master.TryTakeEquipment(out var equipment))
            foreach (var player in _players)
                if (player.AccountId is { } account && equipment.TryGetValue(account, out var current))
                {
                    var old = PlayerRank.Unpack(player.Rank);
                    player.Rank = PlayerRank.Unpack(current.Rank).WithTitle(old.TitleBit).Pack();
                    player.Unlocks = new CosmeticUnlocks(current.Unlocks);
                    if (player.Slot?.Controller?.PlayerReplicationInfo is { } pri) pri.SvmRank = player.Rank;
                    Send(null, RankLine(player));
                }
        if (_closeAt >= 0 && nowMicros >= _closeAt)
        {
            CloseRequested = true;
        }
    }

    /// <summary>
    /// Runs <see cref="Tick"/> at 60 Hz from <paramref name="clock"/> until cancelled or <see cref="Close"/>, calling
    /// <paramref name="afterTick"/> after each tick; a stall drops its backlog. While <see cref="IsIdle"/> it ticks
    /// only <see cref="RealtimeLoop.IdleHz"/> times a second.
    /// </summary>
    public void RunRealtime(IClock clock, CancellationToken cancellationToken, Action<OnlineServer>? afterTick = null) =>
        RealtimeLoop.Run(clock, cancellationToken, now =>
        {
            Tick(now);
            afterTick?.Invoke(this);
        }, () => IsIdle, () => CloseRequested);

    /// <summary>
    /// True when nobody is on the server: in the lobby, no players, no connections (not even one still connecting),
    /// no travel, kick or close waiting. <see cref="RunRealtime"/> then ticks slowly.
    /// </summary>
    public bool IsIdle =>
        _travel is null && Phase == SessionPhase.Lobby && _players.Count == 0 && _kicks.Count == 0 && _closeAt < 0
        && Host.Driver.ClientConnections.Count == 0;

    private void TickKicks()
    {
        for (int i = _kicks.Count - 1; i >= 0; i--)
        {
            if (_now >= _kicks[i].At)
            {
                SessionPlayer p = _kicks[i].Player;
                _kicks.RemoveAt(i);
                p.Slot?.Connection.Close("Kicked");
                if (p.Slot?.Connection.RemoteEndpoint is { } endpoint)
                {
                    _transport.DisconnectNow(endpoint);
                }

                RemovePlayer(p, null);
            }
        }
    }

    private void TickPings()
    {
        if (_now < _nextPing)
        {
            return;
        }

        _nextPing = _now + 2 * Second;
        foreach (SessionPlayer p in _players)
        {
            if (p.Slot is { } slot && slot.Connection.Stats.HasRtt)
            {
                p.PingMs = (int)(slot.Connection.Stats.RttMicros / 1000);
                if (Math.Abs(p.PingMs - p.SentPing) >= 5)
                {
                    BroadcastPlayer(p);
                }
            }
        }
    }

    // ---- map servers and travel ---------------------------------------------------------------------------------

    private ServerHost CreateHost(string map, string? gameOptions)
    {
        SessionTransport.Generation generation = _transport.NewGeneration();
        bool isLobby = gameOptions is null;
        var settings = new ServerSettings
        {
            Map = map,
            MaxPlayers = Settings.MaxPlayers + Settings.MaxSpectators,
            NetRate = Settings.NetRate,
            Profile = Settings.Profile,
            MatchSeed = Settings.MatchSeed + (ulong)RoundsStarted,
            NetMode = Settings.Dedicated ? ENetMode.NM_DedicatedServer : ENetMode.NM_ListenServer,
            Maps = Maps,
            Classes = _classes,
            SpawnWalkers = false,
            GameOptions = isLobby ? null : gameOptions,
            PlayersToStart = Math.Max(1, CountPlayers(spectators: false)),
            UseStartGate = !isLobby,
            LagCompensation = _lagCompensation,
            LagCompensationCeilingMs = _lagCompensationCeilingMs,
        };
        if (Settings.Net is { } net)
        {
            settings = settings with { Net = net };
        }

        // Voice rides on the map's own transport (docs/VOICE.md): the relay works inside the driver's receive pass.
        ITransport transport = Settings.Voice ? new VoiceTransport(generation) : generation;
        ServerHost host;
        try
        {
            host = new ServerHost(transport, settings);
        }
        catch
        {
            generation.Retire();
            throw;
        }

        _voice = transport as VoiceTransport;
        IVoiceDirectory directory = isLobby ? new LobbyVoiceDirectory(this) : new NetDriverVoiceDirectory(host.Driver, new SessionVoiceLinks());
        _relay = _voice is not null ? new VoiceRelay(_voice, directory, new SessionClock(this)) : null;

        host.LoginFilter = FilterLogin;
        host.KeyChallenge = true;
        host.PlayerJoined += OnPlayerJoined;
        host.PlayerLeft += OnPlayerLeft;
        host.SessionLineReceived += OnSessionLine;
        host.ChatFilter = OnChatCommand;
        if (host.Game is { } game)
        {
            // The host settings the rules read (Disk Hunt's disk counts), as the match creation page stores them.
            Setup.ApplyTo(game.PersistentData);
            game.PersistentData.LevelName = map;
            if (game.GameReplicationInfo is { } gri)
            {
                Setup.ApplyTo(gri);
                AlternateRules.ApplyTo(gri, game.PersistentData);

                // The host's own "Allow VoiceChat" (the profile switch the server has no profile for).
                gri.allowVoiceChat = Settings.Voice;
            }
        }

        if (!isLobby)
        {
            StartDemo(host, map, gameOptions!);
            _bots = StartBots(host);
            StartPracticeRound(host);
        }

        HostStarted?.Invoke(host);
        return host;
    }

    /// <summary>Starts the round's demo when the session records them; a failure only costs the demo.</summary>
    private void StartDemo(ServerHost host, string map, string gameOptions)
    {
        if (Settings.DemoFolder is not { Length: > 0 } folder)
        {
            return;
        }

        DateTimeOffset start = Settings.WallClock.GetUtcNow();
        string path = System.IO.Path.Combine(folder,
            start.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + map + DemoFile.Extension);
        DemoWriter? writer = null;
        try
        {
            writer = new DemoWriter(path, new DemoHeader
            {
                GameVersion = BuildInfo.Version,
                ProtocolHash = host.Driver.ProtocolHash,
                ContentHash = host.Map.ContentHash,
                Map = map,
                GameOptions = gameOptions,
                ServerName = Settings.Name,
                StartUnixMs = start.ToUnixTimeMilliseconds(),
                TickRate = Sim.Hz,
                NetRate = host.NetRate,
            });
            host.StartRecording(writer, _now);
            Log($"recording demo {path}");
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            writer?.Dispose();
            Log($"demo not recorded: {e.Message}");
        }
    }

    /// <summary>Finishes the round's demo, if one is recording, and reports it.</summary>
    private void FinishDemo(ServerHost host, MatchResults? results)
    {
        if (host.Recorder is not { IsFinished: false } recorder)
        {
            return;
        }

        try
        {
            recorder.Finish();
            recorder.Writer.Dispose();
            DemoIndex index = recorder.Writer.Index;
            long bytes = new System.IO.FileInfo(recorder.Writer.Path).Length;
            double seconds = Math.Max(1.0, index.DurationTicks / (double)Sim.Hz);
            Log($"demo {recorder.Writer.Path}: {bytes / 1024} KB, {seconds:0} s ({bytes / 1024.0 / seconds:0.0} KB/s, {index.RawBytes / 1024.0 / seconds:0.0} KB/s raw)");

            // A finished round's demo goes to the master's bucket (trusted servers with uploads on).
            if (results is not null && SubmitDemo(recorder.Writer.Path, new DemoMeta
                {
                    DurationSeconds = (int)seconds,
                    GameVersion = recorder.Writer.Header.GameVersion,
                    NetProtocol = recorder.Writer.Header.ProtocolHash,
                    ContentHash = recorder.Writer.Header.ContentHash,
                }))
            {
                Log($"uploading demo {recorder.Writer.Path}");
            }

            DemoRecorded?.Invoke(recorder.Writer.Path, results);
        }
        catch (System.IO.IOException e)
        {
            Log($"demo not saved: {e.Message}");
        }
    }

    /// <summary>
    /// The round's bots on <paramref name="host"/>, each on its lobby side with the series count, so the game's Login
    /// swaps them in a revenge game as it swaps the people. The bots log in and report waiting like loaded clients,
    /// so the start gate (its player count includes them) waits for them too.
    /// </summary>
    private IOnlineBotRunner? StartBots(ServerHost host)
    {
        if (Settings.Bots is not { } factory)
        {
            return null;
        }

        var bots = new List<OnlineBot>();
        foreach (SessionPlayer p in _players)
        {
            if (p.IsBot)
            {
                bots.Add(new OnlineBot(p.Id, p.Name, p.Team, Setup.BotSkill, SeriesOptions(host.Game)));
            }
        }

        if (bots.Count == 0)
        {
            return null;
        }

        try
        {
            IOnlineBotRunner runner = factory(host, Maps, bots);
            Log($"{bots.Count} {Setup.BotSkill} bots on {host.Map.Name}");
            return runner;
        }
        catch (InvalidOperationException e)
        {
            Log($"bots could not play {host.Map.Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>Tells every client to travel to <paramref name="map"/> (null options: the lobby) and starts the change.</summary>
    private void BeginTravel(string map, string? gameOptions, SessionPhase next)
    {
        if (_travel is not null)
        {
            return;
        }

        ulong content = Maps.FindMap(map)?.ContentHash ?? 0;
        foreach (SessionPlayer p in _players)
        {
            p.Expected = p.Slot is not null;
            p.Ready = p.IsBot;
        }

        Send(null, SessionProtocol.Line("travel", map, MasterApi.Hex(content)));
        Host.AcceptingConnections = false;

        // The players come back on the same peers (a punched mapping, a relay session): the old map's server lets
        // go of its connections without dropping the peers underneath.
        if (_transport.Current is { } generation)
        {
            generation.HoldDisconnects = true;
        }

        _travel = new Travel(map, gameOptions, next, _now + (long)(Settings.TravelGraceSeconds * Second), Host);
        _countdownEnd = -1;
        Log($"travel to {map}" + (gameOptions is null ? " (lobby)" : " " + Setup.Describe()));
    }

    private bool AllOldConnectionsClosed()
    {
        foreach (NetConnection c in Host.Driver.ClientConnections)
        {
            if (c.State != EConnectionState.USOCK_Closed)
            {
                return false;
            }
        }

        return true;
    }

    private void FinishTravel(Travel travel)
    {
        _travel = null;
        ServerHost old = travel.Old;
        old.PlayerLeft -= OnPlayerLeft;
        foreach (SessionPlayer p in _players)
        {
            p.Slot = null;
        }

        _byConnection.Clear();

        // Retired first, so the old map's server closes its connections without sending anything more.
        _transport.Current?.Retire();
        _bots?.Dispose();
        _bots = null;
        FinishDemo(old, null);
        old.Dispose();
        try
        {
            Host = CreateHost(travel.Map, travel.GameOptions);
        }
        catch (InvalidOperationException e)
        {
            Log($"could not load {travel.Map}: {e.Message}");
            _notice = $"The server could not load {travel.Map}: {e.Message}";
            Host = CreateHost(MapCatalog.Lobby, null);
            SetPhase(SessionPhase.Lobby);
            return;
        }

        if (travel.GameOptions is not null)
        {
            RoundsStarted++;
        }

        SetPhase(travel.Next);
    }

    private void SetPhase(SessionPhase phase)
    {
        if (phase != Phase)
        {
            _notice = phase == SessionPhase.Lobby ? _notice : null;
        }

        Phase = phase;
        _phaseStart = _now;
        if (phase == SessionPhase.Lobby)
        {
            // The round (or the series) is over: nobody has a place to rejoin any more.
            _dropped.Clear();
        }
        if (phase == SessionPhase.Match)
        {
            _matchStart = _now;
            _matchStartUtc = Settings.WallClock.GetUtcNow();
            _roundMatchId = null;
        }
        _endedAt = -1;
        _resultsEnd = -1;
        if (phase != SessionPhase.Loading)
        {
            foreach (SessionPlayer p in _players)
            {
                p.Expected = false;
            }
        }

        BroadcastPhase();
        if (phase == SessionPhase.Lobby)
        {
            UpdateBots();
        }

        Master?.HeartbeatSoon();
    }

    // ---- lobby --------------------------------------------------------------------------------------------------

    private bool AutoLaunch => Settings.AutoLaunch ?? Settings.Dedicated;

    private void TickLobby()
    {
        if (_countdownEnd >= 0)
        {
            if (LaunchProblem() is { } problem)
            {
                _countdownEnd = -1;
                Announce("Launch stopped: " + problem);
                BroadcastPhase();
            }
            else if (_now >= _countdownEnd)
            {
                Launch();
            }

            return;
        }

        int humans = CountHumans(spectators: false);
        bool waitedLongEnough = Settings.LaunchAfterSeconds > 0 && humans > 0 && _now - _phaseStart >= (long)(Settings.LaunchAfterSeconds * Second);
        if (AutoLaunch && (humans >= Math.Max(1, Settings.MinPlayersToLaunch) || waitedLongEnough) && LaunchProblem() is null)
        {
            StartCountdown();
        }
    }

    private void StartCountdown()
    {
        _countdownEnd = _now + (long)(Settings.LaunchCountdownSeconds * Second);
        BroadcastPhase();
    }

    /// <summary>Why the lobby cannot launch now, or null ("Could not start. Not all players are ready").</summary>
    private string? LaunchProblem()
    {
        int players = 0;
        foreach (SessionPlayer p in _players)
        {
            if (p.IsSpectator || p.Slot is null)
            {
                continue;
            }

            players++;
            if (!p.Ready && !p.IsHost)
            {
                return "Not all players are ready.";
            }

            if (p.Map == MapState.Missing)
            {
                return $"{p.Name} does not have {Setup.Map}.";
            }

            if (p.Map == MapState.Differs)
            {
                return $"{p.Name} has a different version of {Setup.Map}.";
            }
        }

        return players == 0 ? "Nobody is on a side." : null;
    }

    private void Launch()
    {
        _countdownEnd = -1;
        int players = CountPlayers(spectators: false);
        BeginTravel(Setup.Map, Setup.ToGameOptions(players), SessionPhase.Loading);
    }

    // ---- round --------------------------------------------------------------------------------------------------

    private void TickLoading()
    {
        if (Host.StartGate is not { } gate)
        {
            SetPhase(SessionPhase.Match);
            return;
        }

        if (gate.Started)
        {
            SetPhase(SessionPhase.Match);
            Announce($"Round {RoundsStarted} started: {Setup.Describe()}.");
            return;
        }

        if (_now - _phaseStart < (long)(Settings.LoadTimeoutSeconds * Second))
        {
            return;
        }

        // Start with those who loaded; the others are dropped from the session.
        var missing = new List<SessionPlayer>();
        foreach (SessionPlayer p in _players)
        {
            if (p.Slot is null && !p.IsBot)
            {
                missing.Add(p);
            }
        }

        foreach (SessionPlayer p in missing)
        {
            KeepDropped(p, null);
            RemovePlayer(p, $"{p.Name} did not load in time.");
        }

        int present = CountHumans(spectators: false);
        if (present == 0)
        {
            BackToLobby("Nobody loaded the map.");
            return;
        }

        gate.PlayersToStart = Math.Min(gate.PlayersToStart, Math.Max(1, Host.Game?.GameReplicationInfo?.PRIArray.Count ?? present));
        _phaseStart = _now;
    }

    private void TickMatch()
    {
        TickPractice();
        if (CountHumans(spectators: false) == 0 && _now - _phaseStart > 5 * Second)
        {
            BackToLobby("Everyone left the round.");
            return;
        }

        if (Host.Game is not { bGameEnded: true } || Host.Game.GameReplicationInfo is not { WinningTeam: not 255 })
        {
            return;
        }

        if (_endedAt < 0)
        {
            _endedAt = _now;
            Log($"round ended: winner {Host.Game.GameReplicationInfo.WinningTeam}, reason {Host.Game.GameReplicationInfo.WinningReason}");
        }

        if (_now - _endedAt >= (long)(Settings.EndScreenSeconds * Second))
        {
            EnterResults();
        }
    }

    private void EnterResults()
    {
        MatchResults results = BuildResults();
        LastResults = results;
        SetPhase(SessionPhase.Results);
        if (results.GamesLeft == 0)
        {
            // The last game of the series is over: there is no round left to rejoin.
            _dropped.Clear();
        }

        foreach (SessionPlayer p in _players)
        {
            p.Ready = p.IsBot;
            BroadcastPlayer(p);
        }

        SendResults(null, results);
        if (Settings.Dedicated && Settings.ResultsSeconds > 0)
        {
            _resultsEnd = _now + (long)(Settings.ResultsSeconds * Second);
            BroadcastPhase();
        }

        // Reported first: the round's demo is handed to the upload once the master has the round (SubmitDemo).
        ReportResults(results);
        WriteMatchLog(results);
        RoundEnded?.Invoke(results);
        FinishDemo(Host, results);
    }

    /// <summary>
    /// Uploads a finished round's demo file to the master's bucket (docs/ONLINE.md, "Demos"), once the master has
    /// stored the round's results. Call on the host's thread after the round ended (from <see cref="RoundEnded"/> or
    /// during the results) and before the next round starts. False when it will not be uploaded: no trusted master,
    /// uploads off (<see cref="MasterLinkSettings.UploadDemos"/>) or the round was not reported. The file must stay
    /// in place until the upload is done; the outcome comes out of the master link's notices.
    /// </summary>
    public bool SubmitDemo(string path, DemoMeta meta)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(meta);
        if (Master is not { Trusted: true } link || Settings.Master is not { UploadDemos: true } || _roundMatchId is not { } matchId)
        {
            return false;
        }

        _ = UploadWhenReported(link, matchId, path, meta);
        return true;
    }

    private static async Task UploadWhenReported(MasterLink link, Task<long?> reported, string path, DemoMeta meta)
    {
        if (await reported.ConfigureAwait(false) is { } matchId)
        {
            await link.UploadDemo(matchId, path, meta).ConfigureAwait(false);
        }
    }

    /// <summary>The master's id of the round that just ended, once its results are taken (null: not reported).</summary>
    private Task<long?>? _roundMatchId;

    /// <summary>A trusted server's finished round goes to the master for the players' stats (off the tick).</summary>
    private void ReportResults(MatchResults results)
    {
        _roundMatchId = null;
        if (Master is not { Trusted: true, ServerId: { } serverId } link || Settings.Master?.ServerKey is not { Length: > 0 })
        {
            return;
        }

        // A practice round (unlimited lives, !heal and !tank) is not a real match: it counts for no stats or achievements.
        if (_practiceRound)
        {
            Log("practice round: results not reported to the master");
            return;
        }

        var rows = new List<ResultsReportRow>();
        foreach (ResultRow row in results.Rows)
        {
            SessionPlayer? p = _players.Find(x => x.Id == row.Id);
            rows.Add(new ResultsReportRow
            {
                AccountId = p?.AccountId, Name = row.Name, Team = row.Team, Score = row.Score, Kills = row.Kills, Deaths = row.Deaths,
                Bot = p?.IsBot ?? false,
                Feats = row.Feats ?? new Dictionary<string, int>(),
            });
        }

        _roundMatchId = link.ReportResults(new ResultsReport
        {
            ServerId = serverId, Map = results.Map, Mode = (int)Setup.Mode, WinningTeam = results.WinningTeam,
            WinningReason = results.WinningReason, ObjectivesTaken = results.ObjectivesTaken, ObjectivesNeeded = results.ObjectivesNeeded,
            DurationSeconds = _matchStart > 0 ? (int)Math.Max(0, (_now - _matchStart) / Second) : 0, Rows = rows,
        });
    }

    /// <summary>The round's line in the server's match log (Settings.MatchLog), when it keeps one.</summary>
    private void WriteMatchLog(MatchResults results)
    {
        if (Settings.MatchLog is not { } log)
        {
            return;
        }

        var players = new List<MatchLogPlayer>();
        foreach (ResultRow row in results.Rows)
        {
            SessionPlayer? p = _players.Find(x => x.Id == row.Id);
            players.Add(new MatchLogPlayer(row.Name, row.Team, row.Score, row.Kills, row.Deaths, row.Lives, p?.IsBot ?? false,
                p is { Verified: true } ? p.AccountId : null, p?.PublicKey, p?.PingMs ?? 0));
        }

        var entry = new MatchLogEntry
        {
            StartedUtc = _matchStart > 0 ? _matchStartUtc : Settings.WallClock.GetUtcNow(), EndedUtc = Settings.WallClock.GetUtcNow(),
            Server = Settings.Name, Round = RoundsStarted, Setup = Setup, Results = results, Players = players,
        };
        if (!log.Write(entry, out string? error))
        {
            Log($"match log {log.Path}: {error}");
        }
    }

    [PortOf("uc:GUI.Ending_Menu:Timer", Spec = "spec/game/round-travel.md#rules")]
    private void TickResults()
    {
        bool everyoneReady = true;
        int present = 0;
        foreach (SessionPlayer p in _players)
        {
            if (p.Slot is null || p.IsSpectator)
            {
                continue;
            }

            present++;
            everyoneReady &= p.Ready;
        }

        bool timeUp = _resultsEnd >= 0 && _now >= _resultsEnd;
        if ((present > 0 && everyoneReady) || timeUp)
        {
            NextRound();
        }
        else if (present == 0 && _now - _phaseStart > 5 * Second)
        {
            BackToLobby(null);
        }
    }

    /// <summary>
    /// After the results: the next game of the series (EndingMenuRules.NextRoundUrl: GamesInARow one lower, revenge
    /// points swapped), else a rematch of the same setup on a listen host whose players all asked for one, else the
    /// lobby with the next map of the rotation.
    /// </summary>
    private void NextRound()
    {
        if (Host.Game is not { } game)
        {
            BackToLobby(null);
            return;
        }

        string url = EndingMenuRules.NextRoundUrl(game, Setup.Revenge ? LobbySetup.RevengeGames : Setup.GamesInARow);
        if (!url.StartsWith(MapCatalog.Lobby + "?", StringComparison.Ordinal))
        {
            int q = url.IndexOf('?', StringComparison.Ordinal);
            int players = CountPlayers(spectators: false);
            string options = url[q..] + "?NBPlayers=" + players.ToString(CultureInfo.InvariantCulture);
            BeginTravel(url[..q], options, SessionPhase.Loading);
            return;
        }

        // The series is over: everyone goes back to the lobby with the session's options (Ending_Menu.Timer), where
        // the host launches again for a rematch. A dedicated server moves on with its rotation; Random Map draws the
        // next map (spec/game/lobby-and-match-creation.md rules 19 and 20).
        if (Settings.Dedicated && Settings.Rotation.Count > 0)
        {
            _rotationIndex = (_rotationIndex + 1) % Settings.Rotation.Count;
            ApplySetup(KeepPractice(Settings.Rotation[_rotationIndex]), announce: true);
        }

        if (Setup.RandomMap && RandomMap() is { } next)
        {
            LobbySetup fresh = LobbySetup.ForMap(next, Setup.Mode, Maps.GetMapSettings(next), Setup.MaxPlayers);
            ApplySetup(Setup with
            {
                Map = next, Disks = fresh.Disks, DisksToWin = fresh.DisksToWin, DisksMax = fresh.DisksMax,
                ScoreLimit = Setup.Mode == EGameMode.GM_DiskHunt ? fresh.ScoreLimit : Setup.ScoreLimit,
            }, announce: true);
        }

        BackToLobby(null);
    }

    /// <summary>
    /// Random Map (Ending_Menu.ChooseRandomMap): a map drawn from the installed maps that have the mode, never the one
    /// just played; each is drawn once before the pool refills. Null when no other map has the mode.
    /// </summary>
    [PortOf("uc:GUI.Ending_Menu:ChooseRandomMap", Spec = "spec/game/lobby-and-match-creation.md#rules")]
    private string? RandomMap()
    {
        _randomPool.RemoveAll(m => string.Equals(m, Setup.Map, StringComparison.OrdinalIgnoreCase));
        if (_randomPool.Count == 0)
        {
            // The host list (docs/MAPS.md "Order"): never a tutorial, front end or test map.
            foreach (string m in Maps.RotationMaps())
            {
                if (!MapCatalog.IsMenuMap(m) && !string.Equals(m, Setup.Map, StringComparison.OrdinalIgnoreCase) && HasMode(m, Setup.Mode))
                {
                    _randomPool.Add(m);
                }
            }
        }

        if (_randomPool.Count == 0)
        {
            return null;
        }

        // xorshift64: the server's own stream, seeded from the match seed.
        _random ^= _random << 13;
        _random ^= _random >> 7;
        _random ^= _random << 17;
        int pick = (int)(_random % (ulong)_randomPool.Count);
        string map = _randomPool[pick];
        _randomPool.RemoveAt(pick);
        return map;
    }

    private bool HasMode(string map, EGameMode mode)
    {
        try
        {
            return Maps.GetMapSettings(map) is not { } settings || settings.SearchForGameMode(USMatchInfo.RulesModeFor(mode)) >= 0;
        }
        catch (Exception e) when (e is SvMapFormatException or IOException)
        {
            // An incomplete/unsupported conversion is not a rotation candidate. One bad installed map must not
            // terminate a running session when the revenge pair returns to the lobby.
            return false;
        }
    }

    private void BackToLobby(string? why)
    {
        if (why is not null)
        {
            Announce(why);
        }

        BeginTravel(MapCatalog.Lobby, null, SessionPhase.Lobby);
    }

    /// <summary>The round's achievement feats of one player: the counted ones plus those read from retail statistics.</summary>
    private static Dictionary<string, int> FeatsOf(PlayerReplicationInfo pri)
    {
        var feats = new Dictionary<string, int>(StringComparer.Ordinal);
        if (pri.Stats is { } s)
        {
            foreach ((string key, int value) in s.Feats)
            {
                feats[key] = value;
            }

            feats[FeatKeys.SpyStickyKos] = s.Spy_StickyCamKOs;
            feats[FeatKeys.MercChargeHits] = s.Merc_ChargeHits;
            feats[FeatKeys.SpySmokeKos] = s.Spy_SmokeKOs;
            feats[FeatKeys.SpyStickyCamUses] = s.Spy_StickyCamUsed;
            feats[FeatKeys.SpyShockerHeadshots] = s.Spy_ShockerGunHeadshots;
            feats[FeatKeys.SpyLedgeGrabs] = s.Spy_LedgeGrabs;
            feats[FeatKeys.MercTaserHits] = s.Merc_TaserHits;
            feats[FeatKeys.MercMinesPlaced] = s.Merc_MinesPlaced;
            feats[FeatKeys.MercBerserkHits] = s.Merc_BerserkHits;
        }

        feats[FeatKeys.SpyObjectives] = pri.iNbObjectivesTaken;
        foreach (string key in feats.Keys.Where(k => feats[k] <= 0).ToList())
        {
            feats.Remove(key);
        }

        return feats;
    }

    private MatchResults BuildResults()
    {
        SGameInfo? game = Host.Game;
        GameReplicationInfo? gri = game?.GameReplicationInfo;
        var rows = new List<ResultRow>();
        foreach (SessionPlayer p in _players)
        {
            PlayerController? controller = p.IsBot ? _bots?.ControllerOf(p.Id) : p.Slot?.Controller;
            if (controller?.PlayerReplicationInfo is not { } pri || p.IsSpectator)
            {
                continue;
            }

            rows.Add(new ResultRow(p.Id, p.Name, (byte)(pri.Team?.TeamIndex ?? p.Team), (int)pri.Score, pri.Kills, pri.Deaths,
                pri.NumLives >= OpenScct.Game.GameFlow.K.UnlimitedLives ? -1 : pri.NumLives, FeatsOf(pri)));
        }

        rows.Sort((a, b) => a.Team != b.Team ? a.Team.CompareTo(b.Team) : b.Score.CompareTo(a.Score));
        int taken = 0;
        int needed = 0;
        if (game is not null)
        {
            EGameMode mode = game.MatchInfo?.GameMode ?? EGameMode.GM_Undefined;
            foreach (SMission m in Host.World.Level.AllActors<SMission>())
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
        }

        int gamesLeft = Math.Max(0, (game?.MatchInfo?.GamesInARow ?? 1) - 1);
        return new MatchResults
        {
            Map = Setup.Map, WinningTeam = gri?.WinningTeam ?? 255, WinningReason = gri?.WinningReason ?? 255,
            ObjectivesTaken = taken, ObjectivesNeeded = needed, GamesLeft = gamesLeft, Rows = rows,
        };
    }

    // ---- players ------------------------------------------------------------------------------------------------

    /// <summary>The people of the session on a side (or spectating), the bots left out.</summary>
    private int CountHumans(bool spectators)
    {
        int n = 0;
        foreach (SessionPlayer p in _players)
        {
            if (!p.IsBot && p.IsSpectator == spectators)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Players (or spectators) of the session, bots included: joined, joining, or expected back after a travel.</summary>
    private int CountPlayers(bool spectators)
    {
        int n = 0;
        foreach (SessionPlayer p in _players)
        {
            if (p.IsSpectator == spectators)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The Hello of a connection: a returning player (its token), or a new one within the limits.</summary>
    private LoginDecision FilterLogin(NetConnection connection, string name, string options)
    {
        if (_closing)
        {
            return LoginDecision.Refuse("The server is shutting down.");
        }

        ulong token = RelayFrames.TryParseToken(UcOptions.ParseOption(options, "SvmToken"), out ulong t) ? t : 0;
        string installId = UcOptions.ParseOption(options, "SvmId");

        // Proven by the join challenge (ServerHost.KeyChallenge drops a key the client wrote itself).
        string playerKey = UcOptions.ParseOption(options, "SvmKey");
        string address = Host.Driver.Transport.RemoteAddress(connection.RemoteEndpoint) ?? "this machine";
        SessionPlayer? player = token == 0 ? null : _players.Find(p => p.Token == token);
        if (Settings.Bans?.Find(installId, address, player?.Verified == true ? null : name, player?.AccountId,
            playerKey.Length > 0 ? playerKey : null) is { } ban)
        {
            return Banned(ban);
        }

        if (player is not null && player.PublicKey is { } known && known != playerKey)
        {
            // The session token came back with another key (or none): not the same player.
            return LoginDecision.Refuse("The player key check failed.");
        }

        if (player is not null)
        {
            if (player.Slot is { } old)
            {
                player.Slot = null;
                if (old.Connection.State != EConnectionState.USOCK_Closed)
                {
                    old.Connection.Close("Joined again");
                }
            }

            player.Expected = true;
        }
        else
        {
            bool host = !Settings.Dedicated && Settings.HostKey is { Length: > 0 } key && UcOptions.ParseOption(options, "SvmHost") == key;
            PlayerTicket? ticket = CheckTicket(UcOptions.ParseOption(options, "SvmTicket"), out string ticketProblem);
            HostAccount? hostAccount = host && ticket is null ? Settings.HostAccount : null;

            // A player who dropped out of the round comes back to his place; a signed-in one (his ticket proves who
            // he is) needs no password, as he may be rejoining from another PC.
            DroppedPlayer? dropped = host ? null : FindDropped(ticket?.AccountId, installId, playerKey);
            if (Settings.Password is { Length: > 0 } password && UcOptions.ParseOption(options, "Password") != password &&
                dropped is not { AccountId: not null })
            {
                return LoginDecision.Refuse(UcOptions.ParseOption(options, "Password").Length == 0
                    ? "This server needs a password."
                    : "Wrong password.");
            }
            if (hostAccount is not null && Settings.Bans?.Find(null, null, null, hostAccount.AccountId) is { } hostBan)
            {
                return Banned(hostBan);
            }

            if (ticket is not null)
            {
                if (Settings.Bans?.Find(null, null, null, ticket.AccountId) is { } accountBan)
                {
                    return Banned(accountBan);
                }

                if (_players.Exists(p => p.AccountId == ticket.AccountId))
                {
                    return LoginDecision.Refuse("You are already on this server.");
                }
            }
            else if (RequireAccounts && !host)
            {
                Log($"refused {SessionProtocol.Clean(name)} from {address}: {ticketProblem}");
                return LoginDecision.Refuse(Master?.TicketKey is null || Master.ServerId is null
                    ? "This server is for signed-in players and is not registered with the master server yet; try again in a moment."
                    : "This server is for signed-in players: sign in on the Account page (main menu).");
            }

            int requested = UcOptions.GetIntOption(options, "Team", -1);
            bool wantsSpectator = requested == SessionProtocol.SpectatorTeam ||
                UcStrings.EqualsIgnoreCase(UcOptions.ParseOption(options, "SpectatorOnly"), "true");
            bool spectator = wantsSpectator || (Phase != SessionPhase.Lobby && dropped is null);
            // Bots never take a person's place: they make room (UpdateBots) when he joins.
            if (!spectator && CountHumans(spectators: false) >= Settings.MaxPlayers)
            {
                spectator = true;
            }

            if (spectator && CountPlayers(spectators: true) >= Settings.MaxSpectators && !host)
            {
                return LoginDecision.Refuse(Phase == SessionPhase.Lobby ? "The server is full" : "A round is being played and the spectator places are taken.");
            }

            // A claimed name is the account's own (never with '#'); a guest of a server on a master is Name#tag, so
            // nobody passes for a signed-in player. Without a master there are no claimed names to keep apart.
            string shown = ticket is not null
                ? ticket.Name
                : hostAccount is not null
                ? hostAccount.Name
                : UniqueName(Settings.Master is not null ? PlayerNames.GuestName(name, installId) : name);
            bool masterAdmin = ticket is { IsAdmin: true } && Master is { Trusted: true } && Settings.Master?.ServerKey is { Length: > 0 };
            CosmeticUnlocks unlocks = UnlocksOf(ticket, hostAccount);
            player = new SessionPlayer(_nextPlayerId++, shown, token == 0 ? NewToken() : token)
            {
                InstallId = installId.Length > 0 ? installId : null,
                PublicKey = playerKey.Length > 0 ? playerKey : null,
                AccountId = ticket?.AccountId ?? hostAccount?.AccountId,
                Verified = ticket is not null || hostAccount is not null,
                Unlocks = unlocks,
                SpyLook = CheckedLook(options, "SvmLookS", CosmeticSide.Spy, unlocks),
                MercLook = CheckedLook(options, "SvmLookM", CosmeticSide.Merc, unlocks),
                Rank = PlayerRanks.Checked(UcOptions.ParseOption(options, "SvmTitle"), ticket, hostAccount),
                IsAdmin = masterAdmin,
                IsHost = host,
                Ready = Settings.ReadyOnJoin,
                Team = spectator ? SessionProtocol.SpectatorTeam
                    : dropped is not null ? dropped.Team
                    : (byte)(requested is 0 or 1 && !(Setup.AutoBalance && CountTeam((byte)requested) > CountTeam((byte)(1 - requested)))
                        ? requested : SmallerTeam()),
                Rejoin = spectator ? null : dropped,
            };
            _players.Add(player);
            if (player.Rejoin is { } place)
            {
                _dropped.Remove(place);
            }

            if (ticket is not null)
            {
                _usedTickets[ticket.TicketId] = ticket.Expires;
            }

            Log(ticket is not null
                ? $"{player.Name} joined from {address} (id {player.Id}, signed in, account {ticket.AccountId}{(masterAdmin ? ", master admin" : "")})"
                : hostAccount is not null
                ? $"{player.Name} joined from {address} (id {player.Id}, the host, signed in on this game, account {hostAccount.AccountId})"
                : $"{player.Name} joined from {address} (id {player.Id}, guest: {ticketProblem})");
            if (player.PublicKey is { } proven)
            {
                Log($"{player.Name} has player key {PlayerKey.Short(proven)}");
            }

            if (player.Rejoin is not null)
            {
                Log($"{player.Name} rejoins round {RoundsStarted} on side {player.Team}");
            }
        }

        player.Address = address;
        player.Endpoint = connection.RemoteEndpoint;
        _byConnection[connection] = player;
        return LoginDecision.Allow(player.Name, MatchLoginOptions(player));
    }

    private static LoginDecision Banned(BanEntry ban) =>
        LoginDecision.Refuse(ban.Reason.Length > 0 ? "You are banned from this server: " + ban.Reason : "You are banned from this server.");

    /// <summary>Only signed-in players may join (and a master link exists that could give them tickets).</summary>
    private bool RequireAccounts => Settings.RequireAccounts && Master is not null;

    /// <summary>
    /// A new player's master ticket when it is good: signed by the master, for this server, not expired and not used
    /// before (each ticket id is taken once). Null with the reason otherwise.
    /// </summary>
    private PlayerTicket? CheckTicket(string text, out string problem)
    {
        if (text.Length == 0)
        {
            problem = "no ticket";
            return null;
        }

        if (Master is not { TicketKey: { } key, ServerId: { } serverId })
        {
            problem = "no ticket key from the master yet";
            return null;
        }

        DateTimeOffset now = Settings.WallClock.GetUtcNow();
        if (_usedTickets.Count > 0)
        {
            var expired = new List<ulong>();
            foreach ((ulong id, DateTimeOffset expires) in _usedTickets)
            {
                if (expires < now)
                {
                    expired.Add(id);
                }
            }

            foreach (ulong id in expired)
            {
                _usedTickets.Remove(id);
            }
        }

        if (!PlayerTicket.TryVerify(text, key, serverId, now, out PlayerTicket? ticket, out string why) || ticket is null)
        {
            problem = why;
            return null;
        }

        // Taken for good once the player is in (FilterLogin): a refusal for a full server leaves it usable.
        if (_usedTickets.ContainsKey(ticket.TicketId))
        {
            problem = "ticket already used";
            return null;
        }

        problem = string.Empty;
        return ticket;
    }

    /// <summary>The signed-in players' accounts, for the master's heartbeat.</summary>
    private IReadOnlyList<long> SignedInAccounts()
    {
        var ids = new List<long>();
        foreach (SessionPlayer p in _players)
        {
            if (p.AccountId is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>After a "restart": launches the same round again once nobody is still loading back into the lobby.</summary>
    private void TickRelaunch()
    {
        if (_relaunchAt < 0 || _now < _relaunchAt || _players.Exists(p => p.Expected))
        {
            return;
        }

        _relaunchAt = -1;
        if (LaunchProblem() is { } problem)
        {
            Announce("Could not restart the round: " + problem);
            return;
        }

        Launch();
    }

    /// <summary>Every player for the master's admin portal.</summary>
    private List<HeartbeatPlayer> PlayerDetailsForMaster()
    {
        var list = new List<HeartbeatPlayer>(_players.Count);
        foreach (SessionPlayer p in _players)
        {
            list.Add(new HeartbeatPlayer
            {
                Id = p.Id, Name = p.Name, Team = p.Team, PingMs = p.PingMs, AccountId = p.AccountId, Bot = p.IsBot, Admin = p.IsAdmin,
                Host = p.IsHost,
            });
        }

        return list;
    }

    /// <summary>
    /// Admin commands from the master's portal (trusted servers only): each runs as a console line and its output goes
    /// back with the next heartbeat. Closing the server is not taken from the master.
    /// </summary>
    private void OnMasterCommands(IReadOnlyList<MasterCommand> commands)
    {
        foreach (MasterCommand command in commands)
        {
            string verb = command.Line.Trim().Split(' ', 2)[0].ToLowerInvariant();
            IReadOnlyList<string> output = verb is "close" or "quit" or "exit"
                ? ["the master cannot close the server"]
                : Execute(command.Line);
            Log("master admin: " + command.Line + (output.Count > 0 ? " -> " + string.Join(" | ", output) : string.Empty));
            Master?.AddCommandResult(command.Id, output.Count > 0 ? string.Join("\n", output) : "done");
        }
    }

    /// <summary>The master banned these accounts: their players leave (the server's own ban list is not touched).</summary>
    private void OnMasterKicks(IReadOnlyList<long> accounts)
    {
        var banned = new HashSet<long>(accounts);
        foreach (SessionPlayer p in _players.ToArray())
        {
            if (p.AccountId is { } id && banned.Contains(id) && !_kicks.Exists(k => ReferenceEquals(k.Player, p)))
            {
                Kick(p, "You are banned from this master.");
            }
        }
    }

    private static ulong NewToken()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes) | 1;
    }

    /// <summary>The login options of a player for the current map's game rules (its side, and the series count for the revenge swap).</summary>
    private string MatchLoginOptions(SessionPlayer p) => (Settings.Master is null ? "?SvmSidearmsAll=1" : "?SvmSidearmsAll=0") + "?Team=" + p.Team.ToString(CultureInfo.InvariantCulture) + SeriesOptions(Host.Game) +
        (p.Team switch { 0 => p.SpyLook, 1 => p.MercLook, _ => CosmeticLook.Default } is var look && !look.IsDefault ? "?SvmLook=" + look.ToHex() : string.Empty) +
        (p.Rank != 0 ? "?SvmRank=" + p.Rank.ToString(CultureInfo.InvariantCulture) : string.Empty) +
        (p.Rejoin is not null ? "?SvmRejoin=1" : string.Empty);

    /// <summary>A look from the join's options with every item the ticket does not unlock set back to retail.</summary>
    internal static CosmeticLook CheckedLook(string options, string key, CosmeticSide side, CosmeticUnlocks unlocks) =>
        CosmeticLook.ParseHex(UcOptions.ParseOption(options, key)).Validate(side, unlocks);

    /// <summary>What a joining player has unlocked: the master's ticket, or the listen host's own account; none for guests.</summary>
    internal static CosmeticUnlocks UnlocksOf(PlayerTicket? ticket, HostAccount? host) =>
        ticket is not null ? new CosmeticUnlocks(ticket.Unlocks)
        : host?.Unlocked is { } ids ? CosmeticUnlocks.Of(ids)
        : CosmeticUnlocks.None;

    /// <summary>The series count of a revenge game's login (Login swaps the sides of the second game), or nothing.</summary>
    private static string SeriesOptions(SGameInfo? game) =>
        game?.MatchInfo is { Revenge: true } match ? "?GamesInARow=" + match.GamesInARow.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private string UniqueName(string name)
    {
        string clean = SessionProtocol.Clean(name).Trim();
        if (clean.Length == 0)
        {
            clean = "Player";
        }

        if (clean.Length > OpenScct.Game.GameFlow.K.PlayerNameLength)
        {
            clean = clean[..OpenScct.Game.GameFlow.K.PlayerNameLength];
        }

        string candidate = clean;
        for (int n = 2; _players.Exists(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
        {
            string suffix = "(" + n.ToString(CultureInfo.InvariantCulture) + ")";
            candidate = (clean.Length + suffix.Length > OpenScct.Game.GameFlow.K.PlayerNameLength
                ? clean[..(OpenScct.Game.GameFlow.K.PlayerNameLength - suffix.Length)]
                : clean) + suffix;
        }

        return candidate;
    }

    private byte SmallerTeam() => CountTeam(0) <= CountTeam(1) ? (byte)0 : (byte)1;

    private int CountTeam(byte team) => _players.FindAll(p => p.Team == team && !p.IsBot).Count;

    /// <summary>
    /// The lobby's bots for the setup and the people on each side (<see cref="OnlineBotRoster.Wanted"/>): the last
    /// bots of a side leave when it has too many, new ones join (always ready) when it has too few. Only in the lobby:
    /// a series keeps its bots from map to map, and the revenge game swaps their sides as it swaps everyone's.
    /// </summary>
    private void UpdateBots()
    {
        if (Phase != SessionPhase.Lobby || _travel is not null)
        {
            return;
        }

        int spies = 0;
        int mercs = 0;
        foreach (SessionPlayer p in _players)
        {
            if (!p.IsBot && p.Team == 0)
            {
                spies++;
            }
            else if (!p.IsBot && p.Team == 1)
            {
                mercs++;
            }
        }

        (int wantSpies, int wantMercs) = Settings.Bots is null ? (0, 0) : OnlineBotRoster.Wanted(Setup, spies, mercs, Settings.MaxPlayers);
        bool changed = SyncBots(0, wantSpies);
        changed |= SyncBots(1, wantMercs);
        if (changed)
        {
            Master?.HeartbeatSoon();
        }
    }

    private bool SyncBots(byte team, int wanted)
    {
        List<SessionPlayer> side = _players.FindAll(p => p.IsBot && p.Team == team);
        bool changed = false;
        for (int i = side.Count - 1; i >= wanted; i--)
        {
            _players.Remove(side[i]);
            Send(null, SessionProtocol.Line("gone", side[i].Id));
            changed = true;
        }

        for (int n = side.Count; n < wanted; n++)
        {
            int number = 1;
            while (_players.Exists(p => string.Equals(p.Name, OnlineBotRoster.NameOf(team, number), StringComparison.OrdinalIgnoreCase)))
            {
                number++;
            }

            var bot = new SessionPlayer(_nextPlayerId++, OnlineBotRoster.NameOf(team, number), 0)
            {
                IsBot = true, Team = team, Ready = true, Map = MapState.Ok,
            };
            _players.Add(bot);
            BroadcastPlayer(bot);
            changed = true;
        }

        return changed;
    }

    private void OnPlayerJoined(PlayerSlot slot)
    {
        if (!_byConnection.TryGetValue(slot.Connection, out SessionPlayer? p))
        {
            slot.Connection.Close("Unknown player");
            return;
        }

        bool returning = p.Expected;
        p.Slot = slot;
        p.Expected = false;
        slot.Tag = p;
        if (slot.Controller is SPlayerController pc)
        {
            pc.bTeamVoiceOnly = p.TeamVoiceOnly;
        }

        if (slot.Controller?.PlayerReplicationInfo is { } voicePri)
        {
            voicePri.bVoiceBanned = p.VoiceBanned;
        }

        // The session keeps the side the player chose in the lobby (the original's profile team): a revenge game logs
        // him in on the other side (Login), but back in the lobby he is on his own side again. Only a player the
        // lobby gave no side yet takes the side the game put him on.
        if (slot.Controller?.PlayerReplicationInfo?.Team is { } team && !p.IsSpectator && p.Team is not (0 or 1))
        {
            p.Team = (byte)team.TeamIndex;
        }

        SendWelcome(p);
        BroadcastPlayer(p);
        Rejoined(p, slot);
        if (Phase == SessionPhase.Lobby)
        {
            UpdateBots();
        }

        if (!returning && Phase == SessionPhase.Lobby)
        {
            Announce($"{p.Name} joined.");
            if (Settings.Motd is { Length: > 0 } motd)
            {
                SendMessage(p, motd);
            }
        }

        Master?.HeartbeatSoon();
    }

    private void OnPlayerLeft(PlayerSlot slot)
    {
        if (!_byConnection.Remove(slot.Connection, out SessionPlayer? p) || _travel is not null)
        {
            return;
        }

        // The player's own connection ended, or a new player left before it finished joining. A returning player
        // whose new connection failed stays expected until the load timeout.
        if (ReferenceEquals(p.Slot, slot) || (p.Slot is null && !p.Expected))
        {
            p.Slot = null;
            KeepDropped(p, slot.Controller);
            RemovePlayer(p, $"{p.Name} left.");
        }
    }

    private void RemovePlayer(SessionPlayer p, string? message)
    {
        if (!_players.Remove(p))
        {
            return;
        }

        if (p.Endpoint.IsValid)
        {
            _transport.DisconnectNow(p.Endpoint);
        }

        Send(null, SessionProtocol.Line("gone", p.Id));
        if (message is not null)
        {
            Announce(message);
        }

        // Bots alone do not keep a round going.
        if (CountHumans(spectators: false) + CountHumans(spectators: true) == 0 && Phase != SessionPhase.Lobby && _travel is null)
        {
            BackToLobby(null);
        }

        UpdateBots();
        Master?.HeartbeatSoon();
    }

    // ---- discovery ----------------------------------------------------------------------------------------------

    /// <summary>The server's info for browsers, the LAN beacon and the master.</summary>
    public ServerInfo Info()
    {
        var list = new List<ServerInfoPlayer>();
        int players = 0;
        foreach (SessionPlayer p in _players)
        {
            list.Add(new ServerInfoPlayer(p.Name, p.Team));

            // The browser's count (and "full") is of people: a bot gives its place to anyone who joins.
            if (!p.IsSpectator && !p.IsBot)
            {
                players++;
            }
        }

        int left = Phase == SessionPhase.Match && Host.Game?.GameReplicationInfo is { } gri && gri.TimeLimit > 0
            ? Math.Max(0, (gri.TimeLimit * 60) - gri.ElapsedTime)
            : 0;
        return new ServerInfo
        {
            Name = Settings.Name, Map = Setup.Map, Mode = (byte)Setup.Mode, Phase = Phase, Players = players,
            MaxPlayers = Settings.MaxPlayers, GamePort = GamePort, ProtocolHash = Host.Driver.ProtocolHash,
            ContentHash = Maps.FindMap(Setup.Map)?.ContentHash ?? 0, HasPassword = Settings.Password is { Length: > 0 },
            Dedicated = Settings.Dedicated, SecondsLeft = left, PlayerList = list,
            MasterId = Master?.ServerId ?? string.Empty, RequireAccounts = RequireAccounts, Practice = Setup.Practice,
        };
    }

    private void OnUnconnected(IPEndPoint from, ReadOnlySpan<byte> data, bool broadcast)
    {
        switch (DiscoveryPackets.TypeOf(data))
        {
            case DiscoveryPackets.Query when DiscoveryPackets.IsQuery(data) && _answers.Allow(from.Address, _now / 1000):
            {
                int n = DiscoveryPackets.WriteInfo(_infoBuffer, DiscoveryPackets.NonceOf(data), Info());
                if (n > 0)
                {
                    _udp!.SendUnconnected(from, _infoBuffer.AsSpan(0, n));
                }

                break;
            }

            case DiscoveryPackets.Probe:
            {
                int n = DiscoveryPackets.WriteHeader(_probeBuffer, DiscoveryPackets.ProbeReply, DiscoveryPackets.NonceOf(data));
                _udp!.SendUnconnected(from, _probeBuffer.AsSpan(0, n));
                break;
            }
        }
    }

    // ---- shutdown -----------------------------------------------------------------------------------------------

    /// <summary>Tells every player the server closes, takes it off the master and asks the host process to stop.</summary>
    public void Close(string reason = "The server is shutting down.")
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        foreach (SessionPlayer p in _players)
        {
            if (p.Slot is { } slot)
            {
                slot.Connection.ControlChannel?.Send(ControlMessage.TextLine(SessionProtocol.Line("kicked", reason)));
            }
        }

        Host.AcceptingConnections = false;
        Master?.Unlist();

        // Half a second more of ticks gets the reason through before the socket closes.
        _closeAt = _now + (Second / 2);
        Log("closing: " + reason);
    }

    /// <summary>
    /// Closes and keeps ticking (at most a second) until the players had the reason, for a host loop that already
    /// stopped (Ctrl+C, SIGTERM).
    /// </summary>
    public void Shutdown(IClock clock, string reason = "The server is shutting down.")
    {
        ArgumentNullException.ThrowIfNull(clock);
        Close(reason);
        long end = clock.NowMicroseconds + Second;
        while (!CloseRequested && clock.NowMicroseconds < end)
        {
            Tick(clock.NowMicroseconds);
            Thread.Sleep(16);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_closing)
        {
            Master?.Unlist();
        }

        Master?.Dispose();
        _lan?.Dispose();
        _bots?.Dispose();
        _bots = null;
        FinishDemo(Host, null);
        Host.Dispose();
        _transport.Dispose();
    }
}
