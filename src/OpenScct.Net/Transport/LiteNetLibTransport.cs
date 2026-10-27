using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace OpenScct.Net;

/// <summary>A message that arrived outside any connection (server queries, master probes), with its sender.</summary>
public delegate void UnconnectedMessageHandler(IPEndPoint from, ReadOnlySpan<byte> data, bool broadcast);

/// <summary>A NAT introduction that succeeded: the endpoint to talk to and the token the introducer paired on.</summary>
public readonly record struct NatPunchResult(IPEndPoint Target, string Token, bool External);

/// <summary>
/// UDP transport on LiteNetLib. Only unreliable delivery is used: LiteNetLib gives us the socket, peer management, MTU
/// handling and NAT punch-through, and our own packets carry everything else. Events are collected on the caller's
/// thread by <see cref="TryReceive"/> (or <see cref="Poll"/>).
/// <para>Three kinds of remote endpoint share one socket: LiteNetLib peers (direct or after a NAT punch), sessions
/// of a relay this host registered with (<see cref="AttachRelay"/>, frames in <see cref="RelayFrames"/>), and a
/// relay route of a client (<see cref="RelayAddress"/>). Endpoint ids are never reused while the transport lives, so
/// a late datagram of a closed connection can never be taken for a new one.</para>
/// <para>Messages outside connections (server info queries, the master's reachability probes) go to
/// <see cref="Unconnected"/>; <see cref="SendUnconnected"/> answers them.</para>
/// </summary>
public sealed class LiteNetLibTransport : ITransport, ILiteNetEventListener, INatPunchListener
{
    /// <summary>LiteNetLib connection key; peers with another key are refused.</summary>
    public const string DefaultConnectionKey = "SVM";

    /// <summary>
    /// MTU every peer uses. 1232 bytes fits the IPv6 minimum MTU (1280) after IP and UDP headers, so it passes
    /// nearly every path without fragmentation.
    /// </summary>
    public const int Mtu = 1232;

    /// <summary>Prefix of a client's relay route address: <c>relay://host:port/token</c>.</summary>
    public const string RelayScheme = "relay://";

    private readonly struct Received(byte[] data, int length, int endpoint)
    {
        public readonly byte[] Data = data;
        public readonly int Length = length;
        public readonly int Endpoint = endpoint;
    }

    /// <summary>A relay endpoint: a session of a relay this host registered with, or a client's route through a relay.</summary>
    private sealed class RelayRoute
    {
        public required IPEndPoint Relay { get; init; }

        /// <summary>Host side: the relay's session id.</summary>
        public uint Session { get; init; }

        /// <summary>Client side: the client token.</summary>
        public ulong Token { get; init; }

        public bool IsClient { get; init; }

        public string Address { get; set; } = "relay";
    }

    private readonly record struct SessionKey(IPEndPoint Relay, uint Session);

    private readonly LiteNetManager _manager;
    private readonly string _key;
    private readonly int _maxConnections;
    private readonly Queue<Received> _received = new(256);
    private readonly Stack<byte[]> _pool = new();
    private readonly Dictionary<int, LiteNetPeer> _peers = new();
    private readonly Dictionary<int, RelayRoute> _routes = new();
    private readonly Dictionary<SessionKey, int> _hostSessions = new();
    private readonly Dictionary<IPEndPoint, int> _clientRoutes = new();
    private readonly Dictionary<IPEndPoint, ulong> _relays = new();
    private readonly List<NatPunchResult> _punches = new();
    private readonly byte[] _frame = new byte[RelayFrames.MaxFrameBytes];
    private int _nextId = 1;
    private bool _disposed;

    private LiteNetLibTransport(string key, int maxConnections)
    {
        _key = key;
        _maxConnections = maxConnections;
        _manager = new LiteNetManager(this)
        {
            DisconnectTimeout = (int)(K.ConnectionTimeout * 1000f),
            MtuOverride = Mtu,
            AutoRecycle = false,
            UnconnectedMessagesEnabled = true,
            NatPunchEnabled = true,
        };
        _manager.NatPunchModule.Init(this);
    }

    /// <summary>Largest datagram we send: the MTU minus LiteNetLib's unreliable header and a safety margin.</summary>
    public int MaxDatagramSize => 1200;

    /// <summary>Port the socket is bound to.</summary>
    public int LocalPort => _manager.LocalPort;

    /// <summary>True when the socket is bound to 127.0.0.1 and ::1 only (tests): nothing outside this machine reaches it.</summary>
    public bool LoopbackOnly { get; private set; }

    /// <summary>Datagrams dropped because a peer could not take them (not connected, too big).</summary>
    public long SendFailures { get; private set; }

    /// <summary>Messages that arrived outside connections, other than relay frames (called on the polling thread).</summary>
    public UnconnectedMessageHandler? Unconnected { get; set; }

    /// <summary>Decides whether a connection request from an address is accepted (bans); null accepts every request.</summary>
    public Func<IPEndPoint, bool>? AcceptFilter { get; set; }

    /// <summary>The last error a relay sent this client (readable, for the join screen), or null.</summary>
    public string? LastRelayError { get; private set; }

    /// <summary>Relay frames this transport forwarded or received, both ways (diagnostics).</summary>
    public long RelayFramesSent { get; private set; }

    public long RelayFramesReceived { get; private set; }

    /// <summary>
    /// A server socket on <paramref name="port"/>. With <paramref name="loopbackOnly"/> it binds 127.0.0.1 and
    /// ::1 only (tests; no firewall prompt), otherwise every interface.
    /// </summary>
    public static LiteNetLibTransport Listen(int port, int maxConnections, bool loopbackOnly = false,
        string key = DefaultConnectionKey)
    {
        var transport = new LiteNetLibTransport(key, maxConnections) { LoopbackOnly = loopbackOnly };
        bool started = loopbackOnly
            ? transport._manager.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, port)
            : transport._manager.Start(port);
        if (!started)
        {
            transport.Dispose();
            throw new InvalidOperationException($"Could not open UDP port {port}. Is it already used?");
        }

        return transport;
    }

    /// <summary>A client socket on an ephemeral port.</summary>
    public static LiteNetLibTransport CreateClient(bool loopbackOnly = false, string key = DefaultConnectionKey)
    {
        var transport = new LiteNetLibTransport(key, maxConnections: 1) { LoopbackOnly = loopbackOnly };
        bool started = loopbackOnly
            ? transport._manager.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0)
            : transport._manager.Start();
        if (!started)
        {
            transport.Dispose();
            throw new InvalidOperationException("Could not open a UDP socket.");
        }

        return transport;
    }

    /// <summary>The address <see cref="Connect"/> takes for a route through a relay with a client token.</summary>
    public static string RelayAddress(string relay, ulong clientToken) => RelayScheme + relay + "/" + RelayFrames.TokenText(clientToken);

    /// <summary>
    /// Connects to "host:port" (IPv6 as "[addr]:port"), or opens a route through a relay for
    /// <c>relay://host:port/token</c> (<see cref="RelayAddress"/>).
    /// </summary>
    public EndpointId Connect(string address)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (address.StartsWith(RelayScheme, StringComparison.OrdinalIgnoreCase))
        {
            return ConnectRelay(address);
        }

        ParseAddress(address, out string host, out int port);
        LiteNetPeer? peer = _manager.Connect(host, port, _key);
        if (peer is null)
        {
            throw new InvalidOperationException($"Could not connect to '{address}'.");
        }

        if (peer.Tag is int existing && _peers.ContainsKey(existing))
        {
            return new EndpointId(existing);
        }

        int id = _nextId++;
        peer.Tag = id;
        _peers[id] = peer;
        return new EndpointId(id);
    }

    private EndpointId ConnectRelay(string address)
    {
        string rest = address[RelayScheme.Length..];
        int slash = rest.LastIndexOf('/');
        if (slash <= 0 || !RelayFrames.TryParseToken(rest[(slash + 1)..], out ulong token))
        {
            throw new FormatException($"Expected relay://host:port/token, got '{address}'.");
        }

        IPEndPoint relay = Resolve(rest[..slash]);
        if (_clientRoutes.TryGetValue(relay, out int known) && _routes.TryGetValue(known, out RelayRoute? old) && old.Token == token)
        {
            return new EndpointId(known);
        }

        int id = _nextId++;
        _routes[id] = new RelayRoute { Relay = relay, Token = token, IsClient = true, Address = rest[..slash] };
        _clientRoutes[relay] = id;
        LastRelayError = null;
        return new EndpointId(id);
    }

    public bool Send(EndpointId to, ReadOnlySpan<byte> datagram)
    {
        if (_disposed || !to.IsValid || datagram.Length > MaxDatagramSize)
        {
            SendFailures++;
            return false;
        }

        if (_peers.TryGetValue(to.Value, out LiteNetPeer? peer))
        {
            if (peer.ConnectionState != ConnectionState.Connected)
            {
                SendFailures++;
                return false;
            }

            peer.Send(datagram, DeliveryMethod.Unreliable);

            // LiteNetLib sends queued datagrams on its own update (every 15 ms); waking it sends this one now.
            _manager.TriggerUpdate();
            return true;
        }

        if (_routes.TryGetValue(to.Value, out RelayRoute? route))
        {
            int header = route.IsClient
                ? RelayFrames.WriteTokenHeader(_frame, RelayFrames.ClientData, route.Token)
                : RelayFrames.WriteSessionHeader(_frame, RelayFrames.HostData, route.Session);
            datagram.CopyTo(_frame.AsSpan(header));
            RelayFramesSent++;
            return _manager.SendUnconnectedMessage(_frame.AsSpan(0, header + datagram.Length), route.Relay);
        }

        SendFailures++;
        return false;
    }

    public bool TryReceive(Span<byte> buffer, out EndpointId from, out int length)
    {
        if (_received.Count == 0)
        {
            Poll();
        }

        while (_received.TryDequeue(out Received datagram))
        {
            bool known = _peers.ContainsKey(datagram.Endpoint) || _routes.ContainsKey(datagram.Endpoint);
            if (known)
            {
                datagram.Data.AsSpan(0, datagram.Length).CopyTo(buffer);
            }

            _pool.Push(datagram.Data);
            if (!known)
            {
                continue;
            }

            length = datagram.Length;
            from = new EndpointId(datagram.Endpoint);
            return true;
        }

        from = EndpointId.None;
        length = 0;
        return false;
    }

    /// <summary>Runs LiteNetLib's and the NAT punch module's events on this thread (receive, unconnected, punch).</summary>
    public void Poll()
    {
        if (_disposed)
        {
            return;
        }

        _manager.PollEvents();
        _manager.NatPunchModule.PollEvents();
    }

    public void Disconnect(EndpointId endpoint)
    {
        if (!endpoint.IsValid)
        {
            return;
        }

        if (_peers.Remove(endpoint.Value, out LiteNetPeer? peer))
        {
            _manager.DisconnectPeer(peer);
            return;
        }

        if (_routes.Remove(endpoint.Value, out RelayRoute? route))
        {
            if (route.IsClient)
            {
                _clientRoutes.Remove(route.Relay);
                int n = RelayFrames.WriteTokenHeader(_frame, RelayFrames.ClientClose, route.Token);
                _manager.SendUnconnectedMessage(_frame.AsSpan(0, n), route.Relay);
            }
            else
            {
                _hostSessions.Remove(new SessionKey(route.Relay, route.Session));
                int n = RelayFrames.WriteSessionHeader(_frame, RelayFrames.HostClose, route.Session);
                _manager.SendUnconnectedMessage(_frame.AsSpan(0, n), route.Relay);
            }
        }
    }

    /// <summary>The remote address of an endpoint as text (a relay session gives the address the relay reported), or null.</summary>
    public string? RemoteAddress(EndpointId endpoint)
    {
        if (_peers.TryGetValue(endpoint.Value, out LiteNetPeer? peer))
        {
            return FormatEndPoint(peer);
        }

        return _routes.TryGetValue(endpoint.Value, out RelayRoute? route) ? route.Address : null;
    }

    /// <summary>The remote IP address of an endpoint (a LiteNetLib peer only), or null.</summary>
    public IPAddress? RemoteIp(EndpointId endpoint) => _peers.TryGetValue(endpoint.Value, out LiteNetPeer? peer) ? peer.Address : null;

    /// <summary>True when the endpoint reaches its remote through a relay.</summary>
    public bool IsRelayed(EndpointId endpoint) => _routes.ContainsKey(endpoint.Value);

    /// <summary>Round trip time LiteNetLib measured for a peer, in milliseconds, or -1.</summary>
    public int PeerRoundTripMs(EndpointId endpoint) =>
        _peers.TryGetValue(endpoint.Value, out LiteNetPeer? peer) ? peer.RoundTripTime : -1;

    /// <summary>Sends a message outside any connection (an answer to a query, a probe).</summary>
    public bool SendUnconnected(IPEndPoint to, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(to);
        return !_disposed && _manager.SendUnconnectedMessage(data, to);
    }

    // ---- relay (host side) ----------------------------------------------------------------------------------

    /// <summary>
    /// Host: accepts relay sessions from <paramref name="relay"/> under <paramref name="hostToken"/> (from the master's
    /// heartbeat answer) and sends the first hello. Call <see cref="SendRelayHello"/> every few seconds afterwards so the
    /// relay keeps the registration and the NAT keeps the mapping.
    /// </summary>
    public void AttachRelay(IPEndPoint relay, ulong hostToken)
    {
        ArgumentNullException.ThrowIfNull(relay);
        _relays[relay] = hostToken;
        SendRelayHello();
    }

    /// <summary>Host: stops taking sessions from <paramref name="relay"/> (open sessions stay until their connections end).</summary>
    public void DetachRelay(IPEndPoint relay) => _relays.Remove(relay);

    /// <summary>Host: the relays this host registered with.</summary>
    public IEnumerable<IPEndPoint> Relays => _relays.Keys;

    /// <summary>Host: sends the registration hello to every attached relay.</summary>
    public void SendRelayHello()
    {
        foreach (KeyValuePair<IPEndPoint, ulong> relay in _relays)
        {
            int n = RelayFrames.WriteTokenHeader(_frame, RelayFrames.HostHello, relay.Value);
            _manager.SendUnconnectedMessage(_frame.AsSpan(0, n), relay.Key);
        }
    }

    /// <summary>True once a relay answered the host's hello (<see cref="RelayFrames.HostHelloAck"/>).</summary>
    public bool RelayAcknowledged { get; private set; }

    // ---- NAT punch ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asks the introducer to pair this socket with another one registered under the same token (LiteNetLib NAT punch).
    /// A host repeats its registration every few seconds; a client sends its join token once or twice and waits for
    /// <see cref="TryTakePunch"/>.
    /// </summary>
    public void RequestIntroduction(IPEndPoint introducer, string token)
    {
        ArgumentNullException.ThrowIfNull(introducer);
        ArgumentException.ThrowIfNullOrEmpty(token);
        _manager.NatPunchModule.SendNatIntroduceRequest(introducer, token);
    }

    /// <summary>The endpoint a punch with <paramref name="token"/> reached, if one succeeded since the last call.</summary>
    public bool TryTakePunch(string token, out NatPunchResult result)
    {
        for (int i = 0; i < _punches.Count; i++)
        {
            if (_punches[i].Token == token)
            {
                result = _punches[i];
                _punches.RemoveAt(i);
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>Punches that succeeded and nobody took yet (a host sees one per client it was introduced to).</summary>
    public int PendingPunches => _punches.Count;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (int id in new List<int>(_routes.Keys))
        {
            Disconnect(new EndpointId(id));
        }

        _manager.Stop();
    }

    internal static void ParseAddress(string address, out string host, out int port)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        int colon = address.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(address.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
            port is <= 0 or > 65535)
        {
            throw new FormatException($"Expected host:port, got '{address}'.");
        }

        host = address[..colon];
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }
    }

    /// <summary>Resolves "host:port" (a name, IPv4 or bracketed IPv6) to an endpoint, preferring IPv4.</summary>
    public static IPEndPoint Resolve(string address)
    {
        ParseAddress(address, out string host, out int port);
        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            return new IPEndPoint(ip, port);
        }

        IPAddress[] addresses = Dns.GetHostAddresses(host);
        IPAddress? chosen = null;
        foreach (IPAddress a in addresses)
        {
            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                chosen = a;
                break;
            }

            chosen ??= a;
        }

        return chosen is null ? throw new FormatException($"Could not resolve '{host}'.") : new IPEndPoint(chosen, port);
    }

    /// <summary>"ip:port", with IPv6 in brackets.</summary>
    public static string FormatEndPoint(IPEndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        IPAddress ip = endPoint.Address.IsIPv4MappedToIPv6 ? endPoint.Address.MapToIPv4() : endPoint.Address;
        string host = ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString();
        return host + ":" + endPoint.Port.ToString(CultureInfo.InvariantCulture);
    }

    private void Enqueue(ReadOnlySpan<byte> data, int endpoint)
    {
        if (data.Length > MaxDatagramSize)
        {
            return;
        }

        byte[] buffer = _pool.Count > 0 ? _pool.Pop() : new byte[MaxDatagramSize];
        data.CopyTo(buffer);
        _received.Enqueue(new Received(buffer, data.Length, endpoint));
    }

    private int ConnectionCount => _peers.Count + _hostSessions.Count;

    private void ReceivedRelayFrame(IPEndPoint from, ReadOnlySpan<byte> frame)
    {
        RelayFramesReceived++;
        byte type = RelayFrames.TypeOf(frame);
        if (_clientRoutes.TryGetValue(from, out int clientId))
        {
            if (type == RelayFrames.ToClient)
            {
                Enqueue(frame[2..], clientId);
            }
            else if (type == RelayFrames.Error && RelayFrames.TryReadError(frame, out _, out string text))
            {
                LastRelayError = text;
            }

            return;
        }

        if (!_relays.ContainsKey(from))
        {
            return;
        }

        if (type == RelayFrames.HostHelloAck)
        {
            RelayAcknowledged = true;
            return;
        }

        if ((type != RelayFrames.HostData && type != RelayFrames.NewSession) || !RelayFrames.TryReadSession(frame, out uint session))
        {
            return;
        }

        var key = new SessionKey(from, session);
        if (!_hostSessions.TryGetValue(key, out int id))
        {
            if (ConnectionCount >= _maxConnections)
            {
                return;
            }

            id = _nextId++;
            _hostSessions[key] = id;
            _routes[id] = new RelayRoute { Relay = from, Session = session };
        }

        if (type == RelayFrames.NewSession)
        {
            _routes[id].Address = "relay " + System.Text.Encoding.UTF8.GetString(frame[RelayFrames.HostHeaderBytes..]);
            return;
        }

        Enqueue(frame[RelayFrames.HostHeaderBytes..], id);
    }

    // ---- LiteNetLib events (raised inside Poll on the caller's thread) ------------------------------------------

    void ILiteNetEventListener.OnPeerConnected(LiteNetPeer peer)
    {
        if (peer.Tag is not int)
        {
            int id = _nextId++;
            peer.Tag = id;
            _peers[id] = peer;
        }
    }

    void ILiteNetEventListener.OnPeerDisconnected(LiteNetPeer peer, DisconnectInfo disconnectInfo)
    {
        // The endpoint stays known until its connection lets go of it (Disconnect): datagrams that arrived before the
        // remote went away (a kick's reason, the close) are still delivered. Sends fail from now on.
    }

    void ILiteNetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
    }

    void ILiteNetEventListener.OnNetworkReceive(LiteNetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        if (peer.Tag is int id)
        {
            Enqueue(reader.GetRemainingBytesSpan(), id);
        }

        reader.Recycle();
    }

    void ILiteNetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader,
        UnconnectedMessageType messageType)
    {
        ReadOnlySpan<byte> data = reader.GetRemainingBytesSpan();
        if (RelayFrames.IsRelayFrame(data))
        {
            ReceivedRelayFrame(remoteEndPoint, data);
        }
        else
        {
            Unconnected?.Invoke(remoteEndPoint, data, messageType == UnconnectedMessageType.Broadcast);
        }

        reader.Recycle();
    }

    void ILiteNetEventListener.OnNetworkLatencyUpdate(LiteNetPeer peer, int latency)
    {
    }

    void ILiteNetEventListener.OnConnectionRequest(LiteConnectionRequest request)
    {
        if (ConnectionCount < _maxConnections && (AcceptFilter?.Invoke(request.RemoteEndPoint) ?? true))
        {
            request.AcceptIfKey(_key);
        }
        else
        {
            request.Reject();
        }
    }

    void ILiteNetEventListener.OnMessageDelivered(LiteNetPeer peer, object userData)
    {
    }

    void ILiteNetEventListener.OnPeerAddressChanged(LiteNetPeer peer, IPEndPoint previousAddress)
    {
    }

    void INatPunchListener.OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
    }

    void INatPunchListener.OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        if (_punches.Count >= 64)
        {
            _punches.RemoveAt(0);
        }

        _punches.Add(new NatPunchResult(targetEndPoint, token, type == NatAddressType.External));
    }
}
