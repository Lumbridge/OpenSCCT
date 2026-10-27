using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using OpenScct.Net;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

/// <summary>Where a client's visit is.</summary>
public enum OnlineClientState
{
    /// <summary>Trying the join attempts.</summary>
    Connecting,

    Lobby,

    /// <summary>Between maps: the old connection closed, the next one starts after the quiet time.</summary>
    Travelling,

    /// <summary>Connected to the round's map; the round has not started.</summary>
    Loading,

    Match,

    Results,

    /// <summary>The visit ended with an error (<see cref="OnlineClient.Error"/>).</summary>
    Failed,

    /// <summary>The player left.</summary>
    Left,
}

/// <summary>What a session factory gets: the transport generation, the server address, the time and the client settings.</summary>
public sealed record ClientSessionRequest(ITransport Transport, string Address, long NowMicros, ClientSettings Settings, string? Map);

/// <summary>Settings of a client's online visit.</summary>
public sealed record OnlineClientSettings
{
    public string PlayerName { get; init; } = "Player";

    /// <summary>The install's id (bans and returning players); empty sends none.</summary>
    public string InstallId { get; init; } = string.Empty;

    /// <summary>Preferred side when joining a lobby: 0 spies, 1 mercs, -1 the smaller one.</summary>
    public int PreferredTeam { get; init; } = -1;

    public string? Password { get; init; }

    /// <summary>The listen host's key, for the host's own player.</summary>
    public string? HostKey { get; init; }

    /// <summary>The master's ticket for this server (signed-in players; <see cref="OpenScct.Net.Discovery.PlayerTicket"/> text), or null.</summary>
    public string? Ticket { get; init; }

    /// <summary>The player's chosen spy and merc looks (CosmeticLook hex); the server keeps only what the ticket unlocks.</summary>
    public string? SpyLook { get; init; }

    public string? MercLook { get; init; }

    /// <summary>The title the player shows (OpenScct.Net.Cosmetics.TitleCatalog id); the server keeps it only when the ticket unlocks it.</summary>
    public string? Title { get; init; }

    public MapCatalog? Maps { get; init; }

    public NetClassRegistry? Classes { get; init; }

    /// <summary>Bind the client socket to loopback only (tests).</summary>
    public bool LoopbackOnly { get; init; }

    /// <summary>Seconds one attempt waits for the server's welcome.</summary>
    public float ConnectTimeoutSeconds { get; init; } = 10f;

    /// <summary>Seconds a NAT punch may take.</summary>
    public float PunchTimeoutSeconds { get; init; } = 5f;

    /// <summary>
    /// Seconds between the travel line and the next connection: longer than the server's travel grace (1.5 s), so
    /// nothing of the old connection is still on its way (see <see cref="SessionTransport"/>).
    /// </summary>
    public float TravelQuietSeconds { get; init; } = 2f;

    /// <summary>Seconds a travel may take (the server loading the map included) before the visit fails.</summary>
    public float TravelTimeoutSeconds { get; init; } = 90f;

    /// <summary>Net speed asked for, bytes per second.</summary>
    public int NetSpeed { get; init; } = OpenScct.Net.K.ConfiguredInternetSpeed;

    /// <summary>Install the gameplay services after each map load (see <see cref="ClientSettings.InstallGameplayServices"/>).</summary>
    public bool InstallGameplayServices { get; init; } = true;

    /// <summary>
    /// Makes each map's session (null: a plain <see cref="ClientSession"/>). Bots and the game's play scene make their
    /// own, with the request's settings and transport.
    /// </summary>
    public Func<ClientSessionRequest, ClientSession>? SessionFactory { get; init; }

    /// <summary>
    /// Carry voice chat on each map's transport (docs/VOICE.md): <see cref="OnlineClient.Voice"/> is the transport a
    /// <see cref="OpenScct.Net.Voice.VoiceClient"/> connects to, with <see cref="OnlineClient.ServerEndpoint"/>.
    /// </summary>
    public bool Voice { get; init; } = true;
}

/// <summary>
/// A player's online visit to a server (docs/ONLINE.md): joins through the attempts of a <see cref="JoinTarget"/>,
/// holds the lobby as the server describes it (<see cref="Lobby"/>), follows each travel by reconnecting on the same
/// socket after the quiet time, runs each map's <see cref="ClientSession"/>, keeps the results, and turns failures
/// into lines a player can read (<see cref="Error"/>). Godot-free: the game's front end, the bots and the tests drive
/// it the same way. Call <see cref="Tick"/> at 60 Hz from one thread.
/// </summary>
public sealed class OnlineClient : IDisposable
{
    private const long Second = 1_000_000;

    private readonly JoinTarget _target;
    private readonly ulong _token;
    private readonly List<ResultRow> _rows = new();
    private SessionTransport? _transport;
    private ITransport? _inner;
    private int _attempt = -1;
    private long _attemptStart;
    private long _punchSent;
    private int _punchRequests;
    private string? _address;
    private string? _expectedMap;
    private long _travelAt = -1;
    private long _travelStarted;
    private long _now;
    private string? _lastAttemptError;
    private (string Map, ulong Content)? _travelRequest;
    private string? _failRequest;
    private bool _disposed;

    public OnlineClient(JoinTarget target, OnlineClientSettings settings, long nowMicros, ulong token = 0)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Maps = settings.Maps ?? MapCatalog.CreateDefault();
        Classes = settings.Classes ?? NetClasses.Create();
        _token = token != 0 ? token : NewToken();
        _now = nowMicros;
        if (target.Attempts.Count == 0)
        {
            Fail("There is no way to reach this server (no address, punch or relay).");
            return;
        }

        NextAttempt();
    }

    public OnlineClientSettings Settings { get; }

    /// <summary>Where the visit joins (its name, attempts and, for a listed server, the master's id).</summary>
    public JoinTarget Target => _target;

    public MapCatalog Maps { get; }

    public NetClassRegistry Classes { get; }

    public OnlineClientState State { get; private set; } = OnlineClientState.Connecting;

    /// <summary>Why the visit failed, as a player reads it, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The server kicked (or banned) this player.</summary>
    public bool Kicked { get; private set; }

    /// <summary>The lobby as the server describes it.</summary>
    public LobbyState Lobby { get; } = new();

    /// <summary>The current map's session, or null between maps.</summary>
    public ClientSession? Session { get; private set; }

    /// <summary>The map the current session is on (the lobby level or a round's map).</summary>
    public string? CurrentMap { get; private set; }

    /// <summary>The results of the last round.</summary>
    public MatchResults? Results { get; private set; }

    /// <summary>The attempt that reached the server (its kind tells whether the relay carries the game).</summary>
    public JoinAttempt? ConnectedVia { get; private set; }

    /// <summary>
    /// The game's play scene ticks the current round's session with the local input (true while it shows the round);
    /// otherwise the visit ticks it. The lobby's session is always ticked here.
    /// </summary>
    public bool SessionTickedElsewhere { get; set; }

    /// <summary>The underlying socket (for its relay and punch state), or null.</summary>
    public ITransport? Transport => _inner;

    /// <summary>The map a travel goes to (null when not travelling).</summary>
    public string? TravellingTo => State == OnlineClientState.Travelling ? _expectedMap : null;

    /// <summary>The current map's voice transport (null with voice off or between maps).</summary>
    public OpenScct.Net.Voice.VoiceTransport? Voice { get; private set; }

    /// <summary>The server's endpoint on <see cref="Voice"/>.</summary>
    public EndpointId ServerEndpoint => Session?.Connection?.RemoteEndpoint ?? EndpointId.None;

    /// <summary>Lines about the connection for the join screen (attempts, failures).</summary>
    public IReadOnlyList<string> ConnectLog => _connectLog;

    private readonly List<string> _connectLog = new();

    /// <summary>Raised when the lobby, the phase or the results change.</summary>
    public event Action? Changed;

    /// <summary>Raised when a map's session started (after its connection was made).</summary>
    public event Action<ClientSession>? SessionStarted;

    /// <summary>Raised before a map's session is closed for a travel or the end of the visit.</summary>
    public event Action<ClientSession>? SessionEnding;

    /// <summary>Raised when the results of a round arrive.</summary>
    public event Action<MatchResults>? ResultsReceived;

    /// <summary>Raised for each chat or system line.</summary>
    public event Action<SessionMessage>? MessageReceived;

    private static ulong NewToken()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes) | 1;
    }

    // ---- commands -----------------------------------------------------------------------------------------------

    public void SetTeam(int team) => SendLine(SessionProtocol.Line("team", team));

    public void SetReady(bool ready) => SendLine(SessionProtocol.Line("ready", ready));

    /// <summary>The lobby's "Restrict Mic to Team": in the lobby the other team stops hearing this player.</summary>
    public void SetTeamVoiceOnly(bool only) => SendLine(SessionProtocol.Line("teamvoice", only));

    /// <summary>Host or admin: a new match setup.</summary>
    public void SetSetup(LobbySetup setup) => SendLine(SessionProtocol.Line("setup", (setup ?? throw new ArgumentNullException(nameof(setup))).ToOptions()));

    /// <summary>Host or admin: launch from the lobby, or go on to the next round from the results.</summary>
    public void Launch() => SendLine(SessionProtocol.Line("launch"));

    /// <summary>Host or admin: back to the lobby.</summary>
    public void BackToLobby() => SendLine(SessionProtocol.Line("lobby"));

    public void Kick(int playerId) => SendLine(SessionProtocol.Line("kick", playerId));

    public void Ban(int playerId) => SendLine(SessionProtocol.Line("ban", playerId));

    /// <summary>Logs in as an admin of a dedicated server.</summary>
    public void Admin(string password) => SendLine(SessionProtocol.Line("admin", password));

    /// <summary>An admin command (status, map, kick...); the answers arrive as system lines.</summary>
    public void Command(string command) => SendLine(SessionProtocol.Line("cmd", command));

    /// <summary>A chat line to everyone, or to the player's side.</summary>
    public void Chat(string text, bool teamOnly = false) =>
        Session?.SendChat(teamOnly ? EChatScope.TeamSay : EChatScope.Say, text);

    private void SendLine(string line)
    {
        if (Session is { State: EClientState.Joined } s)
        {
            s.SendText(line);
        }
    }

    /// <summary>Leaves the server (the connection closes; the server tells the others).</summary>
    public void Leave()
    {
        if (State is OnlineClientState.Left or OnlineClientState.Failed)
        {
            return;
        }

        EndSession();
        State = OnlineClientState.Left;
        CloseTransport();
        Changed?.Invoke();
    }

    // ---- ticking ------------------------------------------------------------------------------------------------

    /// <summary>One 60 Hz step: the attempts and travels, then the session (unless the play scene ticks it).</summary>
    public void Tick(long nowMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _now = nowMicros;
        switch (State)
        {
            case OnlineClientState.Connecting:
                TickConnecting();
                break;
            case OnlineClientState.Travelling:
                TickTravelling();
                break;
            case OnlineClientState.Failed or OnlineClientState.Left:
                return;
        }

        if (Session is { } session)
        {
            bool external = SessionTickedElsewhere && !string.Equals(CurrentMap, MapCatalog.Lobby, StringComparison.OrdinalIgnoreCase);
            if (!external)
            {
                session.Tick(nowMicros);
            }

            ApplyRequests();
            if (Session is { } still)
            {
                CheckSession(still);
            }
        }
    }

    /// <summary>
    /// A travel or a kick arrives inside the session's tick; the session is closed only after its tick returns (by the
    /// visit, or by the play scene's next call of <see cref="Tick"/>).
    /// </summary>
    private void ApplyRequests()
    {
        if (_failRequest is { } reason)
        {
            _failRequest = null;
            _travelRequest = null;
            Fail(reason);
            return;
        }

        if (_travelRequest is { } travel)
        {
            _travelRequest = null;
            BeginTravel(travel.Map, travel.Content);
        }
    }

    private void CheckSession(ClientSession session)
    {
        if (!ReferenceEquals(session, Session))
        {
            return;
        }

        if (session.State == EClientState.Rejected)
        {
            string reason = session.RejectReason ?? "The server refused the connection.";
            if (session.RejectFile is { Length: > 0 } file)
            {
                reason += " (" + file + ")";
            }

            Fail(reason);
            return;
        }

        if (session.State == EClientState.Disconnected)
        {
            if (State == OnlineClientState.Connecting)
            {
                _lastAttemptError = "the connection closed";
                NextAttempt();
            }
            else if (State != OnlineClientState.Travelling)
            {
                Fail("The connection to the server was lost.");
            }
        }
    }

    // ---- connecting ---------------------------------------------------------------------------------------------

    private void NextAttempt()
    {
        EndSession();
        CloseTransport();
        _attempt++;
        if (_attempt >= _target.Attempts.Count)
        {
            var kinds = new List<string>();
            foreach (JoinAttempt a in _target.Attempts)
            {
                kinds.Add(a.Kind == ConnectKind.Intro ? "NAT punch" : a.Kind.ToString().ToLowerInvariant());
            }

            string tried = string.Join(", ", kinds);
            Fail($"Could not reach {_target.Name} (tried {tried}" + (_lastAttemptError is { } last ? "; " + last : string.Empty) + ").");
            return;
        }

        JoinAttempt attempt = _target.Attempts[_attempt];
        _attemptStart = _now;
        _connectLog.Add("Trying " + attempt.Describe() + "...");
        try
        {
            _inner = attempt.Kind == ConnectKind.Custom ? _target.CustomTransport!() : LiteNetLibTransport.CreateClient(Settings.LoopbackOnly);
            _transport = new SessionTransport(_inner, ownsInner: true);
            if (attempt.Kind == ConnectKind.Intro)
            {
                _punchRequests = 0;
                _punchSent = 0;
                RequestPunch(attempt);
                return;
            }

            StartSession(attempt.Address, map: null);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or System.Net.Sockets.SocketException or ArgumentException)
        {
            _lastAttemptError = e.Message;
            _connectLog.Add("  " + e.Message);
            NextAttempt();
        }
    }

    private void RequestPunch(JoinAttempt attempt)
    {
        if (_inner is not LiteNetLibTransport udp || attempt.Introducer is null || attempt.PunchToken is null)
        {
            return;
        }

        udp.RequestIntroduction(LiteNetLibTransport.Resolve(attempt.Introducer), "c:" + attempt.PunchToken);
        _punchSent = _now;
        _punchRequests++;
    }

    private void TickConnecting()
    {
        JoinAttempt attempt = _target.Attempts[_attempt];
        long elapsed = _now - _attemptStart;
        if (attempt.Kind == ConnectKind.Intro && Session is null)
        {
            var udp = (LiteNetLibTransport)_inner!;
            udp.Poll();
            if (udp.TryTakePunch("c:" + attempt.PunchToken, out NatPunchResult punch))
            {
                _connectLog.Add("  punched through to " + LiteNetLibTransport.FormatEndPoint(punch.Target));
                StartSession(LiteNetLibTransport.FormatEndPoint(punch.Target), map: null);
                _attemptStart = _now;
                return;
            }

            if (elapsed >= (long)(Settings.PunchTimeoutSeconds * Second))
            {
                _lastAttemptError = "the NAT punch did not get through";
                _connectLog.Add("  " + _lastAttemptError);
                NextAttempt();
            }
            else if (_punchRequests < 3 && _now - _punchSent >= Second)
            {
                // The introducer pairs on the token once; a repeat covers a lost request.
                RequestPunch(attempt);
            }

            return;
        }

        if (Session is { State: EClientState.Joined })
        {
            if (ConnectedVia is null)
            {
                ConnectedVia = attempt;
                _connectLog.Add("  connected");
            }

            return;
        }

        if (_inner is LiteNetLibTransport { LastRelayError: { } relayError })
        {
            _lastAttemptError = relayError;
            _connectLog.Add("  " + relayError);
            NextAttempt();
            return;
        }

        float timeout = attempt.IsLan ? Math.Min(3f, Settings.ConnectTimeoutSeconds) : Settings.ConnectTimeoutSeconds;
        if (elapsed >= (long)(timeout * Second))
        {
            _lastAttemptError = "no answer";
            _connectLog.Add("  no answer");
            NextAttempt();
        }
    }

    private string LoginOptions()
    {
        string options = "?SvmToken=" + RelayFrames.TokenText(_token);
        if (Settings.InstallId.Length > 0)
        {
            options += "?SvmId=" + Clean(Settings.InstallId);
        }

        // 255 asks for a spectator's place (a hub player watching a pad's match).
        if (Settings.PreferredTeam is 0 or 1 or SessionProtocol.SpectatorTeam)
        {
            options += "?Team=" + Settings.PreferredTeam.ToString(CultureInfo.InvariantCulture);
        }

        if (Settings.Password is { Length: > 0 } password)
        {
            options += "?Password=" + Clean(password);
        }

        if (Settings.HostKey is { Length: > 0 } key)
        {
            options += "?SvmHost=" + Clean(key);
        }

        if (Settings.Ticket is { Length: > 0 } ticket)
        {
            options += "?SvmTicket=" + Clean(ticket);
        }

        if (Settings.SpyLook is { Length: > 0 } spyLook && spyLook != "0")
        {
            options += "?SvmLookS=" + Clean(spyLook);
        }

        if (Settings.MercLook is { Length: > 0 } mercLook && mercLook != "0")
        {
            options += "?SvmLookM=" + Clean(mercLook);
        }

        if (Settings.Title is { Length: > 0 } title)
        {
            options += "?SvmTitle=" + Clean(title);
        }

        return options;
    }

    private static string Clean(string value) => value.Replace("?", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

    private void StartSession(string address, string? map)
    {
        _address = address;
        SessionTransport.Generation generation = _transport!.NewGeneration(holdDisconnects: true);
        var settings = new ClientSettings
        {
            PlayerName = Settings.PlayerName,
            LoginOptions = LoginOptions(),
            Gameplay = true,
            InstallGameplayServices = Settings.InstallGameplayServices,
            Maps = Maps,
            Classes = Classes,
            NetSpeed = Settings.NetSpeed,
        };
        ITransport transport = generation;
        if (Settings.Voice)
        {
            Voice = new OpenScct.Net.Voice.VoiceTransport(generation);
            transport = Voice;
        }

        var request = new ClientSessionRequest(transport, address, _now, settings, map);
        ClientSession session = Settings.SessionFactory?.Invoke(request) ?? new ClientSession(transport, address, _now, settings);
        Session = session;
        CurrentMap = map;
        session.SessionLineReceived += OnLine;
        session.ChatReceived += OnChat;
        SessionStarted?.Invoke(session);
    }

    private void EndSession()
    {
        if (Session is not { } session)
        {
            return;
        }

        SessionEnding?.Invoke(session);
        session.SessionLineReceived -= OnLine;
        session.ChatReceived -= OnChat;
        Session = null;
        if (Voice is { } voice)
        {
            voice.Voice = null;
            Voice = null;
        }

        session.Dispose();
    }

    private void CloseTransport()
    {
        _transport?.Dispose();
        _transport = null;
        _inner = null;
    }

    // ---- travel -------------------------------------------------------------------------------------------------

    private void BeginTravel(string map, ulong content)
    {
        if (Maps.FindMap(map) is not { } entry)
        {
            Fail($"The server is changing to {map}, which you do not have.");
            return;
        }

        if (content != 0 && entry.ContentHash != content && !string.Equals(map, MapCatalog.Lobby, StringComparison.OrdinalIgnoreCase))
        {
            Fail($"{ControlMessage.VersionMismatchText} ({map} differs).");
            return;
        }

        _expectedMap = entry.Id;
        EndSession();
        State = OnlineClientState.Travelling;
        _travelAt = _now + (long)(Settings.TravelQuietSeconds * Second);
        _travelStarted = _now;
        Changed?.Invoke();
    }

    private void TickTravelling()
    {
        if (Session is null)
        {
            if (_now < _travelAt)
            {
                return;
            }

            _transport!.Drain();
            StartSession(_address!, _expectedMap);
            return;
        }

        if (Session.State == EClientState.Joined)
        {
            return;
        }

        if (_inner is LiteNetLibTransport { LastRelayError: { } relayError })
        {
            Fail("The relay dropped the connection: " + relayError);
            return;
        }

        bool lost = Session.State is EClientState.Disconnected or EClientState.Rejected;
        if (_now - _travelStarted > (long)(Settings.TravelTimeoutSeconds * Second))
        {
            Fail(Session.RejectReason ?? "The server did not finish changing map.");
            return;
        }

        if (Session.State == EClientState.Rejected)
        {
            Fail((Session.RejectReason ?? "The server refused the connection.") +
                (Session.RejectFile is { Length: > 0 } f ? " (" + f + ")" : string.Empty));
            return;
        }

        if (lost || _now - _travelAt > 15 * Second)
        {
            // The server was still loading: try again on the same socket.
            EndSession();
            _travelAt = _now + Second;
        }
    }

    // ---- lines from the server ----------------------------------------------------------------------------------

    private void OnLine(string line)
    {
        if (!SessionProtocol.TryParse(line, out string command, out List<string> f, FieldCount(line)))
        {
            return;
        }

        switch (command)
        {
            case "hello":
                if (SessionProtocol.Int(f, 0) != SessionProtocol.Version)
                {
                    _failRequest = ControlMessage.VersionMismatchText + " (session protocol).";
                    return;
                }

                Lobby.ClearPlayers();
                Lobby.LocalId = SessionProtocol.Int(f, 1, -1);
                Lobby.Dedicated = SessionProtocol.Int(f, 2) != 0;
                Lobby.MaxPlayers = SessionProtocol.Int(f, 3);
                Lobby.ServerName = SessionProtocol.Text(f, 4);
                if (State is OnlineClientState.Connecting or OnlineClientState.Travelling)
                {
                    CurrentMap = Session?.MapName ?? CurrentMap;
                    State = string.Equals(CurrentMap, MapCatalog.Lobby, StringComparison.OrdinalIgnoreCase) ? OnlineClientState.Lobby : OnlineClientState.Loading;
                    _expectedMap = null;
                }

                break;
            case "setup":
            {
                MasterApi.TryParseHex(SessionProtocol.Text(f, 0), out ulong content);
                Lobby.SetupContentHash = content;
                Lobby.Setup = LobbySetup.FromOptions(SessionProtocol.Text(f, 1), Lobby.Setup);
                Lobby.SetupSource = null;
                CheckMap();
                break;
            }

            case "mapsource":
                // Follows the setup when the host downloaded its map: map, fingerprint, commit, id (with spaces).
                if (string.Equals(SessionProtocol.Text(f, 0), Lobby.Setup.Map, StringComparison.OrdinalIgnoreCase)
                    && SessionProtocol.Text(f, 3) is { Length: > 0 } sourceId)
                {
                    Lobby.SetupSource = new MapSourceInfo(sourceId, SessionProtocol.Text(f, 2), SessionProtocol.Text(f, 1));
                }

                break;

            case "player":
                Lobby.Upsert(new LobbyPlayer(SessionProtocol.Int(f, 0), SessionProtocol.Text(f, 5), (byte)SessionProtocol.Int(f, 1),
                    (SessionPlayerFlags)SessionProtocol.Int(f, 2), SessionProtocol.Int(f, 3), (MapState)SessionProtocol.Int(f, 4)));
                break;
            case "rank":
                Lobby.SetRank(SessionProtocol.Int(f, 0), SessionProtocol.Int(f, 1));
                break;
            case "gone":
                Lobby.Remove(SessionProtocol.Int(f, 0));
                break;
            case "phase":
                Lobby.Phase = SessionProtocol.ParsePhase(SessionProtocol.Text(f, 0));
                Lobby.Countdown = SessionProtocol.Int(f, 1) / 10f;
                if (State is OnlineClientState.Lobby or OnlineClientState.Loading or OnlineClientState.Match or OnlineClientState.Results)
                {
                    State = Lobby.Phase switch
                    {
                        SessionPhase.Lobby => OnlineClientState.Lobby,
                        SessionPhase.Loading => OnlineClientState.Loading,
                        SessionPhase.Match => OnlineClientState.Match,
                        _ => OnlineClientState.Results,
                    };
                }

                break;
            case "msg":
                AddMessage(new SessionMessage(SessionProtocol.Text(f, 0)));
                break;
            case "travel":
                MasterApi.TryParseHex(SessionProtocol.Text(f, 1), out ulong travelContent);
                _travelRequest = (SessionProtocol.Text(f, 0), travelContent);
                break;
            case "row":
                _rows.Add(new ResultRow(SessionProtocol.Int(f, 0), SessionProtocol.Text(f, 6), (byte)SessionProtocol.Int(f, 1),
                    SessionProtocol.Int(f, 2), SessionProtocol.Int(f, 3), SessionProtocol.Int(f, 4), SessionProtocol.Int(f, 5)));
                break;
            case "results":
                Results = new MatchResults
                {
                    WinningTeam = SessionProtocol.Int(f, 0, 255), WinningReason = SessionProtocol.Int(f, 1, 255),
                    ObjectivesTaken = SessionProtocol.Int(f, 2), ObjectivesNeeded = SessionProtocol.Int(f, 3),
                    GamesLeft = SessionProtocol.Int(f, 4), Map = SessionProtocol.Text(f, 5), Rows = _rows.ToArray(),
                };
                _rows.Clear();
                ResultsReceived?.Invoke(Results);
                break;
            case "kicked":
                Kicked = true;
                _failRequest = SessionProtocol.Text(f, 0).Length > 0 ? SessionProtocol.Text(f, 0) : "You were removed from the server.";
                return;
        }

        Changed?.Invoke();
    }

    /// <summary>How many fields a command has, so its last one keeps its spaces.</summary>
    private static int FieldCount(string line)
    {
        string command = line.Length > SessionProtocol.Prefix.Length ? line[SessionProtocol.Prefix.Length..].Split(' ', 2)[0] : string.Empty;
        return command switch
        {
            "hello" => 5,
            "setup" => 2,
            "mapsource" => 4,
            "player" => 6,
            "msg" or "kicked" => 1,
            "row" => 7,
            "results" => 6,
            _ => int.MaxValue,
        };
    }

    private void OnChat(ChatLine line) =>
        AddMessage(new SessionMessage(line.Text, line.SenderName.Length > 0 ? line.SenderName : "Server", line.SenderTeam, line.Scope == EChatScope.TeamSay));

    private void AddMessage(SessionMessage message)
    {
        Lobby.AddMessage(message);
        MessageReceived?.Invoke(message);
        Changed?.Invoke();
    }

    /// <summary>
    /// Checks the setup's map again and tells the server, after the map was installed while in the lobby (the
    /// catalog refreshed first). Call from the thread that drives this client.
    /// </summary>
    public void RecheckMap()
    {
        if (State is OnlineClientState.Lobby or OnlineClientState.Results)
        {
            CheckMap();
            Changed?.Invoke();
        }
    }

    /// <summary>Tells the server whether this install has the setup's map, and the same version of it.</summary>
    private void CheckMap()
    {
        string map = Lobby.Setup.Map;
        MapState state = Maps.FindMap(map) is not { } entry
            ? MapState.Missing
            : Lobby.SetupContentHash != 0 && entry.ContentHash != Lobby.SetupContentHash ? MapState.Differs : MapState.Ok;
        SendLine(SessionProtocol.Line("mapcheck", map, (int)state));
    }

    private void Fail(string error)
    {
        Error = error;
        _connectLog.Add(error);
        EndSession();
        CloseTransport();
        State = OnlineClientState.Failed;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EndSession();
        CloseTransport();
    }
}
