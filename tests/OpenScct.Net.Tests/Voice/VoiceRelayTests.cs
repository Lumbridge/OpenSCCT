using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Net.Voice;
using Xunit;

namespace OpenScct.Net.Tests.Voice;

/// <summary>Voice end to end through the server relay: routing, mutes, the talker cap, rate limits, links and loss.</summary>
public sealed class VoiceRelayTests
{
    private const int Spies = 0;
    private const int Mercs = 1;

    private static void AssertTone(float[] heard, double hz = 440)
    {
        Assert.True(heard.Length >= VoiceK.SampleRate / 2, $"heard {heard.Length} samples");
        ReadOnlySpan<float> steady = heard.AsSpan(heard.Length / 4, heard.Length / 2);
        Assert.InRange(VoiceSignals.ZeroCrossingHz(steady), hz - 15, hz + 15);
        Assert.True(VoiceSignals.Rms(steady) > 0.1, "audible");
    }

    [Fact]
    public void ATalkerReachesHisTeamAndNotTheEnemy()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        rig.Add(1, Spies);
        rig.Add(2, Spies);
        rig.Add(3, Mercs);
        rig.Step(5);
        rig.Talk(1);
        rig.Step(100);

        AssertTone(rig[2].From(1));
        Assert.Equal(0, rig[3].FramesFrom(1));
        Assert.Empty(rig[3].Client.Talkers);
        Assert.Equal(0, rig[1].FramesFrom(1));
        Assert.Equal(rig[1].Client.Stats.FramesSent, rig.Relay.Stats.DownPackets);
        Assert.Equal(0, rig.Relay.Stats.UnknownSender + rig.Relay.Stats.Malformed + rig.Relay.Stats.RateLimited);
    }

    [Fact]
    public void TheSpeakingIndicatorFollowsTheTalkSpurt()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        rig.Add(1, Spies);
        rig.Add(2, Spies);
        var seen = new List<VoiceActivity>();
        rig[2].Client.ActivityChanged += seen.Add;
        var local = new List<VoiceActivity>();
        rig[1].Client.ActivityChanged += local.Add;
        rig.Step(5);
        rig.Talk(1, channel: EVoiceChannel.Team);
        rig.Step(20);
        Assert.Contains(new VoiceActivity(1, false, true, EVoiceChannel.Team, false), seen);
        Assert.True(rig[2].Client.FindTalker(1)!.Speaking);
        Assert.True(rig[1].Client.Transmitting);
        Assert.Equal(EVoiceChannel.Team, rig[1].Client.TransmitChannel);

        rig.Silence(1);
        rig.Step(20);
        Assert.Equal(new VoiceActivity(1, false, false, EVoiceChannel.Team, false), seen[^1]);
        Assert.False(rig[2].Client.FindTalker(1)!.Speaking);
        Assert.Equal([true, false], local.Select(a => a.Speaking).ToArray());
        Assert.All(local, a => Assert.True(a.IsLocal));
    }

    [Fact]
    public void AMutedTalkerIsNoLongerSentToTheListener()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        rig.Add(1, Spies);
        rig.Add(2, Spies);
        rig.Step(5);
        rig.Talk(1);
        rig.Step(30);
        long before = rig[2].Client.Stats.PacketsReceived;
        Assert.True(before > 20);

        rig[2].Client.SetMuted(1, true);
        rig.Step(5);
        long afterMute = rig[2].Client.Stats.PacketsReceived;
        rig.Step(30);
        Assert.Equal(afterMute, rig[2].Client.Stats.PacketsReceived);
        Assert.True(rig.Relay.Stats.Unrouted > 0);

        rig[2].Client.SetMuted(1, false);
        rig.Step(30);
        Assert.True(rig[2].Client.Stats.PacketsReceived > afterMute + 20);
    }

    [Fact]
    public void AListenerGetsAtMostFourTalkersAtOnce()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        for (ushort id = 1; id <= 6; id++)
        {
            rig.Add(id, Spies);
        }

        rig.Step(5);
        for (ushort id = 1; id <= 5; id++)
        {
            rig.Talk(id, VoiceSignals.Tone(200 + 100 * id, 10.0));
        }

        rig.Step(50);
        int heard = Enumerable.Range(1, 5).Count(id => rig[6].FramesFrom((ushort)id) > 0);
        Assert.Equal(VoiceK.MaxTalkersPerListener, heard);
        Assert.True(rig.Relay.Stats.TalkerCapReached > 0);

        // When one of the four stops, the fifth gets the slot once the hold has passed.
        ushort silent = (ushort)Enumerable.Range(1, 5).First(id => rig[6].FramesFrom((ushort)id) > 0);
        ushort waiting = (ushort)Enumerable.Range(1, 5).First(id => rig[6].FramesFrom((ushort)id) == 0);
        rig.Silence(silent);
        rig.Step(50);
        Assert.True(rig[6].FramesFrom(waiting) > 0);
    }

    [Fact]
    public void ATalkerFloodingTheServerIsRateLimited()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        rig.Add(1, Spies);
        rig.Add(2, Spies);
        rig.Step(5);

        // A modified client sends 200 frames at once.
        var datagram = new byte[VoiceWire.MaxDatagramBytes];
        VoiceTransport transport = rig[1].Transport!;
        EndpointId server = transport.Connect("server");
        for (int i = 0; i < 200; i++)
        {
            int n = VoiceWire.WriteAudio(datagram, new VoiceHeader(EVoicePacketKind.Up, EVoiceChannel.All, EVoiceFlags.None, (ushort)i, 0), [0xF8, 0xFF, 0xFE]);
            transport.SendVoice(server, datagram.AsSpan(0, n));
        }

        rig.Step(1);
        Assert.InRange(rig.Relay.Stats.DownPackets, 1, VoiceK.PacketBurst + 2);
        Assert.True(rig.Relay.Stats.RateLimited >= 200 - VoiceK.PacketBurst - 2);
    }

    [Fact]
    public void StrangersAndForgedTalkerIdsGetNowhere()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        rig.Add(1, Spies);
        rig.Add(2, Spies);
        rig.Step(5);

        // An endpoint that is no player: dropped.
        using var stranger = new VoiceTransport(rig.Network.Bind("stranger"));
        EndpointId server = stranger.Connect("server");
        var datagram = new byte[VoiceWire.MaxDatagramBytes];
        int n = VoiceWire.WriteAudio(datagram, new VoiceHeader(EVoicePacketKind.Up, EVoiceChannel.All, EVoiceFlags.None, 1, 2), [0xF8, 0xFF, 0xFE]);
        stranger.SendVoice(server, datagram.AsSpan(0, n));
        rig.Step(1);
        Assert.Equal(1, rig.Relay.Stats.UnknownSender);

        // A player writing someone else's id into an Up packet: the server writes the sender's own id.
        n = VoiceWire.WriteAudio(datagram, new VoiceHeader(EVoicePacketKind.Up, EVoiceChannel.All, EVoiceFlags.None, 1, 2), [0xF8, 0xFF, 0xFE]);
        VoiceTransport t1 = rig[1].Transport!;
        t1.SendVoice(t1.Connect("server"), datagram.AsSpan(0, n));
        rig.Step(1);
        Assert.NotNull(rig[2].Client.FindTalker(1));
        Assert.Null(rig[2].Client.FindTalker(2));
    }

    [Fact]
    public void AListenHostsOwnPlayerTalksOverTheLocalLink()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        rig.AddLocal(1, Mercs);
        rig.Add(2, Mercs);
        rig.Step(5);
        rig.Talk(1);
        rig.Step(60);
        AssertTone(rig[2].From(1));

        rig.Silence(1);
        rig.Talk(2, VoiceSignals.Tone(330, 10.0));
        rig.Step(60);
        AssertTone(rig[1].From(2), 330);
    }

    [Fact]
    public void AMicrophoneAt44100HzIsResampledTo48000()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        VoiceRig.Player talker = rig.Add(1, Spies);
        rig.Add(2, Spies);
        talker.CaptureRate = 44100;
        talker.Client.SetCaptureRate(44100);
        rig.Step(5);
        rig.Talk(1, VoiceSignals.Tone(440, 10.0, rate: 44100));
        rig.Step(100);
        AssertTone(rig[2].From(1));
        Assert.InRange(talker.Client.Stats.FramesCaptured, 98, 105);
    }

    [Fact]
    public void DeathChangesWhoHearsYouAtTheNextRosterRebuild()
    {
        using var rig = new VoiceRig(SimLinkProfile.A, mode: EVoiceChatMode.VCM_Classic);
        rig.Add(1, Spies);
        rig.Add(2, Spies);
        rig.Step(5);
        rig.Talk(1);
        rig.Step(20);
        Assert.True(rig[2].Client.Stats.PacketsReceived > 0);

        rig.Directory.Update(1, m => { m.Life = EVoiceLife.Dead; return m; });
        rig.Relay.Invalidate();
        rig.Step(2);
        long dead = rig[2].Client.Stats.PacketsReceived;
        rig.Step(20);
        Assert.Equal(dead, rig[2].Client.Stats.PacketsReceived);
    }

    [Fact]
    public void VoiceSurvivesALossyJitteryLink()
    {
        // Profile C: 120 ms latency with 20 ms jitter, 2% loss, 1% duplicates and reordering.
        using var rig = new VoiceRig(SimLinkProfile.C, seed: 7);
        rig.Add(1, Spies, new VoiceSettings { PushToTalk = true, JitterFrames = 4 });
        rig.Add(2, Spies, new VoiceSettings { PushToTalk = true, JitterFrames = 4 });
        rig.Step(10);
        rig.Talk(1);
        rig.Step(250);

        VoiceTalker talker = rig[2].Client.FindTalker(1)!;
        VoiceJitterStats stats = talker.Buffer.Stats;
        long frames = stats.Played + stats.Fec + stats.Concealed;
        Assert.True(frames > 200, $"played {stats.Played}, fec {stats.Fec}, concealed {stats.Concealed}");
        Assert.True(stats.Played > 0.9 * frames, $"played {stats.Played} of {frames}");
        Assert.True(stats.Underruns <= 3, $"{stats.Underruns} underruns");
        AssertTone(rig[2].From(1));
    }

    [Fact]
    public void BandwidthStaysWithinTheBudget()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        rig.Add(1, Spies);
        rig.Add(2, Spies);
        rig.Add(3, Spies);
        rig.Step(5);
        rig.Talk(1, VoiceSignals.Voice(10.0));
        rig.Step(250);

        // 5 s of talk: uplink at most 69 bytes per 20 ms frame (3,450 bytes a second), the same per listener down.
        (long up, _) = rig.Relay.BytesOf(1);
        (_, long down2) = rig.Relay.BytesOf(2);
        double seconds = 250.0 / VoiceK.FramesPerSecond;
        Assert.InRange(up / seconds, 1000, VoiceWire.MaxAudioDatagramBytes * VoiceK.FramesPerSecond);
        Assert.InRange(down2 / seconds, 1000, VoiceWire.MaxAudioDatagramBytes * VoiceK.FramesPerSecond);
        Assert.InRange(rig[1].Client.Stats.LargestPacket, 1, VoiceK.MaxOpusPacketBytes);
    }

    [Fact]
    public void VoiceActivationSendsSpeechAndNotSilence()
    {
        using var rig = new VoiceRig(SimLinkProfile.A);
        rig.Add(1, Spies, new VoiceSettings());
        rig.Add(2, Spies);
        rig.Step(5);

        // Silence and a quiet hum under the threshold: nothing goes out.
        rig[1].Microphone = VoiceSignals.Tone(100, 10.0, amplitude: 0.01f);
        rig[1].Talking = true;
        rig.Step(50);
        Assert.Equal(0, rig[1].Client.Stats.FramesSent);

        // Speech opens the gate; when it stops, the hold keeps sending for 300 ms (15 frames) plus the closing frame.
        rig[1].Microphone = VoiceSignals.Voice(10.0);
        rig.Step(50);
        Assert.Equal(50, rig[1].Client.Stats.FramesSent);
        rig[1].Talking = false;
        rig.Step(50);
        Assert.Equal(50 + 15, rig[1].Client.Stats.FramesSent);
    }
    [Fact]
    public void RoutingAndThePlayoutPathDoNotAllocatePerFrame()
    {
        // The relay forwarding a talker to two listeners over local links, and the listeners' jitter buffers, in steady
        // state. The codec (Concentus) allocates internally and is measured in docs/VOICE.md, not here.
        var clock = new VirtualClock();
        var directory = new StaticVoiceDirectory();
        var relay = new VoiceRelay(null, directory, clock);
        var links = new VoiceLocalLink[3];
        for (ushort id = 1; id <= 3; id++)
        {
            directory.Set(VoiceMember.Create(id, Spies));
            links[id - 1] = new VoiceLocalLink { Talker = id };
            relay.AttachLocal(links[id - 1]);
        }

        var datagram = new byte[VoiceWire.MaxDatagramBytes];
        var received = new byte[VoiceWire.MaxDatagramBytes];
        var buffers = new[] { new VoiceJitterBuffer(3), new VoiceJitterBuffer(3) };
        ushort seq = 0;
        void Frame()
        {
            clock.Advance(20_000);
            for (int i = 1; i < 3; i++)
            {
                int n = VoiceWire.WriteJoin(datagram, seq, 0, true, ReadOnlySpan<ushort>.Empty);
                links[i].SendUp(datagram.AsSpan(0, n));
            }

            int length = VoiceWire.WriteAudio(datagram, new VoiceHeader(EVoicePacketKind.Up, EVoiceChannel.All, EVoiceFlags.None, seq++, 0), [0xF8, 0xFF, 0xFE]);
            links[0].SendUp(datagram.AsSpan(0, length));
            relay.Tick();
            for (int i = 1; i < 3; i++)
            {
                while (links[i].TryReceiveDown(received, out int got))
                {
                    Assert.True(VoiceWire.TryReadAudio(received.AsSpan(0, got), out VoiceHeader h, out ReadOnlySpan<byte> opus));
                    buffers[i - 1].Push(h.Sequence, h.Flags, h.Channel, opus);
                }

                buffers[i - 1].Next(out _);
            }
        }

        for (int f = 0; f < 50; f++)
        {
            Frame();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 500; f++)
        {
            Frame();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        // The first frame is relayed before the listeners' first joins in the same tick.
        Assert.Equal(2 * 549, relay.Stats.DownPackets);
        Assert.True(buffers[0].Stats.Played >= 540);
    }
}
