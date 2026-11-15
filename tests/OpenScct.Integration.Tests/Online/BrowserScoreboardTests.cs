using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using OpenScct.Net.Scoreboards;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class BrowserScoreboardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowserListenServerTransferIsReadByNativeAndBrowserClients(bool browserClient)
    {
        var expected = new RoundScoreboard
        {
            RoundId = Guid.NewGuid().ToString(), Map = "AquaE", Server = "Browser practice", Sequence = 9,
            ElapsedSeconds = 45, Rows = [new ScoreboardPlayer { ParticipantId = 1, Name = "Browser", Team = 1, Kills = 3 }],
        };
        var receiver = browserClient ? new ScoreboardWire.Receiver(OpenScctGame.Web.BrowserScoreboardCodec.Decompress)
            : new ScoreboardWire.Receiver();
        RoundScoreboard? received = null;
        foreach (string line in ScoreboardWire.Encode(expected, 1,
            openCompressionStream: OpenScctGame.Web.BrowserScoreboardCodec.Compress))
        {
            Assert.True(receiver.Receive(line, out var board, out _, out _));
            if (board is not null) received = board;
        }
        Assert.NotNull(received);
        Assert.Equal(expected.RoundId, received.RoundId);
        Assert.Equal(9, received.Sequence);
        Assert.Equal(3, Assert.Single(received.Rows).Kills);
    }

    [Fact]
    public void ManagedBrowserDecoderReadsTheUnchangedNativeServerTransfer()
    {
        var expected = new RoundScoreboard
        {
            RoundId = Guid.NewGuid().ToString(), Map = "SteeE", Server = "Native server", Sequence = 7,
            ElapsedSeconds = 90, Rows = [new ScoreboardPlayer { ParticipantId = 1, Name = "Browser", Team = 0, Kills = 2 }],
        };
        var receiver = new ScoreboardWire.Receiver(stream => new BrotliSharpLib.BrotliStream(stream, CompressionMode.Decompress));
        RoundScoreboard? received = null;
        int participant = 0;
        bool historical = false;
        foreach (string line in ScoreboardWire.Encode(expected, 1, historical: true))
        {
            Assert.True(receiver.Receive(line, out var board, out int local, out bool history));
            if (board is not null) { received = board; participant = local; historical = history; }
        }
        Assert.NotNull(received);
        Assert.Equal(expected.RoundId, received.RoundId);
        Assert.Equal(2, Assert.Single(received.Rows).Kills);
        Assert.Equal(1, participant);
        Assert.True(historical);
    }

    [Fact]
    public void ManagedDecoderStillRejectsExpandedPayloadsAboveTheWireLimit()
    {
        byte[] raw = Enumerable.Repeat((byte)'x', ScoreboardWire.MaxJsonBytes + 1).ToArray();
        using var packed = new MemoryStream();
        using (var compression = new BrotliStream(packed, CompressionLevel.Optimal, leaveOpen: true)) compression.Write(raw);
        byte[] bytes = packed.ToArray();
        string id = new('a', 32);
        var receiver = new ScoreboardWire.Receiver(stream => new BrotliSharpLib.BrotliStream(stream, CompressionMode.Decompress));
        foreach (string line in new[] { $"#svm boardbegin {id} {bytes.Length} 1 0",
            $"#svm boarddata {id} 0 {Convert.ToBase64String(bytes)}", $"#svm boardend {id}" })
        {
            Assert.True(receiver.Receive(line, out var board, out _, out _));
            Assert.Null(board);
        }
    }
}
