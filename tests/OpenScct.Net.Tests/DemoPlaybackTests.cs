using System;
using System.IO;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class DemoPlaybackTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "openscct-playback-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);

    [Fact]
    public void PlaybackConsumesReliableRepliesWithoutResendingOrTimingOut()
    {
        DemoRecording recording = Write(writer => writer.Index.LastTick = 60 * 600);
        using var transport = new DemoPlaybackTransport(recording);
        using var driver = new NetDriver(transport, new NetClassRegistry(), new IgnoreMessages(), isServer: false);
        NetConnection connection = driver.Connect("demo", 0);
        connection.SetOpen();

        // The spectator sends a reliable target-selection RPC every second. A recording cannot acknowledge it:
        // these used to fill the 128-bunch resend buffer and destroy all replay actors after about two minutes.
        for (int second = 0; second < 600; second++)
        {
            long now = second * 1_000_000L;
            driver.TickDispatch(now);
            connection.ControlChannel!.Send(ControlMessage.TextLine("spectator reply"));
            driver.TickFlush(now);
            Assert.Equal(EConnectionState.USOCK_Open, connection.State);
            Assert.Equal(0, connection.ReliableBunchesInFlight);
            Assert.Equal(0, connection.PacketsInFlight);
        }

        Assert.Equal(0, connection.Stats.OutReliableResends);
        Assert.Equal(0, connection.Stats.OutPacketsLost);
        Assert.Equal(0, connection.OutLoss);
        Assert.Null(connection.CloseReason);
    }

    [Fact]
    public void PlayableStartSkipsLoadingButKeepsItsPacketsForReconstruction()
    {
        DemoRecording recording = Write(writer =>
        {
            writer.Write(10, [1]);
            writer.Write(20, [2]);
            writer.Write(300, [3]);
            writer.Mark(DemoMarkerKind.RoundStart, "Later round", 250);
            writer.Mark(DemoMarkerKind.RoundStart, "First round", 100);
            writer.Mark(DemoMarkerKind.RoundStart, "Outside recording", 1);
        });
        Assert.Equal(100u, recording.PlaybackFirstTick);
        Assert.Equal(10u, recording.Index.FirstTick);

        using var transport = new DemoPlaybackTransport(recording);
        transport.ReleaseUpTo(recording.PlaybackFirstTick);
        Span<byte> buffer = stackalloc byte[transport.MaxDatagramSize];
        Assert.True(transport.TryReceive(buffer, out _, out int length));
        Assert.Equal(1, length);
        Assert.Equal(1, buffer[0]);
        Assert.True(transport.TryReceive(buffer, out _, out length));
        Assert.Equal(1, length);
        Assert.Equal(2, buffer[0]);
        Assert.False(transport.TryReceive(buffer, out _, out _));
    }

    [Fact]
    public void RecordingStartedMidRoundKeepsItsOriginalStart()
    {
        DemoRecording recording = Write(writer =>
        {
            writer.Write(500, [1]);
            writer.Write(900, [2]);
            writer.Mark(DemoMarkerKind.Kill, "Death", 600);
            writer.Mark(DemoMarkerKind.RoundStart, "Outside recording", 950);
        });
        Assert.Equal(500u, recording.PlaybackFirstTick);
    }

    private DemoRecording Write(Action<DemoWriter> fill)
    {
        using (var writer = new DemoWriter(_path, new DemoHeader()))
        {
            fill(writer);
            writer.Finish();
        }
        return DemoFile.Read(_path);
    }

    public void Dispose() => File.Delete(_path);

    private sealed class IgnoreMessages : INetworkNotify
    {
        public void NotifyControlMessage(NetConnection connection, in ControlMessage message) { }
    }
}
