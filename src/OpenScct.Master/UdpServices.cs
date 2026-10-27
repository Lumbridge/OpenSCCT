using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using Microsoft.Extensions.Logging;
using OpenScct.Net;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>
/// A UDP socket of the master on LiteNetLib, taking unconnected messages only (it never accepts connections).
/// Events run on LiteNetLib's receive threads, so handlers take the registry's lock.
/// </summary>
internal abstract class UdpService : IDisposable
{
    private readonly EventBasedLiteNetListener _listener = new();
    private bool _disposed;

    protected UdpService(ServerRegistry registry, ILogger logger, bool natPunch)
    {
        Registry = registry;
        Logger = logger;
        Manager = new LiteNetManager(_listener)
        {
            UnconnectedMessagesEnabled = true,
            UnsyncedEvents = true,
            NatPunchEnabled = natPunch,
        };
        _listener.NetworkReceiveUnconnectedEvent += (from, reader, _) =>
        {
            try
            {
                Received(from, reader.GetRemainingBytesSpan());
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or SocketException)
            {
                Logger.LogWarning(e, "Bad datagram from {From}", from);
            }
            finally
            {
                reader.Recycle();
            }
        };
        _listener.ConnectionRequestEvent += request => request.Reject();
    }

    protected ServerRegistry Registry { get; }

    protected ILogger Logger { get; }

    protected LiteNetManager Manager { get; }

    public int Port => Manager.LocalPort;

    public void Start(string bind, int port)
    {
        bool started;
        if (string.IsNullOrEmpty(bind) || bind == "*")
        {
            started = Manager.Start(port);
        }
        else
        {
            IPAddress v4 = IPAddress.Parse(bind);
            started = Manager.Start(v4, IPAddress.IsLoopback(v4) ? IPAddress.IPv6Loopback : IPAddress.IPv6Any, port);
        }

        if (!started)
        {
            throw new InvalidOperationException($"{GetType().Name}: could not open UDP port {port}.");
        }
    }

    protected abstract void Received(IPEndPoint from, ReadOnlySpan<byte> data);

    protected void SendTo(IPEndPoint to, ReadOnlySpan<byte> data) => Manager.SendUnconnectedMessage(data, to);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Manager.Stop();
    }
}

/// <summary>
/// The NAT punch introducer (LiteNetLib's NAT punch module). A host registers its game socket every few seconds
/// with <c>h:&lt;host token&gt;</c>; a client sends <c>c:&lt;punch token&gt;</c> from the join answer, and the
/// introducer tells both sides each other's internal and external endpoints so their punch packets meet.
/// </summary>
internal sealed class Introducer : UdpService, INatPunchListener
{
    public Introducer(ServerRegistry registry, ILogger<Introducer> logger)
        : base(registry, logger, natPunch: true)
    {
        Manager.NatPunchModule.UnsyncedEvents = true;
        Manager.NatPunchModule.Init(this);
    }

    public long Introductions { get; private set; }

    protected override void Received(IPEndPoint from, ReadOnlySpan<byte> data)
    {
    }

    void INatPunchListener.OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        if (token.StartsWith("h:", StringComparison.Ordinal) && RelayFrames.TryParseToken(token[2..], out ulong hostToken))
        {
            lock (Registry.Gate)
            {
                if (Registry.FindByHostToken(hostToken) is { } record)
                {
                    record.NatInternal = localEndPoint;
                    record.NatExternal = remoteEndPoint;
                    record.NatSeen = Registry.Now;
                }
            }

            return;
        }

        if (!token.StartsWith("c:", StringComparison.Ordinal) || !RelayFrames.TryParseToken(token[2..], out ulong punch))
        {
            return;
        }

        IPEndPoint? hostInternal = null;
        IPEndPoint? hostExternal = null;
        lock (Registry.Gate)
        {
            if (Registry.TakePunch(punch) is { } ticket && Registry.Find(ticket.ServerId) is { NatExternal: not null } record &&
                Registry.Now - record.NatSeen <= Registry.Options.RegistrationSeconds)
            {
                hostInternal = record.NatInternal ?? record.NatExternal;
                hostExternal = record.NatExternal;
            }
        }

        if (hostExternal is null)
        {
            Logger.LogInformation("Punch request {Token} from {From} has no live host", token, remoteEndPoint);
            return;
        }

        Manager.NatPunchModule.NatIntroduce(hostInternal!, hostExternal, localEndPoint, remoteEndPoint, token);
        Introductions++;
    }

    void INatPunchListener.OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
    }
}

/// <summary>
/// The relay (frames in <see cref="RelayFrames"/>): hosts register their game socket with their host token, clients
/// send datagrams tagged with the client token from their join answer, and the relay forwards each one to the other
/// side with the session's id, within per-session rate limits. It never reads what it forwards.
/// </summary>
internal sealed class Relay : UdpService
{
    private readonly byte[] _frame = new byte[RelayFrames.MaxFrameBytes];
    private readonly byte[] _ack = [RelayFrames.Magic, RelayFrames.HostHelloAck];

    public Relay(ServerRegistry registry, ILogger<Relay> logger)
        : base(registry, logger, natPunch: false)
    {
    }

    protected override void Received(IPEndPoint from, ReadOnlySpan<byte> data)
    {
        if (!RelayFrames.IsRelayFrame(data))
        {
            return;
        }

        byte type = RelayFrames.TypeOf(data);
        lock (Registry.Gate)
        {
            switch (type)
            {
                case RelayFrames.HostHello when RelayFrames.TryReadToken(data, out ulong hostToken):
                    if (Registry.FindByHostToken(hostToken) is { } record)
                    {
                        record.RelayEndpoint = from;
                        record.RelaySeen = Registry.Now;
                        SendTo(from, _ack);
                    }

                    break;
                case RelayFrames.ClientData when RelayFrames.TryReadToken(data, out ulong clientToken):
                    FromClient(from, clientToken, data[RelayFrames.ClientHeaderBytes..]);
                    break;
                case RelayFrames.HostData when RelayFrames.TryReadSession(data, out uint session):
                    FromHost(from, session, data[RelayFrames.HostHeaderBytes..]);
                    break;
                case RelayFrames.HostClose when RelayFrames.TryReadSession(data, out uint closed):
                    if (Registry.RelayBySession(closed) is { } s && HostOf(s) is { } host && host.Equals(from))
                    {
                        Registry.CloseSessionLocked(s);
                    }

                    break;
                case RelayFrames.ClientClose when RelayFrames.TryReadToken(data, out ulong left):
                    if (Registry.RelayByToken(left) is { } c && from.Equals(c.Client))
                    {
                        Registry.CloseSessionLocked(c);
                    }

                    break;
            }
        }
    }

    private IPEndPoint? HostOf(RelaySession session) =>
        Registry.Find(session.ServerId) is { RelayEndpoint: { } host } record &&
        Registry.Now - record.RelaySeen <= Registry.Options.RegistrationSeconds
            ? host
            : null;

    private void FromClient(IPEndPoint from, ulong token, ReadOnlySpan<byte> payload)
    {
        if (Registry.RelayByToken(token) is not { } session)
        {
            Refuse(from, payload.Length, RelayFrames.ErrorCode.UnknownToken, "The relay does not know this join; ask the server list again.");
            return;
        }

        if (HostOf(session) is not { } host)
        {
            Refuse(from, payload.Length, RelayFrames.ErrorCode.HostNotRegistered, "The server is not connected to the relay.");
            return;
        }

        double now = Registry.Now;
        if (!Allow(session, now, payload.Length, up: true))
        {
            return;
        }

        if (!from.Equals(session.Client))
        {
            // The first datagram binds the client; a later one from elsewhere moves it (a NAT rebinding).
            session.Client = from;
            session.AnnouncedToHost = false;
        }

        if (!session.AnnouncedToHost)
        {
            int h = RelayFrames.WriteSessionHeader(_frame, RelayFrames.NewSession, session.SessionId);
            int a = System.Text.Encoding.UTF8.GetBytes(LiteNetLibTransportAddress(from), _frame.AsSpan(h));
            SendTo(host, _frame.AsSpan(0, h + a));
            session.AnnouncedToHost = true;
        }

        int n = RelayFrames.WriteSessionHeader(_frame, RelayFrames.HostData, session.SessionId);
        payload.CopyTo(_frame.AsSpan(n));
        SendTo(host, _frame.AsSpan(0, n + payload.Length));
        session.LastActivity = now;
        session.ForwardedUp++;
        Registry.RelayForwarded++;
    }

    /// <summary>An error answer, never bigger than what came in (a spoofed source gains nothing from it).</summary>
    private void Refuse(IPEndPoint to, int payloadLength, RelayFrames.ErrorCode code, string text)
    {
        byte[] frame = RelayFrames.ErrorFrame(code, text);
        if (frame.Length <= RelayFrames.ClientHeaderBytes + payloadLength)
        {
            SendTo(to, frame);
        }
    }

    private void FromHost(IPEndPoint from, uint sessionId, ReadOnlySpan<byte> payload)
    {
        if (Registry.RelayBySession(sessionId) is not { Client: { } client } session || HostOf(session) is not { } host || !host.Equals(from))
        {
            return;
        }

        double now = Registry.Now;
        if (!Allow(session, now, payload.Length, up: false))
        {
            return;
        }

        _frame[0] = RelayFrames.Magic;
        _frame[1] = RelayFrames.ToClient;
        payload.CopyTo(_frame.AsSpan(2));
        SendTo(client, _frame.AsSpan(0, 2 + payload.Length));
        session.LastActivity = now;
        session.ForwardedDown++;
        Registry.RelayForwarded++;
    }

    private bool Allow(RelaySession s, double now, int bytes, bool up)
    {
        if (now - s.WindowStart >= 1.0)
        {
            s.WindowStart = now;
            s.UpFrames = s.UpBytes = s.DownFrames = s.DownBytes = 0;
        }

        MasterOptions o = Registry.Options;
        if (up)
        {
            s.UpFrames++;
            s.UpBytes += bytes;
            return s.UpFrames <= o.RelayFramesPerSecond && s.UpBytes <= o.RelayBytesPerSecond;
        }

        s.DownFrames++;
        s.DownBytes += bytes;
        return s.DownFrames <= o.RelayFramesPerSecond && s.DownBytes <= o.RelayBytesPerSecond;
    }

    private static string LiteNetLibTransportAddress(IPEndPoint endPoint) => LiteNetLibTransport.FormatEndPoint(endPoint);
}

/// <summary>
/// Reachability probes: from a port no host ever sends to, so a NAT that only lets answers in drops them, the master
/// sends <c>SVMP</c> to the host's public address and game port and to the mapping the introducer saw. An
/// <c>SVMp</c> answer means anyone can reach the host directly (port forwarding, UPnP or a public address).
/// </summary>
internal sealed class Prober : UdpService
{
    private readonly Dictionary<ulong, (string ServerId, double Expires)> _pending = new();
    private readonly byte[] _buffer = new byte[DiscoveryPackets.HeaderBytes];

    public Prober(ServerRegistry registry, ILogger<Prober> logger)
        : base(registry, logger, natPunch: false)
    {
    }

    public long ProbesSent { get; private set; }

    /// <summary>Probes <paramref name="record"/> when it never was, its address changed, or the last probe is old.</summary>
    public void MaybeProbe(ServerRecord record)
    {
        var targets = new List<IPEndPoint>();
        lock (Registry.Gate)
        {
            double now = Registry.Now;
            string probedFor = record.PublicIp + ":" + record.Last.GamePort;
            if (record.ProbePending || (record.ProbedFor == probedFor && now - record.ProbedAt < Registry.Options.ReprobeSeconds))
            {
                return;
            }

            record.ProbedFor = probedFor;
            record.ProbedAt = now;
            record.ProbePending = true;
            record.ProbeOk = false;
            if (record.Last.GamePort is > 0 and <= 65535)
            {
                targets.Add(new IPEndPoint(record.PublicIp, record.Last.GamePort));
            }

            if (record.NatExternal is { } external && !targets.Contains(external))
            {
                targets.Add(external);
            }

            ulong nonce = ServerRegistry.NewToken();
            _pending[nonce] = (record.Id, now + Registry.Options.ProbeTimeoutSeconds);
            int n = DiscoveryPackets.WriteHeader(_buffer, DiscoveryPackets.Probe, nonce);
            foreach (IPEndPoint target in targets)
            {
                SendTo(target, _buffer.AsSpan(0, n));
                ProbesSent++;
            }
        }
    }

    /// <summary>Ends probes past their timeout (no answer: not directly reachable).</summary>
    public void Sweep()
    {
        lock (Registry.Gate)
        {
            double now = Registry.Now;
            var done = new List<ulong>();
            foreach (KeyValuePair<ulong, (string ServerId, double Expires)> p in _pending)
            {
                if (p.Value.Expires < now)
                {
                    done.Add(p.Key);
                    if (Registry.Find(p.Value.ServerId) is { } record)
                    {
                        record.ProbePending = false;
                    }
                }
            }

            foreach (ulong nonce in done)
            {
                _pending.Remove(nonce);
            }
        }
    }

    protected override void Received(IPEndPoint from, ReadOnlySpan<byte> data)
    {
        if (DiscoveryPackets.TypeOf(data) != DiscoveryPackets.ProbeReply)
        {
            return;
        }

        lock (Registry.Gate)
        {
            if (_pending.Remove(DiscoveryPackets.NonceOf(data), out (string ServerId, double Expires) p) && Registry.Find(p.ServerId) is { } record)
            {
                record.ProbePending = false;
                record.ProbeOk = true;
                record.ProbeTarget = from;
            }
        }
    }
}
