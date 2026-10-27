using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenScct.Core;

namespace OpenScct.Content.Collision;

/// <summary>
/// Reads <c>sim/collision.bin</c> (collision.md). Known chunks are found by fourcc; a duplicate or a known chunk
/// with another version is an error, and unknown chunks are skipped by their size. Strict mode (the default)
/// also enforces the 1.0 writer rules: header flags 0, every known chunk present once in order, zero padding,
/// no trailing bytes. Every index and range is checked before the data is returned.
/// </summary>
public static class CollisionReader
{
    public const string RelativePath = "sim/collision.bin";

    public const ushort Major = 1;
    public const ushort Minor = 0;
    public const uint ChunkVersion = 1;

    /// <summary>The 1.0 chunks in writer order.</summary>
    public static readonly IReadOnlyList<string> ChunkOrder = ["STRS", "VERT", "TRIS", "TFLG", "PLNS", "HULL", "BSPN", "COLL", "CYLS", "ZONE"];

    internal static ReadOnlySpan<byte> Magic => "SVCL"u8;

    /// <summary>Record size of each fixed-stride chunk.</summary>
    public static int StrideOf(string fourcc) => fourcc switch
    {
        "VERT" => 12,
        "TRIS" => 16,
        "TFLG" => 8,
        "PLNS" => 16,
        "HULL" => 48,
        "BSPN" => 48,
        "COLL" => 128,
        "CYLS" => 32,
        "ZONE" => 32,
        _ => 0,
    };

    public static CollisionData Load(string path, bool strict = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Read(File.ReadAllBytes(path), strict);
    }

    public static CollisionData Read(ReadOnlySpan<byte> data, bool strict = true, string? file = RelativePath)
    {
        void Fail(string message) => throw new SvMapFormatException(file, message);

        if (data.Length < 16)
        {
            Fail("file too small");
        }

        if (!data[..4].SequenceEqual(Magic))
        {
            Fail("bad magic");
        }

        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        uint chunkCount = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        if (major != Major)
        {
            Fail($"unsupported major version {major}");
        }

        if (strict && (flags != 0 || data.Length % 16 != 0))
        {
            Fail("header flags must be 0 and the size a multiple of 16");
        }

        var chunks = new Dictionary<string, (uint Count, int Start, int Size)>(StringComparer.Ordinal);
        var order = new List<string>();
        var unknown = ImmutableArray.CreateBuilder<CollisionUnknownChunk>();
        long pos = 16;
        for (uint n = 0; n < chunkCount; n++)
        {
            if (pos % 16 != 0)
            {
                Fail($"chunk at {pos} is not 16-byte aligned");
            }

            if (pos + 16 > data.Length)
            {
                Fail("chunk header truncated");
            }

            ReadOnlySpan<byte> header = data.Slice((int)pos, 16);
            string fourcc = Encoding.ASCII.GetString(header[..4]);
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
            long start = pos + 16;
            long end = start + size;
            if (end > data.Length)
            {
                Fail($"{fourcc} payload truncated");
            }

            long next = (end + 15) & ~15L;
            if (strict && next <= data.Length && data[(int)end..(int)next].ContainsAnyExcept((byte)0))
            {
                Fail($"{fourcc} padding is not zero");
            }

            pos = next;
            int stride = StrideOf(fourcc);
            if (stride != 0 || fourcc == "STRS")
            {
                if (chunks.ContainsKey(fourcc))
                {
                    Fail($"duplicate chunk {fourcc}");
                }

                if (version != ChunkVersion)
                {
                    Fail($"{fourcc} version {version} is not supported");
                }

                if (stride != 0 && size != (ulong)count * (ulong)stride)
                {
                    Fail($"{fourcc} size {size} does not match count {count}");
                }

                chunks[fourcc] = (count, (int)start, (int)size);
                order.Add(fourcc);
            }
            else
            {
                unknown.Add(new CollisionUnknownChunk(fourcc, version, count, data[(int)start..(int)end].ToArray()));
            }
        }

        if (strict)
        {
            bool inOrder = order.Count == ChunkOrder.Count;
            for (int i = 0; inOrder && i < order.Count; i++)
            {
                inOrder = order[i] == ChunkOrder[i];
            }

            if (!inOrder)
            {
                Fail($"chunks {string.Join(' ', order)} are not the 1.0 set in order");
            }

            if (pos != data.Length)
            {
                Fail("trailing bytes after the last chunk");
            }
        }

        var result = new CollisionData
        {
            Strings = ReadStrings(Payload(data, chunks, "STRS", out int ns), ns, file),
            Vertices = Rows(Payload(data, chunks, "VERT", out int nv), nv, 12, static p => Vec(p)),
            Triangles = Rows(Payload(data, chunks, "TRIS", out int nt), nt, 16, static p => new CollisionTriangle(
                U32(p, 0), U32(p, 4), U32(p, 8), U16(p, 12), U16(p, 14))),
            Materials = Rows(Payload(data, chunks, "TFLG", out int nm), nm, 8, static p => new CollisionMaterial(
                p[0], (CollisionMaterialFlags)U16(p, 2), U32(p, 4))),
            Planes = Rows(Payload(data, chunks, "PLNS", out int np), np, 16, static p => Plane(p)),
            Hulls = Rows(Payload(data, chunks, "HULL", out int nh), nh, 48, static p => new CollisionHull(
                U32(p, 0), U32(p, 4), U32(p, 8), U32(p, 12), U16(p, 16), Vec(p[24..]), Vec(p[36..]))),
            BspNodes = Rows(Payload(data, chunks, "BSPN", out int nb), nb, 48, static p => new CollisionBspNode(
                Plane(p), I32(p, 16), I32(p, 20), I32(p, 24), I32(p, 28), U32(p, 32), U32(p, 36), U16(p, 40), p[42], p[43])),
            Colliders = Rows(Payload(data, chunks, "COLL", out int nc), nc, 128, static p => new Collider(
                (ColliderKind)p[0], (ColliderFlags)U16(p, 2), U32(p, 4), U32(p, 8),
                U32(p, 16), U32(p, 20), U32(p, 24), U32(p, 28), U32(p, 32), U32(p, 36), U32(p, 40), U32(p, 44),
                Vec(p[48..]), new FRotator(I32(p, 60), I32(p, 64), I32(p, 68)), Vec(p[72..]), Vec(p[84..]),
                Vec(p[96..]), Vec(p[108..]))),
            Cylinders = Rows(Payload(data, chunks, "CYLS", out int ny), ny, 32, static p => new CollisionCylinder(
                U32(p, 0), (ColliderFlags)U16(p, 4), Vec(p[8..]), F32(p, 20), F32(p, 24))),
            Zones = Rows(Payload(data, chunks, "ZONE", out int nz), nz, 32, static p => new CollisionZone(
                U32(p, 0), (CollisionZoneFlags)U32(p, 4), Vec(p[8..]), Vec(p[20..]))),
            UnknownChunks = unknown.ToImmutable(),
        };
        result.Validate(file);
        return result;
    }

    private static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> data, Dictionary<string, (uint Count, int Start, int Size)> chunks,
        string fourcc, out int count)
    {
        if (!chunks.TryGetValue(fourcc, out (uint Count, int Start, int Size) c))
        {
            count = 0;
            return default;
        }

        count = checked((int)c.Count);
        return data.Slice(c.Start, c.Size);
    }

    private delegate T RowReader<out T>(ReadOnlySpan<byte> record);

    private static ImmutableArray<T> Rows<T>(ReadOnlySpan<byte> payload, int count, int stride, RowReader<T> read)
    {
        var rows = new T[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = read(payload.Slice(i * stride, stride));
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(rows);
    }

    private static ImmutableArray<string> ReadStrings(ReadOnlySpan<byte> payload, int count, string? file)
    {
        if ((long)payload.Length < 4L * count)
        {
            throw new SvMapFormatException(file, "STRS offsets truncated");
        }

        ReadOnlySpan<byte> blob = payload[(4 * count)..];
        var strings = new string[count];
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        for (int i = 0; i < count; i++)
        {
            uint offset = U32(payload, 4 * i);
            int end = offset <= (uint)blob.Length ? blob[(int)offset..].IndexOf((byte)0) : -1;
            if (end < 0)
            {
                throw new SvMapFormatException(file, $"STRS string {i} out of bounds");
            }

            try
            {
                strings[i] = utf8.GetString(blob.Slice((int)offset, end));
            }
            catch (DecoderFallbackException e)
            {
                throw new SvMapFormatException(file, $"STRS string {i} is not UTF-8", e);
            }
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(strings);
    }

    private static uint U32(ReadOnlySpan<byte> p, int at) => BinaryPrimitives.ReadUInt32LittleEndian(p[at..]);

    private static int I32(ReadOnlySpan<byte> p, int at) => BinaryPrimitives.ReadInt32LittleEndian(p[at..]);

    private static ushort U16(ReadOnlySpan<byte> p, int at) => BinaryPrimitives.ReadUInt16LittleEndian(p[at..]);

    private static float F32(ReadOnlySpan<byte> p, int at) => BinaryPrimitives.ReadSingleLittleEndian(p[at..]);

    private static FVector Vec(ReadOnlySpan<byte> p) => new(F32(p, 0), F32(p, 4), F32(p, 8));

    private static CollisionPlane Plane(ReadOnlySpan<byte> p) => new(Vec(p), F32(p, 12));
}
