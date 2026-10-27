using System;
using OpenScct.Net.Voice;
using Xunit;

namespace OpenScct.Net.Tests.Voice;

public sealed class VoiceWireTests
{
    [Fact]
    public void AudioRoundTrips()
    {
        var buffer = new byte[VoiceWire.MaxDatagramBytes];
        byte[] opus = [1, 2, 3, 4, 5];
        var header = new VoiceHeader(EVoicePacketKind.Down, EVoiceChannel.Team, EVoiceFlags.TalkStart | EVoiceFlags.TalkEnd, 65535, 513);
        int length = VoiceWire.WriteAudio(buffer, header, opus);
        Assert.Equal(VoiceWire.HeaderBytes + 5, length);
        Assert.True(VoiceWire.IsVoice(buffer.AsSpan(0, length)));
        Assert.True(VoiceWire.TryReadAudio(buffer.AsSpan(0, length), out VoiceHeader read, out ReadOnlySpan<byte> payload));
        Assert.Equal(header, read);
        Assert.Equal(opus, payload.ToArray());
    }

    [Fact]
    public void JoinRoundTripsItsMuteList()
    {
        var buffer = new byte[VoiceWire.MaxDatagramBytes];
        int length = VoiceWire.WriteJoin(buffer, 7, 42, receive: true, [3, 9, 300]);
        Assert.True(VoiceWire.TryReadJoin(buffer.AsSpan(0, length), out VoiceHeader header, out bool receive, out ReadOnlySpan<byte> muted));
        Assert.Equal(EVoicePacketKind.Join, header.Kind);
        Assert.Equal(42, header.Talker);
        Assert.True(receive);
        Assert.Equal(3, muted.Length / 2);
        Assert.Equal(300, VoiceWire.MutedAt(muted, 2));
    }

    [Fact]
    public void GamePacketsAndGarbageAreNotVoice()
    {
        // Game packets start with the packet header marker 0xA7.
        Assert.False(VoiceWire.IsVoice([0xA7, 0, 0, 0, 0, 0, 0, 0]));
        Assert.False(VoiceWire.IsVoice([VoiceWire.Magic, 0x41]));

        var buffer = new byte[VoiceWire.MaxDatagramBytes];
        int length = VoiceWire.WriteAudio(buffer, new VoiceHeader(EVoicePacketKind.Up, EVoiceChannel.All, EVoiceFlags.None, 1, 0), [9]);

        // Wrong version, unknown kind, empty or oversized payloads are refused.
        byte[] bad = buffer.AsSpan(0, length).ToArray();
        bad[1] = (byte)((bad[1] & 0x3F) | (2 << 6));
        Assert.False(VoiceWire.TryReadHeader(bad, out _));
        bad = buffer.AsSpan(0, length).ToArray();
        bad[1] = (byte)((bad[1] & ~0x07) | 6);
        Assert.False(VoiceWire.TryReadHeader(bad, out _));
        Assert.False(VoiceWire.TryReadAudio(buffer.AsSpan(0, VoiceWire.HeaderBytes), out _, out _));
        var oversized = new byte[VoiceWire.HeaderBytes + VoiceK.MaxOpusPacketBytes + 1];
        buffer.AsSpan(0, VoiceWire.HeaderBytes).CopyTo(oversized);
        Assert.False(VoiceWire.TryReadAudio(oversized, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VoiceWire.WriteAudio(new byte[100], new VoiceHeader(EVoicePacketKind.Up, EVoiceChannel.All, EVoiceFlags.None, 1, 0), new byte[63]));
    }

    [Fact]
    public void TheLargestDatagramFitsTheBudget()
    {
        // 7 header bytes and at most 62 Opus bytes per 20 ms: 3,450 bytes a second per talker before UDP overhead.
        Assert.Equal(69, VoiceWire.MaxAudioDatagramBytes);
        Assert.Equal(3450, VoiceWire.MaxAudioDatagramBytes * VoiceK.FramesPerSecond);
    }
}

public sealed class VoiceGateTests
{
    private static bool Step(VoiceGate gate, VoiceSettings s, float level, bool talk = false, bool team = false) =>
        gate.Step(s, level, talk, team, out _);

    [Fact]
    public void OpensAtTheThresholdHoldsAndClosesBelowSixtyPercent()
    {
        var s = new VoiceSettings { ActivationThreshold = 0.1f, ActivationHoldMs = 100 };
        var gate = new VoiceGate();
        Assert.False(Step(gate, s, 0.09f));
        Assert.True(Step(gate, s, 0.1f));

        // Above 60% of the threshold keeps it open indefinitely.
        for (int i = 0; i < 20; i++)
        {
            Assert.True(Step(gate, s, 0.061f));
        }

        // Below: 100 ms of hold is five frames, closing on the fifth.
        Assert.True(Step(gate, s, 0.0f));
        Assert.True(Step(gate, s, 0.0f));
        Assert.True(Step(gate, s, 0.0f));
        Assert.True(Step(gate, s, 0.0f));
        Assert.False(Step(gate, s, 0.0f));
        Assert.False(Step(gate, s, 0.09f));
    }

    [Fact]
    public void ThresholdZeroAlwaysSendsAndOneNever()
    {
        var gate = new VoiceGate();
        Assert.True(Step(gate, new VoiceSettings { ActivationThreshold = 0f }, 0f));
        Assert.False(Step(gate, new VoiceSettings { ActivationThreshold = 1f }, 1f));
    }

    [Fact]
    public void PushToTalkFollowsTheKeysAndTheTeamKeyPicksTheTeamChannel()
    {
        var s = new VoiceSettings { PushToTalk = true };
        var gate = new VoiceGate();
        Assert.False(gate.Step(s, 0.5f, false, false, out _));
        Assert.True(gate.Step(s, 0.0f, true, false, out EVoiceChannel channel));
        Assert.Equal(EVoiceChannel.All, channel);
        Assert.True(gate.Step(s, 0.0f, false, true, out channel));
        Assert.Equal(EVoiceChannel.Team, channel);

        // The team key works in activation mode too.
        s.PushToTalk = false;
        Assert.True(gate.Step(s, 0.0f, false, true, out channel));
        Assert.Equal(EVoiceChannel.Team, channel);
    }

    [Fact]
    public void SelfMuteAndVoiceOffSendNothing()
    {
        var gate = new VoiceGate();
        Assert.False(Step(gate, new VoiceSettings { MicMuted = true, ActivationThreshold = 0f }, 1f, talk: true, team: true));
        Assert.False(Step(gate, new VoiceSettings { Enabled = false, ActivationThreshold = 0f }, 1f, talk: true));
    }
}

public sealed class VoiceTransportTests
{
    private sealed class Sink : IVoiceEndpoint
    {
        public readonly System.Collections.Generic.List<EndpointId> From = new();
        public int Dispatches;

        public void OnVoiceDatagram(EndpointId from, ReadOnlySpan<byte> datagram) => From.Add(from);

        public void OnDispatch() => Dispatches++;
    }

    private static readonly byte[] GamePacket = [0xA7, 1, 2, 3, 4, 5, 6, 7];

    [Fact]
    public void InsideAListenServersCompositeVoiceSeesTheDriversEndpointIds()
    {
        var clock = new VirtualClock();
        var sim = new SimLinkNetwork(clock, 1, SimLinkProfile.A);
        using LoopbackTransport loopback = new LoopbackNetwork().Bind("listen-server");
        var remote = new VoiceTransport(sim.Bind("host"), compositeIndex: 1);
        using var composite = new CompositeTransport(loopback, remote);
        var sink = new Sink();
        remote.Voice = sink;
        using SimLinkTransport client = sim.Bind("client");
        EndpointId toHost = client.Connect("host");

        var join = new byte[VoiceWire.MaxDatagramBytes];
        int joinLength = VoiceWire.WriteJoin(join, 0, 0, true, []);
        Assert.True(client.Send(toHost, join.AsSpan(0, joinLength)));
        Assert.True(client.Send(toHost, GamePacket));

        // The driver reads the game packet through the composite; the voice datagram went to the sink on the way, under
        // the same id the driver sees.
        var buffer = new byte[1500];
        Assert.True(composite.TryReceive(buffer, out EndpointId driverId, out int length));
        Assert.Equal(GamePacket.Length, length);
        Assert.False(composite.TryReceive(buffer, out _, out _));
        Assert.Equal([driverId], sink.From);
        Assert.True(sink.Dispatches >= 1);

        // Both directions reach the client: voice through SendVoice, game packets through the composite.
        Assert.True(remote.SendVoice(driverId, join.AsSpan(0, joinLength)));
        Assert.True(composite.Send(driverId, GamePacket));
        Assert.True(client.TryReceive(buffer, out _, out length));
        Assert.True(VoiceWire.IsVoice(buffer.AsSpan(0, length)));
        Assert.True(client.TryReceive(buffer, out _, out length));
        Assert.Equal(0xA7, buffer[0]);

        // An id of another transport of the composite is not this one's.
        Assert.False(remote.SendVoice(new EndpointId((1 << 3) | 0), join.AsSpan(0, joinLength)));
    }
}
