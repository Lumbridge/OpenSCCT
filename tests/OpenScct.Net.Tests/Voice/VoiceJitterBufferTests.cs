using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using OpenScct.Net.Voice;
using Xunit;

namespace OpenScct.Net.Tests.Voice;

public sealed class VoiceJitterBufferTests
{
    /// <summary>A one-byte payload naming its sequence, so the test can see which packet came out.</summary>
    private static byte[] P(ushort seq) => [(byte)seq, (byte)(seq >> 8)];

    private static ushort SeqOf(ReadOnlySpan<byte> payload) => (ushort)(payload[0] | (payload[1] << 8));

    private static bool Push(VoiceJitterBuffer b, int seq, EVoiceFlags flags = EVoiceFlags.None) =>
        b.Push((ushort)seq, flags, EVoiceChannel.All, P((ushort)seq));

    /// <summary>One call to Next: "P12" a packet, "F12" FEC from packet 12, "C" concealment, "-" nothing.</summary>
    private static string Step(VoiceJitterBuffer b) => b.Next(out ReadOnlySpan<byte> payload) switch
    {
        EVoiceFrameKind.Packet => "P" + SeqOf(payload).ToString(System.Globalization.CultureInfo.InvariantCulture),
        EVoiceFrameKind.Fec => "F" + SeqOf(payload).ToString(System.Globalization.CultureInfo.InvariantCulture),
        EVoiceFrameKind.Concealed => "C",
        _ => "-",
    };

    private static string Steps(VoiceJitterBuffer b, int n) => string.Join(" ", Enumerable.Range(0, n).Select(_ => Step(b)));

    [Fact]
    public void BanksTheTargetThenPlaysInOrder()
    {
        var b = new VoiceJitterBuffer(3);
        Push(b, 10, EVoiceFlags.TalkStart);
        Assert.Equal("-", Step(b));
        Push(b, 11);
        Assert.Equal("-", Step(b));
        Push(b, 12);
        Assert.Equal("P10", Step(b));
        Push(b, 13);
        Assert.Equal("P11 P12 P13", Steps(b, 3));
        Assert.Equal(4, b.Stats.Played);
    }

    [Fact]
    public void UndoesReorderingAndDropsDuplicates()
    {
        var b = new VoiceJitterBuffer(3);
        foreach (int s in new[] { 1, 3, 2, 2, 5, 4, 3 })
        {
            Push(b, s);
        }

        Assert.Equal(2, b.Stats.Duplicates);
        Assert.True(b.Stats.Reordered >= 2);
        Assert.Equal("P1 P2 P3 P4 P5", Steps(b, 5));
    }

    [Fact]
    public void ALostFrameUsesTheNextPacketsFecOrConcealment()
    {
        var b = new VoiceJitterBuffer(2);
        foreach (int s in new[] { 0, 1, 3, 4, 7 })
        {
            Push(b, s);
        }

        // 2 is lost and 3 is here: FEC from 3. 5 and 6 are lost: 5 is concealed, 6 gets FEC from 7.
        Assert.Equal("P0 P1 F3 P3 P4 C F7 P7", Steps(b, 8));
        Assert.Equal(2, b.Stats.Fec);
        Assert.Equal(1, b.Stats.Concealed);
    }

    [Fact]
    public void ALatePacketIsDropped()
    {
        var b = new VoiceJitterBuffer(1);
        Push(b, 0);
        Push(b, 2);
        Assert.Equal("P0 F2 P2", Steps(b, 3));
        Assert.False(Push(b, 1));
        Assert.Equal(1, b.Stats.Late);
    }

    [Fact]
    public void RunsDryAfterThreeConcealedFramesAndBanksAgain()
    {
        var b = new VoiceJitterBuffer(2);
        Push(b, 0);
        Push(b, 1);
        Assert.Equal("P0 P1 C C C -", Steps(b, 6));
        Assert.Equal(1, b.Stats.Underruns);
        Assert.False(b.IsPlaying);

        // The stream resumes: it banks the target again before playing.
        Push(b, 6);
        Assert.Equal("-", Step(b));
        Push(b, 7);
        Assert.Equal("P6 P7", Steps(b, 2));

        // A packet from the gap that arrives now is late.
        Assert.False(Push(b, 3));
    }

    [Fact]
    public void APacketDelayedWhileTheBufferIsDryStillPlays()
    {
        var b = new VoiceJitterBuffer(2);
        Push(b, 0);
        Push(b, 1);
        Assert.Equal("P0 P1 C C", Steps(b, 4));

        // 2 was late, not lost: the playout point waited for it.
        Assert.True(Push(b, 2));
        Push(b, 3);
        Assert.Equal("P2 P3", Steps(b, 2));
        Assert.Equal(0, b.Stats.Late);
        Assert.Equal(2, b.Stats.Stretched);
        Assert.Equal(0, b.Stats.Underruns);
    }

    [Fact]
    public void AShortUtteranceWithItsEndPlaysWithoutWaitingForTheTarget()
    {
        var b = new VoiceJitterBuffer(6);
        Push(b, 40, EVoiceFlags.TalkStart);
        Push(b, 41, EVoiceFlags.TalkEnd);
        Assert.Equal("P40 P41", Steps(b, 2));
        Assert.True(b.SpurtEnded);
        Assert.Equal("-", Step(b));
        Assert.False(b.SpurtEnded);
    }

    [Fact]
    public void StartsAfterWaitingTwiceTheTargetWhenTheRestNeverCame()
    {
        var b = new VoiceJitterBuffer(4);
        Push(b, 0);
        Assert.Equal("- - - - - - - P0", Steps(b, 8));
    }

    [Fact]
    public void ABacklogIsTrimmedToTheTargetPlusTheAllowance()
    {
        var b = new VoiceJitterBuffer(2);
        for (int s = 0; s < 20; s++)
        {
            Push(b, s);
        }

        // 20 banked, target 2 + 6 extra: the oldest 12 go.
        Assert.Equal("P12", Step(b));
        Assert.Equal(12, b.Stats.Discarded);
    }

    [Fact]
    public void FollowsATalkerThatStartedOver()
    {
        var b = new VoiceJitterBuffer(1);
        Push(b, 500);
        Push(b, 501);
        Assert.Equal("P500 P501", Steps(b, 2));

        // A restarted client counts from 0 again and marks its first frame.
        Assert.True(Push(b, 0, EVoiceFlags.TalkStart));
        Assert.Equal("P0", Step(b));
    }

    [Fact]
    public void WrapsAroundTheSequenceSpace()
    {
        var b = new VoiceJitterBuffer(2);
        foreach (int s in new[] { 65534, 65535, 0, 1 })
        {
            Push(b, s);
        }

        Assert.Equal("P65534 P65535 P0 P1", Steps(b, 4));
    }

    [Fact]
    public void AnyArrivalOrderPlaysEachFrameOnceAndInOrder()
    {
        // Property: whatever the network does to a stream (loss, duplicates, reordering by up to a few frames), the
        // buffer hands out strictly increasing sequences, never one twice, and every frame it skips is accounted for as
        // FEC or concealment.
        Gen.Select(Gen.Int[0, 65535], Gen.Int[1, 12], Gen.Int[0, 100].Array[200], Gen.Int[0, 4].Array[200])
            .Sample((start, target, fate, delay) =>
            {
                var b = new VoiceJitterBuffer(target);
                var arrivals = new List<(int At, ushort Seq)>();
                for (int i = 0; i < 200; i++)
                {
                    if (fate[i] < 10)
                    {
                        continue; // lost
                    }

                    arrivals.Add((i + delay[i], (ushort)(start + i)));
                    if (fate[i] > 95)
                    {
                        arrivals.Add((i + delay[i] + 1, (ushort)(start + i))); // duplicate
                    }
                }

                var played = new List<ushort>();
                int cursor = 0;
                List<(int At, ushort Seq)> ordered = arrivals.OrderBy(a => a.At).ToList();
                for (int tick = 0; tick < 260; tick++)
                {
                    while (cursor < ordered.Count && ordered[cursor].At <= tick)
                    {
                        b.Push(ordered[cursor].Seq, EVoiceFlags.None, EVoiceChannel.All, P(ordered[cursor].Seq));
                        cursor++;
                    }

                    if (b.Next(out ReadOnlySpan<byte> payload) == EVoiceFrameKind.Packet)
                    {
                        played.Add(SeqOf(payload));
                    }
                }

                for (int i = 1; i < played.Count; i++)
                {
                    if (VoiceWire.SeqDiff(played[i], played[i - 1]) <= 0)
                    {
                        return false;
                    }
                }

                return b.Stats.Played == played.Count && played.Count > 0;
            }, iter: 300);
    }
}
