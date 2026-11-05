using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenScct.Net.Scoreboards;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class DemoScoreboardTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "openscct-demo-scoreboard-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);

    private static RoundScoreboard Board() => new()
    {
        RoundId = Guid.NewGuid().ToString("N"), Completed = true, Map = "TestBox", Mode = 5, Winner = 0,
        ElapsedSeconds = 60, AsOfSeconds = 60,
        Rows = [new ScoreboardPlayer { ParticipantId = 7, Name = "Agent", Team = 0,
            Stats = new Dictionary<string, int> { ["spy.neutralisations"] = 3 } }],
    };

    [Fact]
    public void Current_format_lists_round_identity_and_final_stats_without_replaying_packets()
    {
        var board = Board();
        using (var writer = new DemoWriter(_path, new DemoHeader { RoundId = board.RoundId, Map = board.Map }))
        {
            writer.Write(10, [1, 2]);
            writer.NotePlayer(7, "Agent", 0);
            writer.Index.Scoreboard = board;
            writer.Finish();
        }
        var info = DemoFile.ReadInfo(_path);
        Assert.Equal(DemoFile.Version, info.Header.FormatVersion);
        Assert.Equal(board.RoundId, info.Header.RoundId);
        Assert.Equal(board.RoundId, info.Index.Scoreboard!.RoundId);
        Assert.Equal(3, Assert.Single(info.Index.Scoreboard.Rows).Stats!["spy.neutralisations"]);
        Assert.Equal(new byte[] { 1, 2 }, DemoFile.Read(_path).Packet(0).ToArray());
    }

    [Fact]
    public void Version_three_scoreboard_remains_readable_without_checkpoints()
    {
        var board = Board();
        WriteFixture(3, board.RoundId, JsonSerializer.SerializeToUtf8Bytes(board));
        var recording = DemoFile.Read(_path);
        Assert.Equal(3, recording.Header.FormatVersion);
        Assert.Equal(board.RoundId, recording.Index.Scoreboard!.RoundId);
        Assert.Equal(0, recording.Index.CheckpointCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Legacy_container_versions_remain_readable_without_invented_scoreboard_data(int version)
    {
        WriteFixture((ushort)version, "", null);
        var info = DemoFile.ReadInfo(_path);
        Assert.Equal(version, info.Header.FormatVersion);
        Assert.Equal(string.Empty, info.Header.RoundId);
        Assert.Null(info.Index.Scoreboard);
        var recording = DemoFile.Read(_path);
        Assert.Equal(9, Assert.Single(recording.Packet(0).ToArray()));
        Assert.Equal(0, recording.Index.ViewFrames);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("schema")]
    [InlineData("rows")]
    [InlineData("round")]
    [InlineData("player")]
    [InlineData("stats")]
    [InlineData("length")]
    [InlineData("truncated")]
    public void Invalid_or_unbounded_scoreboard_metadata_is_rejected_while_listing(string problem)
    {
        var board = Board();
        var bad = problem switch
        {
            "schema" => board with { SchemaVersion = 2 },
            "rows" => board with { Rows = null! },
            "round" => board with { RoundId = "invalid" },
            "player" => board with { Rows = [board.Rows[0] with { Name = new string('x', 33) }] },
            "stats" => board with { Rows = [board.Rows[0] with { Stats = Enumerable.Range(0, 257).ToDictionary(i => "stat" + i, _ => 1) }] },
            _ => board,
        };
        byte[] bytes = problem == "json" ? "["u8.ToArray() : JsonSerializer.SerializeToUtf8Bytes(bad);
        WriteFixture(3, board.RoundId, bytes, problem == "length" ? 1024 * 1024 + 1 : problem == "truncated" ? bytes.Length + 5 : null);
        Assert.Throws<InvalidDataException>(() => DemoFile.ReadInfo(_path, new DemoReadLimits { MaxStringBytes = 32 }));
    }

    [Fact]
    public void Scoreboard_round_identity_must_match_the_recording_header()
    {
        var board = Board();
        WriteFixture(3, Guid.NewGuid().ToString("N"), JsonSerializer.SerializeToUtf8Bytes(board));
        Assert.Throws<InvalidDataException>(() => DemoFile.ReadInfo(_path));
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(_path));
    }

    [Fact]
    public void Scoreboard_respects_the_readers_player_limit()
    {
        var board = Board();
        board = board with { Rows = [board.Rows[0], board.Rows[0] with { ParticipantId = 8 }] };
        WriteFixture(3, board.RoundId, JsonSerializer.SerializeToUtf8Bytes(board));
        Assert.Throws<InvalidDataException>(() => DemoFile.ReadInfo(_path, new DemoReadLimits { MaxPlayers = 1 }));
    }

    private void WriteFixture(ushort version, string roundId, byte[]? scoreboard, int? declaredLength = null)
    {
        using var file = File.Create(_path);
        using var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
        writer.Write("SCCTDEMO"u8); writer.Write(version);
        writer.Write(""); writer.Write(0UL); writer.Write(0UL);
        writer.Write("TestBox"); writer.Write(""); writer.Write(""); writer.Write(0L); writer.Write(60); writer.Write(0);
        if (version >= 3) writer.Write(roundId);
        using (var compressed = new BrotliStream(file, CompressionLevel.Optimal, leaveOpen: true)) compressed.Write([0, 1, 9]);
        long start = file.Position;
        writer.Write(10u); writer.Write(10u); writer.Write(1L); writer.Write(1L);
        writer.Write7BitEncodedInt(0); writer.Write7BitEncodedInt(0);
        if (version >= 2) { writer.Write(0); writer.Write(0L); }
        if (version >= 3)
        {
            writer.Write7BitEncodedInt(declaredLength ?? scoreboard?.Length ?? 0);
            if (scoreboard is not null) writer.Write(scoreboard);
        }
        writer.Write(file.Position - start); writer.Write("SCCTDEND"u8);
    }

    public void Dispose() => File.Delete(_path);
}
