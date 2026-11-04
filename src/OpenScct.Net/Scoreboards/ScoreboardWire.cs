using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenScct.Net.Scoreboards;

/// <summary>Bounded scoreboard transfers on reliable control text. A receiver publishes only a complete snapshot.</summary>
public static class ScoreboardWire
{
    public const int MaxJsonBytes = 1024 * 1024;
    public const int MaxCompressedBytes = 262144;
    private const int ChunkBytes = 300;
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(ScoreboardPlayer)) return;
            foreach (var property in info.Properties)
                if (property.Name is "team" or "score" or "kills" or "deaths" or "lives" or "objectives" or "rank" or "pingMs" or "bot" or "leftEarly")
                    property.ShouldSerialize = (_, value) => value is not 0 and not false;
        });
        return new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = resolver
        };
    }

    public static IReadOnlyList<string> Encode(RoundScoreboard board, int localParticipantId, bool historical = false)
    {
        // Version 1 defines missing counters as zero. Keep the full server snapshot, but
        // avoid repeatedly transmitting dozens of zero counters for every participant.
        var wireBoard = board with
        {
            Rows = board.Rows.Select(row => row.StatsVersion == 1 && row.Stats is not null
                ? row with { Stats = row.Stats.Where(counter => counter.Value != 0).ToDictionary(counter => counter.Key, counter => counter.Value) }
                : row).ToArray(),
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(wireBoard, Json);
        if (json.Length > MaxJsonBytes) throw new InvalidDataException("Scoreboard exceeds the transfer limit.");
        using var packed = new MemoryStream();
        using (var compression = new BrotliStream(packed, CompressionLevel.Optimal, leaveOpen: true)) compression.Write(json);
        byte[] bytes = packed.ToArray();
        if (bytes.Length > MaxCompressedBytes) throw new InvalidDataException("Scoreboard exceeds the transfer limit.");
        string id = Guid.NewGuid().ToString("N");
        var lines = new List<string> { $"#svm boardbegin {id} {bytes.Length} {localParticipantId} {(historical ? 1 : 0)}" };
        for (int offset = 0; offset < bytes.Length; offset += ChunkBytes)
            lines.Add($"#svm boarddata {id} {offset} {Convert.ToBase64String(bytes, offset, Math.Min(ChunkBytes, bytes.Length - offset))}");
        lines.Add($"#svm boardend {id}");
        return lines;
    }

    public sealed class Receiver
    {
        private string? _id;
        private byte[]? _bytes;
        private int _used;
        private int _local;
        private bool _historical;
        public bool Receive(string? line, out RoundScoreboard? board, out int localParticipantId, out bool historical)
        {
            board = null;
            localParticipantId = -1;
            historical = false;
            if (line is null || !line.StartsWith("#svm board", StringComparison.Ordinal)) return false;
            if (line.Length > 511) { Clear(); return true; }
            string[] fields = line.Split(' ');
            try
            {
                if (fields.Length == 6 && fields[1] == "boardbegin" && fields[2].Length == 32 &&
                    int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int length) &&
                    length is > 0 and <= MaxCompressedBytes && int.TryParse(fields[4], out int local) && fields[5] is "0" or "1")
                {
                    _id = fields[2]; _bytes = new byte[length]; _used = 0; _local = local; _historical = fields[5] == "1";
                }
                else if (fields.Length == 5 && fields[1] == "boarddata" && fields[2] == _id && _bytes is not null &&
                    int.TryParse(fields[3], out int offset) && offset == _used)
                {
                    byte[] chunk = Convert.FromBase64String(fields[4]);
                    if (chunk.Length is <= 0 or > ChunkBytes || _used + chunk.Length > _bytes.Length) { Clear(); return true; }
                    chunk.CopyTo(_bytes, _used); _used += chunk.Length;
                }
                else if (fields.Length == 3 && fields[1] == "boardend" && fields[2] == _id && _bytes is not null && _used == _bytes.Length)
                {
                    using var input = new MemoryStream(_bytes);
                    using var decompression = new BrotliStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    byte[] buffer = new byte[4096];
                    int n;
                    while ((n = decompression.Read(buffer)) != 0)
                    {
                        if (output.Length + n > MaxJsonBytes) { Clear(); return true; }
                        output.Write(buffer, 0, n);
                    }
                    RoundScoreboard? decoded = JsonSerializer.Deserialize<RoundScoreboard>(output.ToArray(), Json);
                    if (Valid(decoded)) { board = decoded; localParticipantId = _local; historical = _historical; }
                    Clear();
                }
                else Clear();
            }
            catch (Exception error) when (error is FormatException or IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
            { Clear(); }
            return true;
        }

        private static bool Valid(RoundScoreboard? b) => b is { SchemaVersion: 1, RoundId.Length: > 0 and <= 128, Rows: not null, Map: not null, Server: not null } &&
            Guid.TryParse(b.RoundId, out _) &&
            b.Rows.Count <= 256 && b.Sequence >= 0 && double.IsFinite(b.ElapsedSeconds) && b.ElapsedSeconds >= 0 &&
            b.Rows.Select(r => r?.ParticipantId).Distinct().Count() == b.Rows.Count &&
            (b.AsOfSeconds is null || double.IsFinite(b.AsOfSeconds.Value) && b.AsOfSeconds >= 0 && b.AsOfSeconds <= b.ElapsedSeconds) &&
            b.Rows.All(r => r is not null && r.ParticipantId > 0 && r.Team is 0 or 1 && r.Name is { Length: <= 256 } && r.StatsVersion is 0 or 1 &&
                (r.Stats is null || r.Stats.Count <= 128 && r.Stats.All(s => s.Key.Length <= 96 && s.Value >= 0)));

        private void Clear() { _id = null; _bytes = null; _used = 0; }
    }
}
