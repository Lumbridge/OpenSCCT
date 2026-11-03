using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class DemoReadLimitsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "openscct-demo-limits-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);

    [Fact]
    public void ExistingVersionOneRecordingsStillRoundTripAtTheConfiguredLimits()
    {
        Write(writer => { writer.Write(30, [1, 2, 3]); writer.Write(60, [4, 5]); });
        DemoRecording recording = DemoFile.Read(_path, new DemoReadLimits { MaxPackets = 2, MaxPacketBytes = 3, MaxExpandedBytes = 9 });
        Assert.Equal(2, recording.PacketCount);
        Assert.Equal(30u, recording.TickOf(0));
        Assert.Equal(60u, recording.TickOf(1));
        Assert.Equal(new byte[] { 4, 5 }, recording.Packet(1).ToArray());
    }

    [Theory]
    [InlineData("string")]
    [InlineData("players")]
    [InlineData("markers")]
    [InlineData("index")]
    [InlineData("negative-count")]
    [InlineData("excessive-count")]
    [InlineData("excessive-raw")]
    [InlineData("rate")]
    [InlineData("version")]
    public void MetadataIsBoundedEvenWhenOnlyListingDemos(string invalid)
    {
        var limits = new DemoReadLimits { MaxStringBytes = 32, MaxPlayers = 1, MaxMarkers = 1, MaxPackets = 10, MaxExpandedBytes = 100 };
        using (var writer = new DemoWriter(_path, new DemoHeader
        {
            ServerName = invalid == "string" ? new string('x', 33) : "Server",
            TickRate = invalid == "rate" ? 0 : 60,
        }))
        {
            if (invalid == "players") { writer.NotePlayer(1, "A", 0); writer.NotePlayer(2, "B", 1); }
            if (invalid == "markers") { writer.Mark(DemoMarkerKind.Kill, "A"); writer.Mark(DemoMarkerKind.Kill, "B"); }
            if (invalid == "negative-count") writer.Index.Packets = -1;
            if (invalid == "excessive-count") writer.Index.Packets = long.MaxValue;
            if (invalid == "excessive-raw") writer.Index.RawBytes = long.MaxValue;
            writer.Finish();
        }
        if (invalid == "index") limits = limits with { MaxIndexBytes = 1 };
        if (invalid == "version")
        {
            using var file = File.OpenWrite(_path);
            file.Position = 8;
            file.Write([0, 0]);
        }
        Assert.Throws<InvalidDataException>(() => DemoFile.ReadInfo(_path, limits));
    }

    [Fact]
    public void ExpansionStopsWhenCompressedFramesExceedTheirDeclaredBound()
    {
        Write(writer => { writer.Write(0, new byte[100_000]); writer.Index.RawBytes = 1; });
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(_path));
    }

    [Fact]
    public void PacketSizeLimitAppliesToActualDecodedFrames()
    {
        Write(writer => writer.Write(0, new byte[100]));
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(_path, new DemoReadLimits { MaxPacketBytes = 99 }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrameTotalsMustMatchTheIndex(bool packetCount)
    {
        Write(writer =>
        {
            writer.Write(0, [1, 2, 3]);
            if (packetCount) writer.Index.Packets++;
            else writer.Index.RawBytes++;
        });
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(_path));
    }

    [Theory]
    [InlineData("varint")]
    [InlineData("tick")]
    [InlineData("packet")]
    [InlineData("truncated")]
    public void MalformedFrameArithmeticIsRejected(string invalid)
    {
        byte[] frames = invalid switch
        {
            "varint" => [0xff, 0xff, 0xff, 0xff, 0x1f, 0],
            "tick" => [2, 0],
            "packet" => [0, 0xff, 0xff, 0xff, 0xff, 0x0f],
            _ => [0, 5, 1],
        };
        // Minimal version-one fixture. The index remains small and valid; only its packet framing is malformed.
        using (var file = File.Create(_path))
        using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("SCCTDEMO"u8); writer.Write((ushort)1);
            writer.Write(""); writer.Write(0UL); writer.Write(0UL);
            writer.Write(""); writer.Write(""); writer.Write(""); writer.Write(0L); writer.Write(60); writer.Write(0);
            using (var compressed = new BrotliStream(file, CompressionLevel.Optimal, leaveOpen: true)) compressed.Write(frames);
            long indexStart = file.Position;
            writer.Write(invalid == "tick" ? uint.MaxValue - 1 : 0u); writer.Write(uint.MaxValue);
            writer.Write(1L); writer.Write(invalid == "truncated" ? 5L : 0L);
            writer.Write7BitEncodedInt(0); writer.Write7BitEncodedInt(0);
            long indexLength = file.Position - indexStart;
            writer.Write(indexLength); writer.Write("SCCTDEND"u8);
        }
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(_path));
    }

    private void Write(Action<DemoWriter> fill)
    {
        using var writer = new DemoWriter(_path, new DemoHeader());
        fill(writer);
        writer.Finish();
    }

    public void Dispose() => File.Delete(_path);
}
