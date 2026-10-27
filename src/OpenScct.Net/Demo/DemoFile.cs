using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OpenScct.Net;

/// <summary>Takes the packets of a demo recorder connection (<see cref="NetDriver.OpenRecorderConnection"/>).</summary>
public interface IDemoSink
{
    /// <summary>One packet the server sent at server tick <paramref name="tick"/>.</summary>
    void Write(uint tick, ReadOnlySpan<byte> datagram);
}

/// <summary>What a demo was recorded from (the header of a .scctdemo file, docs/DEMOS.md).</summary>
public sealed record DemoHeader
{
    /// <summary>The game build that recorded it (for the list; playback checks the hashes).</summary>
    public string GameVersion { get; init; } = string.Empty;

    /// <summary>The replication protocol (<see cref="NetDriver.ProtocolHash"/>): playback needs the same.</summary>
    public ulong ProtocolHash { get; init; }

    /// <summary>The map's content hash: playback needs the same map files.</summary>
    public ulong ContentHash { get; init; }

    public string Map { get; init; } = string.Empty;

    /// <summary>The game rules' URL options (mode, time limit, lives).</summary>
    public string GameOptions { get; init; } = string.Empty;

    public string ServerName { get; init; } = string.Empty;

    /// <summary>When recording started, in Unix milliseconds (UTC).</summary>
    public long StartUnixMs { get; init; }

    /// <summary>World ticks per second (60).</summary>
    public int TickRate { get; init; } = 60;

    /// <summary>The server's network frames per second while recording.</summary>
    public int NetRate { get; init; }
}

/// <summary>A player seen in a demo.</summary>
public readonly record struct DemoPlayer(int PlayerId, string Name, int Team);

public enum DemoMarkerKind : byte
{
    RoundStart = 0,
    RoundEnd = 1,
    Kill = 2,
    Objective = 3,
    MatchEnd = 4,
}

/// <summary>A point on the seek bar: a round start, a kill, an objective.</summary>
public readonly record struct DemoMarker(uint Tick, DemoMarkerKind Kind, string Text);

/// <summary>The footer of a demo: length, players and markers.</summary>
public sealed class DemoIndex
{
    public uint FirstTick { get; set; }

    public uint LastTick { get; set; }

    public long Packets { get; set; }

    public long RawBytes { get; set; }

    public List<DemoPlayer> Players { get; } = new();

    public List<DemoMarker> Markers { get; } = new();

    public uint DurationTicks => LastTick >= FirstTick ? LastTick - FirstTick : 0;
}

/// <summary>
/// The .scctdemo file (docs/DEMOS.md): magic and version, the header, the packets as a Brotli stream of
/// (tick delta, length, bytes) frames, then the index and a trailer (index length, end magic) so a reader finds
/// the index from the end. Little endian, strings UTF-8 with a 7-bit length.
/// </summary>
public static class DemoFile
{
    public const string Extension = ".scctdemo";

    /// <summary>Format version; readers refuse newer files.</summary>
    public const ushort Version = 1;

    private static readonly byte[] Magic = "SCCTDEMO"u8.ToArray();
    private static readonly byte[] EndMagic = "SCCTDEND"u8.ToArray();
    private const int TrailerBytes = 8 + 8;

    internal static void WriteHeader(BinaryWriter w, DemoHeader h)
    {
        w.Write(Magic);
        w.Write(Version);
        w.Write(h.GameVersion);
        w.Write(h.ProtocolHash);
        w.Write(h.ContentHash);
        w.Write(h.Map);
        w.Write(h.GameOptions);
        w.Write(h.ServerName);
        w.Write(h.StartUnixMs);
        w.Write(h.TickRate);
        w.Write(h.NetRate);
    }

    internal static DemoHeader ReadHeader(BinaryReader r)
    {
        byte[] magic = r.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a demo file.");
        }

        ushort version = r.ReadUInt16();
        if (version > Version)
        {
            throw new InvalidDataException($"Demo format {version} is newer than this game ({Version}).");
        }

        return new DemoHeader
        {
            GameVersion = r.ReadString(),
            ProtocolHash = r.ReadUInt64(),
            ContentHash = r.ReadUInt64(),
            Map = r.ReadString(),
            GameOptions = r.ReadString(),
            ServerName = r.ReadString(),
            StartUnixMs = r.ReadInt64(),
            TickRate = r.ReadInt32(),
            NetRate = r.ReadInt32(),
        };
    }

    internal static void WriteIndex(BinaryWriter w, DemoIndex index)
    {
        w.Write(index.FirstTick);
        w.Write(index.LastTick);
        w.Write(index.Packets);
        w.Write(index.RawBytes);
        w.Write7BitEncodedInt(index.Players.Count);
        foreach (DemoPlayer p in index.Players)
        {
            w.Write(p.PlayerId);
            w.Write(p.Name);
            w.Write(p.Team);
        }

        w.Write7BitEncodedInt(index.Markers.Count);
        foreach (DemoMarker m in index.Markers)
        {
            w.Write(m.Tick);
            w.Write((byte)m.Kind);
            w.Write(m.Text);
        }
    }

    internal static DemoIndex ReadIndex(BinaryReader r)
    {
        var index = new DemoIndex
        {
            FirstTick = r.ReadUInt32(),
            LastTick = r.ReadUInt32(),
            Packets = r.ReadInt64(),
            RawBytes = r.ReadInt64(),
        };
        int players = r.Read7BitEncodedInt();
        for (int i = 0; i < players; i++)
        {
            index.Players.Add(new DemoPlayer(r.ReadInt32(), r.ReadString(), r.ReadInt32()));
        }

        int markers = r.Read7BitEncodedInt();
        for (int i = 0; i < markers; i++)
        {
            index.Markers.Add(new DemoMarker(r.ReadUInt32(), (DemoMarkerKind)r.ReadByte(), r.ReadString()));
        }

        return index;
    }

    /// <summary>Reads only the header and the index (for a demo list), without the packets.</summary>
    public static (DemoHeader Header, DemoIndex Index) ReadInfo(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var r = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        DemoHeader header = ReadHeader(r);
        long bodyStart = file.Position;
        (long indexStart, _) = FindIndex(file, r, bodyStart);
        file.Position = indexStart;
        return (header, ReadIndex(r));
    }

    /// <summary>Reads a whole demo into memory (a few MB) for playback.</summary>
    public static DemoRecording Read(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var r = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        DemoHeader header = ReadHeader(r);
        long bodyStart = file.Position;
        (long indexStart, long bodyLength) = FindIndex(file, r, bodyStart);
        file.Position = indexStart;
        DemoIndex index = ReadIndex(r);

        file.Position = bodyStart;
        var body = new MemoryStream(capacity: (int)Math.Min(index.RawBytes + index.Packets * 4, int.MaxValue));
        using (var slice = new SubStream(file, bodyLength))
        using (var brotli = new BrotliStream(slice, CompressionMode.Decompress))
        {
            brotli.CopyTo(body);
        }

        var recording = new DemoRecording(header, index, body.GetBuffer(), (int)body.Length);
        return recording;
    }

    private static (long IndexStart, long BodyLength) FindIndex(FileStream file, BinaryReader r, long bodyStart)
    {
        if (file.Length < bodyStart + TrailerBytes)
        {
            throw new InvalidDataException("The demo is cut short (no index): the recording did not finish.");
        }

        file.Position = file.Length - TrailerBytes;
        long indexLength = r.ReadInt64();
        byte[] end = r.ReadBytes(EndMagic.Length);
        if (!end.AsSpan().SequenceEqual(EndMagic) || indexLength < 0 || indexLength > file.Length - bodyStart - TrailerBytes)
        {
            throw new InvalidDataException("The demo is cut short (no index): the recording did not finish.");
        }

        long indexStart = file.Length - TrailerBytes - indexLength;
        return (indexStart, indexStart - bodyStart);
    }

    internal static void WriteTrailer(BinaryWriter w, long indexLength)
    {
        w.Write(indexLength);
        w.Write(EndMagic);
    }

    /// <summary>A read-only window of a stream, so the Brotli decoder stops at the end of the body.</summary>
    private sealed class SubStream : Stream
    {
        private readonly Stream _inner;
        private long _left;

        public SubStream(Stream inner, long length)
        {
            _inner = inner;
            _left = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left <= 0)
            {
                return 0;
            }

            int n = _inner.Read(buffer, offset, (int)Math.Min(count, _left));
            _left -= n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// Writes a demo while it records: the packets go through Brotli to the file as they come, the index is written
/// by <see cref="Finish"/>. Until then the file has no index and readers refuse it.
/// </summary>
public sealed class DemoWriter : IDemoSink, IDisposable
{
    private readonly FileStream _file;
    private readonly BrotliStream _body;
    private readonly byte[] _varint = new byte[10];
    private bool _started;
    private uint _lastTick;
    private bool _finished;

    public DemoWriter(string path, DemoHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        Path = path;
        Header = header;
        string? folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using (var w = new BinaryWriter(_file, Encoding.UTF8, leaveOpen: true))
        {
            DemoFile.WriteHeader(w, header);
        }

        _body = new BrotliStream(_file, CompressionLevel.Optimal, leaveOpen: true);
    }

    public string Path { get; }

    public DemoHeader Header { get; }

    public DemoIndex Index { get; } = new();

    /// <summary>Bytes in the file so far (compressed), for logs.</summary>
    public long FileBytes => _file.Length;

    public bool IsFinished => _finished;

    public void Write(uint tick, ReadOnlySpan<byte> datagram)
    {
        if (_finished)
        {
            return;
        }

        if (!_started)
        {
            _started = true;
            _lastTick = tick;
            Index.FirstTick = tick;
        }

        uint delta = tick >= _lastTick ? tick - _lastTick : 0;
        _lastTick = Math.Max(_lastTick, tick);
        Index.LastTick = _lastTick;
        WriteVarint(delta);
        WriteVarint((uint)datagram.Length);
        _body.Write(datagram);
        Index.Packets++;
        Index.RawBytes += datagram.Length;
    }

    /// <summary>Adds a marker at the last tick written (or <paramref name="tick"/>).</summary>
    public void Mark(DemoMarkerKind kind, string text, uint? tick = null) =>
        Index.Markers.Add(new DemoMarker(tick ?? _lastTick, kind, text ?? string.Empty));

    /// <summary>Notes a player (once per id; a later call updates the name and team).</summary>
    public void NotePlayer(int playerId, string name, int team)
    {
        for (int i = 0; i < Index.Players.Count; i++)
        {
            if (Index.Players[i].PlayerId == playerId)
            {
                Index.Players[i] = new DemoPlayer(playerId, name, team);
                return;
            }
        }

        Index.Players.Add(new DemoPlayer(playerId, name, team));
    }

    /// <summary>Ends the packet stream and writes the index: the file is complete and readable.</summary>
    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _body.Dispose();
        using var w = new BinaryWriter(_file, Encoding.UTF8, leaveOpen: true);
        long start = _file.Position;
        DemoFile.WriteIndex(w, Index);
        w.Flush();
        DemoFile.WriteTrailer(w, _file.Position - start);
        w.Flush();
        _file.Flush(flushToDisk: true);
    }

    private void WriteVarint(uint value)
    {
        int n = 0;
        while (value >= 0x80)
        {
            _varint[n++] = (byte)(value | 0x80);
            value >>= 7;
        }

        _varint[n++] = (byte)value;
        _body.Write(_varint, 0, n);
    }

    /// <summary>Closes the file; a demo that was not finished stays unreadable (cut short).</summary>
    public void Dispose()
    {
        if (!_finished)
        {
            _finished = true;
            _body.Dispose();
        }

        _file.Dispose();
    }
}

/// <summary>A demo in memory: the header, the index and the packets in order with their server ticks.</summary>
public sealed class DemoRecording
{
    private readonly byte[] _body;
    private readonly List<(uint Tick, int Offset, int Length)> _frames = new();

    internal DemoRecording(DemoHeader header, DemoIndex index, byte[] body, int length)
    {
        Header = header;
        Index = index;
        _body = body;
        int pos = 0;
        uint tick = index.FirstTick;
        while (pos < length)
        {
            tick += ReadVarint(body, ref pos, length);
            int size = (int)ReadVarint(body, ref pos, length);
            if (size < 0 || pos + size > length)
            {
                throw new InvalidDataException("The demo's packet stream is damaged.");
            }

            _frames.Add((tick, pos, size));
            pos += size;
        }
    }

    public DemoHeader Header { get; }

    public DemoIndex Index { get; }

    public int PacketCount => _frames.Count;

    public uint TickOf(int packet) => _frames[packet].Tick;

    public ReadOnlySpan<byte> Packet(int packet)
    {
        (uint _, int offset, int length) = _frames[packet];
        return _body.AsSpan(offset, length);
    }

    /// <summary>The first packet at or after <paramref name="tick"/> (or <see cref="PacketCount"/>).</summary>
    public int FirstPacketAtOrAfter(uint tick)
    {
        int lo = 0;
        int hi = _frames.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_frames[mid].Tick < tick)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static uint ReadVarint(byte[] data, ref int pos, int length)
    {
        uint value = 0;
        int shift = 0;
        while (true)
        {
            if (pos >= length || shift > 28)
            {
                throw new InvalidDataException("The demo's packet stream is damaged.");
            }

            byte b = data[pos++];
            value |= (uint)(b & 0x7f) << shift;
            if (b < 0x80)
            {
                return value;
            }

            shift += 7;
        }
    }
}
