using System;
using System.Collections.Generic;
using OpenScct.Net.Voice;
using Xunit;

namespace OpenScct.Net.Tests.Voice;

public sealed class VoiceCodecTests
{
    /// <summary>Opus look-ahead at 48 kHz is 6.5 ms (312 samples); allow some margin when aligning.</summary>
    private const int MaxCodecDelay = 960;

    private static List<byte[]> EncodeAll(ReadOnlySpan<float> signal, VoiceEncoder encoder)
    {
        short[] pcm = VoiceSignals.ToPcm(signal);
        var packets = new List<byte[]>();
        var packet = new byte[VoiceK.MaxOpusPacketBytes];
        for (int at = 0; at + VoiceK.FrameSamples <= pcm.Length; at += VoiceK.FrameSamples)
        {
            int n = encoder.Encode(pcm.AsSpan(at, VoiceK.FrameSamples), packet);
            packets.Add(packet.AsSpan(0, n).ToArray());
        }

        return packets;
    }

    private static float[] ToFloat(short[] pcm)
    {
        var f = new float[pcm.Length];
        for (int i = 0; i < pcm.Length; i++)
        {
            f[i] = pcm[i] / 32768f;
        }

        return f;
    }

    [Fact]
    public void RoundTripKeepsAToneAndStaysWithinThePacketCap()
    {
        float[] tone = VoiceSignals.Tone(440, 2.0);
        var encoder = new VoiceEncoder();
        List<byte[]> packets = EncodeAll(tone, encoder);
        Assert.Equal(100, packets.Count);

        int total = 0;
        foreach (byte[] p in packets)
        {
            Assert.InRange(p.Length, 1, VoiceK.MaxOpusPacketBytes);
            total += p.Length;
        }

        // Average bitrate stays near the target and under Reloaded's cap of 62 bytes per 20 ms (24.8 kbit/s).
        double kbps = total * 8.0 / 2.0 / 1000.0;
        Assert.InRange(kbps, 8.0, VoiceK.MaxOpusPacketBytes * 8 * VoiceK.FramesPerSecond / 1000.0);

        var decoder = new VoiceDecoder();
        var decoded = new short[packets.Count * VoiceK.FrameSamples];
        for (int i = 0; i < packets.Count; i++)
        {
            Assert.True(decoder.Decode(packets[i], decoded.AsSpan(i * VoiceK.FrameSamples, VoiceK.FrameSamples)));
        }

        float[] output = ToFloat(decoded);

        // Skip the first 100 ms (encoder start-up) and compare.
        int skip = 4800;
        (double correlation, int delay) = VoiceSignals.BestCorrelation(tone.AsSpan(skip, 48000), output.AsSpan(skip), MaxCodecDelay);
        Assert.True(correlation > 0.9, $"correlation {correlation:F3} at delay {delay}");
        double hz = VoiceSignals.ZeroCrossingHz(output.AsSpan(skip, 48000));
        Assert.InRange(hz, 430, 450);
        double inBand = VoiceSignals.Power(output.AsSpan(skip, 48000), 440);
        Assert.True(inBand > 20 * VoiceSignals.Power(output.AsSpan(skip, 48000), 1000), "the tone dominates");
        Assert.InRange(VoiceSignals.Rms(output.AsSpan(skip, 48000)), 0.15, 0.3);
    }

    [Fact]
    public void ConcealmentFillsALostFrameWithSignal()
    {
        float[] tone = VoiceSignals.Voice(1.0);
        List<byte[]> packets = EncodeAll(tone, new VoiceEncoder());
        var decoder = new VoiceDecoder();
        var frame = new short[VoiceK.FrameSamples];
        for (int i = 0; i < 20; i++)
        {
            Assert.True(decoder.Decode(packets[i], frame));
        }

        // Frame 20 is lost: concealment continues the voice instead of a hole.
        Assert.True(decoder.Conceal(frame));
        Assert.True(VoiceSignals.Rms(ToFloat(frame)) > 0.05, "concealed frame is not silent");
        Assert.True(decoder.Decode(packets[21], frame));
    }

    [Fact]
    public void FecRebuildsALostFrameFromTheNextPacket()
    {
        float[] voice = VoiceSignals.Voice(1.0);
        var encoder = new VoiceEncoder { PacketLossPercent = 20 };
        List<byte[]> packets = EncodeAll(voice, encoder);

        // Reference: decode everything.
        var reference = new VoiceDecoder();
        var expected = new short[VoiceK.FrameSamples];
        var lossy = new VoiceDecoder();
        var rebuilt = new short[VoiceK.FrameSamples];
        for (int i = 0; i < 30; i++)
        {
            Assert.True(reference.Decode(packets[i], expected));
            if (i < 29)
            {
                Assert.True(lossy.Decode(packets[i], rebuilt));
            }
        }

        // Packet 29 is lost; packet 30's FEC carries a coarse copy of it.
        Assert.True(lossy.DecodeFec(packets[30], rebuilt));
        (double correlation, _) = VoiceSignals.BestCorrelation(ToFloat(expected), ToFloat(rebuilt), 0);
        Assert.True(correlation > 0.5, $"FEC frame correlates with the real one ({correlation:F3})");
    }

    [Fact]
    public void ACorruptPacketDecodesToSilenceAndReportsIt()
    {
        var decoder = new VoiceDecoder();
        var frame = new short[VoiceK.FrameSamples];
        Array.Fill(frame, (short)123);

        // A code 3 packet that claims more frames than fit is invalid.
        Assert.False(decoder.Decode([0xFF, 0xFF, 0xFF], frame));
        Assert.All(frame, s => Assert.Equal(0, s));
    }
}
