using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenScct.Net;

/// <summary>Versioned, bounded JSONL telemetry alongside a demo. It is data for analysis, never replay input.</summary>
public static class TrainingDataFile
{
    public const string Extension = ".training.jsonl.gz";
    public const string Format = "OpenSCCTTraining";
    public const int Version = 1;
    public const long MaxCompressedBytes = 64L * 1024 * 1024;
    public const long MaxExpandedBytes = 256L * 1024 * 1024;
    public const int MaxLineBytes = 1024 * 1024;

    public static string PathForDemo(string demoPath) => System.IO.Path.ChangeExtension(demoPath, null) + Extension;

    /// <summary>Checks every bounded JSON line, its header and completion count; throws for an incomplete or invalid file.</summary>
    public static void Validate(string path, DemoHeader? expectedHeader = null)
    {
        using var file = File.OpenRead(path);
        if (file.Length is <= 0 or > MaxCompressedBytes) throw new InvalidDataException("Training data size is invalid.");
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var line = new MemoryStream();
        byte[] buffer = new byte[8192];
        long expanded = 0, records = 0;
        uint lastTick = 0;
        bool headerSeen = false, complete = false;
        int count;
        while ((count = gzip.Read(buffer)) > 0)
        {
            expanded += count;
            if (expanded > MaxExpandedBytes) throw new InvalidDataException("Training data exceeds its expanded size limit.");
            foreach (byte value in buffer.AsSpan(0, count))
            {
                if (value != (byte)'\n')
                {
                    if (line.Length >= MaxLineBytes) throw new InvalidDataException("Training data line is too long.");
                    line.WriteByte(value);
                    continue;
                }
                if (complete || line.Length == 0) throw new InvalidDataException("Unexpected training data after completion or empty line.");
                using JsonDocument document = JsonDocument.Parse(line.GetBuffer().AsMemory(0, (int)line.Length), new JsonDocumentOptions { MaxDepth = 16 });
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement) ||
                    typeElement.ValueKind != JsonValueKind.String || !root.TryGetProperty("tick", out var tickElement) ||
                    !tickElement.TryGetUInt32(out uint tick) || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Invalid training data record.");
                string type = typeElement.GetString()!;
                if (!headerSeen)
                {
                    if (type != "header" || tick != 0) throw new InvalidDataException("Training data header is missing.");
                    ValidateHeader(data, expectedHeader);
                    headerSeen = true;
                }
                else if (type == "complete")
                {
                    if (!data.TryGetProperty("records", out var recordCount) || !recordCount.TryGetInt64(out long total) || total != records ||
                        !data.TryGetProperty("complete", out var finished) || finished.ValueKind != JsonValueKind.True || tick != lastTick)
                        throw new InvalidDataException("Training data completion record is invalid.");
                    complete = true;
                }
                else
                {
                    if (!ValidType(type) || type == "header") throw new InvalidDataException("Invalid training data record type.");
                    records++;
                    lastTick = Math.Max(lastTick, tick);
                }
                line.SetLength(0);
            }
        }
        if (!headerSeen || !complete || line.Length != 0) throw new InvalidDataException("Training data is incomplete.");
    }

    private static void ValidateHeader(JsonElement data, DemoHeader? expected)
    {
        if (!data.TryGetProperty("format", out var format) || format.GetString() != Format ||
            !data.TryGetProperty("version", out var version) || !version.TryGetInt32(out int v) || v != Version ||
            !data.TryGetProperty("gameVersion", out var game) || game.ValueKind != JsonValueKind.String ||
            !data.TryGetProperty("protocolHash", out var protocol) || !ValidHash(protocol) ||
            !data.TryGetProperty("contentHash", out var content) || !ValidHash(content) ||
            !data.TryGetProperty("map", out var map) || map.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(map.GetString()) ||
            !data.TryGetProperty("tickRate", out var rate) || !rate.TryGetInt32(out int ticks) || ticks is <= 0 or > 1000 ||
            !data.TryGetProperty("netRate", out var net) || !net.TryGetInt32(out int netRate) || netRate < 0 ||
            !data.TryGetProperty("startUnixMs", out var start) || !start.TryGetInt64(out long startMs))
            throw new InvalidDataException("Training data header is invalid or unsupported.");
        if (expected is not null && (map.GetString() != expected.Map || ticks != expected.TickRate || startMs != expected.StartUnixMs ||
            !string.Equals(protocol.GetString(), expected.ProtocolHash.ToString("X16"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(content.GetString(), expected.ContentHash.ToString("X16"), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Training data does not belong to this demo.");
    }

    private static bool ValidHash(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { Length: 16 } text) return false;
        foreach (char c in text) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    internal static bool ValidType(string type)
    {
        if (string.IsNullOrEmpty(type) || type.Length > 64) return false;
        foreach (char c in type) if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')) return false;
        return true;
    }

    internal static JsonSerializerOptions JsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (typeof(OpenScct.Core.UObject).IsAssignableFrom(info.Type) ||
                (!info.Type.IsEnum && info.Type.Namespace?.StartsWith("OpenScct.Core", StringComparison.Ordinal) == true))
                throw new NotSupportedException("Training records require primitive DTOs, not engine objects.");
        });
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 16, TypeInfoResolver = resolver };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    // DTO property getters/converters can throw arbitrary exceptions while serializing. Keep those failures
    // inside telemetry too; fatal runtime failures are not safe to recover from here.
    internal static bool TelemetryFailure(Exception error) => error is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    internal sealed class LimitedWriteStream(Stream inner, long limit) : Stream
    {
        private long _written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > limit - _written) throw new InvalidDataException("Training data size limit reached.");
            inner.Write(buffer);
            _written += buffer.Length;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>Best-effort training telemetry. Failures abandon only this sidecar, leaving demo recording and play intact.</summary>
public sealed class TrainingDataWriter : IDisposable
{
    private static readonly JsonSerializerOptions Options = TrainingDataFile.JsonOptions();
    private FileStream? _file;
    private GZipStream? _gzip;
    private string? _temporary;
    private long _expanded, _records;
    private uint _lastTick;
    public string Path { get; private set; } = string.Empty;
    public string? Error { get; private set; }
    public bool IsFinished { get; private set; }

    public TrainingDataWriter(string demoPath, DemoHeader header)
    {
        try
        {
            Path = TrainingDataFile.PathForDemo(demoPath);
            _temporary = Path + ".partial-" + Guid.NewGuid().ToString("N");
            _file = new FileStream(_temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            _gzip = new GZipStream(new TrainingDataFile.LimitedWriteStream(_file, TrainingDataFile.MaxCompressedBytes), CompressionLevel.Fastest, leaveOpen: true);
            Write("header", 0, new { format = TrainingDataFile.Format, version = TrainingDataFile.Version, header.GameVersion,
                protocolHash = header.ProtocolHash.ToString("X16"), contentHash = header.ContentHash.ToString("X16"), header.Map,
                header.GameOptions, header.ServerName, header.StartUnixMs, header.TickRate, header.NetRate });
        }
        catch (Exception error) when (TrainingDataFile.TelemetryFailure(error)) { Fail(error); }
    }

    public bool TryWrite(string type, uint tick, object data)
    {
        if (_gzip is null || IsFinished || Error is not null) return false;
        try
        {
            if (!TrainingDataFile.ValidType(type) || type is "header" or "complete" || data is null)
                throw new ArgumentException("Invalid training data record.");
            if (Options.GetTypeInfo(data.GetType()).Kind is not (JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary))
                throw new ArgumentException("Training record data must be a primitive DTO object.");
            Write(type, tick, data);
            _records++;
            _lastTick = Math.Max(_lastTick, tick);
            return true;
        }
        catch (Exception error) when (TrainingDataFile.TelemetryFailure(error)) { Fail(error); return false; }
    }

    private void Write(string type, uint tick, object data)
    {
        using var bytes = new MemoryStream();
        using var bounded = new TrainingDataFile.LimitedWriteStream(bytes, TrainingDataFile.MaxLineBytes);
        JsonSerializer.Serialize(bounded, new { type, tick, data }, Options);
        if (bytes.Length + 1 > TrainingDataFile.MaxExpandedBytes - _expanded) throw new InvalidDataException("Training data expanded size limit reached.");
        _gzip!.Write(bytes.GetBuffer().AsSpan(0, (int)bytes.Length));
        _gzip.WriteByte((byte)'\n');
        _expanded += bytes.Length + 1;
    }

    public bool Finish()
    {
        if (IsFinished) return true;
        if (_gzip is null || Error is not null) return false;
        try
        {
            Write("complete", _lastTick, new { complete = true, records = _records });
            _gzip.Dispose();
            _gzip = null;
            _file!.Flush(flushToDisk: true);
            _file.Dispose();
            _file = null;
            File.Move(_temporary!, Path, overwrite: false);
            _temporary = null;
            IsFinished = true;
            return true;
        }
        catch (Exception error) when (TrainingDataFile.TelemetryFailure(error)) { Fail(error); return false; }
    }

    private void Fail(Exception error) { Error = error.Message; Dispose(); }

    public void Dispose()
    {
        try { _gzip?.Dispose(); } catch (Exception error) when (TrainingDataFile.TelemetryFailure(error)) { Error ??= error.Message; }
        _gzip = null;
        try { _file?.Dispose(); } catch (Exception error) when (TrainingDataFile.TelemetryFailure(error)) { Error ??= error.Message; }
        _file = null;
        if (_temporary is not null)
        {
            try { File.Delete(_temporary); } catch (Exception error) when (TrainingDataFile.TelemetryFailure(error)) { Error ??= error.Message; }
            _temporary = null;
        }
    }
}
