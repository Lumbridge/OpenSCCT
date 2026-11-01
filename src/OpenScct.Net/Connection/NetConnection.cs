using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// One connection between a client and a server (UE2 UNetConnection): packets with sequence numbers and
/// piggybacked acks, loss detection, round trip estimates, and the channels whose bunches the packets carry.
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>Only newer packets advance the snapshot timeline and receive packet acknowledgements. Reliable
/// bunches and explicitly reorder-safe extensions from the preceding 32 packets may still fill sequence gaps;
/// other stale unreliable data is discarded. Partial deliveries are not acked, preserving property resends.</item>
/// <item>A packet with no ack after the ack timeout (at least 1 s, more on slow links) is also treated as lost.</item>
/// <item>A lost packet makes its reliable bunches go out again in the next packet and tells each actor channel,
/// which marks the unreliable properties it carried as dirty.</item>
/// <item>Bunches are written into the packet being built; a bunch that does not fit flushes it first.</item>
/// <item>Bandwidth: sending adds bytes to the send budget, time pays them off (spec/native/net-driver.md rules 2
/// and 3). In the game build the budget belongs to the driver: on a server every client connection shares one
/// budget and one adaptive rate (<see cref="NetDriver.NetSpeed"/>), so they all become not ready at the same
/// moment. Connections over loopback (a listen host's local players) are not counted.</item>
/// <item>Once per statistics period (1 s) the connection averages the acknowledgement lag and the loss, and the
/// driver steps the adaptive net speed (the SCCT CONF* law, rule 8).</item>
/// </list>
/// </summary>
public sealed partial class NetConnection : INetObjectMap
{
    private const int SentWindow = 1024;
    private const int SentMask = SentWindow - 1;

    /// <summary>IPv4 and UDP header bytes, counted against the net speed like UE2 does.</summary>
    public const int PacketOverheadBytes = 28;

    /// <summary>
    /// Largest packet the wire format allows. Bunch lengths are written against this fixed limit, so two sides
    /// with different packet sizes still agree on the format.
    /// </summary>
    public const int MaxPacketBytesLimit = 2048;

    private const uint BunchLengthMax = MaxPacketBytesLimit * 8;

    private struct SentPacket
    {
        public long SendTime;
    }

    private long _periodStart;
    private long _lagSumMicros;
    private int _lagCount;
    private long _periodInPackets;
    private long _periodInLost;
    private long _periodOutPackets;
    private long _periodOutLost;
    private long _lastTickTime;

    private readonly NetDriver _driver;
    private readonly IDemoSink? _recorder;
    private readonly FBitWriter _body;
    private readonly FBitWriter _packet;
    private readonly FBitReader _reader = new();
    private readonly FInBunch _inBunch = new();
    private readonly FInBunch _queuedInBunch = new();
    private readonly FOutBunch _outBunch;
    private readonly SentPacket[] _sent = new SentPacket[SentWindow];
    private readonly List<ReliableBunch> _reliableOut = new(K.RELIABLE_BUFFER);
    private readonly Stack<ReliableBunch> _reliablePool = new(K.RELIABLE_BUFFER);
    private readonly Stack<QueuedBunch> _queuedPool = new(K.RELIABLE_BUFFER);
    private ReliableBunch[] _ackedScratch = new ReliableBunch[K.RELIABLE_BUFFER];
    private readonly int _maxPacketBits;
    private readonly int _bodyMaxBits;
    private readonly int _maxPayloadBits;

    private ushort _outSeq;
    private ushort _oldestInFlight;
    private int _inFlight;
    private bool _resendPending;
    private bool _flushComplete;
    private long _lastSendTime;

    private uint _salt;
    private bool _saltKnown;
    private bool _hasReceived;
    private ushort _inSeq;
    private uint _inAckBits;
    private ushort _inSendTime;
    private long _inReceiveTime;
    private bool _ackPending;
    private bool _hasAckProcessed;
    private ushort _lastAckProcessed;
    private int _nextChannelSearch = 1;
    private uint _packetTick;

    internal readonly Channel?[] Channels = new Channel?[K.MAX_CHANNELS];
    internal readonly int[] OutReliable = new int[K.MAX_CHANNELS];
    internal readonly int[] InReliable = new int[K.MAX_CHANNELS];
    internal readonly List<ActorChannel> ActorChannelList = new();
    internal readonly Dictionary<int, ActorChannel> ActorChannelByActorId = new();

    /// <summary>Actor ids of bNetTemporary actors already sent on this connection (never sent again).</summary>
    internal readonly HashSet<int> SentTemporaries = new();

    internal NetConnection(NetDriver driver, EndpointId remoteEndpoint, int connectionId, long now, IDemoSink? recorder = null)
    {
        _driver = driver;
        _recorder = recorder;
        RemoteEndpoint = remoteEndpoint;
        ConnectionId = connectionId;
        ConnectTime = now;
        LastReceiveTime = now;
        _lastSendTime = now;
        State = EConnectionState.USOCK_Pending;
        CurrentNetSpeed = driver.Settings.DefaultNetSpeed;
        SendRate = driver.Settings.MaxClientRate;
        IsLocal = recorder is not null || driver.Transport.IsLocalEndpoint(remoteEndpoint);
        _periodStart = now;
        _lastTickTime = now;

        _maxPacketBits = driver.MaxPacketBytes * 8;
        _bodyMaxBits = _maxPacketBits - PacketHeader.MaxBits - 1;
        _maxPayloadBits = _bodyMaxBits - MaxBunchHeaderBits;
        _body = new FBitWriter(_bodyMaxBits);
        _packet = new FBitWriter(_maxPacketBits);
        _outBunch = new FOutBunch(_maxPayloadBits);
        // Keep one channel window available on both sides before simulation starts. A later reorder/loss peak
        // must not allocate a few additional packet records merely because warm-up happened on a quieter link.
        int payloadBytes = (_maxPayloadBits + 7) >> 3;
        for (int i = 0; i < K.RELIABLE_BUFFER; i++)
        {
            _reliablePool.Push(new ReliableBunch(payloadBytes));
            _queuedPool.Push(new QueuedBunch(payloadBytes));
        }
    }

    public NetDriver Driver => _driver;

    public EndpointId RemoteEndpoint { get; }

    /// <summary>Unique within the driver, in creation order.</summary>
    public int ConnectionId { get; }

    public EConnectionState State { get; internal set; }

    /// <summary>
    /// The connection's salt, stamped on every packet both ways (docs/NETCODE-M0.md "Packets"): the connecting side
    /// picks it at random, the other side takes it from the first packet. A packet with another salt is dropped, so a
    /// datagram spoofed from this endpoint, or a late one of an earlier connection on the same socket, cannot disturb
    /// this one. 0 until known.
    /// </summary>
    public uint Salt => _salt;

    /// <summary>Sets the salt before anything is sent (a client connecting out).</summary>
    internal void SetSalt(uint salt)
    {
        _salt = salt;
        _saltKnown = true;
    }

    /// <summary>Marks a pending connection open once the handshake succeeded (the host decides when).</summary>
    public void SetOpen()
    {
        if (State == EConnectionState.USOCK_Pending)
        {
            State = EConnectionState.USOCK_Open;
        }
    }

    /// <summary>
    /// The actor this connection plays through (UE2 UNetConnection::Actor, normally the PlayerController).
    /// Actors owned by it, directly or through their Owner chain, replicate with bNetOwner set.
    /// </summary>
    public Actor? Actor { get; set; }

    /// <summary>True once the client has joined: the server replicates actors to it.</summary>
    public bool bJoined { get; set; }

    /// <summary>Free slot for the host (player record, session).</summary>
    public object? UserData { get; set; }

    public NetStats Stats { get; } = new();

    /// <summary>
    /// Bytes per second (UE2 CurrentNetSpeed). On a client it is the send rate, steered by the client CONF* row; on
    /// a server it is the speed the client asked for, capped at MaxClientRate, which in the game build only feeds
    /// the correction rate limit of the move protocol (spec/native/net-driver.md rule 10).
    /// </summary>
    public int CurrentNetSpeed { get; set; }

    /// <summary>True for a connection over loopback (or a demo recorder): it does not count against the send budget.</summary>
    public bool IsLocal { get; }

    /// <summary>
    /// True for a demo recorder (<see cref="NetDriver.OpenRecorderConnection"/>): its packets go to a demo file instead
    /// of the transport, each counts as acked once written (so nothing is resent), and it never times out.
    /// </summary>
    public bool IsRecorder => _recorder is not null;

    /// <summary>
    /// On a server with <see cref="SendBudgetMode.PerClientAdaptive"/>: this client's own adaptive send rate in bytes
    /// per second, steered by the CONF* law from this connection's lag, up to MaxClientRate. Unused otherwise.
    /// </summary>
    public int SendRate { get; internal set; }

    /// <summary>
    /// Bytes sent and not yet paid off, for <see cref="SendBudgetMode.PerConnection"/> and, on a server,
    /// <see cref="SendBudgetMode.PerClientAdaptive"/>.
    /// </summary>
    internal long QueuedBytes;

    /// <summary>
    /// True while there is send budget left, counting the packet being built (UE2 IsNetReady: the queued bytes
    /// plus the send buffer must not be above zero).
    /// </summary>
    [UcNative("UNetConnection::IsNetReady", Editor = 0x11144570)]
    public bool IsNetReady => IsLocal || _driver.BudgetReady(this, (_body.NumBits + 7) >> 3);

    /// <summary>Mean acknowledgement lag of the last statistics period, in seconds.</summary>
    public float AvgLag { get; private set; }

    /// <summary>Percent of incoming packets lost in the last statistics period.</summary>
    public float InLoss { get; private set; }

    /// <summary>Percent of outgoing packets lost in the last statistics period.</summary>
    public float OutLoss { get; private set; }

    /// <summary>Statistics periods in a row with more than 20 % loss either way (kept, no reader found).</summary>
    public int LossyPeriods { get; private set; }

    /// <summary>
    /// Server: driver time of this connection's last replication pass (spec/native/net-replication.md rule 8). It
    /// starts at 0, so the first pass after a connection opens has a long dt and every actor passes the frequency gate.
    /// </summary>
    public long LastServerTickTime { get; internal set; }

    public long ConnectTime { get; }

    public long LastReceiveTime { get; private set; }

    /// <summary>
    /// An absolute driver-clock deadline before which receive silence alone cannot close this connection.
    /// The host sets it once for an accepted scene load and clears it when the player reports ready.
    /// Zero keeps the normal idle timeout; this does not bypass protocol failures or explicit closes.
    /// </summary>
    public long ReceiveTimeoutNotBeforeMicros { get; set; }

    /// <summary>Simulation tick stamped on the newest packet received.</summary>
    public uint RemoteTick { get; private set; }

    public bool HasRemoteTick => _hasReceived;

    public string? CloseReason { get; private set; }

    /// <summary>The control channel (index 0), once opened.</summary>
    public ControlChannel? ControlChannel => Channels[0] as ControlChannel;

    /// <summary>Open actor channels in the order they were opened.</summary>
    public IReadOnlyList<ActorChannel> ActorChannels => ActorChannelList;

    /// <summary>Largest bunch payload in bits.</summary>
    public int MaxPayloadBits => _maxPayloadBits;

    /// <summary>Sent packets not yet acknowledged or given up.</summary>
    public int PacketsInFlight => _inFlight;

    /// <summary>Reliable bunches not yet acknowledged, over every channel.</summary>
    public int ReliableBunchesInFlight => _reliableOut.Count;

    /// <summary>Wrapped difference of two packet sequence numbers.</summary>
    public static int SeqDiff(ushort a, ushort b) => (short)(ushort)(a - b);

    /// <summary>Wrapped difference of two reliable bunch sequence numbers.</summary>
    public static int ReliableSeqDiff(int a, int b)
    {
        int d = (a - b) & (K.MAX_CHSEQUENCE - 1);
        return d >= K.MAX_CHSEQUENCE / 2 ? d - K.MAX_CHSEQUENCE : d;
    }

    private static int MaxBunchHeaderBits =>
        1 + 2 + 1 + FBitWriter.CeilLogTwo(K.MAX_CHANNELS) + FBitWriter.CeilLogTwo(K.MAX_CHSEQUENCE) +
        FBitWriter.CeilLogTwo((uint)EChannelType.CHTYPE_MAX) + FBitWriter.CeilLogTwo(BunchLengthMax);

    /// <summary>
    /// True when a datagram from an unknown endpoint can start a connection: a valid header that acks nothing
    /// and a first bunch that opens the control channel. Anything else (stray, late or duplicated packets of a
    /// closed connection, garbage) does not create a connection.
    /// </summary>
    internal static bool IsConnectionStart(byte[] data, int length, FBitReader reader)
    {
        int bits = FBitReader.FindTerminatedBitLength(data.AsSpan(0, length));
        if (bits < PacketHeader.MinBits)
        {
            return false;
        }

        reader.SetData(data, 0, bits);
        if (!PacketHeader.Read(reader, out PacketHeader header) || header.HasAck)
        {
            return false;
        }

        bool control = reader.ReadBit();
        bool bOpen = control && reader.ReadBit();
        _ = control && reader.ReadBit();
        bool reliable = reader.ReadBit();
        uint chIndex = reader.ReadInt(K.MAX_CHANNELS);
        if (!bOpen || !reliable || chIndex != 0)
        {
            return false;
        }

        _ = reader.ReadInt(K.MAX_CHSEQUENCE);
        var chType = (EChannelType)reader.ReadInt((uint)EChannelType.CHTYPE_MAX);
        return !reader.IsError && chType == EChannelType.CHTYPE_Control;
    }

    // ---- channels ------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a channel. A negative index picks the next free index after the last one handed out (round robin
    /// above the control channel), so an index that just closed is not reused at once.
    /// </summary>
    public Channel CreateChannel(EChannelType type, bool openedLocally, int chIndex = -1)
    {
        if (chIndex < 0)
        {
            chIndex = FindFreeChannelIndex();
            if (chIndex < 0)
            {
                throw new InvalidOperationException("No free channel index.");
            }
        }

        if (Channels[chIndex] is not null)
        {
            throw new InvalidOperationException($"Channel {chIndex} is in use.");
        }

        Channel channel = type switch
        {
            EChannelType.CHTYPE_Control => new ControlChannel(),
            EChannelType.CHTYPE_Actor => new ActorChannel(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported channel type."),
        };

        channel.Init(this, chIndex, type, openedLocally);
        Channels[chIndex] = channel;
        if (channel is ActorChannel actorChannel)
        {
            ActorChannelList.Add(actorChannel);
        }

        return channel;
    }

    private int FindFreeChannelIndex()
    {
        for (int n = 0; n < K.MAX_CHANNELS - 1; n++)
        {
            int index = 1 + ((_nextChannelSearch - 1 + n) % (K.MAX_CHANNELS - 1));
            if (Channels[index] is null)
            {
                _nextChannelSearch = index + 1 >= K.MAX_CHANNELS ? 1 : index + 1;
                return index;
            }
        }

        return -1;
    }

    /// <summary>Frees a channel index (after its close was acknowledged, or the other side closed it).</summary>
    internal void ReleaseChannel(Channel channel)
    {
        if (!ReferenceEquals(Channels[channel.ChIndex], channel))
        {
            return;
        }

        Channels[channel.ChIndex] = null;
        channel.ReleaseQueued();
        if (channel is ActorChannel actorChannel)
        {
            ActorChannelList.Remove(actorChannel);
        }

        channel.CleanUp();
    }

    internal void ChannelClosedByRemote(Channel channel)
    {
        if (channel.ChType == EChannelType.CHTYPE_Control)
        {
            CloseInternal("Closed by the other side", notifyRemote: false);
            return;
        }

        ReleaseChannel(channel);
    }

    // ---- receiving -----------------------------------------------------------------------------------------

    internal void ReceivedRawPacket(byte[] data, int length, long now)
    {
        if (State == EConnectionState.USOCK_Closed)
        {
            return;
        }

        Stats.InBytes += length;
        Stats.InPackets++;
        int bits = FBitReader.FindTerminatedBitLength(data.AsSpan(0, length));
        if (bits < PacketHeader.MinBits)
        {
            Stats.InMalformed++;
            return;
        }

        _reader.SetData(data, 0, bits);
        if (!PacketHeader.Read(_reader, out PacketHeader header))
        {
            Stats.InMalformed++;
            return;
        }

        if (!_saltKnown)
        {
            SetSalt(header.Salt);
        }
        else if (header.Salt != _salt)
        {
            Stats.InWrongSalt++;
            return;
        }

        if (_hasReceived)
        {
            int delta = SeqDiff(header.Seq, _inSeq);
            if (delta <= 0)
            {
                Stats.InPacketsOld++;
                // Jitter can put a unique reliable control behind a newer movement packet. Dropping it
                // wholesale stalls every movement command fenced behind that control until retransmission.
                // Channels already order/deduplicate reliable sequences. Do not ack this partial delivery:
                // the sender must still resend the unreliable properties that we deliberately skip.
                if (delta < 0 && delta >= -32)
                {
                    ReadPacketBunches(data, header.Tick, reordered: true);
                }
                return;
            }

            if (delta > 1)
            {
                Stats.InPacketsLost += delta - 1;
                _periodInLost += delta - 1;
            }

            uint ackBits = delta < 32 ? _inAckBits << delta : 0u;
            if (delta <= 32)
            {
                ackBits |= 1u << (delta - 1);
            }

            _inAckBits = ackBits;
        }
        else
        {
            _hasReceived = true;
            _inAckBits = 0;
        }

        _periodInPackets++;
        _inSeq = header.Seq;
        _inSendTime = header.SendTime;
        _inReceiveTime = now;
        _ackPending = true;
        LastReceiveTime = now;
        RemoteTick = header.Tick;
        _driver.OnPacketReceived(this, header.Tick);

        if (header.HasAck)
        {
            ReceivedAcks(in header, now);
        }

        ReadPacketBunches(data, header.Tick, reordered: false);

        if (header.bFlushComplete && State != EConnectionState.USOCK_Closed)
        {
            _driver.OnFlushComplete(this, header.Tick);
        }
    }

    private void ReadPacketBunches(byte[] data, uint tick, bool reordered)
    {
        _packetTick = tick;
        while (!_reader.AtEnd && State != EConnectionState.USOCK_Closed)
        {
            if (!ReadBunch(data, reordered))
            {
                Stats.InMalformed++;
                Close("Malformed bunch");
                return;
            }
        }

    }

    private bool ReadBunch(byte[] data, bool reordered)
    {
        FBitReader r = _reader;
        bool control = r.ReadBit();
        bool bOpen = control && r.ReadBit();
        bool bClose = control && r.ReadBit();
        bool reliable = r.ReadBit();
        int chIndex = (int)r.ReadInt(K.MAX_CHANNELS);
        int chSequence = reliable ? (int)r.ReadInt(K.MAX_CHSEQUENCE) : 0;
        var chType = reliable || bOpen ? (EChannelType)r.ReadInt((uint)EChannelType.CHTYPE_MAX) : EChannelType.CHTYPE_None;
        int payloadBits = (int)r.ReadInt(BunchLengthMax);
        if (r.IsError || payloadBits > r.BitsLeft)
        {
            return false;
        }

        int payloadStart = r.AbsolutePosition;
        r.Skip(payloadBits);
        Stats.InBunches++;
        Channel? channel = Channels[chIndex];
        if (reordered && !reliable && (bOpen || bClose || channel is not ActorChannel)) return true;
        if (channel is null)
        {
            // Unreliable data for a channel that is not open is ignored, like UE2, except an unreliable opening (a
            // bNetTemporary actor, opened and closed in one bunch).
            if (!reliable && !bOpen)
            {
                return true;
            }

            // A stale copy of a bunch already handled must not reopen a channel that has since closed.
            if (reliable && ReliableSeqDiff(chSequence, InReliable[chIndex]) <= 0)
            {
                return true;
            }

            if (!_driver.AcceptChannel(this, chType, chIndex))
            {
                return false;
            }

            // The first reliable bunch to arrive opens the channel, even if the open bunch itself is still on
            // its way; bunches wait in order inside the channel.
            channel = CreateChannel(chType, openedLocally: false, chIndex);
        }

        FInBunch bunch = _inBunch;
        bunch.Connection = this;
        bunch.PacketTick = _packetTick;
        bunch.ChIndex = chIndex;
        bunch.ChType = chType == EChannelType.CHTYPE_None ? channel.ChType : chType;
        bunch.ChSequence = chSequence;
        bunch.bOpen = bOpen;
        bunch.bClose = bClose;
        bunch.bReliable = reliable;
        bunch.Reader.SetData(data, payloadStart, payloadBits);
        if (reordered && !reliable)
            ((ActorChannel)channel).ReceivedReorderedFunctions(bunch);
        else
            channel.ReceivedRawBunch(bunch);
        return true;
    }

    internal QueuedBunch RentQueued(FInBunch bunch)
    {
        QueuedBunch queued = _queuedPool.Count > 0 ? _queuedPool.Pop() : new QueuedBunch((_maxPayloadBits + 7) >> 3);
        int bits = bunch.Reader.BitsLeft;
        bunch.Reader.ReadBitsInto(queued.Data, bits);
        queued.Bits = bits;
        queued.ChSequence = bunch.ChSequence;
        queued.ChType = bunch.ChType;
        queued.bOpen = bunch.bOpen;
        queued.bClose = bunch.bClose;
        queued.PacketTick = bunch.PacketTick;
        queued.Next = null;
        return queued;
    }

    /// <summary>A queued bunch ready to process. Uses its own bunch object, stamped with its own packet's tick.</summary>
    internal FInBunch PrepareQueuedBunch(QueuedBunch queued, int chIndex)
    {
        FInBunch bunch = _queuedInBunch;
        bunch.Connection = this;
        bunch.ChIndex = chIndex;
        bunch.ChType = queued.ChType;
        bunch.ChSequence = queued.ChSequence;
        bunch.bOpen = queued.bOpen;
        bunch.bClose = queued.bClose;
        bunch.bReliable = true;
        bunch.PacketTick = queued.PacketTick;
        bunch.Reader.SetData(queued.Data, 0, queued.Bits);
        return bunch;
    }

    internal void ReturnQueued(QueuedBunch queued)
    {
        queued.Next = null;
        _queuedPool.Push(queued);
    }

    private void ReceivedAcks(in PacketHeader header, long now)
    {
        if (!_hasAckProcessed || SeqDiff(header.Ack, _lastAckProcessed) > 0)
        {
            _hasAckProcessed = true;
            _lastAckProcessed = header.Ack;
            if (header.EchoDelay != ushort.MaxValue)
            {
                int rttUnits = (ushort)(TimeUnits(now) - header.EchoTime - header.EchoDelay);
                if (rttUnits < 50_000)
                {
                    Stats.AddRttSample(rttUnits * 100L);
                }
            }
        }

        while (_inFlight > 0)
        {
            ushort seq = _oldestInFlight;
            int d = SeqDiff(header.Ack, seq);
            if (d < 0)
            {
                break;
            }

            bool acked = d == 0 || (d <= 32 && (header.AckBits & (1u << (d - 1))) != 0);
            _oldestInFlight++;
            _inFlight--;
            if (acked)
            {
                // Lag from acknowledgements: now - send time - half a frame (spec/native/net-driver.md rule 7).
                long lag = now - _sent[seq & SentMask].SendTime - _driver.FrameMicros / 2;
                _lagSumMicros += Math.Max(0, lag);
                _lagCount++;
                ReceivedAck(seq);
            }
            else
            {
                ReceivedNak(seq);
            }
        }
    }

    private void ReceivedAck(ushort packetSeq)
    {
        bool any = false;
        for (int i = 0; i < _reliableOut.Count; i++)
        {
            ReliableBunch rec = _reliableOut[i];
            if (!rec.Acked && rec.PacketSeq == packetSeq)
            {
                rec.Acked = true;
                any = true;
            }
        }

        if (!any)
        {
            return;
        }

        // Compact first, then tell the channels: a close ack frees the channel, which may touch the list.
        int write = 0;
        int ackedCount = 0;
        for (int read = 0; read < _reliableOut.Count; read++)
        {
            ReliableBunch rec = _reliableOut[read];
            if (rec.Acked)
            {
                _ackedScratch[ackedCount++] = rec;
            }
            else
            {
                _reliableOut[write++] = rec;
            }
        }

        _reliableOut.RemoveRange(write, _reliableOut.Count - write);
        for (int i = 0; i < ackedCount; i++)
        {
            ReliableBunch rec = _ackedScratch[i];
            _ackedScratch[i] = null!;
            rec.Channel.ReceivedReliableAck(rec.bOpen, rec.bClose);
            rec.Channel = null!;
            _reliablePool.Push(rec);
        }
    }

    private void ReceivedNak(ushort packetSeq)
    {
        Stats.OutPacketsLost++;
        _periodOutLost++;
        for (int i = 0; i < _reliableOut.Count; i++)
        {
            ReliableBunch rec = _reliableOut[i];
            if (!rec.Acked && !rec.NeedsResend && rec.PacketSeq == packetSeq)
            {
                rec.NeedsResend = true;
                _resendPending = true;
            }
        }

        for (int i = 0; i < ActorChannelList.Count; i++)
        {
            ActorChannelList[i].ReceivedNak(packetSeq);
        }
    }

    // ---- sending -------------------------------------------------------------------------------------------

    /// <summary>Hands out the connection's bunch builder, emptied, for <paramref name="channel"/>.</summary>
    public FOutBunch BeginBunch(Channel channel, bool reliable)
    {
        _outBunch.Reset(channel, reliable);
        return _outBunch;
    }

    /// <summary>
    /// Queues a bunch in the packet being built and returns that packet's sequence number, or -1 when nothing
    /// was sent (connection closed, bunch overflowed, reliable buffer full). Reliable bunches are kept until
    /// acknowledged and resent when their packet is lost.
    /// </summary>
    public int SendBunch(FOutBunch bunch)
    {
        ArgumentNullException.ThrowIfNull(bunch);
        Channel channel = bunch.Channel;
        if (State == EConnectionState.USOCK_Closed)
        {
            return -1;
        }

        if (bunch.Writer.IsError)
        {
            Close("Bunch overflow");
            return -1;
        }

        if (!bunch.bReliable)
        {
            return WriteBunch(bunch.bOpen, bunch.bClose, false, channel.ChIndex, 0, channel.ChType,
                bunch.Writer.Buffer, bunch.Writer.NumBits);
        }

        if (channel.NumOutRec >= K.RELIABLE_BUFFER)
        {
            Close("Outgoing reliable buffer overflow");
            return -1;
        }

        int chSequence = (OutReliable[channel.ChIndex] + 1) & (K.MAX_CHSEQUENCE - 1);
        OutReliable[channel.ChIndex] = chSequence;

        ReliableBunch rec = _reliablePool.Count > 0 ? _reliablePool.Pop() : new ReliableBunch((_maxPayloadBits + 7) >> 3);
        int bytes = bunch.Writer.NumBytes;
        bunch.Writer.Buffer.AsSpan(0, bytes).CopyTo(rec.Data);
        rec.Bits = bunch.Writer.NumBits;
        rec.Channel = channel;
        rec.ChSequence = chSequence;
        rec.bOpen = bunch.bOpen;
        rec.bClose = bunch.bClose;
        rec.Acked = false;
        rec.NeedsResend = false;

        int packetSeq = WriteBunch(rec.bOpen, rec.bClose, true, channel.ChIndex, chSequence, channel.ChType, rec.Data, rec.Bits);
        rec.PacketSeq = (ushort)packetSeq;
        _reliableOut.Add(rec);
        if (_reliableOut.Count > _ackedScratch.Length)
        {
            Array.Resize(ref _ackedScratch, _reliableOut.Count * 2);
        }

        channel.NumOutRec++;
        return packetSeq;
    }

    private int WriteBunch(bool bOpen, bool bClose, bool reliable, int chIndex, int chSequence, EChannelType chType,
        byte[] payload, int payloadBits)
    {
        bool control = bOpen || bClose;
        int headerBits = 2 + (control ? 2 : 0) +
            FBitWriter.CountIntBits((uint)chIndex, K.MAX_CHANNELS) +
            (reliable ? FBitWriter.CeilLogTwo(K.MAX_CHSEQUENCE) : 0) +
            (reliable || bOpen ? FBitWriter.CountIntBits((uint)chType, (uint)EChannelType.CHTYPE_MAX) : 0) +
            FBitWriter.CountIntBits((uint)payloadBits, BunchLengthMax);
        if (headerBits + payloadBits > _bodyMaxBits)
        {
            throw new InvalidOperationException("Bunch is larger than a packet.");
        }

        if (_body.NumBits + headerBits + payloadBits > _bodyMaxBits)
        {
            FlushNet(_driver.Time, force: true);
        }

        FBitWriter w = _body;
        w.WriteBit(control);
        if (control)
        {
            w.WriteBit(bOpen);
            w.WriteBit(bClose);
        }

        w.WriteBit(reliable);
        w.WriteInt((uint)chIndex, K.MAX_CHANNELS);
        if (reliable)
        {
            w.WriteIntWrapped((uint)chSequence, K.MAX_CHSEQUENCE);
        }

        if (reliable || bOpen)
        {
            w.WriteInt((uint)chType, (uint)EChannelType.CHTYPE_MAX);
        }

        w.WriteInt((uint)payloadBits, BunchLengthMax);
        w.AppendBits(payload, payloadBits);
        Stats.OutBunches++;
        if (Channels[chIndex] is { } channel) channel.PayloadBytesSent += (payloadBits + 7) >> 3;
        return _outSeq;
    }

    /// <summary>Marks the packet being built as the last one of a complete replication pass.</summary>
    internal void MarkFlushComplete() => _flushComplete = true;

    /// <summary>True when the packet being built has bunches.</summary>
    public bool HasPendingData => _body.NumBits > 0;

    /// <summary>
    /// Sends the packet being built. Without <paramref name="force"/> nothing goes out unless there are
    /// bunches, an ack is owed, a flush-complete mark is set or the keep-alive time has passed.
    /// </summary>
    public void FlushNet(long now, bool force = false)
    {
        if (State == EConnectionState.USOCK_Closed)
        {
            return;
        }

        if (!force && _body.NumBits == 0 && !_ackPending && (!_flushComplete || _driver.Settings.PawnProxies == PawnProxyMode.Snapshots) &&
            now - _lastSendTime < _driver.KeepAliveMicros)
        {
            return;
        }

        if (_inFlight >= SentWindow)
        {
            ushort seq = _oldestInFlight;
            _oldestInFlight++;
            _inFlight--;
            ReceivedNak(seq);
        }

        var header = new PacketHeader
        {
            Salt = _salt,
            Seq = _outSeq,
            HasAck = _hasReceived,
            Ack = _inSeq,
            AckBits = _inAckBits,
            EchoTime = _inSendTime,
            EchoDelay = HoldUnits(now - _inReceiveTime),
            Tick = _driver.CurrentTick,
            SendTime = TimeUnits(now),
            bFlushComplete = _flushComplete,
        };

        _packet.Reset();
        header.Write(_packet);
        _packet.AppendBits(_body);
        _packet.WriteBit(true);
        int bytes = _packet.NumBytes;
        if (_recorder is not null)
        {
            _recorder.Write(header.Tick, _packet.Data);
        }
        else
        {
            _driver.Transport.Send(RemoteEndpoint, _packet.Data);
        }

        _sent[_outSeq & SentMask].SendTime = now;
        if (_inFlight == 0)
        {
            _oldestInFlight = _outSeq;
        }

        _inFlight++;
        _outSeq++;
        _body.Reset();
        _ackPending = false;
        _flushComplete = false;
        _lastSendTime = now;
        Stats.OutPackets++;
        Stats.OutBytes += bytes;
        _periodOutPackets++;
        if (_recorder is not null)
        {
            // A demo file loses nothing: the packet is acked as soon as it is written.
            ushort seq = _oldestInFlight;
            _oldestInFlight++;
            _inFlight--;
            ReceivedAck(seq);
        }

        if (!IsLocal)
        {
            _driver.OnBytesSent(this, bytes + PacketOverheadBytes);
        }
    }

    private static ushort TimeUnits(long micros) => (ushort)(micros / 100);

    private static ushort HoldUnits(long micros)
    {
        long units = micros / 100;
        return units >= ushort.MaxValue ? ushort.MaxValue : (ushort)units;
    }

    /// <summary>
    /// Housekeeping before sending: timeouts, packets whose ack is overdue, resends of lost reliable bunches,
    /// and paying off queued bits.
    /// </summary>
    internal void Tick(long now)
    {
        if (State == EConnectionState.USOCK_Closed)
        {
            return;
        }

        // A hitch does not cause a timeout (spec/native/net-driver.md rule 5.3).
        if (now - _lastTickTime > K.HitchGuardMicros)
        {
            LastReceiveTime = now;
        }

        _lastTickTime = now;
        if (_recorder is not null)
        {
            LastReceiveTime = now;
        }

        if (now - _periodStart > K.StatisticsPeriodMicros)
        {
            EndStatisticsPeriod(now);
        }

        long timeout = State == EConnectionState.USOCK_Open ? _driver.ConnectionTimeoutMicros : _driver.InitialConnectTimeoutMicros;
        if (now - LastReceiveTime > timeout && now >= ReceiveTimeoutNotBeforeMicros)
        {
            Close("Connection timed out");
            return;
        }

        long ackTimeout = _driver.AckTimeoutMicros;
        if (Stats.HasRtt && Stats.RttMicros * 3 > ackTimeout)
        {
            ackTimeout = Stats.RttMicros * 3;
        }

        while (_inFlight > 0 && now - _sent[_oldestInFlight & SentMask].SendTime > ackTimeout)
        {
            ushort seq = _oldestInFlight;
            _oldestInFlight++;
            _inFlight--;
            ReceivedNak(seq);
        }

        if (_resendPending)
        {
            _resendPending = false;
            for (int i = 0; i < _reliableOut.Count; i++)
            {
                ReliableBunch rec = _reliableOut[i];
                if (rec.NeedsResend && !rec.Acked)
                {
                    rec.NeedsResend = false;
                    Channel channel = rec.Channel;
                    rec.PacketSeq = (ushort)WriteBunch(rec.bOpen, rec.bClose, true, channel.ChIndex, rec.ChSequence,
                        channel.ChType, rec.Data, rec.Bits);
                    Stats.OutReliableResends++;
                }
            }
        }

    }

    /// <summary>Per-second rates, lag and loss of the period, then the adaptive speed step (rules 5.2 and 8).</summary>
    [UcNative("UNetConnection::Tick", Exe = 0x10a0e570, Editor = 0x11148d40)]
    private void EndStatisticsPeriod(long now)
    {
        if (_lagCount > 0)
        {
            AvgLag = _lagSumMicros / (float)_lagCount / 1_000_000f;
        }

        long inTotal = _periodInPackets + _periodInLost;
        InLoss = inTotal > 0 ? 100f * _periodInLost / inTotal : 0f;
        OutLoss = _periodOutPackets > 0 ? 100f * _periodOutLost / _periodOutPackets : 0f;
        LossyPeriods = InLoss > K.LossyPeriodPercent || OutLoss > K.LossyPeriodPercent ? LossyPeriods + 1 : 0;
        _lagSumMicros = 0;
        _lagCount = 0;
        _periodInPackets = 0;
        _periodInLost = 0;
        _periodOutPackets = 0;
        _periodOutLost = 0;
        _periodStart = now;
        _driver.AdaptNetSpeed(this, now);
    }

    /// <summary>Zeroes the lag and loss accumulators (resetConnectionPing).</summary>
    internal void ResetLagStatistics()
    {
        _lagSumMicros = 0;
        _lagCount = 0;
        AvgLag = 0f;
        InLoss = 0f;
        OutLoss = 0f;
    }

    // ---- closing -------------------------------------------------------------------------------------------

    /// <summary>
    /// Closes the connection: the control channel close goes out at once (UE2 tells the other side by closing
    /// channel 0), every channel is cleaned up and the driver is told.
    /// </summary>
    public void Close(string? reason = null) => CloseInternal(reason, notifyRemote: true);

    private void CloseInternal(string? reason, bool notifyRemote)
    {
        if (State == EConnectionState.USOCK_Closed)
        {
            return;
        }

        CloseReason ??= reason;
        if (notifyRemote && Channels[0] is ControlChannel control && !control.Closing)
        {
            control.Close();
            FlushNet(_driver.Time, force: true);
        }

        State = EConnectionState.USOCK_Closed;
        for (int i = 0; i < Channels.Length; i++)
        {
            Channel? channel = Channels[i];
            if (channel is not null)
            {
                ReleaseChannel(channel);
            }
        }

        _driver.OnConnectionClosed(this);
    }

    public override string ToString() => $"NetConnection#{ConnectionId}({RemoteEndpoint})";
}
