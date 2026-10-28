using System;
using System.Collections.Generic;
using Concentus.Common;
using OpenScct.Net.Voice.Dsp;
using OpenScct.Provenance;

namespace OpenScct.Net.Voice;

/// <summary>A change the HUD shows: a talker started or stopped speaking, changed channel, or was muted.</summary>
/// <param name="Talker">The player id; for <paramref name="IsLocal"/> the local player's (0 until known).</param>
/// <param name="IsLocal">The local player's own microphone.</param>
/// <param name="Speaking">Transmitting (local) or being heard (remote).</param>
/// <param name="Channel">The channel of the transmission.</param>
/// <param name="Muted">The local player muted this talker, or (local) muted his microphone.</param>
public readonly record struct VoiceActivity(ushort Talker, bool IsLocal, bool Speaking, EVoiceChannel Channel, bool Muted);

/// <summary>Counters of one voice client.</summary>
public sealed class VoiceClientStats
{
    public long FramesCaptured { get; internal set; }

    public long FramesSent { get; internal set; }

    public long BytesSent { get; internal set; }

    public long JoinsSent { get; internal set; }

    public long PacketsReceived { get; internal set; }

    public long BytesReceived { get; internal set; }

    /// <summary>Datagrams ignored: malformed, not from the server, or about the local player.</summary>
    public long Ignored { get; internal set; }

    public long SendFailures { get; internal set; }

    /// <summary>Largest Opus packet sent, in bytes.</summary>
    public int LargestPacket { get; internal set; }
}

/// <summary>
/// A player's voice chat: microphone frames in (any rate, resampled to 48 kHz), gain, the transmit gate and keys
/// (<see cref="VoiceGate"/>), Opus encoding and Up packets to the server; Down packets out to one
/// <see cref="VoiceTalker"/> per remote talker, which the audio output reads frame by frame. The client also tells the
/// server it takes part and which talkers it muted (Join, repeated every <see cref="VoiceK.JoinRefreshMs"/>), and raises
/// <see cref="ActivityChanged"/> for the HUD's speaking indicators.
/// <para>Single-threaded: capture, <see cref="Update"/>, the talkers and the transport callbacks all run on the
/// client's thread (the Godot main thread in the game). No allocation per frame after the first frames of a talker.
/// The microphone clean-up (<see cref="VoiceDsp"/>: echo, noise, automatic gain) runs before the gain and the gate, on
/// that thread or, with <see cref="UseVoiceThread"/>, on the voice thread; the cleaned frames then come back to the
/// client's thread in the next <see cref="SubmitCapture"/> or <see cref="Update"/>.</para>
/// </summary>
[PortOf("rl:voice.activation", Spec = "spec/reloaded/FEATURES.md#rlvoiceactivation---push-to-talk-and-voice-activation-gate")]
[PortOf("rl:voice.mix", Spec = "spec/reloaded/FEATURES.md#rlvoicemix---volume-mute-indicators-and-per-player-control")]
public sealed class VoiceClient : IVoiceEndpoint, IDisposable
{
    private const int ResampleChunk = 480;

    private readonly IClock _clock;
    private readonly VoiceEncoder _encoder;
    private readonly VoiceGate _gate = new();
    private readonly float[] _frame = new float[VoiceK.FrameSamples];
    private readonly float[] _cleaned = new float[VoiceK.FrameSamples];
    private readonly short[] _pcm = new short[VoiceK.FrameSamples];
    private readonly byte[] _packet = new byte[VoiceK.MaxOpusPacketBytes];
    private readonly byte[] _datagram = new byte[VoiceWire.MaxDatagramBytes];
    private readonly byte[] _receive = new byte[VoiceWire.MaxDatagramBytes];
    private readonly float[] _resampleIn = new float[ResampleChunk];
    private readonly float[] _resampleOut = new float[ResampleChunk * VoiceK.SampleRate / 8000 + 16];
    private readonly Dictionary<ushort, VoiceTalker> _talkerById = new();
    private readonly List<VoiceTalker> _talkers = new();
    private readonly List<ushort> _muted = new();
    private readonly VoiceDsp _dsp = new();
    private VoiceDspThread? _voiceThread;
    private SpeexResampler? _resampler;
    private int _captureRate = VoiceK.SampleRate;
    private int _frameFill;
    private VoiceTransport? _transport;
    private EndpointId _server;
    private VoiceLocalLink? _link;
    private ushort _sequence;
    private ushort _joinSequence;
    private long _lastJoin = long.MinValue;
    private bool _joinDirty = true;
    private bool _joinedReceiving;
    private bool _transmitting;
    private bool _localMutedShown;
    private EVoiceChannel _channel;
    private ushort _localTalker;
    private int _appliedJitter;

    public VoiceClient(VoiceSettings settings, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        Settings = settings;
        _clock = clock;
        _encoder = new VoiceEncoder();
        _appliedJitter = settings.JitterFrames;
        _localMutedShown = settings.MicMuted;
    }

    public VoiceSettings Settings { get; }

    public VoiceClientStats Stats { get; } = new();

    /// <summary>The local player's id (PlayerReplicationInfo.PlayerID) once known; a local link carries it to the server.</summary>
    public ushort LocalTalker
    {
        get => _localTalker;
        set
        {
            if (_localTalker != value)
            {
                _localTalker = value;
                _joinDirty = true;
                if (_link is not null)
                {
                    _link.Talker = value;
                }
            }
        }
    }

    /// <summary>
    /// Run the microphone clean-up on the voice thread (an MMCSS "Pro Audio" thread on Windows) instead of the
    /// client's own. The game sets it; tests leave it off, so a submitted frame is gated at once.
    /// </summary>
    public bool UseVoiceThread { get; set; }

    /// <summary>The automatic gain in use (1 while it is off).</summary>
    public float AutoGain => _dsp.AutoGain;

    /// <summary>The push to talk key ("all" channel) is held.</summary>
    public bool TalkKey { get; set; }

    /// <summary>The team push to talk key is held.</summary>
    public bool TeamKey { get; set; }

    /// <summary>The microphone is being sent.</summary>
    public bool Transmitting => _transmitting;

    /// <summary>The channel being sent on.</summary>
    public EVoiceChannel TransmitChannel => _channel;

    /// <summary>RMS of the last microphone frame after gain, 0 to 1 (the level meter).</summary>
    public float InputLevel { get; private set; }

    /// <summary>Whether the client has somewhere to send.</summary>
    public bool IsConnected => _link is not null || (_transport is not null && _server.IsValid);

    /// <summary>Remote talkers heard so far, in the order they first spoke.</summary>
    public IReadOnlyList<VoiceTalker> Talkers => _talkers;

    /// <summary>Expected uplink loss in percent (from the connection's statistics): the encoder's FEC follows it.</summary>
    public int UplinkLossPercent
    {
        get => _encoder.PacketLossPercent;
        set => _encoder.PacketLossPercent = value;
    }

    /// <summary>Raised on <see cref="Update"/> and capture when a speaking indicator should change.</summary>
    public event Action<VoiceActivity>? ActivityChanged;

    /// <summary>Talks to a server over the game's transport. <paramref name="server"/> is the server connection's endpoint.</summary>
    public void Connect(VoiceTransport transport, EndpointId server)
    {
        ArgumentNullException.ThrowIfNull(transport);
        Disconnect();
        _transport = transport;
        _server = server;
        transport.Voice = this;
        _joinDirty = true;
    }

    /// <summary>Talks to a listen server in this process.</summary>
    public void Connect(VoiceLocalLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        Disconnect();
        _link = link;
        link.Talker = _localTalker;
        _joinDirty = true;
    }

    public void Disconnect()
    {
        if (_transport is not null && ReferenceEquals(_transport.Voice, this))
        {
            _transport.Voice = null;
        }

        _transport = null;
        _server = EndpointId.None;
        _link = null;
        StopTransmitting(send: false);
        _voiceThread?.Dispose();
        _voiceThread = null;
    }

    /// <summary>Sets the rate of the samples <see cref="SubmitCapture"/> receives (the audio server's mix rate).</summary>
    public void SetCaptureRate(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleRate, 192000);
        if (sampleRate == _captureRate)
        {
            return;
        }

        _captureRate = sampleRate;
        _resampler = sampleRate == VoiceK.SampleRate ? null : CreateResampler(sampleRate);
    }

    /// <summary>Microphone samples, mono, -1 to 1, at the capture rate.</summary>
    public void SubmitCapture(ReadOnlySpan<float> mono)
    {
        TakeCleaned();
        if (_resampler is null)
        {
            Accumulate(mono);
            return;
        }

        while (mono.Length > 0)
        {
            int take = Math.Min(mono.Length, ResampleChunk);
            mono[..take].CopyTo(_resampleIn);
            int consumed = take;
            int produced = _resampleOut.Length;
            _resampler.Process(0, _resampleIn.AsSpan(0, take), ref consumed, _resampleOut, ref produced);
            Accumulate(_resampleOut.AsSpan(0, produced));
            if (consumed <= 0)
            {
                break;
            }

            mono = mono[consumed..];
        }
    }

    /// <summary>
    /// What the voice output is playing (48 kHz mono, the mix of every talker as it is handed to the output): the echo
    /// canceller's reference. Ignored while echo cancellation is off.
    /// </summary>
    public void SubmitPlayback(ReadOnlySpan<float> mono)
    {
        if (!Settings.EchoCancellation || mono.Length == 0)
        {
            return;
        }

        if (_voiceThread is not null)
        {
            _voiceThread.SubmitFar(mono);
        }
        else
        {
            _dsp.SubmitFarEnd(mono);
        }
    }

    /// <summary>
    /// Receives from a local link, refreshes the join message, applies setting changes and updates the speaking
    /// indicators. Call once per frame on the client's thread.
    /// </summary>
    public void Update()
    {
        long now = _clock.NowMicroseconds;
        TakeCleaned();
        if (_link is not null)
        {
            _link.Talker = _localTalker;
            while (_link.TryReceiveDown(_receive, out int length))
            {
                HandleDown(_receive.AsSpan(0, length), now);
            }
        }

        if (!Settings.Enabled || Settings.MicMuted)
        {
            StopTransmitting(send: true);
        }

        if (_localMutedShown != Settings.MicMuted)
        {
            _localMutedShown = Settings.MicMuted;
            RaiseLocal();
        }

        if (IsConnected && (_joinDirty || _joinedReceiving != Settings.Enabled || _lastJoin == long.MinValue ||
            now - _lastJoin >= VoiceK.JoinRefreshMs * 1000L))
        {
            SendJoin(now);
        }

        if (_appliedJitter != Settings.JitterFrames)
        {
            _appliedJitter = Settings.JitterFrames;
            foreach (VoiceTalker talker in _talkers)
            {
                talker.Buffer.TargetFrames = _appliedJitter;
            }
        }

        long timeout = VoiceK.SpeakingTimeoutMs * 1000L;
        foreach (VoiceTalker talker in _talkers)
        {
            bool speaking = Settings.Enabled && !talker.Muted && !talker.SpurtEndReceived &&
                talker.LastPacketMicros != long.MinValue && now - talker.LastPacketMicros < timeout;
            if (speaking != talker.Speaking)
            {
                talker.Speaking = speaking;
                ActivityChanged?.Invoke(new VoiceActivity(talker.Id, false, speaking, talker.Channel, talker.Muted));
            }
        }
    }

    public VoiceTalker? FindTalker(ushort id) => _talkerById.GetValueOrDefault(id);

    public bool IsMuted(ushort talker) => _muted.Contains(talker);

    /// <summary>Mutes or unmutes a talker for this player (and asks the server to stop or resume sending him).</summary>
    public void SetMuted(ushort talker, bool muted)
    {
        if (talker == 0 || muted == _muted.Contains(talker))
        {
            return;
        }

        if (muted)
        {
            if (_muted.Count >= VoiceK.MaxMutedTalkers)
            {
                return;
            }

            _muted.Add(talker);
        }
        else
        {
            _muted.Remove(talker);
        }

        _joinDirty = true;
        VoiceTalker t = GetTalker(talker);
        t.Muted = muted;
        if (muted)
        {
            t.Clear();
        }

        t.Speaking = false;
        ActivityChanged?.Invoke(new VoiceActivity(talker, false, false, t.Channel, muted));
    }

    /// <summary>Sets this player's volume for a talker (0 to 2).</summary>
    public void SetVolume(ushort talker, float volume)
    {
        if (talker != 0)
        {
            GetTalker(talker).Volume = volume;
        }
    }

    public void OnVoiceDatagram(EndpointId from, ReadOnlySpan<byte> datagram)
    {
        if (from != _server)
        {
            Stats.Ignored++;
            return;
        }

        HandleDown(datagram, _clock.NowMicroseconds);
    }

    public void OnDispatch()
    {
    }

    public void Dispose() => Disconnect();

    private VoiceTalker GetTalker(ushort id)
    {
        if (!_talkerById.TryGetValue(id, out VoiceTalker? talker))
        {
            talker = new VoiceTalker(id, Settings.JitterFrames) { Muted = _muted.Contains(id) };
            _talkerById.Add(id, talker);
            _talkers.Add(talker);
        }

        return talker;
    }

    private void HandleDown(ReadOnlySpan<byte> datagram, long now)
    {
        if (!Settings.Enabled || !VoiceWire.TryReadAudio(datagram, out VoiceHeader header, out ReadOnlySpan<byte> opus) ||
            header.Kind != EVoicePacketKind.Down || header.Talker == 0 || header.Talker == _localTalker)
        {
            Stats.Ignored++;
            return;
        }

        Stats.PacketsReceived++;
        Stats.BytesReceived += datagram.Length;
        GetTalker(header.Talker).Receive(in header, opus, now);
    }

    private void Accumulate(ReadOnlySpan<float> samples)
    {
        while (samples.Length > 0)
        {
            int take = Math.Min(samples.Length, VoiceK.FrameSamples - _frameFill);
            samples[..take].CopyTo(_frame.AsSpan(_frameFill));
            _frameFill += take;
            samples = samples[take..];
            if (_frameFill == VoiceK.FrameSamples)
            {
                _frameFill = 0;
                CleanFrame();
            }
        }
    }

    /// <summary>
    /// A whole microphone frame: through the clean-up, here or on the voice thread. The thread is started when the
    /// clean-up is wanted, and used until it has no frame left, so frames never overtake each other.
    /// </summary>
    private void CleanFrame()
    {
        bool wanted = VoiceDsp.Wanted(Settings);
        if (UseVoiceThread && wanted && _voiceThread is null)
        {
            _voiceThread = new VoiceDspThread(_dsp, Settings);
        }

        if (_voiceThread is not null && (wanted || !_voiceThread.Idle))
        {
            _voiceThread.SubmitMicrophone(_frame);
            return;
        }

        // Also with every stage off: the clean-up notes the switch and starts afresh when one comes back on.
        _dsp.Process(Settings, _frame);
        ProcessFrame(_frame);
    }

    /// <summary>The voice thread's cleaned frames, gated and sent in order.</summary>
    private void TakeCleaned()
    {
        if (_voiceThread is null)
        {
            return;
        }

        while (_voiceThread.TryTake(_cleaned))
        {
            ProcessFrame(_cleaned);
        }
    }

    private void ProcessFrame(float[] frame)
    {
        float gain = Settings.AutoGain ? 1f : Settings.InputGain;
        double sum = 0;
        for (int i = 0; i < VoiceK.FrameSamples; i++)
        {
            float s = Math.Clamp(frame[i] * gain, -1f, 1f);
            _pcm[i] = (short)(s * 32767f);
            sum += s * s;
        }

        InputLevel = (float)Math.Sqrt(sum / VoiceK.FrameSamples);
        Stats.FramesCaptured++;
        bool send = _gate.Step(Settings, InputLevel, TalkKey, TeamKey, out EVoiceChannel channel) && IsConnected;
        if (send)
        {
            EVoiceFlags flags = EVoiceFlags.None;
            if (!_transmitting)
            {
                _encoder.Reset();
                flags = EVoiceFlags.TalkStart;
                _transmitting = true;
                _channel = channel;
                RaiseLocal();
            }
            else if (channel != _channel)
            {
                _channel = channel;
                RaiseLocal();
            }

            SendFrame(flags);
        }
        else
        {
            StopTransmitting(send: true);
        }
    }

    /// <summary>Ends a talk spurt; with <paramref name="send"/> the closing frame goes out flagged as the last.</summary>
    private void StopTransmitting(bool send)
    {
        if (!_transmitting)
        {
            return;
        }

        if (send && IsConnected)
        {
            SendFrame(EVoiceFlags.TalkEnd);
        }

        _transmitting = false;
        RaiseLocal();
    }

    private void SendFrame(EVoiceFlags flags)
    {
        int length = _encoder.Encode(_pcm, _packet);
        var header = new VoiceHeader(EVoicePacketKind.Up, _channel, flags, _sequence++, 0);
        int size = VoiceWire.WriteAudio(_datagram, header, _packet.AsSpan(0, length));
        if (SendRaw(_datagram.AsSpan(0, size)))
        {
            Stats.FramesSent++;
            Stats.BytesSent += size;
            Stats.LargestPacket = Math.Max(Stats.LargestPacket, length);
        }
    }

    private void SendJoin(long now)
    {
        Span<ushort> muted = stackalloc ushort[VoiceK.MaxMutedTalkers];
        int count = Math.Min(_muted.Count, VoiceK.MaxMutedTalkers);
        for (int i = 0; i < count; i++)
        {
            muted[i] = _muted[i];
        }

        int size = VoiceWire.WriteJoin(_datagram, _joinSequence++, _localTalker, Settings.Enabled, muted[..count]);
        if (SendRaw(_datagram.AsSpan(0, size)))
        {
            Stats.JoinsSent++;
            _lastJoin = now;
            _joinDirty = false;
            _joinedReceiving = Settings.Enabled;
        }
    }

    private bool SendRaw(ReadOnlySpan<byte> datagram)
    {
        bool sent = _link is not null
            ? _link.SendUp(datagram)
            : _transport is not null && _server.IsValid && _transport.SendVoice(_server, datagram);
        if (!sent)
        {
            Stats.SendFailures++;
        }

        return sent;
    }

    private void RaiseLocal() =>
        ActivityChanged?.Invoke(new VoiceActivity(_localTalker, true, _transmitting, _channel, Settings.MicMuted));

#pragma warning disable CS0618 // The managed resampler on purpose, as for the codec.
    private static SpeexResampler CreateResampler(int sampleRate) => new(1, sampleRate, VoiceK.SampleRate, 5);
#pragma warning restore CS0618
}
