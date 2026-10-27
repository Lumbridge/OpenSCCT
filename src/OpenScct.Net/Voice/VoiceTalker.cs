using System;
using OpenScct.Net.Voice.Proximity;

namespace OpenScct.Net.Voice;

/// <summary>
/// One remote talker on a client: its jitter buffer, its Opus decoder, the player's mute and volume for it, and what the
/// HUD shows (speaking, channel, level). <see cref="ReadFrame"/> is driven by the audio output, one 20 ms frame at a time.
/// </summary>
public sealed class VoiceTalker
{
    private readonly VoiceDecoder _decoder = new();
    private readonly short[] _pcm = new short[VoiceK.FrameSamples];
    private float _volume = 1f;
    private float _lowPass;

    internal VoiceTalker(ushort id, int jitterFrames)
    {
        Id = id;
        Buffer = new VoiceJitterBuffer(jitterFrames);
    }

    /// <summary>The talker's player id (PlayerReplicationInfo.PlayerID).</summary>
    public ushort Id { get; }

    public VoiceJitterBuffer Buffer { get; }

    /// <summary>This player's volume for the talker, 0 to 2 (the overlay's scroll wheel).</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 2f);
    }

    /// <summary>
    /// Positional voice (the hub, docs/HUB.md): the gain and low-pass cut-off of this talker as the listener hears him
    /// (<see cref="ProximityPlayback"/>). The gain already holds the player's volume for the talker, so it replaces
    /// <see cref="Volume"/> while set. Null (a match): the talker plays at <see cref="Volume"/>, unfiltered.
    /// </summary>
    public VoiceMix? Spatial { get; set; }

    /// <summary>Muted by this player: nothing is played, and the server is asked to stop sending.</summary>
    public bool Muted { get; internal set; }

    /// <summary>The speaking indicator.</summary>
    public bool Speaking { get; internal set; }

    /// <summary>The channel of the talker's latest packet.</summary>
    public EVoiceChannel Channel { get; private set; }

    /// <summary>RMS of the last frame played, after volume (0 to 1).</summary>
    public float Level { get; private set; }

    /// <summary>Client clock of the latest packet, in microseconds.</summary>
    public long LastPacketMicros { get; private set; } = long.MinValue;

    /// <summary>The latest packet closed a talk spurt.</summary>
    public bool SpurtEndReceived { get; private set; }

    public long FramesDecoded { get; private set; }

    public long DecodeErrors { get; private set; }

    internal void Receive(in VoiceHeader header, ReadOnlySpan<byte> opus, long now)
    {
        if (Muted)
        {
            return;
        }

        if (Buffer.Push(header.Sequence, header.Flags, header.Channel, opus) || (header.Flags & EVoiceFlags.TalkEnd) != 0)
        {
            LastPacketMicros = now;
            Channel = header.Channel;
            SpurtEndReceived = (header.Flags & EVoiceFlags.TalkEnd) != 0;
        }
    }

    internal void Clear()
    {
        Buffer.Reset();
        _decoder.Reset();
        _lowPass = 0f;
        Level = 0f;
        SpurtEndReceived = true;
    }

    /// <summary>
    /// The next 20 ms of this talker at 48 kHz (<see cref="VoiceK.FrameSamples"/> samples), scaled by the talker's volume
    /// and <paramref name="masterVolume"/>. False when there is nothing to play (the destination is left alone).
    /// </summary>
    public bool ReadFrame(Span<float> destination, float masterVolume = 1f)
    {
        if (destination.Length < VoiceK.FrameSamples)
        {
            throw new ArgumentException("A frame holds 960 samples.", nameof(destination));
        }

        bool ok;
        switch (Buffer.Next(out ReadOnlySpan<byte> payload))
        {
            case EVoiceFrameKind.Packet:
                ok = _decoder.Decode(payload, _pcm);
                break;
            case EVoiceFrameKind.Fec:
                ok = _decoder.DecodeFec(payload, _pcm);
                break;
            case EVoiceFrameKind.Concealed:
                ok = _decoder.Conceal(_pcm);
                break;
            default:
                Level = 0f;
                return false;
        }

        FramesDecoded++;
        if (!ok)
        {
            DecodeErrors++;
        }

        VoiceMix? spatial = Spatial;
        float volume = spatial is { } mix ? mix.Gain : _volume;
        float gain = Muted ? 0f : volume * Math.Clamp(masterVolume, 0f, 1f) / 32768f;
        float a = spatial is { } m ? ProximityVoice.LowPassCoefficient(m.LowPassHz, VoiceK.SampleRate) : 1f;
        double sum = 0;
        for (int i = 0; i < VoiceK.FrameSamples; i++)
        {
            float s = _pcm[i] * gain;
            if (a < 1f)
            {
                // One-pole low-pass of the muffled voice.
                _lowPass += a * (s - _lowPass);
                s = _lowPass;
            }
            else
            {
                _lowPass = s;
            }

            destination[i] = s;
            sum += s * s;
        }

        Level = (float)Math.Sqrt(sum / VoiceK.FrameSamples);
        return true;
    }
}
