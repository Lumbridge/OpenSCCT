using System;
using OpenScct.Provenance;

namespace OpenScct.Net.Voice;

/// <summary>What <see cref="VoiceJitterBuffer.Next"/> hands the decoder for one 20 ms frame.</summary>
public enum EVoiceFrameKind : byte
{
    /// <summary>Nothing to play: idle, or banking frames before playback starts.</summary>
    None = 0,

    /// <summary>The frame's own packet: decode it.</summary>
    Packet = 1,

    /// <summary>The frame was lost but the next packet is here: decode its in-band FEC.</summary>
    Fec = 2,

    /// <summary>The frame was lost: conceal it.</summary>
    Concealed = 3,
}

/// <summary>Counters of one jitter buffer.</summary>
public sealed class VoiceJitterStats
{
    public long Received { get; internal set; }

    public long Duplicates { get; internal set; }

    /// <summary>Packets that arrived after their frame was played or concealed.</summary>
    public long Late { get; internal set; }

    /// <summary>Packets that arrived after a later one (kept when still in time).</summary>
    public long Reordered { get; internal set; }

    public long Played { get; internal set; }

    public long Fec { get; internal set; }

    public long Concealed { get; internal set; }

    /// <summary>Concealed frames played while nothing was banked; they do not use up the missing frame's slot.</summary>
    public long Stretched { get; internal set; }

    /// <summary>Times playback ran dry mid-stream and went back to banking.</summary>
    public long Underruns { get; internal set; }

    /// <summary>Frames dropped to bring a backlog down to the target depth.</summary>
    public long Discarded { get; internal set; }

    /// <summary>Times the stream jumped too far to follow and started over.</summary>
    public long Resets { get; internal set; }
}

/// <summary>
/// Per-talker jitter buffer in 20 ms frames (FEATURES rl:voice.jitter). Packets are slotted by sequence, so reordering
/// within the window is undone and duplicates are dropped. Playback starts once <see cref="TargetFrames"/> frames are
/// banked (or the talk spurt's last frame is here), then takes one frame per call: the packet, the next packet's FEC when
/// only this one is missing, or concealment. With nothing banked, up to <see cref="MaxConcealFrames"/> frames are
/// concealed while the playout point waits for the missing frame (a late packet still plays), then playback stops and
/// banks again (re-buffer after an underrun). A backlog beyond the target plus
/// <see cref="MaxExtraFrames"/> drops the oldest frames, so delay cannot grow without bound.
/// <para>Allocation-free after construction. The payload a call returns is valid until the next call to this buffer.</para>
/// </summary>
[PortOf("rl:voice.jitter", Spec = "spec/reloaded/FEATURES.md#rlvoicejitter---jitter-buffer")]
public sealed class VoiceJitterBuffer
{
    /// <summary>Frames the window holds (640 ms).</summary>
    public const int Capacity = 32;

    /// <summary>Frames concealed in a row with nothing banked before playback counts as run dry.</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice: 60 ms of concealment bridges a short dropout")]
    public const int MaxConcealFrames = 3;

    /// <summary>Frames banked beyond the target before the oldest are dropped.</summary>
    [Provenance(ProvenanceKind.Estimated, "rewrite choice")]
    public const int MaxExtraFrames = 6;

    private const int Mask = Capacity - 1;
    private const int SlotBytes = VoiceK.MaxOpusPacketBytes;

    private readonly byte[] _data = new byte[Capacity * SlotBytes];
    private readonly int[] _length = new int[Capacity];
    private readonly ushort[] _seq = new ushort[Capacity];
    private readonly bool[] _present = new bool[Capacity];
    private readonly EVoiceFlags[] _flags = new EVoiceFlags[Capacity];
    private readonly EVoiceChannel[] _channel = new EVoiceChannel[Capacity];
    private int _targetFrames;
    private int _count;
    private bool _playing;
    private ushort _next;
    private ushort _highestHeld;
    private ushort _highestSeen;
    private bool _seenAny;
    private ushort _floor;
    private bool _floorValid;
    private int _waitFrames;
    private int _missRun;
    private int _endsHeld;

    public VoiceJitterBuffer(int targetFrames = VoiceK.DefaultJitterFrames) => TargetFrames = targetFrames;

    /// <summary>Frames banked before playback starts, 1 to 12.</summary>
    public int TargetFrames
    {
        get => _targetFrames;
        set => _targetFrames = Math.Clamp(value, VoiceK.MinJitterFrames, VoiceK.MaxJitterFrames);
    }

    public VoiceJitterStats Stats { get; } = new();

    /// <summary>Packets banked.</summary>
    public int Count => _count;

    /// <summary>True while frames are being played out.</summary>
    public bool IsPlaying => _playing;

    /// <summary>The channel of the last frame played.</summary>
    public EVoiceChannel Channel { get; private set; }

    /// <summary>Set when the frame just played closed its talk spurt; cleared by the next call.</summary>
    public bool SpurtEnded { get; private set; }

    /// <summary>Banks one packet. False when it is late, a duplicate or malformed.</summary>
    public bool Push(ushort seq, EVoiceFlags flags, EVoiceChannel channel, ReadOnlySpan<byte> payload)
    {
        if (payload.Length is 0 or > SlotBytes)
        {
            return false;
        }

        Stats.Received++;
        if (_seenAny && VoiceWire.SeqDiff(seq, _highestSeen) < 0)
        {
            Stats.Reordered++;
        }
        else
        {
            _highestSeen = seq;
            _seenAny = true;
        }

        if (_floorValid)
        {
            int behind = VoiceWire.SeqDiff(seq, _floor);
            if (behind <= 0)
            {
                // Within the window behind the playout point: a late packet. Further back, or a new spurt marker, means
                // the talker started over (a restarted client): follow it.
                if (behind > -Capacity && (flags & EVoiceFlags.TalkStart) == 0)
                {
                    Stats.Late++;
                    return false;
                }

                StartOver();
            }
            else if (behind > Capacity)
            {
                if (_playing || _count > 0)
                {
                    StartOver();
                }

                _floorValid = false;
            }
        }

        if (_count == 0)
        {
            if (!_playing)
            {
                _next = seq;
            }

            _highestHeld = seq;
        }
        else
        {
            int fromNext = VoiceWire.SeqDiff(seq, _next);
            if (fromNext >= Capacity || VoiceWire.SeqDiff(_highestHeld, seq) >= Capacity)
            {
                StartOver();
                _next = seq;
                _highestHeld = seq;
            }
            else
            {
                if (fromNext < 0)
                {
                    if (_playing)
                    {
                        Stats.Late++;
                        return false;
                    }

                    _next = seq;
                }

                if (VoiceWire.SeqDiff(seq, _highestHeld) > 0)
                {
                    _highestHeld = seq;
                }
            }
        }

        int slot = seq & Mask;
        if (_present[slot])
        {
            if (_seq[slot] == seq)
            {
                Stats.Duplicates++;
                return false;
            }

            RemoveAt(slot);
            Stats.Discarded++;
        }

        payload.CopyTo(_data.AsSpan(slot * SlotBytes, SlotBytes));
        _length[slot] = payload.Length;
        _seq[slot] = seq;
        _flags[slot] = flags;
        _channel[slot] = channel;
        _present[slot] = true;
        _count++;
        if ((flags & EVoiceFlags.TalkEnd) != 0)
        {
            _endsHeld++;
        }

        return true;
    }

    /// <summary>The next 20 ms frame to decode; <paramref name="payload"/> is the packet for Packet and Fec.</summary>
    public EVoiceFrameKind Next(out ReadOnlySpan<byte> payload)
    {
        payload = default;
        SpurtEnded = false;
        if (!_playing)
        {
            if (_count == 0)
            {
                _waitFrames = 0;
                return EVoiceFrameKind.None;
            }

            // Bank the target, but do not hold a short utterance (its end is here) or a stream whose rest was lost.
            _waitFrames++;
            if (_count < _targetFrames && _endsHeld == 0 && _waitFrames < 2 * _targetFrames)
            {
                return EVoiceFrameKind.None;
            }

            _playing = true;
            _waitFrames = 0;
            _missRun = 0;
        }

        while (_count > _targetFrames + MaxExtraFrames)
        {
            int oldest = _next & Mask;
            if (_present[oldest] && _seq[oldest] == _next)
            {
                RemoveAt(oldest);
            }

            Stats.Discarded++;
            Consume();
        }

        int slot = _next & Mask;
        if (_present[slot] && _seq[slot] == _next)
        {
            payload = _data.AsSpan(slot * SlotBytes, _length[slot]);
            Channel = _channel[slot];
            bool end = (_flags[slot] & EVoiceFlags.TalkEnd) != 0;
            RemoveAt(slot);
            Consume();
            _missRun = 0;
            Stats.Played++;
            if (end)
            {
                _playing = false;
                SpurtEnded = true;
            }

            return EVoiceFrameKind.Packet;
        }

        if (_count == 0)
        {
            // Nothing banked: the frame is late or lost, and nothing later tells which. Conceal without moving the
            // playout point, so the frame still plays if it turns up (the delay grows by a frame; the backlog trim
            // takes it back). After MaxConcealFrames the stream counts as run dry.
            if (++_missRun > MaxConcealFrames)
            {
                _playing = false;
                _missRun = 0;
                Stats.Underruns++;
                return EVoiceFrameKind.None;
            }

            Stats.Concealed++;
            Stats.Stretched++;
            return EVoiceFrameKind.Concealed;
        }

        Consume();
        _missRun = 0;
        int after = _next & Mask;
        if (_present[after] && _seq[after] == _next)
        {
            payload = _data.AsSpan(after * SlotBytes, _length[after]);
            Stats.Fec++;
            return EVoiceFrameKind.Fec;
        }

        Stats.Concealed++;
        return EVoiceFrameKind.Concealed;
    }

    /// <summary>Forgets everything (talker left, settings changed).</summary>
    public void Reset()
    {
        StartOver();
        _floorValid = false;
        _seenAny = false;
        Channel = EVoiceChannel.All;
        SpurtEnded = false;
    }

    private void Consume()
    {
        _floor = _next;
        _floorValid = true;
        _next++;
    }

    private void RemoveAt(int slot)
    {
        if ((_flags[slot] & EVoiceFlags.TalkEnd) != 0)
        {
            _endsHeld--;
        }

        _present[slot] = false;
        _count--;
    }

    private void StartOver()
    {
        if (_count > 0 || _playing)
        {
            Stats.Resets++;
        }

        Array.Clear(_present);
        _count = 0;
        _endsHeld = 0;
        _playing = false;
        _waitFrames = 0;
        _missRun = 0;
        _floorValid = false;
    }
}
