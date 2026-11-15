using System;
using System.IO;
using System.IO.Compression;
using OpenScct.Net.Scoreboards;

namespace OpenScctGame.Web;

/// <summary>The same Brotli wire on a runtime without the native compression library.</summary>
internal static class BrowserScoreboardCodec
{
    internal static Stream Compress(Stream output)
    {
        var stream = new BrotliSharpLib.BrotliStream(output, CompressionMode.Compress, leaveOpen: true);
        stream.SetQuality(4);
        stream.SetWindow(18);
        return stream;
    }

    internal static Stream Decompress(Stream input) => new BrotliSharpLib.BrotliStream(input, CompressionMode.Decompress);

    internal static void Prepare()
    {
        ScoreboardWire.OpenCompressionStream = Compress;
        ScoreboardWire.OpenDecompressionStream = Decompress;
        // Initialize the managed codec and serializer under the loading cover, before any local-host tick.
        var sample = new RoundScoreboard
        {
            RoundId = Guid.Empty.ToString(), Map = "Warmup", Server = "Warmup",
            Rows = [new ScoreboardPlayer { ParticipantId = 1, Name = "Warmup", Team = 0 }],
        };
        var receiver = new ScoreboardWire.Receiver(Decompress);
        RoundScoreboard? decoded = null;
        foreach (string line in ScoreboardWire.Encode(sample, 1))
            if (receiver.Receive(line, out var board, out _, out _) && board is not null) decoded = board;
        if (decoded is null) throw new InvalidDataException("Browser scoreboard compression could not initialize.");
    }
}
