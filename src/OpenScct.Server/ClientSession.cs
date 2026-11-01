using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using K = OpenScct.Net.K;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>Client settings.</summary>
public sealed record ClientSettings
{
    public string PlayerName { get; init; } = "Player";

    /// <summary>Net speed asked for in Hello, bytes per second.</summary>
    public int NetSpeed { get; init; } = K.ConfiguredInternetSpeed;

    public NetDriverSettings Net { get; init; } = new();

    /// <summary>Interpolation delay in server send intervals ("about two" keeps one lost update covered).</summary>
    public int InterpolationIntervals { get; init; } = 2;

    public bool AdaptiveInterpolation { get; init; } = true;

    /// <summary>
    /// Content hash sent in Hello: 0 (the default) lets the server accept and the client check the map after
    /// Welcome; a non-zero value must equal the server map's (tests use it to provoke a refusal).
    /// </summary>
    public ulong ContentHash { get; init; }

    /// <summary>The replicated classes; null builds a new registry (it must match the server's).</summary>
    public NetClassRegistry? Classes { get; init; }

    /// <summary>Where maps come from; null uses <see cref="MapCatalog.CreateDefault"/>.</summary>
    public MapCatalog? Maps { get; init; }

    /// <summary>
    /// URL options sent after the name in Hello (for example <c>?Team=1</c>): the server's game rules log the player
    /// in with them.
    /// </summary>
    public string LoginOptions { get; init; } = string.Empty;

    /// <summary>
    /// The player's key (rl:ac.bans): it signs the server's join challenge, so the server knows the player by it. Null
    /// answers a challenge without a key (bots, tests, tools).
    /// </summary>
    public Online.PlayerKey? Key { get; init; }

    /// <summary>
    /// Run the gameplay services on the loaded map (movement, devices, equipment and combat, the device events from
    /// the server) for a server running the SBase game rules, and report the player waiting once its controller
    /// arrives, as the client's loading page does (spec/game/match-lifecycle.md rule 11).
    /// </summary>
    public bool Gameplay { get; init; }

    /// <summary>Headless clients are ready when their controller arrives. Rendered clients call
    /// <see cref="ClientSession.MarkReadyToPlay"/> after their scene finishes loading.</summary>
    public bool AutoReportReady { get; init; } = true;

    /// <summary>
    /// The view layer's host: the game rules' presentation calls for this player (sounds, messages, menus, HUD) and
    /// the server's calls no port runs arrive here. Null drops them.
    /// </summary>
    public IGameHost? ViewHost { get; init; }

    /// <summary>
    /// With <see cref="Gameplay"/>: install <see cref="GameplayServices"/> after the map loads (default). A client whose
    /// map loader already installs a playing machine's services (the game's play map loader) turns it off and keeps
    /// only the network bridges.
    /// </summary>
    public bool InstallGameplayServices { get; init; } = true;

    /// <summary>
    /// With <see cref="Gameplay"/>: draw the impacts of this player's own bullets at once from its own trace, instead
    /// of a round trip later from the server (Reloaded's fire prediction, spec/net/lag-compensation.md). On by default,
    /// as in Reloaded; the server then does not send this player those impacts, so they are never drawn twice.
    /// </summary>
    public bool FirePrediction { get; init; } = true;
}

public enum EClientState : byte
{
    Connecting,
    Joined,
    Rejected,
    Disconnected,
}

/// <summary>
/// A client: its own <see cref="World"/> in NM_Client mode with the server's map loaded (map actors prepared for
/// replication and bound by SVMap index), a client <see cref="NetDriver"/> and the local player's input. Each tick
/// it receives, sends the newest walker moves (the last <see cref="K.MoveResendCount"/>, as the original resends
/// moves), ticks its world and shows every interpolated actor at a render time about two server send intervals in
/// the past.
/// <para>After Welcome the client loads the map and compares its content hash with the server's; on a mismatch it
/// leaves with the original wording and names the sim file that differs.</para>
/// </summary>
public sealed class ClientSession : INetworkNotify, IDisposable
{
    /// <summary>Chat lines kept for <see cref="TakeChat"/>; older ones beyond it are dropped.</summary>
    public const int MaxPendingChat = 64;

    private readonly CubeMove[] _moves = new CubeMove[K.MoveResendCount];
    private readonly List<string> _texts = new();
    private readonly List<ChatLine> _chat = new();
    private int _moveCount;
    private readonly ITransport _transport;
    private readonly bool _ownsTransport;
    private readonly MapCatalog _maps;
    private NetConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Connects to <paramref name="serverAddress"/> and sends Hello at once. With <paramref name="ownsTransport"/>
    /// the session disposes the transport when it is disposed.
    /// </summary>
    public ClientSession(ITransport transport, string serverAddress, long nowMicros, ClientSettings? settings = null,
        bool ownsTransport = false)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _ownsTransport = ownsTransport;
        Settings = settings ?? new ClientSettings();
        _maps = Settings.Maps ?? MapCatalog.CreateDefault();
        _readyToPlay = Settings.AutoReportReady;
        World = new World(0, ENetMode.NM_Client);
        Driver = new NetDriver(transport, Settings.Classes ?? NetClasses.Create(), this, isServer: false, Settings.Net) { World = World };
        var gameHost = new NetGameHost(Driver, Settings.ViewHost);
        World.Level.SetService<IGameHost>(gameHost);
        Movement = NetMovementBridge.Install(World.Level, Driver);
        if (Settings.Gameplay)
        {
            gameHost.DeliverUnportedCalls();
            World.Level.SetService<IObjectiveNet>(new NetObjectiveBridge(Driver));
            World.Level.SetService<OpenScct.Game.Devices.IDeviceNet>(new NetDeviceBridge(Driver));
            Combat = NetCombatBridge.Install(World.Level, Driver);
            if (Settings.FirePrediction)
            {
                World.Level.SetService(new OpenScct.Game.Combat.FirePrediction());
            }
        }

        // The combat calls to the server carry this session's view time (lag compensation).
        World.Level.SetService(this);
        _connection = Driver.Connect(serverAddress, nowMicros);
        _connection.ControlChannel!.Send(ControlMessage.Hello(Driver.ProtocolHash, Settings.ContentHash,
            Settings.PlayerName + Settings.LoginOptions, Settings.NetSpeed, Settings.Gameplay ? _maps.CharacterContentHash : 0));
        _connection.FlushNet(nowMicros, force: true);
    }

    public ClientSettings Settings { get; }

    public World World { get; }

    public NetDriver Driver { get; }

    /// <summary>The move protocol of this player's controller (the movement port's network calls).</summary>
    public NetMovementBridge Movement { get; }

    /// <summary>With <see cref="ClientSettings.Gameplay"/>: the combat calls to and from the server.</summary>
    public NetCombatBridge? Combat { get; }

    /// <summary>With <see cref="ClientSettings.Gameplay"/>: the device events from the server, once the map loaded.</summary>
    public NetDeviceEvents? DeviceEvents { get; private set; }

    /// <summary>The local player's controller (the autonomous player controller the server sent), or null.</summary>
    public PlayerController? LocalPlayer => _connection?.Actor as PlayerController;

    /// <summary>True once the player reported waiting to the server (<c>PlayerWaitingToPlayToServer(true)</c>).</summary>
    public bool ReportedWaiting { get; private set; }

    private bool _readyToPlay;

    /// <summary>Releases the loading gate. The next network tick reports readiness once the controller exists.</summary>
    public void MarkReadyToPlay() => _readyToPlay = true;

    /// <summary>
    /// Raised each tick after the packets were received and before the world ticks: the place to write the local
    /// player's input (a bot, the input layer).
    /// </summary>
    public event Action<ClientSession>? BeforeWorldTick;

    public EClientState State { get; private set; } = EClientState.Connecting;

    /// <summary>The server connection (kept after it closes, for its stats and close reason).</summary>
    public NetConnection? Connection => _connection;

    public int PlayerId { get; private set; }

    public string? MapName { get; private set; }

    /// <summary>The loaded map, once Welcome arrived.</summary>
    public MapInfo? Map { get; private set; }

    /// <summary>Server replication rate from Welcome.</summary>
    public int ServerNetRate { get; private set; } = K.NetServerMaxTickRate;

    public string? RejectReason { get; private set; }

    public string? RejectFile { get; private set; }

    /// <summary>The walker this client controls, once replicated.</summary>
    public CubeWalker? LocalWalker { get; private set; }

    public ActorChannel? LocalChannel { get; private set; }

    /// <summary>Input used for the move made on the next tick.</summary>
    public CubeInput Input { get; set; }

    /// <summary>Ticks run so far.</summary>
    public long LocalTick { get; private set; }

    /// <summary>Server time shown this tick (server ticks times <see cref="NetTimeline.Fraction"/>), or -1 before any packet.</summary>
    public long RenderTimeFixed { get; private set; } = -1;
    private long _previousRenderTimeFixed = -1;
    public long PreviousRenderTimeFixed => _previousRenderTimeFixed;
    private long _presentedTimeFixed = -1;

    /// <summary>Record the actual frame interpolation time for subsequent shot timestamps.</summary>
    public void ReportRenderedFrame(float alpha)
    {
        if (RenderTimeFixed < 0) return;
        float fraction = float.IsFinite(alpha) ? Math.Clamp(alpha, 0f, 1f) : 1f;
        long previous = _previousRenderTimeFixed >= 0 ? _previousRenderTimeFixed : RenderTimeFixed;
        long shown = previous + (long)((RenderTimeFixed - previous) * fraction);
        if (PawnProxies == PawnProxyMode.DeadReckoning) shown += (long)InterpolationDelayTicks * NetTimeline.Fraction;
        _presentedTimeFixed = Math.Max(_presentedTimeFixed, shown);
    }

    public PresentationClock Presentation { get; } = new();

    /// <summary>
    /// How far behind the server's estimated time the client shows replicated actors: <see
    /// cref="ClientSettings.InterpolationIntervals"/> send intervals of the server's rate (<see cref="ServerNetRate"/>, from
    /// Welcome), in world ticks. Stock 20 Hz: 6 ticks (100 ms); Enhanced 60 Hz: 2 ticks (33 ms). The view time it gives is
    /// <see cref="RenderTimeFixed"/>; lag compensation rewinds other players to it.
    /// </summary>
    public int InterpolationDelayTicks => Settings.InterpolationIntervals * ((Sim.Hz + ServerNetRate - 1) / ServerNetRate);

    /// <summary>
    /// The server time at which this client shows other players this tick (fixed point like <see cref="RenderTimeFixed"/>),
    /// or -1 before any packet: the render time when it interpolates them; with the stock dead reckoning, which walks
    /// them on to the newest server time it knows, that newer time. Combat calls to the server carry it, and a server
    /// with lag compensation traces their shots there (<see cref="LagCompensation"/>).
    /// </summary>
    public long ViewTimeFixed => _presentedTimeFixed >= 0 ? _presentedTimeFixed : RenderTimeFixed < 0
        ? -1
        : PawnProxies != PawnProxyMode.DeadReckoning ? RenderTimeFixed : RenderTimeFixed + (long)InterpolationDelayTicks * NetTimeline.Fraction;

    /// <summary><see cref="InterpolationDelayTicks"/> in milliseconds.</summary>
    public float InterpolationDelayMs => Settings.AdaptiveInterpolation ? Presentation.DelayMs : InterpolationDelayTicks * Sim.DeltaTime * 1000f;

    /// <summary>
    /// How this client shows other players' pawns: the server's mode from Welcome (Enhanced hosts interpolate, Stock
    /// hosts dead reckon).
    /// </summary>
    public PawnProxyMode PawnProxies { get; private set; } = PawnProxyMode.DeadReckoning;

    /// <summary>With <see cref="PawnProxyMode.Interpolate"/> and gameplay: the proxy pawns' buffers, once the map loaded.</summary>
    public PawnInterpolation? PawnInterpolation { get; private set; }

    public PawnStateReplication? PawnStates { get; private set; }

    /// <summary>Text lines received from the server, in order (session lines go to <see cref="SessionLineReceived"/>).</summary>
    public IReadOnlyList<string> ReceivedTexts => _texts;

    /// <summary>Raised on the client's thread for each session line (<c>#svm ...</c>, docs/ONLINE.md) from the server.</summary>
    public event Action<string>? SessionLineReceived;

    /// <summary>Replicated actor channels, in open order.</summary>
    public IReadOnlyList<ActorChannel> ActorChannels => _connection?.ActorChannels ?? [];

    /// <summary>Raised on this client's thread when a replicated actor channel opens.</summary>
    public event Action<ActorChannel>? ActorChannelOpened;

    public void Tick(long nowMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Driver.LocalTick = LocalTick;
        Driver.TickDispatch(nowMicros);
        Driver.PrepareSendFrame(nowMicros);

        // This tick's view time, before the world ticks: the proxy pawns' tick shows them at it.
        _previousRenderTimeFixed = RenderTimeFixed;
        Driver.Timeline.Advance();
        if (Driver.Timeline.IsValid)
        {
            Presentation.Observe(Driver.NewestServerTick, LocalTick);
            RenderTimeFixed = Settings.AdaptiveInterpolation
                ? Presentation.Advance(LocalTick, Driver.Timeline, PawnProxies == PawnProxyMode.Snapshots ? Math.Max(4, InterpolationDelayTicks) : InterpolationDelayTicks)
                : Driver.Timeline.RenderTimeFixed(LocalTick, InterpolationDelayTicks);
        }

        if (PawnInterpolation is { } pawns)
        {
            pawns.RenderTimeFixed = RenderTimeFixed;
            pawns.AfterReceive(Driver.NewestServerTick);
        }

        PawnStates?.AfterReceive(RenderTimeFixed);

        if (State == EClientState.Joined)
        {
            SendMove();
            ReportWaiting();
        }

        if (Map is not null)
        {
            Movement.BeforeClientTick();
            World.Level.GetService<PawnSoundReplication>()?.Pump(RenderTimeFixed);
            BeforeWorldTick?.Invoke(this);
        }

        // The world starts ticking once the server's map is loaded into it (a level loads before its first tick).
        if (Map is not null)
        {
            World.Step();
            Movement.AfterTick();
        }

        if (Driver.Timeline.IsValid)
        {
            Driver.UpdateProxies(RenderTimeFixed);
        }

        Combat?.Pump();
        World.Level.GetService<MercShockStream>()?.AfterReceive();
        Driver.TickFlush(nowMicros);
        LocalTick++;
    }

    /// <summary>Sends a text line to the server.</summary>
    public void SendText(string text) => _connection?.ControlChannel?.Send(ControlMessage.TextLine(text));

    /// <summary>
    /// Sends a chat line the player typed (Say or TeamSay); the server checks the chat rules and relays it to its
    /// recipients, this player included (spec/hud/chat.md).
    /// </summary>
    public void SendChat(EChatScope scope, string text)
    {
        if (State == EClientState.Joined && !string.IsNullOrWhiteSpace(text))
        {
            _connection?.ControlChannel?.Send(ControlMessage.Chat(scope, text));
        }
    }

    /// <summary>Chat lines received since the last <see cref="TakeChat"/>, oldest first.</summary>
    public int PendingChat => _chat.Count;

    /// <summary>Raised on the client's thread for every chat line the server relays to this player.</summary>
    public event Action<ChatLine>? ChatReceived;

    /// <summary>Moves the chat lines received so far into <paramref name="into"/>; returns how many.</summary>
    public int TakeChat(List<ChatLine> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        int n = _chat.Count;
        into.AddRange(_chat);
        _chat.Clear();
        return n;
    }

    /// <summary>Closes the connection (the server hears it through the control channel close).</summary>
    public void Disconnect()
    {
        if (_connection is { State: not EConnectionState.USOCK_Closed } connection)
        {
            connection.Close("Disconnected");
        }

        State = State == EClientState.Rejected ? EClientState.Rejected : EClientState.Disconnected;
    }

    /// <summary>The loading page's s_Client: the player is in the map and waits for the match.</summary>
    private void ReportWaiting()
    {
        if (!Settings.Gameplay || !_readyToPlay || ReportedWaiting || LocalPlayer is not { bDeleteMe: false } pc)
        {
            return;
        }

        ReportedWaiting = true;
        Driver.ProcessRemoteFunction(pc, "PlayerWaitingToPlayToServer", new NetCallArgs().Bool(0, true));
    }

    private void SendMove()
    {
        ActorChannel? channel = LocalChannel;
        if (channel is null || channel.Closing || LocalWalker is null || LocalWalker.bDeleteMe ||
            channel.Layout?.FindFunction(CubeWalkerNet.ServerMove) is not { } function)
        {
            return;
        }

        CubeInput input = Input;
        var move = new CubeMove((uint)(LocalTick + 1), input.Forward, input.Strafe, input.ViewYaw, input.Buttons);
        if (_moveCount < _moves.Length)
        {
            _moves[_moveCount++] = move;
        }
        else
        {
            Array.Copy(_moves, 1, _moves, 0, _moves.Length - 1);
            _moves[^1] = move;
        }

        FOutBunch bunch = channel.BeginFunction(function, reliable: false);
        CubeWalkerNet.WriteMoves(bunch.Writer, _moves.AsSpan(0, _moveCount));
        channel.SendFunction(bunch);
    }

    // ---- INetworkNotify ------------------------------------------------------------------------------------

    void INetworkNotify.NotifyControlMessage(NetConnection connection, in ControlMessage message)
    {
        switch (message.Type)
        {
            case EControlMessage.Welcome:
                if (State != EClientState.Connecting)
                {
                    return;
                }

                ReceivedWelcome(connection, message);
                break;
            case EControlMessage.Challenge:
                if (State == EClientState.Connecting)
                {
                    connection.ControlChannel?.Send(ControlMessage.Proof(Settings.Key?.AnswerChallenge(message.Text ?? string.Empty, connection.Salt)));
                }

                break;
            case EControlMessage.Reject:
                RejectReason = message.Text;
                RejectFile = message.File;
                State = EClientState.Rejected;
                connection.Close("Rejected: " + message.Text);
                break;
            case EControlMessage.Text:
                if (Online.SessionProtocol.IsSession(message.Text))
                {
                    SessionLineReceived?.Invoke(message.Text!);
                }
                else
                {
                    _texts.Add(message.Text ?? string.Empty);
                }

                break;
            case EControlMessage.Chat:
            {
                ChatLine line = message.ToChatLine();
                if (_chat.Count < MaxPendingChat)
                {
                    _chat.Add(line);
                }

                ChatReceived?.Invoke(line);
                break;
            }

        }
    }

    private void ReceivedWelcome(NetConnection connection, in ControlMessage message)
    {
        PlayerId = message.PlayerId;
        MapName = message.Text;
        ServerNetRate = message.NetRate is >= 1 and <= Sim.Hz ? message.NetRate : K.NetServerMaxTickRate;
        PawnProxies = message.PawnProxies;
        MapCatalog maps = _maps;
        if (!maps.TryLoad(World, MapName ?? string.Empty, isClient: true, out MapInfo? map, out string? error))
        {
            RejectReason = error;
            State = EClientState.Rejected;
            connection.Close(error);
            return;
        }

        if (map!.ContentHash != message.ContentHash)
        {
            RejectReason = ControlMessage.VersionMismatchText;
            RejectFile = map.FirstDifferingFile(message.File) ?? map.ContentFile;
            State = EClientState.Rejected;
            connection.Close(RejectReason);
            return;
        }

        Map = map;
        Driver.MapBinding.Bind(map.MapActors);
        if (Settings.Gameplay)
        {
            if (Settings.InstallGameplayServices)
            {
                GameplayServices.Install(World.Level, map.Physics, maps.PawnAnimations);
            }

            DeviceEvents = NetDeviceEvents.InstallClient(World.Level, Driver);
            if (PawnProxies == PawnProxyMode.Snapshots) PawnStates = new PawnStateReplication(Driver);
            PawnInterpolation = PawnProxies == PawnProxyMode.Interpolate
                ? new PawnInterpolation(Driver.Settings.MaxExtrapolationTicks) { RenderTimeFixed = RenderTimeFixed }
                : null;
            PawnProxyReceiver.Install(World.Level, Driver, PawnInterpolation);
        }

        connection.SetOpen();
        connection.ControlChannel?.Send(ControlMessage.Join());
        State = EClientState.Joined;
    }

    void INetworkNotify.NotifyActorChannelOpened(ActorChannel channel)
    {
        if (channel.Actor is CubeWalker walker && walker.Role == ENetRole.ROLE_AutonomousProxy)
        {
            LocalWalker = walker;
            LocalChannel = channel;
        }

        ActorChannelOpened?.Invoke(channel);
    }

    void INetworkNotify.NotifyConnectionClosed(NetConnection connection)
    {
        if (State != EClientState.Rejected)
        {
            State = EClientState.Disconnected;
        }

        LocalWalker = null;
        LocalChannel = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disconnect();
        Driver.Dispose();
        if (_ownsTransport)
        {
            _transport.Dispose();
        }
    }
}
