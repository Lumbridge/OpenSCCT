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

    [Fact]
    public void CheckpointSeekReplaysTheBootstrapThenResumesAtTheExactPacketCursor()
    {
        DemoRecording recording = Write(writer =>
        {
            writer.Write(10, [1]);
            writer.Write(100, [2]);
            Assert.True(writer.TryWriteCheckpoint(100, sink =>
            {
                writer.Write(100, [3]); // primary packets flushed during capture belong to the saved state
                sink.Write(100, [21]);
                sink.Write(100, [22]);
                return [23];
            }));
            writer.Write(100, [4]);
            writer.Write(170, [5]);
            Assert.True(writer.TryWriteCheckpoint(200, sink =>
            {
                sink.Write(200, [24]);
                return [25];
            }));
            writer.Write(300, [6]);
        });
        Assert.Equal(2, DemoFile.ReadInfo(_path).Index.CheckpointCount);
        Assert.Equal(6, recording.PacketCount);
        Assert.Throws<InvalidDataException>(() => DemoFile.ReadInfo(_path, new DemoReadLimits { MaxCheckpoints = 1 }));
        Assert.Throws<InvalidDataException>(() => DemoFile.ReadInfo(_path, new DemoReadLimits { MaxCheckpointExpandedBytes = 6 }));

        using var playback = new DemoPlaybackTransport(recording, seekTo: 150);
        Assert.Equal(100u, playback.StartTick);
        Assert.Equal(100u, playback.ReleasedTick);
        Assert.False(playback.TryTakeCheckpointContinuation(out _));
        Assert.Equal(21, ReadPacket(playback));
        Assert.False(playback.TryTakeCheckpointContinuation(out _));
        Assert.Equal(22, ReadPacket(playback));
        Assert.Equal(-1, ReadPacket(playback));
        Assert.True(playback.TryTakeCheckpointContinuation(out var continuation));
        Assert.Equal(new byte[] { 23 }, continuation.ToArray());
        Assert.False(playback.TryTakeCheckpointContinuation(out _));
        Assert.Equal(4, ReadPacket(playback)); // same tick, after the captured cursor
        Assert.Equal(-1, ReadPacket(playback));
        playback.ReleaseUpTo(300);
        Assert.Equal(5, ReadPacket(playback));
        Assert.Equal(6, ReadPacket(playback));
        Assert.True(playback.AtEnd);

        using var later = new DemoPlaybackTransport(recording, seekTo: 200);
        Assert.Equal(200u, later.StartTick);
        Assert.Equal(24, ReadPacket(later));
        Assert.True(later.TryTakeCheckpointContinuation(out _));
        Assert.Equal(-1, ReadPacket(later));

        using var linear = new DemoPlaybackTransport(recording);
        linear.ReleaseUpTo(300);
        Assert.Equal(10u, linear.StartTick);
        for (int packet = 1; packet <= 6; packet++) Assert.Equal(packet, ReadPacket(linear));
        Assert.False(linear.TryTakeCheckpointContinuation(out _));
        Assert.True(linear.AtEnd);
    }

    [Fact]
    public void AbortedAndOversizedCheckpointsLeaveThePrimaryRecordingPlayable()
    {
        DemoRecording recording = Write(writer =>
        {
            writer.Write(10, [1]);
            Assert.False(writer.TryWriteCheckpoint(100, sink =>
            {
                sink.Write(100, [2]);
                return null;
            }));
            Assert.False(writer.TryWriteCheckpoint(100, sink =>
            {
                sink.Write(100, new byte[NetConnection.MaxPacketBytesLimit + 1]);
                return [3];
            }));
            writer.Write(120, [4]);
        });

        Assert.Equal(0, recording.Index.CheckpointCount);
        using var playback = new DemoPlaybackTransport(recording, seekTo: 110);
        Assert.Equal(10u, playback.StartTick);
        Assert.Equal(1, ReadPacket(playback));
        playback.ReleaseUpTo(120);
        Assert.Equal(4, ReadPacket(playback));
        Assert.True(playback.AtEnd);
    }

    [Fact]
    public void DamagedCheckpointPayloadFallsBackToThePrimaryRecording()
    {
        DemoRecording recording = Write(writer =>
        {
            writer.Write(10, [1]);
            writer.Write(100, [2]);
            Assert.True(writer.TryWriteCheckpoint(100, sink =>
            {
                sink.Write(100, [3]);
                return [4];
            }));
            writer.Write(120, [5]);
        });
        int compressedBytes = Assert.Single(recording.Index.Checkpoints).CompressedBytes;
        using (var file = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite))
        using (var reader = new BinaryReader(file))
        {
            file.Position = file.Length - DemoFile.TrailerBytes;
            long indexBytes = reader.ReadInt64();
            file.Position = file.Length - DemoFile.TrailerBytes - indexBytes - compressedBytes;
            file.Write(new byte[compressedBytes]);
        }

        using var playback = new DemoPlaybackTransport(DemoFile.Read(_path), seekTo: 100);
        Assert.Equal(10u, playback.StartTick);
        playback.ReleaseUpTo(120);
        Assert.Equal(1, ReadPacket(playback));
        Assert.Equal(2, ReadPacket(playback));
        Assert.Equal(5, ReadPacket(playback));
        Assert.True(playback.AtEnd);
    }

    private static int ReadPacket(DemoPlaybackTransport playback)
    {
        Span<byte> buffer = stackalloc byte[playback.MaxDatagramSize];
        if (!playback.TryReceive(buffer, out _, out int length)) return -1;
        Assert.Equal(1, length);
        return buffer[0];
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
