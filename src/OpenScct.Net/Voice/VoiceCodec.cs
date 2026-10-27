using System;
using Concentus.Enums;
using Concentus.Structs;
using OpenScct.Provenance;

namespace OpenScct.Net.Voice;

/// <summary>
/// The Opus encoder of one microphone (Concentus, the managed Opus port): 48 kHz mono, VOIP, 20 ms frames, constrained
/// VBR at <see cref="VoiceK.Bitrate"/>, complexity 10, voice signal, 16-bit depth and in-band FEC, every packet capped
/// at <see cref="VoiceK.MaxOpusPacketBytes"/>. Allocates only when created.
/// </summary>
[PortOf("rl:voice.codec", Spec = "spec/reloaded/FEATURES.md#rlvoicecodec---modern-codec-and-transport")]
public sealed class VoiceEncoder
{
    private readonly OpusEncoder _encoder;

    public VoiceEncoder(int bitrate = VoiceK.Bitrate, int complexity = VoiceK.Complexity, int packetLossPercent = VoiceK.DefaultPacketLossPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bitrate, 6000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitrate, VoiceK.MaxOpusPacketBytes * 8 * VoiceK.FramesPerSecond);
        _encoder = CreateEncoder();
        _encoder.Bitrate = bitrate;
        _encoder.Complexity = Math.Clamp(complexity, 0, 10);
        _encoder.UseVBR = true;
        _encoder.UseConstrainedVBR = true;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        _encoder.LSBDepth = 16;
        _encoder.UseInbandFEC = true;
        _encoder.UseDTX = false;
        PacketLossPercent = packetLossPercent;
    }

    /// <summary>Expected uplink loss (0 to 100); the encoder spends bits on FEC for it.</summary>
    public int PacketLossPercent
    {
        get => _encoder.PacketLossPercent;
        set => _encoder.PacketLossPercent = Math.Clamp(value, 0, 100);
    }

    /// <summary>Encodes one frame of <see cref="VoiceK.FrameSamples"/> samples and returns the packet length (1 to 62).</summary>
    public int Encode(ReadOnlySpan<short> frame, Span<byte> packet)
    {
        if (frame.Length < VoiceK.FrameSamples)
        {
            throw new ArgumentException("A frame holds 960 samples.", nameof(frame));
        }

        int length = _encoder.Encode(frame[..VoiceK.FrameSamples], VoiceK.FrameSamples, packet, Math.Min(packet.Length, VoiceK.MaxOpusPacketBytes));
        if (length <= 0)
        {
            throw new InvalidOperationException($"Opus encode failed ({length}).");
        }

        return length;
    }

    /// <summary>Back to the initial state at the start of a talk spurt (Reloaded resets rather than recreates its encoder).</summary>
    public void Reset() => _encoder.ResetState();

#pragma warning disable CS0618 // The managed encoder on purpose: the factory may pick a native libopus found on the machine.
    private static OpusEncoder CreateEncoder() => new(VoiceK.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
#pragma warning restore CS0618
}

/// <summary>
/// The Opus decoder of one talker: decodes a packet, decodes a lost frame from the in-band FEC of the packet after it,
/// or conceals a lost frame (packet loss concealment). Allocates only when created.
/// </summary>
[PortOf("rl:voice.codec", Spec = "spec/reloaded/FEATURES.md#rlvoicecodec---modern-codec-and-transport")]
public sealed class VoiceDecoder
{
    private readonly OpusDecoder _decoder;

    public VoiceDecoder() => _decoder = CreateDecoder();

    /// <summary>Decodes <paramref name="packet"/> into <see cref="VoiceK.FrameSamples"/> samples. False for a corrupt packet.</summary>
    public bool Decode(ReadOnlySpan<byte> packet, Span<short> pcm) => Run(packet, pcm, fec: false);

    /// <summary>Rebuilds the frame before <paramref name="nextPacket"/> from its in-band FEC (or concealment when it has none).</summary>
    public bool DecodeFec(ReadOnlySpan<byte> nextPacket, Span<short> pcm) => Run(nextPacket, pcm, fec: true);

    /// <summary>Fills a lost frame by concealment.</summary>
    public bool Conceal(Span<short> pcm) => Run(ReadOnlySpan<byte>.Empty, pcm, fec: false);

    public void Reset() => _decoder.ResetState();

    private bool Run(ReadOnlySpan<byte> packet, Span<short> pcm, bool fec)
    {
        if (pcm.Length < VoiceK.FrameSamples)
        {
            throw new ArgumentException("A frame holds 960 samples.", nameof(pcm));
        }

        int samples;
        try
        {
            samples = _decoder.Decode(packet, pcm, VoiceK.FrameSamples, fec);
        }
        catch (Exception e) when (e is Concentus.OpusException or ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            samples = -1;
        }

        if (samples == VoiceK.FrameSamples)
        {
            return true;
        }

        pcm[..VoiceK.FrameSamples].Clear();
        return false;
    }

#pragma warning disable CS0618 // The managed decoder on purpose, as for the encoder.
    private static OpusDecoder CreateDecoder() => new(VoiceK.SampleRate, 1);
#pragma warning restore CS0618
}
