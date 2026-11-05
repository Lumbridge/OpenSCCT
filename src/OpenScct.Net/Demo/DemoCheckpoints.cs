using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace OpenScct.Net;

internal readonly record struct DemoCheckpointInfo(uint Tick, int NextPacket, int CompressedBytes, int ExpandedBytes);

public static partial class DemoFile
{
    internal const int CheckpointIndexEntryBytes = sizeof(uint) + 3 * sizeof(int);
    internal const int MaxContinuationBytes = 8 + 4 * K.MAX_CHANNELS;

    private static void ReadCheckpointIndex(BinaryReader reader, DemoIndex index, DemoReadLimits limits)
    {
        int count = ReadCount(reader, limits.MaxCheckpoints);
        uint previousTick = index.FirstTick;
        int previousPacket = 1;
        long totalBytes = 0;
        for (int i = 0; i < count; i++)
        {
            var item = new DemoCheckpointInfo(reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            totalBytes += item.CompressedBytes;
            if (item.Tick <= previousTick || item.Tick > index.LastTick ||
                item.NextPacket < previousPacket || item.NextPacket > index.Packets ||
                item.CompressedBytes <= 0 || totalBytes > limits.MaxCheckpointCompressedBytes ||
                item.ExpandedBytes < 2 * sizeof(ushort) + 2 || item.ExpandedBytes > limits.MaxCheckpointExpandedBytes)
                throw new InvalidDataException("The demo's checkpoint index exceeds playback limits.");
            index.Checkpoints.Add(item);
            previousTick = item.Tick;
            previousPacket = item.NextPacket;
        }
    }

    private static long PacketBodyLength(DemoIndex index, long length)
    {
        if (index.ViewCompressedBytes > length)
            throw new InvalidDataException("The demo's recorded view stream is cut short.");
        length -= index.ViewCompressedBytes;
        foreach (DemoCheckpointInfo item in index.Checkpoints)
        {
            if (item.CompressedBytes > length)
                throw new InvalidDataException("The demo's checkpoint stream is cut short.");
            length -= item.CompressedBytes;
        }
        return length;
    }
}

public sealed partial class DemoWriter
{
    private readonly List<byte[]> _checkpointBytes = new();
    private int _checkpointCompressedBytes;

    /// <summary>
    /// Captures a bootstrap for the current tick and the next primary packet. An incomplete capture leaves the
    /// original packet stream playable. The capture must run on the same thread as primary recording.
    /// </summary>
    public bool TryWriteCheckpoint(uint tick, Func<IDemoSink, byte[]?> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        DemoReadLimits limits = DemoReadLimits.Default;
        if (_finished || !_started || tick <= Index.FirstTick || tick < _lastTick ||
            Index.CheckpointCount >= limits.MaxCheckpoints || _checkpointCompressedBytes >= limits.MaxCheckpointCompressedBytes ||
            (Index.Checkpoints.Count > 0 && tick <= Index.Checkpoints[^1].Tick))
            return false;

        using var packets = new MemoryStream();
        var sink = new CheckpointSink(tick, packets);
        byte[]? continuation = capture(sink);
        if (_finished || continuation is not { Length: > 0 and <= DemoFile.MaxContinuationBytes } ||
            sink.Overflow || packets.Length == 0 || _lastTick > tick || Index.Packets > limits.MaxPackets)
            return false;
        int expanded = sizeof(ushort) + continuation.Length + (int)packets.Length;
        if (expanded > limits.MaxCheckpointExpandedBytes) return false;

        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            WriteLength(brotli, continuation.Length);
            brotli.Write(continuation);
            brotli.Write(packets.GetBuffer(), 0, (int)packets.Length);
        }
        if (compressed.Length > limits.MaxCheckpointCompressedBytes - _checkpointCompressedBytes) return false;

        byte[] bytes = compressed.ToArray();
        Index.Checkpoints.Add(new DemoCheckpointInfo(tick, (int)Index.Packets, bytes.Length, expanded));
        _checkpointBytes.Add(bytes);
        _checkpointCompressedBytes += bytes.Length;
        Index.LastTick = Math.Max(Index.LastTick, tick);
        return true;
    }

    private void FinishCheckpoints(BinaryWriter metadata)
    {
        // Keep within the lobby download limit whenever the primary recording itself fits. Reserve room for
        // the trailer and a two-byte count (the writer captures at most 256 checkpoints).
        const int maxCountBytes = 2;
        long room = DemoFile.MaxPersonalDemoBytes - _file.Position - metadata.BaseStream.Length -
            DemoFile.TrailerBytes - maxCountBytes;
        long indexRoom = DemoReadLimits.Default.MaxIndexBytes - metadata.BaseStream.Length - maxCountBytes;
        int retained = 0;
        foreach (byte[] bytes in _checkpointBytes)
        {
            if (room < bytes.Length + DemoFile.CheckpointIndexEntryBytes || indexRoom < DemoFile.CheckpointIndexEntryBytes)
                break;
            _file.Write(bytes);
            retained++;
            room -= bytes.Length + DemoFile.CheckpointIndexEntryBytes;
            indexRoom -= DemoFile.CheckpointIndexEntryBytes;
        }

        Index.Checkpoints.RemoveRange(retained, Index.CheckpointCount - retained);
        _checkpointBytes.Clear();
        metadata.Write7BitEncodedInt(retained);
        foreach (DemoCheckpointInfo item in Index.Checkpoints)
        {
            metadata.Write(item.Tick);
            metadata.Write(item.NextPacket);
            metadata.Write(item.CompressedBytes);
            metadata.Write(item.ExpandedBytes);
        }
    }

    private static void WriteLength(Stream stream, int length)
    {
        Span<byte> data = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)length);
        stream.Write(data);
    }

    private sealed class CheckpointSink(uint tick, MemoryStream packets) : IDemoSink
    {
        public bool Overflow { get; private set; }

        public void Write(uint packetTick, ReadOnlySpan<byte> datagram)
        {
            if (Overflow) return;
            if (packetTick != tick || datagram.IsEmpty || datagram.Length > NetConnection.MaxPacketBytesLimit ||
                packets.Length + sizeof(ushort) + datagram.Length > DemoReadLimits.Default.MaxCheckpointExpandedBytes)
            {
                Overflow = true;
                return;
            }

            WriteLength(packets, datagram.Length);
            packets.Write(datagram);
        }
    }
}

public sealed partial class DemoRecording
{
    private readonly List<byte[]> _checkpointBytes = new();
    private DemoReadLimits _checkpointLimits = DemoReadLimits.Default;

    internal void ReadCheckpoints(BinaryReader reader, DemoReadLimits limits)
    {
        _checkpointLimits = limits;
        foreach (DemoCheckpointInfo item in Index.Checkpoints)
        {
            if (TickOf(item.NextPacket - 1) > item.Tick ||
                (item.NextPacket < PacketCount && TickOf(item.NextPacket) < item.Tick))
                throw new InvalidDataException("A checkpoint's continuation is outside its tick.");
            byte[] bytes = reader.ReadBytes(item.CompressedBytes);
            if (bytes.Length != item.CompressedBytes) throw new InvalidDataException("The demo's checkpoint is cut short.");
            _checkpointBytes.Add(bytes);
        }
    }

    internal DemoCheckpoint? FindCheckpoint(uint tick)
    {
        int lo = 0;
        int hi = Index.CheckpointCount;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (Index.Checkpoints[mid].Tick <= tick) lo = mid + 1;
            else hi = mid;
        }

        return lo == 0 ? null : DemoCheckpoint.Decode(Index.Checkpoints[lo - 1], _checkpointBytes[lo - 1], _checkpointLimits);
    }
}

internal sealed class DemoCheckpoint(DemoCheckpointInfo info, byte[] data, int continuationBytes)
{
    public uint Tick => info.Tick;
    public int NextPacket => info.NextPacket;
    public int FirstOffset => sizeof(ushort) + continuationBytes;
    public ReadOnlyMemory<byte> Continuation => data.AsMemory(sizeof(ushort), continuationBytes);
    public bool HasPacket(int offset) => offset < data.Length;

    public ReadOnlySpan<byte> TakePacket(ref int offset)
    {
        int size = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        offset += sizeof(ushort) + size;
        return data.AsSpan(offset - size, size);
    }

    public static DemoCheckpoint? Decode(DemoCheckpointInfo info, byte[] compressed, DemoReadLimits limits)
    {
        // Decode only the chosen checkpoint. A damaged optional bootstrap falls back to the original stream.
        var data = new byte[info.ExpandedBytes];
        if (!BrotliDecoder.TryDecompress(compressed, data, out int written) || written != data.Length) return null;
        int continuation = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (continuation is <= 0 or > DemoFile.MaxContinuationBytes) return null;
        int offset = sizeof(ushort) + continuation;
        int packets = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < sizeof(ushort) || ++packets > limits.MaxPackets) return null;
            int size = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
            offset += sizeof(ushort);
            if (size <= 0 || size > Math.Min(limits.MaxPacketBytes, NetConnection.MaxPacketBytesLimit) ||
                size > data.Length - offset) return null;
            offset += size;
        }
        return packets > 0 ? new DemoCheckpoint(info, data, continuation) : null;
    }
}
