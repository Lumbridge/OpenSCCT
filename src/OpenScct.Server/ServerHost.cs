using System;
using System.Collections.Generic;
using System.Threading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using K = OpenScct.Net.K;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>Server settings. Defaults: TestBox, 4 players, internet replication rate (20 Hz), a dedicated server.</summary>
public sealed record ServerSettings
{
    public string Map { get; init; } = MapCatalog.TestBox;

    public int MaxPlayers { get; init; } = 4;

    /// <summary>
    /// Network frames (replication passes and flushes) per second: 20 internet, 35 LAN
    /// (<see cref="K.LanServerMaxTickRate"/>), up to 60. The world always ticks at 60 Hz.
    /// </summary>
    public int NetRate { get; init; } = K.NetServerMaxTickRate;

    public ulong MatchSeed { get; init; } = 1;

    public ENetMode NetMode { get; init; } = ENetMode.NM_DedicatedServer;

    /// <summary>
    /// Driver settings. The server default keeps the decoded update frequency gate with its starvation guard
    /// (<see cref="NetUpdateGate.DecodedGuarded"/>, docs/NETCODE.md "Update frequency on a dedicated server"); it
    /// changes nothing on a listen server, which has no tick cap.
    /// </summary>
    public NetDriverSettings Net { get; init; } = new() { UpdateGate = NetUpdateGate.DecodedGuarded };

    /// <summary>The replicated classes; null builds a new registry (share one between the drivers of a process).</summary>
    public NetClassRegistry? Classes { get; init; }

    /// <summary>Where maps come from; null uses <see cref="MapCatalog.CreateDefault"/>.</summary>
    public MapCatalog? Maps { get; init; }

    /// <summary>Spawn an M0 <see cref="CubeWalker"/> for each player that joins (ignored when <see cref="GameOptions"/> is set).</summary>
    public bool SpawnWalkers { get; init; } = true;

    /// <summary>
    /// URL options of the SBase game rules (for example <c>?GameMode=5?TimeLimit=10?LivesATT=4?LivesDEF=3</c>). When
    /// set, the server spawns <see cref="SGameInfo"/> with them during the map load, runs the gameplay services, logs
    /// each joining player in as an SPlayerController (with the URL options its Hello carried after the name) and,
    /// on a dedicated server, starts the match with <see cref="DedicatedStartGate"/> (a listen host's loading page does
    /// it otherwise). Null runs the M0 walkers.
    /// </summary>
    public string? GameOptions { get; init; }

    /// <summary>With <see cref="GameOptions"/>: players that must be in and waiting before a dedicated server starts the match.</summary>
    public int PlayersToStart { get; init; } = 2;

    /// <summary>Seconds a refused connection stays open so the reason gets through (it is sent reliably).</summary>
    public float RejectLingerSeconds { get; init; } = 2f;

    /// <summary>
    /// With <see cref="GameOptions"/>: start the match with <see cref="DedicatedStartGate"/> whatever the net mode
    /// (an online session's listen host, which has no loading page of its own). Null: on a dedicated server only.
    /// </summary>
    public bool? UseStartGate { get; init; }

    /// <summary>
    /// With <see cref="GameOptions"/>: spawns the game info from the level and the full URL (the map name and the
    /// options), for rules built on SGameInfo (the hub's). Null spawns <see cref="SGameInfo"/>.
    /// </summary>
    public Func<Level, string, SGameInfo>? GameFactory { get; init; }
}

/// <summary>What <see cref="ServerHost.LoginFilter"/> decides for a connection's Hello.</summary>
public readonly record struct LoginDecision(bool Accept, string Name, string Options, string? Reason)
{
    /// <summary>Accepts the player with this name and these login options.</summary>
    public static LoginDecision Allow(string name, string options) => new(true, name, options, null);

    /// <summary>Refuses the player with a readable reason.</summary>
    public static LoginDecision Refuse(string reason) => new(false, string.Empty, string.Empty, reason);
}

/// <summary>A player on the server.</summary>
public sealed class PlayerSlot
{
    internal PlayerSlot(int playerId, string name, NetConnection connection)
    {
        PlayerId = playerId;
        Name = name;
        Connection = connection;
    }

    public int PlayerId { get; }

    public string Name { get; }

    public NetConnection Connection { get; }

    public CubeWalker? Walker { get; internal set; }

    /// <summary>The URL options the player's Hello carried after its name (for example <c>?Team=1</c>).</summary>
    public string LoginOptions { get; internal set; } = string.Empty;

    /// <summary>With the game rules: the player's controller (Login's SPlayerController).</summary>
    public PlayerController? Controller { get; internal set; }

    /// <summary>The player's address as the transport knows it (an IP and port, a relay session), or null.</summary>
    public string? Address { get; internal set; }

    /// <summary>Free for the code that owns the session (an online session keeps its player record here).</summary>
    public object? Tag { get; set; }
}

/// <summary>
/// The server: a <see cref="World"/> ticking at 60 Hz with its map (actors bound to channels by SVMap index,
/// physics attached), a server <see cref="NetDriver"/> running a replication pass and a flush at
/// <see cref="ServerSettings.NetRate"/>, the handshake, the objective and game-host bridges, and one
/// <see cref="CubeWalker"/> per joined player when <see cref="ServerSettings.SpawnWalkers"/> is set. The same code
/// runs a dedicated server (any transport) and a listen server (see <see cref="ListenServer"/>).
/// <para>Single-threaded: call <see cref="Tick"/> (or <see cref="RunRealtime"/>) from one thread only.</para>
/// </summary>
public sealed class ServerHost : INetworkNotify, IDisposable
{
    private readonly List<PlayerSlot> _players = new();
    private readonly List<NetConnection> _rejected = new();
    private readonly List<long> _rejectDeadlines = new();
    private readonly List<string> _texts = new();
    private readonly long _rejectLingerMicros;
    private int _flushAccumulator;
    private int _nextPlayerId = 1;
    private bool _disposed;

    public ServerHost(ITransport transport, ServerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        Settings = settings ?? new ServerSettings();
        if (Settings.NetRate is < 1 or > Sim.Hz)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), Settings.NetRate, "NetRate must be 1 to 60.");
        }

        World = new World(Settings.MatchSeed, Settings.NetMode);
        MapCatalog maps = Settings.Maps ?? MapCatalog.CreateDefault();
        Driver = new NetDriver(transport, Settings.Classes ?? NetClasses.Create(), this, isServer: true, Settings.Net) { World = World };
        World.Level.SetService<IObjectiveNet>(new NetObjectiveBridge(Driver));
        World.Level.SetService<IGameHost>(new NetGameHost(Driver));
        World.Level.SetService<OpenScct.Game.Devices.IDeviceNet>(new NetDeviceBridge(Driver));
        Movement = NetMovementBridge.Install(World.Level, Driver);
        Chat = new ChatRelay(() => _players);

        // The engine spawns the game info after the map's actors and before the begin-play passes.
        string? options = Settings.GameOptions;
        string gameError = string.Empty;
        Action<Level>? spawnGame = options is null
            ? null
            : Settings.GameFactory is { } factory
                ? level => Game = factory(level, NativeUrl.Rebuild("?MapName=" + Settings.Map + options))
                : level => Game = GameInfo.SpawnGameInfo<SGameInfo>(level, NativeUrl.Rebuild("?MapName=" + Settings.Map + options), ref gameError);
        if (!maps.TryLoad(World, Settings.Map, isClient: false, out MapInfo? map, out string? error, spawnGame))
        {
            throw new InvalidOperationException(error);
        }

        Map = map!;
        Driver.MapBinding.Bind(Map.MapActors);
        if (Map.Physics is { } physics)
        {
            Driver.Relevancy.LineOfSight = new PhysicsLineOfSight(physics);
        }

        // Other players' pawns go to each viewer as position samples and action batches, whoever installs the game
        // rules (a play session's map loader installs its own).
        ProxySender = new PawnProxySender(Driver);
        if (Game is not null)
        {
            GameplayServices.Install(World.Level, Map.Physics, maps.PawnAnimations);
            Combat = NetCombatBridge.Install(World.Level, Driver);
            DeviceEvents = NetDeviceEvents.InstallServer(World.Level, Driver);
            HearSound = NetHearSound.InstallServer(World, Driver);
            BeforeWorldTick += static host => host.HearSound?.RelayLate();
            if (Settings.UseStartGate ?? Settings.NetMode == ENetMode.NM_DedicatedServer)
            {
                StartGate = new DedicatedStartGate(Game, Settings.PlayersToStart);
            }
        }

        _rejectLingerMicros = (long)(Settings.RejectLingerSeconds * 1_000_000f);
    }

    public ServerSettings Settings { get; }

    public World World { get; }

    public NetDriver Driver { get; }

    public MapInfo Map { get; }

    /// <summary>The move protocol of the players' controllers (the movement port's network calls).</summary>
    public NetMovementBridge Movement { get; }

    /// <summary>Text chat: players' Say and TeamSay lines, relayed to their recipients (spec/hud/chat.md).</summary>
    public ChatRelay Chat { get; }

    /// <summary>With <see cref="ServerSettings.GameOptions"/>: the game rules, else null.</summary>
    public SGameInfo? Game { get; private set; }

    /// <summary>With the game rules: the combat calls to and from players.</summary>
    public NetCombatBridge? Combat { get; }

    /// <summary>With the game rules: the device events relayed to clients.</summary>
    public NetDeviceEvents? DeviceEvents { get; }

    /// <summary>With the game rules: the server's sounds sent to the players who hear them (ClientHearSound).</summary>
    public NetHearSound? HearSound { get; }

    /// <summary>With the game rules: the position samples of other players' pawns.</summary>
    public PawnProxySender? ProxySender { get; }

    /// <summary>With the game rules on a dedicated server: the match start.</summary>
    public DedicatedStartGate? StartGate { get; }

    public IReadOnlyList<PlayerSlot> Players => _players;

    /// <summary>Text lines received from clients, oldest first.</summary>
    public IReadOnlyList<string> ReceivedTexts => _texts;

    /// <summary>Replication flushes so far.</summary>
    public long FlushCount { get; private set; }

    /// <summary>Ticks between flushes, rounded up (3 at 20 Hz).</summary>
    public int FlushIntervalTicks => (Sim.Hz + Settings.NetRate - 1) / Settings.NetRate;

    /// <summary>
    /// Raised when a player joins (after its walker, if any, spawned). The handler may give the connection its
    /// actor (normally a player controller); replication to the player starts with the next pass.
    /// </summary>
    public event Action<PlayerSlot>? PlayerJoined;

    /// <summary>Raised when a player's connection closed (left, timed out or kicked), before its slot is removed.</summary>
    public event Action<PlayerSlot>? PlayerLeft;

    /// <summary>
    /// Raised for each session line (<c>#svm ...</c> text, docs/ONLINE.md) a player sends; such lines are not kept in
    /// <see cref="ReceivedTexts"/>. The slot is null before the player was accepted.
    /// </summary>
    public event Action<NetConnection, PlayerSlot?, string>? SessionLineReceived;

    /// <summary>
    /// Decides each Hello that passed the version checks, instead of the player count: the name and login options to
    /// use, or the reason to refuse (an online session's capacity, password, bans and returning players).
    /// </summary>
    public Func<NetConnection, string, string, LoginDecision>? LoginFilter { get; set; }

    /// <summary>
    /// Takes a chat line before the chat relay does (for example an admin's <c>!</c> command); true consumes it.
    /// </summary>
    public Func<PlayerSlot, string, bool>? ChatFilter { get; set; }

    /// <summary>False refuses new connections at once (an online session changing map); true by default.</summary>
    public bool AcceptingConnections { get; set; } = true;

    /// <summary>
    /// Raised on the host's thread after the received messages are dispatched and before the world steps: server-side
    /// controllers (the bots of OpenScct.Bots) write their input bytes here, as the input layer of a local player would.
    /// </summary>
    public event Action<ServerHost>? BeforeWorldTick;

    /// <summary>
    /// One fixed 60 Hz step: receive (handshakes, moves, calls), tick the world, then run the replication pass and
    /// flush when a network frame is due. Frames are spread evenly (35 Hz runs 35 of every 60 ticks).
    /// </summary>
    public void Tick(long nowMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Driver.TickDispatch(nowMicros);
        BeforeWorldTick?.Invoke(this);
        World.Step();
        Chat.Tick(World.TimeSeconds);
        DeviceEvents?.RelayTick(World);
        StartGate?.Tick(PendingClients());
        CloseExpiredRejections(nowMicros);
        _flushAccumulator += Settings.NetRate;
        if (_flushAccumulator >= Sim.Hz)
        {
            _flushAccumulator -= Sim.Hz;
            ProxySender?.Pass(World.Level);
            Driver.TickFlush(nowMicros, replicate: true);
            Combat?.Pump();
            FlushCount++;
        }
    }

    /// <summary>
    /// Runs <see cref="Tick"/> on a fixed 60 Hz schedule from <paramref name="clock"/> until cancelled, calling
    /// <paramref name="afterTick"/> (on this thread) after each tick. When the loop falls more than
    /// <paramref name="maxCatchUpTicks"/> behind (a stall), the backlog is dropped.
    /// </summary>
    public void RunRealtime(IClock clock, CancellationToken cancellationToken, Action<ServerHost>? afterTick = null,
        int maxCatchUpTicks = 5)
    {
        ArgumentNullException.ThrowIfNull(clock);
        long start = clock.NowMicroseconds;
        long ticks = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            long now = clock.NowMicroseconds;
            long due = start + VirtualClock.TickToMicroseconds(ticks + 1, Sim.Hz);
            if (now < due)
            {
                if (due - now > 2000)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.Yield();
                }

                continue;
            }

            if (now - due > VirtualClock.TickToMicroseconds(maxCatchUpTicks, Sim.Hz))
            {
                start = now - VirtualClock.TickToMicroseconds(ticks + 1, Sim.Hz);
            }

            Tick(now);
            ticks++;
            afterTick?.Invoke(this);
        }
    }

    /// <summary>Connections still connecting: pending, or open and not joined (NbPendingClientOnServer).</summary>
    public int PendingClients()
    {
        int pending = 0;
        foreach (NetConnection c in Driver.ClientConnections)
        {
            if (c.State == EConnectionState.USOCK_Pending || (c.State == EConnectionState.USOCK_Open && !c.bJoined))
            {
                pending++;
            }
        }

        return pending;
    }

    /// <summary>Sends a text line to every joined player.</summary>
    public void Broadcast(string text)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            _players[i].Connection.ControlChannel?.Send(ControlMessage.TextLine(text));
        }
    }

    // ---- INetworkNotify ------------------------------------------------------------------------------------

    void INetworkNotify.NotifyControlMessage(NetConnection connection, in ControlMessage message)
    {
        switch (message.Type)
        {
            case EControlMessage.Hello:
                ReceivedHello(connection, in message);
                break;
            case EControlMessage.Join:
                if (connection.State == EConnectionState.USOCK_Open && connection.UserData is PlayerSlot slot && !connection.bJoined)
                {
                    Join(slot);
                }

                break;
            case EControlMessage.Chat:
                if (connection.State == EConnectionState.USOCK_Open && connection.UserData is PlayerSlot sender &&
                    !(ChatFilter?.Invoke(sender, message.Text ?? string.Empty) ?? false))
                {
                    Chat.Receive(sender, message.Scope, message.Text);
                }

                break;
            case EControlMessage.Text:
                if (connection.State == EConnectionState.USOCK_Open)
                {
                    if (Online.SessionProtocol.IsSession(message.Text))
                    {
                        SessionLineReceived?.Invoke(connection, connection.UserData as PlayerSlot, message.Text!);
                    }
                    else
                    {
                        _texts.Add(message.Text ?? string.Empty);
                    }
                }

                break;
        }
    }

    bool INetworkNotify.NotifyAcceptingConnection(EndpointId endpoint) => AcceptingConnections;

    void INetworkNotify.NotifyConnectionClosed(NetConnection connection)
    {
        int rejected = _rejected.IndexOf(connection);
        if (rejected >= 0)
        {
            _rejected.RemoveAt(rejected);
            _rejectDeadlines.RemoveAt(rejected);
        }

        if (connection.UserData is PlayerSlot slot)
        {
            PlayerLeft?.Invoke(slot);
            if (slot.Walker is { bDeleteMe: false } walker)
            {
                walker.Destroy();
            }

            if (slot.Controller is { bDeleteMe: false } pc)
            {
                Game?.Logout(pc);
                if (pc.Pawn is { bDeleteMe: false } pawn)
                {
                    pawn.Destroy();
                }

                pc.Destroy();
            }

            slot.Walker = null;
            slot.Controller = null;
            _players.Remove(slot);
        }
    }

    private void ReceivedHello(NetConnection connection, in ControlMessage hello)
    {
        if (connection.State != EConnectionState.USOCK_Pending || connection.UserData is not null)
        {
            return;
        }

        // The protocol covers every replicated class and layout; a client naming a content hash must have the map.
        if (hello.ProtocolHash != Driver.ProtocolHash || (hello.ContentHash != 0 && hello.ContentHash != Map.ContentHash))
        {
            Reject(connection, ControlMessage.VersionMismatchText, hello.ProtocolHash != Driver.ProtocolHash ? "OpenScct.Net" : Map.ContentFile);
            return;
        }

        // The Hello text is a login URL: the name, then options such as ?Team=1.
        string text = hello.Text ?? string.Empty;
        int query = text.IndexOf('?', StringComparison.Ordinal);
        string name = (query >= 0 ? text[..query] : text).Trim();
        string loginOptions = query >= 0 ? text[query..] : string.Empty;
        if (name.Length == 0)
        {
            name = "Player";
        }

        if (LoginFilter is { } filter)
        {
            LoginDecision decision = filter(connection, name, loginOptions);
            if (!decision.Accept)
            {
                Reject(connection, decision.Reason ?? "Refused", null);
                return;
            }

            name = decision.Name;
            loginOptions = decision.Options;
        }
        else if (_players.Count >= Settings.MaxPlayers)
        {
            Reject(connection, "The server is full", null);
            return;
        }

        connection.SetOpen();
        int requested = hello.NetSpeed >= K.MinNetSpeed ? hello.NetSpeed : Driver.Settings.DefaultNetSpeed;
        connection.CurrentNetSpeed = Math.Min(requested, Driver.Settings.MaxClientRate);
        var slot = new PlayerSlot(_nextPlayerId++, name, connection)
        {
            LoginOptions = loginOptions, Address = Driver.Transport.RemoteAddress(connection.RemoteEndpoint),
        };
        connection.UserData = slot;
        _players.Add(slot);
        connection.ControlChannel?.Send(ControlMessage.Welcome(slot.PlayerId, Map.Name, (uint)World.TickCount, Settings.NetRate,
            Map.ContentHash, Map.FileListing));
    }

    private void Reject(NetConnection connection, string reason, string? file)
    {
        connection.UserData = reason;
        connection.ControlChannel?.Send(ControlMessage.Reject(reason, file));
        connection.FlushNet(Driver.Time, force: true);
        _rejected.Add(connection);
        _rejectDeadlines.Add(Driver.Time + _rejectLingerMicros);
    }

    private void CloseExpiredRejections(long now)
    {
        for (int i = _rejected.Count - 1; i >= 0; i--)
        {
            if (now >= _rejectDeadlines[i])
            {
                _rejected[i].Close("Rejected");
            }
        }
    }

    private void Join(PlayerSlot slot)
    {
        NetConnection connection = slot.Connection;
        if (Game is not null)
        {
            // ULevel::SpawnPlayActor for a remote player: Login, the connection's player and roles, the connection's
            // actor, then PostLogin. The joined connection owns the controller before PostLogin, so the login's calls
            // to the player's machine (ClientSetHUD, ClientSetRotation) are sent to it instead of running on the
            // server (the client never got its SHud: no alarms, HUD messages or objective countdowns).
            string error = string.Empty;
            PlayerController? pc = Game.Login(string.Empty, NativeUrl.Rebuild("?Name=" + slot.Name + slot.LoginOptions), ref error);
            if (pc is null)
            {
                connection.Close(string.IsNullOrEmpty(error) ? "Login failed" : error);
                return;
            }

            pc.Player = new SimNetConnection();
            pc.Role = ENetRole.ROLE_Authority;
            pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Controller = pc;
            connection.Actor = pc;
            connection.bJoined = true;
            Game.PostLogin(pc);
        }
        else if (Settings.SpawnWalkers)
        {
            IReadOnlyList<FVector> starts = Map.PlayerStarts;
            FVector start = starts[(slot.PlayerId - 1) % starts.Count];
            CubeWalker? walker = World.Level.Spawn<CubeWalker>(start, default);
            if (walker is null)
            {
                connection.Close("Could not spawn");
                return;
            }

            walker.PlayerId = slot.PlayerId;
            walker.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Walker = walker;
            connection.Actor = walker;
        }

        connection.bJoined = true;
        Driver.ResetConnectionPing(connection);
        PlayerJoined?.Invoke(slot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Driver.Dispose();
    }
}
