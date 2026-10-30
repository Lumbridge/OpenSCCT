using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>How a client shows replicated movement.</summary>
public enum ProxyMotionMode : byte
{
    /// <summary>Apply replicated values as they arrive (the UE2 way; <c>net.proxy=ue2</c>).</summary>
    None = 0,

    /// <summary>Buffer states and interpolate at a delayed render time (<see cref="ProxyMotion"/>), for classes that ask for it.</summary>
    Interpolate = 1,
}

/// <summary>
/// How other players' pawns move on a client (the Stock/Enhanced split of rl:net.clientsync, docs/NETCODE.md "Proxy pawn
/// interpolation"). The server's send schedule follows its own driver's setting.
/// </summary>
public enum PawnProxyMode : byte
{
    /// <summary>Stock: the original's dead reckoning (spec/native/net-proxy.md rules 1 to 15).</summary>
    DeadReckoning = 0,

    /// <summary>
    /// Enhanced: the client shows each proxy pawn from a buffer of received states about two send intervals in the
    /// past; the server pushes Rotation and the aim properties every pass instead of every 0.09 s.
    /// </summary>
    Interpolate = 1,
}

/// <summary>Who pays for what a driver sends.</summary>
public enum SendBudgetMode : byte
{
    /// <summary>
    /// The game build: one budget per driver. A server's client connections share one adaptive rate
    /// (<see cref="NetDriver.NetSpeed"/>, the CONF* law); a client spends its connection's rate.
    /// </summary>
    SharedAdaptive,

    /// <summary>Stock UE2 (the editor build): a budget per connection at its CurrentNetSpeed.</summary>
    PerConnection,

    /// <summary>No limit (tests, tools).</summary>
    Unlimited,

    /// <summary>
    /// Enhanced hosts (<see cref="NetProfile.Enhanced"/>): a budget per client connection at that connection's own
    /// adaptive rate (<see cref="NetConnection.SendRate"/>), so one saturated client stalls only itself. The CONF* law
    /// steers each rate from that connection's lag with the server row's thresholds and full step, between the row's
    /// minimum and <see cref="NetDriverSettings.MaxClientRate"/>. A client driver spends its connection's rate, as
    /// with <see cref="SharedAdaptive"/>.
    /// </summary>
    PerClientAdaptive,
}

/// <summary>The update frequency test of the server pass (spec/native/net-replication.md rule 4).</summary>
public enum NetUpdateGate : byte
{
    /// <summary>As decoded: <c>NetUpdateFrequency &gt;= Rate</c> or <c>NetUpdateFrequency × dt &gt; 1</c>.</summary>
    Decoded,

    /// <summary>The stock UE2.0 form: <c>NetUpdateFrequency &gt;= Rate</c> or <c>FRand() &lt; NetUpdateFrequency × dt</c>.</summary>
    Stochastic,

    /// <summary>Every candidate every pass.</summary>
    Always,

    /// <summary>
    /// The decoded rule plus a starvation guard: an actor that fails it still goes when its channel on this
    /// connection has not sent for <c>1 / NetUpdateFrequency</c> seconds, or has never sent. It only adds passes the
    /// decoded rule refuses, at most at the actor's own update frequency; with passes stalled on the budget it is
    /// the decoded rule. The dedicated server default (docs/NETCODE.md, "Update frequency on a dedicated server").
    /// </summary>
    DecodedGuarded,
}

/// <summary>Network settings for one driver. Defaults are the shipped SCCT values (see <see cref="K"/>).</summary>
public sealed record NetDriverSettings
{
    /// <summary>Largest packet in bytes (also capped by the transport and by <see cref="NetConnection.MaxPacketBytesLimit"/>).</summary>
    public int MaxPacketBytes { get; init; } = 1200;

    /// <summary>Dedicated server tick cap on the internet (GetMaxTickRate).</summary>
    public int NetServerMaxTickRate { get; init; } = K.NetServerMaxTickRate;

    /// <summary>Dedicated server tick cap with <see cref="LanPlay"/>.</summary>
    public int LanServerMaxTickRate { get; init; } = K.LanServerMaxTickRate;

    /// <summary>The dedicated server was started with the lanplay switch.</summary>
    public bool LanPlay { get; init; }

    /// <summary>The engine-wide net type is LAN (EnterLAN): the LAN rows of the CONF* settings apply.</summary>
    public bool IsLan { get; init; }

    /// <summary>Highest net speed the server grants a client, bytes per second.</summary>
    public int MaxClientRate { get; init; } = K.MaxClientRate;

    /// <summary>Net speed a connection starts with, bytes per second (a client asks for its own in Hello).</summary>
    public int DefaultNetSpeed { get; init; } = K.ConfiguredInternetSpeed;

    public NetSpeedConf Conf { get; init; } = NetSpeedConf.Default;

    public SendBudgetMode Budget { get; init; } = SendBudgetMode.SharedAdaptive;

    public NetUpdateGate UpdateGate { get; init; } = NetUpdateGate.Decoded;

    public float ConnectionTimeout { get; init; } = K.ConnectionTimeout;

    public float InitialConnectTimeout { get; init; } = K.InitialConnectTimeout;

    public float AckTimeout { get; init; } = K.AckTimeout;

    public float KeepAliveTime { get; init; } = K.KeepAliveTime;

    public float RelevantTimeout { get; init; } = K.RelevantTimeout;

    /// <summary>Open and pending connections a server keeps at most.</summary>
    public int MaxConnections { get; init; } = 64;

    public ProxyMotionMode ProxyMode { get; init; } = ProxyMotionMode.Interpolate;

    /// <summary>Ticks a proxy may be extrapolated past its newest state before it holds.</summary>
    public int MaxExtrapolationTicks { get; init; } = 6;

    /// <summary>How other players' pawns move (Stock dead reckoning or Enhanced interpolation).</summary>
    public PawnProxyMode PawnProxies { get; init; } = PawnProxyMode.DeadReckoning;

    /// <summary>
    /// Interest management for large sessions (an OpenSCCT addition, <see cref="INetInterest"/>); null, the default,
    /// runs the stock pass unchanged.
    /// </summary>
    public INetInterest? Interest { get; init; }
}

/// <summary>
/// Callbacks from a driver to its host (UE2 FNetworkNotify). Calls happen inside
/// <see cref="NetDriver.TickDispatch"/> or <see cref="NetDriver.TickFlush"/> on the driver's thread.
/// </summary>
public interface INetworkNotify
{
    /// <summary>Server: a datagram came from an unknown endpoint. Return false to ignore it.</summary>
    bool NotifyAcceptingConnection(EndpointId endpoint) => true;

    /// <summary>Server: a new (pending) connection was created.</summary>
    void NotifyAcceptedConnection(NetConnection connection)
    {
    }

    /// <summary>A control message arrived, in order.</summary>
    void NotifyControlMessage(NetConnection connection, in ControlMessage message);

    /// <summary>Client: a replicated actor was spawned or bound and its initial properties applied.</summary>
    void NotifyActorChannelOpened(ActorChannel channel)
    {
    }

    /// <summary>A connection closed (either side, or a timeout). It is removed after this call.</summary>
    void NotifyConnectionClosed(NetConnection connection)
    {
    }
}

/// <summary>Counters of what a driver did beyond packets and bytes.</summary>
public sealed class NetDriverStats
{
    public long ChannelsOpened { get; internal set; }

    public long ChannelsClosedIrrelevant { get; internal set; }

    public long CallsSent { get; internal set; }

    public long CallsReceived { get; internal set; }

    /// <summary>Unreliable calls dropped by a saturated connection, or calls with no channel to go on.</summary>
    public long CallsDropped { get; internal set; }

    /// <summary>Received calls that failed the owner or condition test (server) and were discarded.</summary>
    public long CallsRejected { get; internal set; }

    /// <summary>Received calls swallowed on a proxy (not simulated).</summary>
    public long CallsAbsorbed { get; internal set; }

    /// <summary>Received calls with no C# port and no handler.</summary>
    public long CallsUnhandled { get; internal set; }

    private readonly Dictionary<string, long> _unhandled = new(StringComparer.Ordinal);

    /// <summary>Received calls with no C# port and no handler, by function name (diagnostics).</summary>
    public IReadOnlyDictionary<string, long> UnhandledByName => _unhandled;

    internal void CountUnhandled(string function)
    {
        CallsUnhandled++;
        _unhandled[function] = _unhandled.GetValueOrDefault(function) + 1;
    }

    /// <summary>Client: channels for map actors this client does not have.</summary>
    public long UnboundMapActors { get; internal set; }

    /// <summary>Client: replicated actors whose spawn failed.</summary>
    public long FailedSpawns { get; internal set; }

    /// <summary>Server: passes skipped for a connection because the send budget was spent.</summary>
    public long PassesSkippedSaturated { get; internal set; }

    /// <summary>Server: actor loops cut short by saturation.</summary>
    public long PassesCutShort { get; internal set; }

    /// <summary>Server, with interest management: candidates the interest layer culled.</summary>
    public long InterestCulled { get; internal set; }

    /// <summary>Server, with interest management: channel updates the interest layer moved to a later pass.</summary>
    public long InterestDeferred { get; internal set; }
}

/// <summary>
/// The network driver (UE2 UNetDriver plus the level's server replication): owns a transport and the connections
/// on it. A server driver accepts connections and replicates the actors of its <see cref="World"/>; a client
/// driver has one <see cref="ServerConnection"/> and spawns or binds replicated actors in its World.
/// <para>The host calls <see cref="TickDispatch"/> at the start of every simulation tick (receive, run
/// handshakes and remote functions) and <see cref="TickFlush"/> when the network frame ends: a server at its
/// replication rate (the frame of a tick-capped dedicated server), a client every tick.</para>
/// </summary>
public sealed class NetDriver : IDisposable
{
    private readonly List<NetConnection> _connections = new();
    private readonly Dictionary<EndpointId, NetConnection> _byEndpoint = new();
    private readonly Dictionary<uint, Actor> _netGuids = new();
    private readonly List<ActorNetState> _states = new();
    private readonly Dictionary<int, ActorNetState> _stateByActorId = new();
    private readonly byte[] _receiveBuffer;
    private readonly FBitReader _probe = new();
    private readonly NetCallArgs _receiveArgs = new(64);
    private int _nextConnectionId = 1;
    private long _captureStamp;
    private long _lastFlush = -1;
    private long _netSpeedResetTime;
    private SimRandom _random = new(0, 0x4E45, 1);
    private bool _disposed;

    public NetDriver(ITransport transport, NetClassRegistry classes, INetworkNotify notify, bool isServer,
        NetDriverSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(notify);
        Transport = transport;
        Classes = classes;
        Notify = notify;
        IsServer = isServer;
        Settings = settings ?? new NetDriverSettings();
        MaxPacketBytes = Math.Min(Math.Min(Settings.MaxPacketBytes, transport.MaxDatagramSize), NetConnection.MaxPacketBytesLimit);
        if (MaxPacketBytes < 64)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Packets must hold at least 64 bytes.");
        }

        _receiveBuffer = new byte[transport.MaxDatagramSize];
        ConnectionTimeoutMicros = (long)(Settings.ConnectionTimeout * 1_000_000f);
        InitialConnectTimeoutMicros = (long)(Settings.InitialConnectTimeout * 1_000_000f);
        AckTimeoutMicros = (long)(Settings.AckTimeout * 1_000_000f);
        KeepAliveMicros = (long)(Settings.KeepAliveTime * 1_000_000f);
        RelevantTimeoutMicros = (long)(Settings.RelevantTimeout * 1_000_000f);
        ProtocolHash = classes.ComputeProtocolHash();
        Relevancy = new NetRelevancy(classes.Readers);
        NetSpeed = ServerRow.Max(Settings.IsLan);
    }

    public ITransport Transport { get; }

    public NetClassRegistry Classes { get; }

    public INetworkNotify Notify { get; }

    public bool IsServer { get; }

    public NetDriverSettings Settings { get; }

    public int MaxPacketBytes { get; }

    /// <summary>Hash of the wire protocol and replicated classes, compared at the handshake.</summary>
    public ulong ProtocolHash { get; }

    /// <summary>The world to replicate (server) or spawn replicated actors into (client).</summary>
    public World? World
    {
        get => _world;
        set
        {
            _world = value;
            if (value is not null)
            {
                _random = value.CreateRandom(0x4E45, 1);
            }
        }
    }

    private World? _world;

    /// <summary>The level's net mode (the driver's role when no world is attached).</summary>
    public ENetMode NetMode => World?.NetMode ?? (IsServer ? ENetMode.NM_DedicatedServer : ENetMode.NM_Client);

    /// <summary>Host clock at the last dispatch or flush, in microseconds.</summary>
    public long Time { get; private set; }

    /// <summary>Length of the last network frame (between two flushes), microseconds.</summary>
    public long FrameMicros { get; private set; }

    /// <summary>Simulation tick stamped on outgoing packets: the world's tick count.</summary>
    public uint CurrentTick => World is null ? 0u : (uint)World.TickCount;

    /// <summary>Client: local tick used for <see cref="Timeline"/> samples. The host sets it every tick.</summary>
    public long LocalTick { get; set; }

    /// <summary>Client: estimate of the server tick.</summary>
    public NetTimeline Timeline { get; } = new();

    /// <summary>Client: the newest server tick stamped on a packet received from the server, or -1.</summary>
    public long NewestServerTick { get; private set; } = -1;

    /// <summary>Client: the server tick of the packet whose bunch is being received (the one a received call came in).</summary>
    public long ReceivingTick { get; internal set; } = -1;

    /// <summary>Map actors by SVMap actor index, set by the host after loading the map.</summary>
    public NetMapBinding MapBinding { get; } = new();

    /// <summary>Handlers for received function calls (the default runs the C# port of the function).</summary>
    public NetFunctionHandlers Functions { get; } = new();

    /// <summary>The IsNetRelevantFor tests.</summary>
    public NetRelevancy Relevancy { get; }

    public NetDriverStats Stats { get; } = new();

    /// <summary>
    /// The camera of a connection's pass (PlayerCalcView). By default the connection's actor location, which the
    /// move protocol keeps at the client's camera (UpdatePositionForServer).
    /// </summary>
    public Func<NetConnection, FVector>? ViewPoint { get; set; }

    /// <summary>The server's adaptive send rate shared by every client connection, bytes per second.</summary>
    public int NetSpeed { get; private set; }

    /// <summary>The send budget: bytes sent and not yet paid off at the rate (negative: saved-up rate).</summary>
    public long QueuedBytes { get; private set; }

    internal long ConnectionTimeoutMicros { get; }

    internal long InitialConnectTimeoutMicros { get; }

    internal long AckTimeoutMicros { get; }

    internal long KeepAliveMicros { get; }

    internal long RelevantTimeoutMicros { get; }

    /// <summary>Server: every connection, open or pending, in creation order.</summary>
    public IReadOnlyList<NetConnection> ClientConnections => _connections;

    /// <summary>
    /// A class's native list adding a property its condition leaves out, per connection and update (as
    /// ASBasePawn::GetOptimizedRepList pushes a pawn's Rotation on its own schedule, spec/native/net-proxy.md rule 3).
    /// The property still goes only when it differs from what the connection last got.
    /// </summary>
    public Func<Actor, NetConnection, RepProperty, bool>? PropertyPush { get; set; }

    /// <summary>
    /// A class's native list leaving out a property its condition includes, per connection and update (as
    /// ASBasePawn::GetOptimizedRepList sends a pawn's aim properties only every 0.09 s, spec/native/net-proxy.md rule 3).
    /// A withheld change stays different from what the connection last got, so it goes on a later update.
    /// </summary>
    public Func<Actor, NetConnection, RepProperty, bool>? PropertyWithhold { get; set; }

    /// <summary>
    /// Client: a class's native PreNetReceive, run before the properties of each received bunch of an actor (after
    /// the spawn of an opening bunch). The engine's own saves (rule 24) are not here; this is for a class that adds
    /// its own, as ASBasePawn does (spec/native/net-proxy.md rule 15a).
    /// </summary>
    public Action<Actor>? PreNetReceive { get; set; }

    /// <summary>Client: a class's native PostNetReceive, run after the properties of each received bunch of an actor.</summary>
    public Action<Actor>? PostNetReceive { get; set; }

    /// <summary>Client: the connection to the server.</summary>
    public NetConnection? ServerConnection { get; private set; }

    /// <summary>Server: the network state of every actor that has had a channel, in order.</summary>
    public IReadOnlyList<ActorNetState> ActorStates => _states;

    /// <summary>Client: the replicated actor with this net GUID.</summary>
    public Actor? FindActorByNetGuid(uint netGuid) => _netGuids.GetValueOrDefault(netGuid);

    internal void AddNetGuid(uint netGuid, Actor actor) => _netGuids[netGuid] = actor;

    internal void RemoveNetGuid(uint netGuid) => _netGuids.Remove(netGuid);

    /// <summary>Server: the network state of <paramref name="actor"/>, or null when it never had a channel.</summary>
    public ActorNetState? FindState(Actor actor) => _stateByActorId.GetValueOrDefault(actor.ActorId);

    private NetSpeedRow ServerRow => NetMode == ENetMode.NM_DedicatedServer ? Settings.Conf.DedicatedServer : Settings.Conf.ListenServer;

    // ---- ownership -------------------------------------------------------------------------------------------

    /// <summary>The top of <paramref name="actor"/>'s Owner chain.</summary>
    public static Actor TopOwner(Actor actor)
    {
        Actor top = actor;
        for (int guard = 0; top.Owner is { } next && guard < 64; guard++)
        {
            top = next;
        }

        return top;
    }

    /// <summary>
    /// Server: the connection owns <paramref name="actor"/> (bNetOwner): the top of its Owner chain is the
    /// connection's actor, normally its player controller (spec/native/net-replication.md rule 10).
    /// </summary>
    public bool IsNetOwner(Actor actor, NetConnection connection) =>
        connection.Actor is { } viewer && ReferenceEquals(TopOwner(actor), viewer);

    /// <summary>Client: the top of the owner chain is this client's own player (rule 23).</summary>
    public bool IsLocallyOwned(Actor actor) =>
        ServerConnection?.Actor is { } local && ReferenceEquals(TopOwner(actor), local);

    /// <summary>Server: the connection of the player that owns <paramref name="actor"/>, or null.</summary>
    public NetConnection? OwningConnection(Actor actor)
    {
        Actor top = TopOwner(actor);
        for (int i = 0; i < _connections.Count; i++)
        {
            if (ReferenceEquals(_connections[i].Actor, top))
            {
                return _connections[i];
            }
        }

        return null;
    }

    // ---- client --------------------------------------------------------------------------------------------

    /// <summary>
    /// Client: starts a connection to <paramref name="address"/> with its control channel open. Send Hello on
    /// <see cref="NetConnection.ControlChannel"/> next.
    /// </summary>
    public NetConnection Connect(string address, long now)
    {
        if (IsServer)
        {
            throw new InvalidOperationException("A server driver does not connect out.");
        }

        if (ServerConnection is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        Time = now;
        EndpointId endpoint = Transport.Connect(address);
        var connection = new NetConnection(this, endpoint, _nextConnectionId++, now);

        // The salt that ties the server's packets to this connection; a recording's packets carry the recorded one.
        if (!Transport.AdoptsRemoteSalt)
        {
            connection.SetSalt(NewSalt());
        }

        connection.CreateChannel(EChannelType.CHTYPE_Control, openedLocally: true, chIndex: 0);
        ServerConnection = connection;
        _connections.Add(connection);
        _byEndpoint[endpoint] = connection;
        ResetConnectionPing();
        return connection;
    }

    /// <summary>
    /// A random, non-zero connection salt. It never touches the simulation, so it comes from the system's random
    /// source: someone who cannot see the traffic must not be able to guess it.
    /// </summary>
    private static uint NewSalt()
    {
        Span<byte> bytes = stackalloc byte[4];
        uint salt;
        do
        {
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            salt = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }
        while (salt == 0);
        return salt;
    }

    // ---- demo recording ------------------------------------------------------------------------------------

    /// <summary>
    /// Server: opens a demo recorder connection (docs/DEMOS.md). It looks like a client whose control channel the
    /// remote opened, but its packets go to <paramref name="sink"/> instead of the transport and count as acked once
    /// written. The host logs it in as a spectator; <see cref="NetConnection.IsRecorder"/> makes every actor relevant
    /// to it (see <see cref="NetRelevancy"/>).
    /// </summary>
    public NetConnection OpenRecorderConnection(IDemoSink sink, long now)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (!IsServer)
        {
            throw new InvalidOperationException("Only a server records demos.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        Time = now;
        var connection = new NetConnection(this, EndpointId.None, _nextConnectionId++, now, sink);
        connection.CreateChannel(EChannelType.CHTYPE_Control, openedLocally: false, chIndex: 0);
        _connections.Add(connection);
        return connection;
    }

    // ---- ticking -------------------------------------------------------------------------------------------

    /// <summary>Receives every waiting datagram and processes it.</summary>
    [UcNative("UNetDriver::TickDispatch", Exe = 0x109eeff0, Editor = 0x1113d670)]
    public void TickDispatch(long now)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Time = now;
        byte[] buffer = _receiveBuffer;
        while (Transport.TryReceive(buffer, out EndpointId from, out int length))
        {
            if (!_byEndpoint.TryGetValue(from, out NetConnection? connection))
            {
                if (!IsServer || _connections.Count >= Settings.MaxConnections ||
                    !NetConnection.IsConnectionStart(buffer, length, _probe) || !Notify.NotifyAcceptingConnection(from))
                {
                    continue;
                }

                connection = new NetConnection(this, from, _nextConnectionId++, now);
                _connections.Add(connection);
                _byEndpoint[from] = connection;
                Notify.NotifyAcceptedConnection(connection);
            }

            connection.ReceivedRawPacket(buffer, length, now);
        }

        RemoveClosedConnections();
    }

    /// <summary>
    /// Ends a network frame: refills the send budget, runs the server replication pass when
    /// <paramref name="replicate"/> is set, then ticks and flushes every connection (spec/native/net-driver.md rule 1).
    /// </summary>
    [UcNative("UNetDriver::TickFlush", Exe = 0x109eef50, Editor = 0x1113da00)]
    public void TickFlush(long now, bool replicate = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Time = now;
        // A clock that steps back (a caller switching clocks) must not count as negative time: the refill would turn
        // into a send debt of twice the step, and every unreliable call would be dropped until it was paid off.
        FrameMicros = _lastFlush < 0 ? 0 : Math.Max(0, now - _lastFlush);
        _lastFlush = now;
        RefillBudget(FrameMicros);
        if (IsServer && replicate && World is not null)
        {
            ServerReplicateActors(now);
        }

        for (int i = 0; i < _connections.Count; i++)
        {
            NetConnection connection = _connections[i];
            connection.Tick(now);
            if (connection.State != EConnectionState.USOCK_Closed)
            {
                connection.FlushNet(now);
            }
        }

        RemoveClosedConnections();
    }

    /// <summary>Client: moves every interpolated proxy to its state at <paramref name="renderTimeFixed"/>.</summary>
    public void UpdateProxies(long renderTimeFixed)
    {
        NetConnection? connection = ServerConnection;
        if (connection is null)
        {
            return;
        }

        List<ActorChannel> channels = connection.ActorChannelList;
        for (int i = 0; i < channels.Count; i++)
        {
            ActorChannel channel = channels[i];
            Actor? actor = channel.Actor;
            if (channel.Motion is not null && actor is not null && !actor.bDeleteMe)
            {
                channel.Motion.Apply(actor, renderTimeFixed, Settings.MaxExtrapolationTicks);
            }
        }
    }

    // ---- send budget and adaptive speed ----------------------------------------------------------------------

    /// <summary>
    /// GetMaxTickRate: a dedicated server's tick cap (the lanplay one with <see cref="NetDriverSettings.LanPlay"/>),
    /// clamped to 10..120; 0 (no cap) for a listen server and a client. The pass's frequency gate uses it.
    /// </summary>
    [UcNative("UGameEngine::GetMaxTickRate", Exe = 0x109ecb10)]
    public int GetMaxTickRate() => IsServer && NetMode == ENetMode.NM_DedicatedServer
        ? Math.Clamp(Settings.LanPlay ? Settings.LanServerMaxTickRate : Settings.NetServerMaxTickRate, K.MinServerTickRate, K.MaxServerTickRate)
        : 0;

    /// <summary>
    /// Rule 2: time pays the budget off at the rate (a client's connection speed, a server's shared
    /// <see cref="NetSpeed"/>); at most two frames of unused rate are saved up.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 2, game 0x109eeff0")]
    private void RefillBudget(long frameMicros)
    {
        switch (Settings.Budget)
        {
            case SendBudgetMode.SharedAdaptive:
            {
                int rate = IsServer ? NetSpeed : ServerConnection?.CurrentNetSpeed ?? Settings.DefaultNetSpeed;
                QueuedBytes = Refill(QueuedBytes, rate, frameMicros);
                break;
            }

            case SendBudgetMode.PerConnection:
                for (int i = 0; i < _connections.Count; i++)
                {
                    NetConnection c = _connections[i];
                    c.QueuedBytes = Refill(c.QueuedBytes, c.CurrentNetSpeed, frameMicros);
                }

                break;

            case SendBudgetMode.PerClientAdaptive when IsServer:
                for (int i = 0; i < _connections.Count; i++)
                {
                    NetConnection c = _connections[i];
                    c.QueuedBytes = Refill(c.QueuedBytes, c.SendRate, frameMicros);
                }

                break;

            case SendBudgetMode.PerClientAdaptive:
                QueuedBytes = Refill(QueuedBytes, ServerConnection?.CurrentNetSpeed ?? Settings.DefaultNetSpeed, frameMicros);
                break;
        }
    }

    private static long Refill(long queued, int rate, long frameMicros)
    {
        long pay = rate * frameMicros / 1_000_000;
        queued -= pay;
        long floor = -K.BudgetCarryFrames * pay;
        return queued < floor ? floor : queued;
    }

    /// <summary>Rule 3: ready when the budget plus the bytes waiting in the connection's packet is not above zero.</summary>
    internal bool BudgetReady(NetConnection connection, int pendingBytes) => Settings.Budget switch
    {
        SendBudgetMode.SharedAdaptive => QueuedBytes + pendingBytes <= 0,
        SendBudgetMode.PerConnection => connection.QueuedBytes + pendingBytes <= 0,
        SendBudgetMode.PerClientAdaptive => (IsServer ? connection.QueuedBytes : QueuedBytes) + pendingBytes <= 0,
        _ => true,
    };

    internal void OnBytesSent(NetConnection connection, int bytes)
    {
        if (Settings.Budget == SendBudgetMode.SharedAdaptive ||
            (Settings.Budget == SendBudgetMode.PerClientAdaptive && !IsServer))
        {
            QueuedBytes += bytes;
        }
        else if (Settings.Budget is SendBudgetMode.PerConnection or SendBudgetMode.PerClientAdaptive)
        {
            connection.QueuedBytes += bytes;
        }
    }

    /// <summary>
    /// Rule 8, once per statistics period of a connection: a client steers its own rate by its lag; a server
    /// steers the shared rate by the worst lag of its client connections, one share of the step per connection.
    /// </summary>
    [UcNative("UNetConnection::AdaptNetSpeed", Exe = 0x10a0eac0)]
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-driver.md rule 8 (CONF* control law)")]
    internal void AdaptNetSpeed(NetConnection connection, long now)
    {
        bool lan = Settings.IsLan;
        if (!IsServer)
        {
            int speed = connection.CurrentNetSpeed;
            NetSpeedRow client = Settings.Conf.Client;
            Adapt(ref speed, connection.AvgLag, client, client.StepUp, client.Min(lan), client.Max(lan), now);
            connection.CurrentNetSpeed = speed;
            return;
        }

        NetSpeedRow row = connection.Actor is null ? Settings.Conf.ListenServer : ServerRow;
        if (Settings.Budget == SendBudgetMode.PerClientAdaptive)
        {
            // Enhanced: this connection's own rate from its own lag, with the whole step.
            int max = Settings.MaxClientRate;
            int own = connection.SendRate;
            Adapt(ref own, connection.AvgLag, row, row.StepUp, Math.Min(row.Min(lan), max), max, now);
            connection.SendRate = own;
            return;
        }

        float worst = 0f;
        int count = 0;
        for (int i = 0; i < _connections.Count; i++)
        {
            NetConnection c = _connections[i];
            if (c.State == EConnectionState.USOCK_Closed)
            {
                continue;
            }

            count++;
            worst = Math.Max(worst, c.AvgLag);
        }

        int rate = NetSpeed;
        Adapt(ref rate, worst, row, row.StepUp / Math.Max(1, count), row.Min(lan), row.Max(lan), now);
        NetSpeed = rate;
    }

    private void Adapt(ref int speed, float lag, NetSpeedRow row, int step, int min, int max, long now)
    {
        speed = Math.Clamp(speed, min, max);
        if (now - _netSpeedResetTime < K.NetSpeedHoldMicros)
        {
            speed = max;
            return;
        }

        float a = row.LagToTakeAction;
        float m = row.MaxAllowedLag;
        if (lag > m)
        {
            speed = min;
        }
        else if (lag > a && speed > min)
        {
            speed = SvmMath.Trunc(max - (max - min) * (lag - a) / (m - a));
        }
        else if (lag <= a && speed < max)
        {
            speed += step;
        }
    }

    /// <summary>
    /// PlayerController.resetConnectionPing (rule 9): the adaptive rate goes to its maximum and is held there for
    /// 2 s; the lag and loss accumulators of <paramref name="connection"/> (every connection when null) are zeroed.
    /// </summary>
    [UcNative("APlayerController::execresetConnectionPing", Exe = 0x10a21f00)]
    public void ResetConnectionPing(NetConnection? connection = null)
    {
        _netSpeedResetTime = Time;
        bool lan = Settings.IsLan;
        if (IsServer)
        {
            NetSpeed = Math.Clamp(K.NetSpeedResetRate, ServerRow.Min(lan), ServerRow.Max(lan));
        }
        else if (ServerConnection is { } server)
        {
            server.CurrentNetSpeed = Math.Clamp(K.NetSpeedResetRate, Settings.Conf.Client.Min(lan), Settings.Conf.Client.Max(lan));
        }

        bool perClient = IsServer && Settings.Budget == SendBudgetMode.PerClientAdaptive;
        for (int i = 0; i < _connections.Count; i++)
        {
            if (connection is null || ReferenceEquals(_connections[i], connection))
            {
                _connections[i].ResetLagStatistics();
                if (perClient)
                {
                    _connections[i].SendRate = Settings.MaxClientRate;
                }
            }
        }
    }

    // ---- server replication --------------------------------------------------------------------------------

    /// <summary>
    /// ULevel::TickNetServer: dirty propagation, then a pass per connection (spec/native/net-replication.md rules
    /// 1 to 8).
    /// </summary>
    [UcNative("ULevel::TickNetServer", Exe = 0x10a30ba0, Editor = 0x11184200)]
    private void ServerReplicateActors(long now)
    {
        long stamp = ++_captureStamp;
        PropagateDirty(stamp);
        for (int i = 0; i < _connections.Count; i++)
        {
            NetConnection connection = _connections[i];
            if (connection.State != EConnectionState.USOCK_Open || !connection.bJoined || connection.Actor is null)
            {
                continue;
            }

            if (now - connection.LastReceiveTime > (long)(K.ReceiveWindow * 1_000_000f))
            {
                continue;
            }

            if (!connection.IsNetReady)
            {
                Stats.PassesSkippedSaturated++;
                continue;
            }

            ServerTickClient(connection, World!.Level, stamp, now);
        }
    }

    /// <summary>Rule 1, and the channels of destroyed actors close.</summary>
    private void PropagateDirty(long stamp)
    {
        int write = 0;
        for (int i = 0; i < _states.Count; i++)
        {
            ActorNetState state = _states[i];
            Actor actor = state.Actor;
            if (actor.bDeleteMe)
            {
                for (int c = state.Channels.Count - 1; c >= 0; c--)
                {
                    state.Channels[c].Close();
                }

                _stateByActorId.Remove(actor.ActorId);
                continue;
            }

            _states[write++] = state;
            if (state.Channels.Count == 0)
            {
                state.HasPrevious = false;
                continue;
            }

            bool dirty = state.CaptureAndCompare(stamp) | actor.bNetDirty;
            actor.bNetDirty = false;
            if (!dirty)
            {
                continue;
            }

            for (int c = 0; c < state.Channels.Count; c++)
            {
                ActorChannel channel = state.Channels[c];
                if (channel.Connection.State == EConnectionState.USOCK_Open && channel.Connection.bJoined)
                {
                    channel.ActorDirty = true;
                }
            }
        }

        _states.RemoveRange(write, _states.Count - write);
    }

    /// <summary>ULevel::ServerTickClient for one connection (rules 3 to 8).</summary>
    [UcNative("ULevel::ServerTickClient", Exe = 0x10a30730, Editor = 0x11183250)]
    private void ServerTickClient(NetConnection connection, Level level, long stamp, long now)
    {
        NetViewer viewer = ViewerOf(connection);
        bool recorder = connection.IsRecorder;
        INetInterest? interest = recorder ? null : Settings.Interest;
        Relevancy.BeginPass();
        float dt = (now - connection.LastServerTickTime) / 1_000_000f;
        int rate = GetMaxTickRate();
        long recheck = (long)(K.RelevancyRecheck * 1_000_000f);
        bool complete = true;
        IReadOnlyList<Actor> actors = level.Actors;
        for (int i = 0; i < actors.Count; i++)
        {
            Actor actor = actors[i];
            if (actor.bDeleteMe || actor.RemoteRole == ENetRole.ROLE_None || !OnNetworkList(actor) ||
                connection.SentTemporaries.Contains(actor.ActorId) || !CanBeNamed(actor))
            {
                continue;
            }

            bool candidate = actor.bAlwaysRelevant || !actor.bOnlyRelevantToOwner ||
                ReferenceEquals(actor.Owner, viewer.Viewer) || ReferenceEquals(actor.Owner, viewer.RealViewer);
            if (!candidate)
            {
                continue;
            }

            ActorChannel? channel = connection.FindActorChannel(actor);
            bool culled = false;
            bool deferred = false;
            if (interest is not null)
            {
                NetInterestDecision decision = interest.Evaluate(actor, viewer, channel is { Closing: false });
                culled = decision.Cull;
                if (culled)
                {
                    Stats.InterestCulled++;
                    if (channel is null)
                    {
                        continue;
                    }
                }

                deferred = decision.MinUpdateSeconds > 0f && channel is { SentInitial: true } &&
                    now - channel.LastUpdateTime < (long)(decision.MinUpdateSeconds * 1_000_000f);
            }

            if (!PassesUpdateGate(actor, rate, dt) && !PassesStarvationGuard(actor, channel, now))
            {
                continue;
            }

            if (actor.bOnlyDirtyReplication && !culled && channel is { SentInitial: true, ActorDirty: false } && !channel.HasDirtyProperties)
            {
                channel.RelevantTime = now;
                continue;
            }

            bool relevant = false;
            if (!actor.bTearOff && !culled && (channel is null || now - channel.RelevantTime > recheck))
            {
                relevant = recorder ? Relevancy.IsRelevantToRecorder(actor, viewer) : Relevancy.IsRelevant(actor, viewer);
            }

            if (relevant)
            {
                if (channel is null)
                {
                    NetClassInfo? info = Classes.Find(actor.GetType());
                    channel = info is null ? null : OpenActorChannel(connection, actor, info);
                    if (channel is null)
                    {
                        continue;
                    }
                }

                channel.RelevantTime = now + (long)(K.RelevantTimeJitter * _random.FRand() * 1_000_000f);
            }
            else if (channel is not null)
            {
                if (now - channel.RelevantTime >= RelevantTimeoutMicros)
                {
                    CloseIrrelevant(connection, channel, actor);
                    continue;
                }
            }
            else
            {
                continue;
            }

            if (deferred)
            {
                // Interest management: the channel stays open and its changes stay dirty for a later pass.
                Stats.InterestDeferred++;
                continue;
            }

            if (!connection.IsNetReady)
            {
                complete = false;
                break;
            }

            if (channel.IsNetReady)
            {
                if (channel.ReplicateActor(stamp) == ActorChannel.ReplicateResult.Deferred)
                {
                    complete = false;
                }
            }
            else
            {
                complete = false;
            }

            if (connection.State == EConnectionState.USOCK_Closed)
            {
                return;
            }

            if (!connection.IsNetReady)
            {
                complete = false;
                break;
            }
        }

        if (!complete)
        {
            Stats.PassesCutShort++;
        }

        connection.LastServerTickTime = now;
        if (complete)
        {
            connection.MarkFlushComplete();
        }
    }

    /// <summary>
    /// A map actor (bStatic or bNoDelete) can only be named by its SVMap index; one the map binding does not know
    /// cannot be sent (a client cannot spawn it).
    /// </summary>
    private bool CanBeNamed(Actor actor) => !(actor.bStatic || actor.bNoDelete) || MapBinding.IndexOf(actor) >= 0;

    /// <summary>The level's network actor list: everything but static actors that are not always relevant (LevelInfo stays).</summary>
    private static bool OnNetworkList(Actor actor) => !actor.bStatic || actor.bAlwaysRelevant || actor is LevelInfo;

    /// <summary>Rule 4, the frequency gate: with a listen server's rate of 0 everything passes.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-replication.md rule 4 (see its Open question 1)")]
    private bool PassesUpdateGate(Actor actor, int rate, float dt)
    {
        if (rate == 0 || actor.NetUpdateFrequency >= rate)
        {
            return true;
        }

        return Settings.UpdateGate switch
        {
            NetUpdateGate.Decoded or NetUpdateGate.DecodedGuarded => actor.NetUpdateFrequency * dt > 1f,
            NetUpdateGate.Stochastic => _random.FRand() < actor.NetUpdateFrequency * dt,
            _ => true,
        };
    }

    /// <summary>
    /// <see cref="NetUpdateGate.DecodedGuarded"/>: an actor the decoded gate refuses goes when its channel never sent
    /// on this connection or last sent at least <c>1 / NetUpdateFrequency</c> seconds ago.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Starvation guard for a steady dedicated server (net-replication.md open question 1)")]
    private bool PassesStarvationGuard(Actor actor, ActorChannel? channel, long now)
    {
        if (Settings.UpdateGate != NetUpdateGate.DecodedGuarded || actor.NetUpdateFrequency <= 0f)
        {
            return false;
        }

        if (channel is null || !channel.SentInitial)
        {
            return true;
        }

        return now - channel.LastUpdateTime >= (long)(1_000_000f / actor.NetUpdateFrequency);
    }

    /// <summary>Who looks through <paramref name="connection"/> (its controller's view target and view point), as the server pass sees it.</summary>
    public NetViewer ViewerFor(NetConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Actor is null)
        {
            throw new InvalidOperationException("The connection has no actor yet.");
        }

        return ViewerOf(connection);
    }

    private NetViewer ViewerOf(NetConnection connection)
    {
        Actor real = connection.Actor!;
        Actor viewer = real is PlayerController pc ? pc.ViewTarget ?? pc.Pawn ?? real : real;
        FVector source = ViewPoint?.Invoke(connection) ?? real.Location;
        return new NetViewer(real, viewer, source);
    }

    private ActorChannel? OpenActorChannel(NetConnection connection, Actor actor, NetClassInfo info)
    {
        if (!connection.HasFreeChannelIndex || !CanBeNamed(actor))
        {
            return null;
        }

        ActorNetState state = GetOrCreateState(actor, info);
        var channel = (ActorChannel)connection.CreateChannel(EChannelType.CHTYPE_Actor, openedLocally: true);
        channel.SetChannelActor(actor, info, state);
        channel.RelevantTime = Time;
        Stats.ChannelsOpened++;
        return channel;
    }

    private ActorNetState GetOrCreateState(Actor actor, NetClassInfo info)
    {
        if (!_stateByActorId.TryGetValue(actor.ActorId, out ActorNetState? state))
        {
            state = new ActorNetState(actor, info);
            _stateByActorId.Add(actor.ActorId, state);
            _states.Add(state);
        }

        return state;
    }

    /// <summary>Rule 6: a channel irrelevant for RelevantTimeout closes, with those of a skeletal actor's bone attachments.</summary>
    private void CloseIrrelevant(NetConnection connection, ActorChannel channel, Actor actor)
    {
        channel.Close();
        Stats.ChannelsClosedIrrelevant++;
        if (actor.DrawType != EDrawType.DT_Mesh)
        {
            return;
        }

        foreach (Actor attached in actor.Attached)
        {
            if (!attached.bAlwaysRelevant && !attached.AttachmentBone.IsNone && connection.FindActorChannel(attached) is { Closing: false } a)
            {
                a.Close();
                Stats.ChannelsClosedIrrelevant++;
            }
        }
    }

    // ---- replicated function calls -------------------------------------------------------------------------

    /// <summary>
    /// AActor::ProcessRemoteFunction (spec/native/net-replication.md rules 19 and 20): decides whether a call of
    /// <paramref name="functionName"/> on <paramref name="actor"/> runs here, is swallowed or goes to the other side,
    /// and sends it. Ports call this where the script calls a replicated function; with
    /// <see cref="ENetCallResult.RunLocally"/> or <see cref="ENetCallResult.SentAndRunLocally"/> they run the body.
    /// </summary>
    [UcNative("AActor::ProcessRemoteFunction", Exe = 0x10a29830, Editor = 0x1118fa40)]
    public ENetCallResult ProcessRemoteFunction(Actor actor, string functionName, NetCallArgs? args = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(functionName);
        RepFunction? f = Classes.Find(actor.GetType())?.Layout.FindFunction(functionName);
        OpenScct.Core.Uc.UcFunctionInfo? declared = actor.GetUcClass().FindFunction(functionName);
        bool simulated = f?.IsSimulated ?? declared?.IsSimulated ?? false;
        bool native = f?.IsNative ?? declared?.IsNative ?? false;

        // 19.1
        if ((declared?.IsStatic ?? false) || actor.bDeleteMe || NetMode == ENetMode.NM_Standalone)
        {
            return ENetCallResult.RunLocally;
        }

        // 19.2, 19.3
        bool absorb = actor.Role <= ENetRole.ROLE_SimulatedProxy && !simulated && !native;
        ENetCallResult local = absorb ? ENetCallResult.Absorbed : ENetCallResult.RunLocally;
        if (f is null || !f.IsNet)
        {
            return local;
        }

        // 19.4: the owning connection (not for the broadcast flag).
        NetConnection? target = null;
        if (!f.IsNetAllClients)
        {
            target = IsServer ? (actor.Role == ENetRole.ROLE_Authority ? OwningConnection(actor) : null) : ServerConnection;
            if (target is null)
            {
                return local;
            }
        }

        // 19.5: the condition, once, in the actor's current flags.
        var flags = new RepFlags(actor.bNetInitial, actor.bNetOwner, actor.bNetDirty, actor.Role, actor.RemoteRole, NetMode);
        if (!f.Condition.Evaluate(actor, flags))
        {
            return local;
        }

        if (f.IsNetAllClients)
        {
            // Rule 20: every client connection with an open channel; no channel is opened for a broadcast.
            if (!IsServer)
            {
                return ENetCallResult.Absorbed;
            }

            if (FindState(actor) is { } state)
            {
                for (int i = 0; i < state.Channels.Count; i++)
                {
                    ActorChannel channel = state.Channels[i];
                    if (!channel.Closing && channel.Connection.State == EConnectionState.USOCK_Open)
                    {
                        SendCall(channel.Connection, channel, f, args);
                    }
                }
            }

            if (NetMode == ENetMode.NM_ListenServer)
            {
                SendBroadcastToLocalPlayers(actor, f, args);
            }

            return NetMode == ENetMode.NM_ListenServer ? ENetCallResult.SentAndRunLocally : ENetCallResult.Sent;
        }

        // 19.6, 19.7
        ActorChannel? ch = target!.FindActorChannel(actor);
        if (ch is null && IsServer && Classes.Find(actor.GetType()) is { } info && target.bJoined)
        {
            ch = OpenActorChannel(target, actor, info);
            ch?.ReplicateActor(++_captureStamp);
        }

        if (ch is null || ch.Closing)
        {
            Stats.CallsDropped++;
            return ENetCallResult.Sent;
        }

        SendCall(target, ch, f, args);
        return ENetCallResult.Sent;
    }

    /// <summary>
    /// Rule 20 on a listen server: the original runs a broadcast locally, so the host's own players see it wherever
    /// they are. Here they are loopback clients, so a local connection without a channel for the actor gets one opened
    /// (as rule 19.7 does) and the call; remote clients still need an open channel.
    /// </summary>
    private void SendBroadcastToLocalPlayers(Actor actor, RepFunction f, NetCallArgs? args)
    {
        if (Classes.Find(actor.GetType()) is not { } info)
        {
            return;
        }

        for (int i = 0; i < _connections.Count; i++)
        {
            NetConnection c = _connections[i];
            if (!c.IsLocal || !c.bJoined || c.State != EConnectionState.USOCK_Open || c.FindActorChannel(actor) is not null)
            {
                continue;
            }

            if (OpenActorChannel(c, actor, info) is { } ch)
            {
                ch.ReplicateActor(++_captureStamp);
                SendCall(c, ch, f, args);
            }
        }
    }

    /// <summary>
    /// The channel a hand-written call of <paramref name="actor"/> goes on to <paramref name="connection"/> (rule 19.7
    /// for extension functions): on a server a missing channel is opened and the actor replicated first; on a client
    /// only an existing one is used. Null when there is none.
    /// </summary>
    public ActorChannel? ChannelForCall(Actor actor, NetConnection connection)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != EConnectionState.USOCK_Open)
        {
            return null;
        }

        ActorChannel? ch = connection.FindActorChannel(actor);
        if (ch is null && IsServer && connection.bJoined && Classes.Find(actor.GetType()) is { } info)
        {
            ch = OpenActorChannel(connection, actor, info);
            ch?.ReplicateActor(++_captureStamp);
        }

        return ch is { Closing: false } ? ch : null;
    }

    private void SendCall(NetConnection connection, ActorChannel channel, RepFunction f, NetCallArgs? args)
    {
        if (!f.IsReliable && !connection.IsNetReady)
        {
            Stats.CallsDropped++;
            return;
        }

        channel.SendCall(f, args);
        Stats.CallsSent++;
    }

    /// <summary>Rule 21: reads a received call and runs it when this side accepts it.</summary>
    internal void ReceivedFunction(ActorChannel channel, RepFunction f, FBitReader r)
    {
        NetConnection connection = channel.Connection;
        Actor? actor = channel.Actor;
        if (f.Extension is { } extension)
        {
            if (actor is null || (IsServer && (!extension.ClientToServer || !IsNetOwner(actor, connection))))
            {
                // Not allowed: drop the rest of the bunch (only the function knows its argument sizes).
                r.Skip(r.BitsLeft);
                Stats.CallsRejected++;
                return;
            }

            extension.Receive(actor, r, connection);
            Stats.CallsReceived++;
            return;
        }

        NetCallArgs args = _receiveArgs.Values.Length >= f.ValueCount ? _receiveArgs : new NetCallArgs(f.ValueCount);
        NetWire.ReadArguments(r, f, args, connection);
        if (r.IsError || actor is null || actor.bDeleteMe)
        {
            return;
        }

        if (IsServer)
        {
            if (!IsNetOwner(actor, connection))
            {
                Stats.CallsRejected++;
                return;
            }

            actor.bNetOwner = true;
            var flags = new RepFlags(actor.bNetInitial, true, actor.bNetDirty, actor.RemoteRole, actor.Role, NetMode);
            if (!f.Condition.Evaluate(actor, flags))
            {
                Stats.CallsRejected++;
                return;
            }
        }
        else if (actor.Role <= ENetRole.ROLE_SimulatedProxy && !f.IsSimulated && !f.IsNative)
        {
            Stats.CallsAbsorbed++;
            return;
        }

        NetFunctionHandler? handler = Functions.Find(actor, f);
        if (handler is null)
        {
            Stats.CountUnhandled(f.Name);
            return;
        }

        Stats.CallsReceived++;
        handler(actor, f, args, connection);
    }

    // ---- callbacks from connections ------------------------------------------------------------------------

    internal bool AcceptChannel(NetConnection connection, EChannelType type, int chIndex) => IsServer
        ? type == EChannelType.CHTYPE_Control && chIndex == 0
        : (type == EChannelType.CHTYPE_Control && chIndex == 0) || (type == EChannelType.CHTYPE_Actor && chIndex > 0);

    /// <summary>Client: a channel's actor arrived. The autonomous player controller becomes the local player.</summary>
    internal void OnClientChannelOpened(ActorChannel channel)
    {
        if (channel.Actor is PlayerController { Role: ENetRole.ROLE_AutonomousProxy } pc && ServerConnection is { } server &&
            server.Actor is null or PlayerController)
        {
            server.Actor = pc;
            pc.Player ??= new SimViewport();
            if (World is { } world)
            {
                world.Level.Info.LocalPlayerController = pc;
            }
        }

        Notify.NotifyActorChannelOpened(channel);
    }

    internal void OnPacketReceived(NetConnection connection, uint remoteTick)
    {
        if (!IsServer && ReferenceEquals(connection, ServerConnection))
        {
            Timeline.AddSample(remoteTick, LocalTick);
            if (remoteTick > NewestServerTick)
            {
                NewestServerTick = remoteTick;
            }
        }
    }

    internal void OnFlushComplete(NetConnection connection, uint tick)
    {
        if (IsServer)
        {
            return;
        }

        List<ActorChannel> channels = connection.ActorChannelList;
        for (int i = 0; i < channels.Count; i++)
        {
            channels[i].OnFlushComplete(tick);
        }
    }

    internal void OnConnectionClosed(NetConnection connection) => Notify.NotifyConnectionClosed(connection);

    private void RemoveClosedConnections()
    {
        for (int i = _connections.Count - 1; i >= 0; i--)
        {
            NetConnection connection = _connections[i];
            if (connection.State != EConnectionState.USOCK_Closed)
            {
                continue;
            }

            _connections.RemoveAt(i);
            if (connection.IsRecorder)
            {
                continue;
            }

            _byEndpoint.Remove(connection.RemoteEndpoint);
            Transport.Disconnect(connection.RemoteEndpoint);
            if (ReferenceEquals(connection, ServerConnection))
            {
                ServerConnection = null;
            }
        }
    }

    /// <summary>Closes every connection (sending the close) and stops using the transport.</summary>
    public void Shutdown(string reason = "Shutting down")
    {
        for (int i = _connections.Count - 1; i >= 0; i--)
        {
            _connections[i].Close(reason);
        }

        RemoveClosedConnections();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
    }
}

/// <summary>
/// Map actors by SVMap actor index (index 0 is the LevelInfo). Server and client bind the actors of the same map,
/// so a channel or an object reference can name a map actor by index (spec/native/net-replication.md rule 12,
/// spec/net/model.md rule 14).
/// </summary>
public sealed class NetMapBinding
{
    private Actor?[] _actors = [];
    private readonly Dictionary<int, int> _indexByActorId = new();
    private readonly Dictionary<int, bool[]> _placedByActorId = new();

    public int Count => _actors.Length;

    /// <summary>Binds the actors of a loaded map, by record index (null for records whose spawn failed).</summary>
    public void Bind(IReadOnlyList<Actor?> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        _actors = new Actor?[actors.Count];
        _indexByActorId.Clear();
        _placedByActorId.Clear();
        for (int i = 0; i < actors.Count; i++)
        {
            _actors[i] = actors[i];
            if (actors[i] is { } a)
            {
                _indexByActorId[a.ActorId] = i;
            }
        }
    }

    /// <summary>The SVMap index of <paramref name="actor"/>, or -1 when it is not a bound map actor.</summary>
    public int IndexOf(Actor actor) => _indexByActorId.TryGetValue(actor.ActorId, out int i) && ReferenceEquals(_actors[i], actor) ? i : -1;

    /// <summary>The map actor at <paramref name="index"/>, or null.</summary>
    public Actor? Get(int index) => (uint)index < (uint)_actors.Length && _actors[index] is { bDeleteMe: false } a ? a : null;

    /// <summary>
    /// Server: records, for each bound bStatic or bNoDelete actor, which replicated properties the map placed at a
    /// value other than the class default. Call once after <see cref="Bind"/>, when the map has loaded and before any
    /// client connects. The original opens every channel against the class defaults only, so a map actor's property
    /// set back to its default before a client's channel opens never reaches that client, which keeps the map's
    /// value; a channel to a map actor sends these properties in its first update as well (an OpenSCCT addition,
    /// spec/net/model.md rule 14a).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT addition (spec/net/model.md rule 14a): the original's SetChannelActor (editor 0x11140b30) baselines map actors on class defaults only")]
    public void CapturePlaced(NetClassRegistry classes)
    {
        ArgumentNullException.ThrowIfNull(classes);
        _placedByActorId.Clear();
        foreach (Actor? actor in _actors)
        {
            if (actor is not { bDeleteMe: false } || !(actor.bStatic || actor.bNoDelete) ||
                classes.Find(actor.GetType()) is not { } info)
            {
                continue;
            }

            RepLayout layout = info.Layout;
            var placed = new int[layout.WordCount];
            var placedObjects = new object?[layout.ObjectCount];
            var defaults = new int[layout.WordCount];
            var defaultObjects = new object?[layout.ObjectCount];
            layout.Capture(actor, placed, placedObjects);
            layout.CopyDefaults(defaults, defaultObjects);
            RepProperty[] props = layout.Properties;
            bool[]? differs = null;
            for (int i = 0; i < props.Length; i++)
            {
                if (!RepLayout.Same(props[i], placed, defaults, placedObjects, defaultObjects))
                {
                    differs ??= new bool[props.Length];
                    differs[i] = true;
                }
            }

            if (differs is not null)
            {
                _placedByActorId[actor.ActorId] = differs;
            }
        }
    }

    /// <summary>
    /// Per replicated property of <paramref name="actor"/>, whether the map placed it at a value other than the class
    /// default (<see cref="CapturePlaced"/>); null when none was, or the actor is not a bound map actor.
    /// </summary>
    internal bool[]? PlacedDiffers(Actor actor) =>
        IndexOf(actor) >= 0 && _placedByActorId.TryGetValue(actor.ActorId, out bool[]? differs) ? differs : null;

    /// <summary>
    /// UE2 client map setup: map actors that are bStatic or bNoDelete exchange Role and RemoteRole (a class with
    /// RemoteRole None keeps Role None on the client), every other map actor is destroyed; the server sends those
    /// back if they matter (spec/net/model.md rule 14). Call between <see cref="Level.BeginLoad"/> and
    /// <see cref="Level.EndLoad"/>, before the begin-play passes.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "spec/net/model.md rule 14 (UE2 client load; to confirm in the client load path)")]
    public static void PrepareClientMap(Level level, IReadOnlyList<Actor?> mapActors)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(mapActors);
        for (int i = 0; i < mapActors.Count; i++)
        {
            if (mapActors[i] is not { bDeleteMe: false } actor)
            {
                continue;
            }

            if (actor.bStatic || actor.bNoDelete)
            {
                (actor.Role, actor.RemoteRole) = (actor.RemoteRole, actor.Role);
            }
            else
            {
                level.Destroy(actor);
            }
        }
    }
}
