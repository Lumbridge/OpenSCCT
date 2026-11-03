using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class DemoViewTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "openscct-demo-view-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);

    private static DemoViewFrame View(uint tick, float x, int pawn = 8) => new(tick, 7, pawn,
        new FVector(x, 12, 45), new FRotator(100, 65000, 0), 25f,
        new FRotator(10, 20, 0), .5f, .1f, true);

    [Fact]
    public void CameraViewRoundTripsAndSeekingInterpolatesTheCorrectPlayerWithoutChangingPackets()
    {
        using (var writer = new DemoWriter(_path, new DemoHeader()))
        {
            writer.Write(10, [1, 2]);
            writer.WriteView(View(10, 100));
            writer.WriteView(View(16, 160) with { Rotation = new FRotator(100, 536, 0), Fov = 15f });
            writer.Write(20, [3]);
            writer.Finish();
        }
        DemoRecording recording = DemoFile.Read(_path);
        Assert.Equal(2, recording.Header.FormatVersion);
        Assert.Equal(2, recording.Index.ViewFrames);
        Assert.Equal(new byte[] { 1, 2 }, recording.Packet(0).ToArray());
        Assert.Equal(new byte[] { 3 }, recording.Packet(1).ToArray());
        Assert.True(recording.TryView(7, 16, out var last));
        Assert.Equal(160f, last.Location.X);
        Assert.True(recording.TryView(7, 13, out var middle));
        Assert.Equal(130f, middle.Location.X);
        Assert.Equal(20f, middle.Fov);
        Assert.Equal(0, middle.Rotation.Yaw & 0xffff);
        Assert.True(middle.SpyFirstPerson);
        Assert.Equal(.5f, middle.Breath);
        Assert.Equal(.1f, middle.Accuracy);
        Assert.False(recording.TryView(99, 13, out _));
        Assert.False(recording.TryView(7, 9, out _));
        Assert.False(recording.TryView(7, 100, out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CameraCutsAndRespawnsDoNotBlendAcrossTheTransition(bool respawn)
    {
        using (var writer = new DemoWriter(_path, new DemoHeader()))
        {
            writer.WriteView(View(10, 100));
            writer.WriteView(View(16, respawn ? 160 : 3000, respawn ? 9 : 8));
            writer.Finish();
        }
        var recording = DemoFile.Read(_path);
        Assert.True(recording.TryView(7, 13, out var before));
        Assert.Equal(100f, before.Location.X);
        Assert.True(recording.TryView(7, 16, out var after));
        Assert.Equal(respawn ? 160f : 3000f, after.Location.X);
    }

    [Fact]
    public void CameraDataHasIndependentFrameLimitsAndRejectsNonFiniteViews()
    {
        using (var writer = new DemoWriter(_path, new DemoHeader()))
        {
            writer.WriteView(View(1, float.NaN));
            writer.WriteView(View(1, 100) with { Fov = 0 });
            writer.WriteView(View(1, 100));
            writer.WriteView(View(2, 120));
            writer.Finish();
        }
        Assert.Equal(2, DemoFile.ReadInfo(_path).Index.ViewFrames);
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(_path, new DemoReadLimits { MaxViewFrames = 1 }));
        Assert.True(DemoViewFrame.TryDecode(View(1, 100).Encode(), out var decoded));
        Assert.Equal(View(1, 100), decoded);
        Assert.False(DemoViewFrame.TryDecode("invalid", out _));
        Assert.False(DemoViewFrame.TryDecode(new string('x', 10000), out _));
        Assert.False(DemoViewFrame.TryDecode((View(1, 0) with { Accuracy = float.PositiveInfinity }).Encode(), out _));
    }

    [Fact]
    public void RecordedViewsCannotDeclareExtraFramesOrUnboundedPlayers()
    {
        using (var writer = new DemoWriter(_path, new DemoHeader()))
        {
            writer.WriteView(View(1, 100));
            writer.WriteView(View(2, 120) with { PlayerId = 8 });
            writer.Finish();
        }
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(_path, new DemoReadLimits { MaxPlayers = 1 }));
        using (var file = File.OpenWrite(_path))
        using (var writer = new BinaryWriter(file))
        {
            file.Position = file.Length - 16 - 12; // version 2 index: view count then compressed length
            writer.Write(3);
        }
        Assert.ThrowsAny<IOException>(() => DemoFile.Read(_path));
    }

    [Fact]
    public void RemoteCameraModeIsRetainedAndDoesNotBlendIntoThePlayersBodyView()
    {
        using (var writer = new DemoWriter(_path, new DemoHeader()))
        {
            writer.WriteView(View(10, 100));
            writer.WriteView(View(16, 160) with { FirstPersonBody = false, ViewSource = 3 });
            writer.Finish();
        }
        var recording = DemoFile.Read(_path);
        Assert.True(recording.TryView(7, 13, out var body));
        Assert.Equal(100f, body.Location.X);
        Assert.True(body.FirstPersonBody);
        Assert.True(recording.TryView(7, 16, out var remote));
        Assert.False(remote.FirstPersonBody);
        Assert.Equal(3, remote.ViewSource);
    }

    [Fact]
    public void TheLastRecordedViewCannotBeTruncatedByAnEarlierPacketOrLieBeyondTheDeclaredTimeline()
    {
        using (var writer = new DemoWriter(_path, new DemoHeader()))
        {
            writer.Write(10, [1]);
            writer.WriteView(View(16, 100));
            writer.Write(12, [2]);
            writer.Finish();
        }
        var recording = DemoFile.Read(_path);
        Assert.Equal(16u, recording.Index.LastTick);
        Assert.True(recording.TryView(7, 16, out _));
        using (var file = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite))
        using (var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true))
        using (var writer = new BinaryWriter(file))
        {
            file.Position = file.Length - 16;
            long indexBytes = reader.ReadInt64();
            file.Position = file.Length - 16 - indexBytes + sizeof(uint);
            writer.Write(12u);
        }
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(_path));
    }

    [Fact]
    public void LegacyVersionOneFilesUseTheExistingPawnViewFallback()
    {
        using (var file = File.Create(_path))
        using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("SCCTDEMO"u8); writer.Write((ushort)1);
            writer.Write(""); writer.Write(0UL); writer.Write(0UL);
            writer.Write(""); writer.Write(""); writer.Write(""); writer.Write(0L); writer.Write(60); writer.Write(0);
            using (var compressed = new BrotliStream(file, CompressionLevel.Optimal, leaveOpen: true)) compressed.Write([0, 1, 9]);
            long start = file.Position;
            writer.Write(20u); writer.Write(20u); writer.Write(1L); writer.Write(1L);
            writer.Write7BitEncodedInt(0); writer.Write7BitEncodedInt(0);
            writer.Write(file.Position - start); writer.Write("SCCTDEND"u8);
        }
        var recording = DemoFile.Read(_path);
        Assert.Equal(1, recording.Header.FormatVersion);
        Assert.Single(recording.Packet(0).ToArray());
        Assert.Equal(9, recording.Packet(0)[0]);
        Assert.False(recording.TryView(7, 20, out _));
    }

    public void Dispose() => File.Delete(_path);
}
