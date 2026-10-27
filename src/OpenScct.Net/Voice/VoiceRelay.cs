using System;
using System.Collections.Generic;
using OpenScct.Net.Voice.Proximity;
using OpenScct.Provenance;

namespace OpenScct.Net.Voice;

/// <summary>What the server's voice relay did.</summary>
public sealed class VoiceRelayStats
{
    public long UpPackets { get; internal set; }

    public long UpBytes { get; internal set; }

    public long DownPackets { get; internal set; }

    public long DownBytes { get; internal set; }

    public long Joins { get; internal set; }

    /// <summary>Datagrams from an endpoint that is no player (or a local link without its player id).</summary>
    public long UnknownSender { get; internal set; }

    public long Malformed { get; internal set; }

    /// <summary>Audio over the per-talker packet rate.</summary>
    public long RateLimited { get; internal set; }

    /// <summary>Audio that nobody could hear.</summary>
    public long Unrouted { get; internal set; }

    /// <summary>Copies not sent because the listener already received <see cref="VoiceK.MaxTalkersPerListener"/> talkers.</summary>
    public long TalkerCapReached { get; internal set; }

    /// <summary>Copies not sent because the listener was out of the talker's hearing range (proximity voice only).</summary>
    public long OutOfRange { get; internal set; }

    public long SendFailures { get; internal set; }
}

/// <summary>
/// The server side of voice chat (FEATURES rl:voice.codec, rl:voice.routing). Clients send their Opus frames to the
/// server, never to each other; the relay works out who may hear each frame (<see cref="VoiceRouting"/> over the
/// roster its <see cref="IVoiceDirectory"/> rebuilds every <see cref="VoiceK.RosterRefreshMs"/>), skips listeners who
/// have not joined voice or muted the talker, keeps at most <see cref="VoiceK.MaxTalkersPerListener"/> talkers per
/// listener, and forwards the frame with the talker's id written by the server. It never decodes audio.
/// <para>Runs on the server's thread: <see cref="VoiceTransport"/> calls it while the driver receives, or the host
/// calls <see cref="Tick"/>. Local links may be attached from any thread.</para>
/// </summary>
[PortOf("rl:voice.routing", Spec = "spec/game/voice-chat.md#server-relay")]
public sealed class VoiceRelay : IVoiceEndpoint
{
    private sealed class Peer(ushort talker)
    {
        public readonly ushort Talker = talker;
        public readonly ushort[] Muted = new ushort[VoiceK.MaxMutedTalkers];
        public readonly ushort[] SlotTalker = new ushort[VoiceK.MaxTalkersPerListener];
        public readonly long[] SlotTime = new long[VoiceK.MaxTalkersPerListener];
        public int MutedCount;
        public long JoinedAt = long.MinValue;
        public bool Receive;
        public long Credit = BurstCredit;
        public long CreditTime = long.MinValue;
        public long BytesUp;
        public long BytesDown;
        public bool Seen;

        public bool HasMuted(ushort talker)
        {
            for (int i = 0; i < MutedCount; i++)
            {
                if (Muted[i] == talker)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private const long CreditPerPacket = 1_000_000;
    private const long BurstCredit = VoiceK.PacketBurst * CreditPerPacket;

    /// <summary>A datagram from an unknown endpoint rebuilds the roster at most this often (a flood of strangers stays cheap).</summary>
    private const long UnknownRefreshMicros = VoiceK.FrameMilliseconds * 1000L;

    private readonly VoiceTransport? _transport;
    private readonly IVoiceDirectory _directory;
    private readonly IClock _clock;
    private readonly Dictionary<ushort, Peer> _peers = new();
    private readonly List<ushort> _stale = new();
    private readonly byte[] _down = new byte[VoiceWire.MaxAudioDatagramBytes];
    private readonly byte[] _local = new byte[VoiceWire.MaxDatagramBytes];
    private readonly object _linksGate = new();
    private VoiceLocalLink[] _links = [];
    private long _rosterTime = long.MinValue;

    /// <param name="transport">The transport remote players' voice arrives on (it is pointed at this relay), or null for local players only.</param>
    public VoiceRelay(VoiceTransport? transport, IVoiceDirectory directory, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(clock);
        _transport = transport;
        _directory = directory;
        _clock = clock;
        if (transport is not null)
        {
            transport.Voice = this;
        }
    }

    /// <summary>A relay for a server driver, routing by the game's state (<see cref="NetDriverVoiceDirectory"/>).</summary>
    public static VoiceRelay ForDriver(VoiceTransport transport, NetDriver driver, IClock clock, IVoiceLinkSource? links = null) =>
        new(transport, new NetDriverVoiceDirectory(driver, links), clock);

    public VoiceRelayStats Stats { get; } = new();

    /// <summary>
    /// Positional voice (the hub, docs/HUB.md): when set, a frame goes only to the listeners in the talker's hearing
    /// range, on top of the stock routing (it never widens what <see cref="VoiceRouting"/> allows). Null for a match.
    /// </summary>
    public ProximityRelayFilter? Proximity { get; set; }

    /// <summary>The roster of the last rebuild.</summary>
    public VoiceRoster Roster { get; } = new();

    /// <summary>Connects a player in this process (a listen host's own player).</summary>
    public void AttachLocal(VoiceLocalLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        lock (_linksGate)
        {
            if (Array.IndexOf(_links, link) < 0)
            {
                _links = [.. _links, link];
            }
        }
    }

    public void DetachLocal(VoiceLocalLink link)
    {
        lock (_linksGate)
        {
            _links = Array.FindAll(_links, l => !ReferenceEquals(l, link));
        }
    }

    /// <summary>Rebuild the roster before the next frame (a team change, a death...).</summary>
    public void Invalidate() => _rosterTime = long.MinValue;

    /// <summary>Uplink and downlink bytes of one talker so far.</summary>
    public (long Up, long Down) BytesOf(ushort talker) => _peers.TryGetValue(talker, out Peer? p) ? (p.BytesUp, p.BytesDown) : (0, 0);

    /// <summary>For hosts whose driver does not read through a <see cref="VoiceTransport"/>: refreshes and serves local links.</summary>
    public void Tick() => OnDispatch();

    public void OnDispatch()
    {
        long now = _clock.NowMicroseconds;
        Refresh(now);
        VoiceLocalLink[] links = _links;
        Span<byte> buffer = _local;
        foreach (VoiceLocalLink link in links)
        {
            while (link.TryReceiveUp(buffer, out int length))
            {
                ushort talker = link.Talker;
                if (talker == 0 || Roster.IndexOf(talker) < 0)
                {
                    Stats.UnknownSender++;
                    continue;
                }

                Handle(talker, buffer[..length], now);
            }
        }
    }

    public void OnVoiceDatagram(EndpointId from, ReadOnlySpan<byte> datagram)
    {
        long now = _clock.NowMicroseconds;
        Refresh(now);
        int index = Roster.IndexOfEndpoint(from);
        if (index < 0 && now - _rosterTime >= UnknownRefreshMicros)
        {
            // A player who logged in since the last rebuild: look again rather than make him wait for the next one.
            Refresh(now, force: true);
            index = Roster.IndexOfEndpoint(from);
        }

        if (index < 0 && VoiceWire.TryReadHeader(datagram, out VoiceHeader claim) && claim.Kind == EVoicePacketKind.Join &&
            _directory.TryClaim(claim.Talker, from))
        {
            Refresh(now, force: true);
            index = Roster.IndexOfEndpoint(from);
        }

        if (index < 0)
        {
            Stats.UnknownSender++;
            return;
        }

        Handle(Roster[index].Talker, datagram, now);
    }

    private void Refresh(long now, bool force = false)
    {
        if (!force && _rosterTime != long.MinValue && now - _rosterTime < VoiceK.RosterRefreshMs * 1000L)
        {
            return;
        }

        _rosterTime = now;
        _directory.Fill(Roster);
        foreach (Peer peer in _peers.Values)
        {
            peer.Seen = false;
        }

        for (int i = 0; i < Roster.Count; i++)
        {
            if (_peers.TryGetValue(Roster[i].Talker, out Peer? peer))
            {
                peer.Seen = true;
            }
        }

        _stale.Clear();
        foreach (Peer peer in _peers.Values)
        {
            if (!peer.Seen)
            {
                _stale.Add(peer.Talker);
            }
        }

        foreach (ushort talker in _stale)
        {
            _peers.Remove(talker);
            Proximity?.Forget(talker);
        }
    }

    private Peer PeerOf(ushort talker)
    {
        if (!_peers.TryGetValue(talker, out Peer? peer))
        {
            peer = new Peer(talker);
            _peers.Add(talker, peer);
        }

        return peer;
    }

    private void Handle(ushort talker, ReadOnlySpan<byte> datagram, long now)
    {
        if (!VoiceWire.TryReadHeader(datagram, out VoiceHeader header))
        {
            Stats.Malformed++;
            return;
        }

        switch (header.Kind)
        {
            case EVoicePacketKind.Join:
                HandleJoin(talker, datagram, now);
                break;
            case EVoicePacketKind.Up:
                HandleUp(talker, datagram, now);
                break;
            default:
                Stats.Malformed++;
                break;
        }
    }

    private void HandleJoin(ushort talker, ReadOnlySpan<byte> datagram, long now)
    {
        if (!VoiceWire.TryReadJoin(datagram, out _, out bool receive, out ReadOnlySpan<byte> muted))
        {
            Stats.Malformed++;
            return;
        }

        Peer peer = PeerOf(talker);
        peer.JoinedAt = now;
        peer.Receive = receive;
        peer.MutedCount = muted.Length / 2;
        for (int i = 0; i < peer.MutedCount; i++)
        {
            peer.Muted[i] = VoiceWire.MutedAt(muted, i);
        }

        Stats.Joins++;
    }

    private void HandleUp(ushort talker, ReadOnlySpan<byte> datagram, long now)
    {
        if (!VoiceWire.TryReadAudio(datagram, out VoiceHeader header, out ReadOnlySpan<byte> opus))
        {
            Stats.Malformed++;
            return;
        }

        Peer from = PeerOf(talker);
        Stats.UpPackets++;
        Stats.UpBytes += datagram.Length;
        from.BytesUp += datagram.Length;
        if (!TakeCredit(from, now))
        {
            Stats.RateLimited++;
            return;
        }

        int speakerIndex = Roster.IndexOf(talker);
        if (speakerIndex < 0)
        {
            Stats.UnknownSender++;
            return;
        }

        var down = new VoiceHeader(EVoicePacketKind.Down, header.Channel, header.Flags, header.Sequence, talker);
        int length = VoiceWire.WriteAudio(_down, down, opus);
        ReadOnlySpan<byte> copy = _down.AsSpan(0, length);
        ref VoiceMember speaker = ref Roster[speakerIndex];
        long expiry = VoiceK.JoinExpiryMs * 1000L;
        bool routed = false;
        for (int i = 0; i < Roster.Count; i++)
        {
            ref VoiceMember listener = ref Roster[i];
            if (listener.Talker == talker || !_peers.TryGetValue(listener.Talker, out Peer? to) || !to.Receive ||
                now - to.JoinedAt > expiry || to.HasMuted(talker) ||
                !VoiceRouting.CanHear(Roster, in listener, in speaker, header.Channel))
            {
                continue;
            }

            if (Proximity is { } proximity && !proximity.CanHear(listener.Talker, talker))
            {
                Stats.OutOfRange++;
                continue;
            }

            routed = true;
            if (!TakeSlot(to, talker, now))
            {
                Stats.TalkerCapReached++;
                continue;
            }

            if (Send(in listener, copy))
            {
                Stats.DownPackets++;
                Stats.DownBytes += length;
                to.BytesDown += length;
            }
            else
            {
                Stats.SendFailures++;
            }
        }

        if (!routed)
        {
            Stats.Unrouted++;
        }
    }

    private bool Send(in VoiceMember listener, ReadOnlySpan<byte> datagram)
    {
        foreach (VoiceLocalLink link in _links)
        {
            if (link.Talker == listener.Talker)
            {
                return link.SendDown(datagram);
            }
        }

        return _transport is not null && listener.Endpoint.IsValid && _transport.SendVoice(listener.Endpoint, datagram);
    }

    private static bool TakeCredit(Peer peer, long now)
    {
        if (peer.CreditTime != long.MinValue)
        {
            long elapsed = Math.Max(0, now - peer.CreditTime);
            peer.Credit = Math.Min(BurstCredit, peer.Credit + elapsed * VoiceK.MaxPacketsPerSecond);
        }

        peer.CreditTime = now;
        if (peer.Credit < CreditPerPacket)
        {
            return false;
        }

        peer.Credit -= CreditPerPacket;
        return true;
    }

    private static bool TakeSlot(Peer listener, ushort talker, long now)
    {
        long release = VoiceK.TalkerSlotReleaseMs * 1000L;
        int free = -1;
        for (int i = 0; i < listener.SlotTalker.Length; i++)
        {
            if (listener.SlotTalker[i] == talker)
            {
                listener.SlotTime[i] = now;
                return true;
            }

            if (free < 0 && (listener.SlotTalker[i] == 0 || now - listener.SlotTime[i] > release))
            {
                free = i;
            }
        }

        if (free < 0)
        {
            return false;
        }

        listener.SlotTalker[free] = talker;
        listener.SlotTime[free] = now;
        return true;
    }
}
