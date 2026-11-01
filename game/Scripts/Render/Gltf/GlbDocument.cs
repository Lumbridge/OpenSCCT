using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenScctGame.Render.Gltf;

/// <summary>
/// A small reader for the binary glTF files the content pipeline writes (render/world.glb): the JSON chunk as a
/// <see cref="JsonDocument"/> and typed views of accessors in the BIN chunk. It reads geometry only; materials and
/// textures are named by the map's own JSON files. Values stay in render space; nothing is converted here.
/// </summary>
public sealed class GlbDocument : IDisposable
{
    private const uint Magic = 0x46546C67;      // "glTF"
    private const uint ChunkJson = 0x4E4F534A;  // "JSON"
    private const uint ChunkBin = 0x004E4942;   // "BIN\0"

    private readonly byte[] _bin;
    private readonly List<BufferView> _views = [];
    private readonly List<Accessor> _accessors = [];

    private GlbDocument(JsonDocument json, byte[] bin)
    {
        Json = json;
        _bin = bin;
        JsonElement root = json.RootElement;
        if (root.TryGetProperty("bufferViews", out JsonElement views))
        {
            foreach (JsonElement v in views.EnumerateArray())
            {
                _views.Add(new BufferView(
                    v.TryGetProperty("byteOffset", out JsonElement o) ? o.GetInt32() : 0,
                    v.GetProperty("byteLength").GetInt32(),
                    v.TryGetProperty("byteStride", out JsonElement s) ? s.GetInt32() : 0));
            }
        }

        if (root.TryGetProperty("accessors", out JsonElement accessors))
        {
            foreach (JsonElement a in accessors.EnumerateArray())
            {
                _accessors.Add(new Accessor(
                    a.TryGetProperty("bufferView", out JsonElement bv) ? bv.GetInt32() : -1,
                    a.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0,
                    a.GetProperty("componentType").GetInt32(),
                    a.GetProperty("count").GetInt32(),
                    ComponentsOf(a.GetProperty("type").GetString()),
                    a.TryGetProperty("normalized", out JsonElement n) && n.GetBoolean()));
            }
        }
    }

    public JsonDocument Json { get; }

    public JsonElement Root => Json.RootElement;

    public static GlbDocument Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        ReadOnlySpan<byte> span = data;
        if (span.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(span) != Magic)
        {
            throw new InvalidDataException($"{path}: not a binary glTF file");
        }

        int offset = 12;
        JsonDocument? json = null;
        byte[] bin = [];
        while (offset + 8 <= span.Length)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            int start = offset + 8;
            if (length < 0 || start + length > span.Length)
            {
                throw new InvalidDataException($"{path}: chunk overruns the file");
            }

            if (type == ChunkJson)
            {
                json = JsonDocument.Parse(data.AsMemory(start, length));
            }
            else if (type == ChunkBin)
            {
                bin = data.AsSpan(start, length).ToArray();
            }

            offset = start + ((length + 3) & ~3);
        }

        return json is null ? throw new InvalidDataException($"{path}: no JSON chunk") : new GlbDocument(json, bin);
    }

    public int AccessorCount(int accessor) => _accessors[accessor].Count;

    /// <summary>Float vectors of an accessor (VEC2, VEC3 or VEC4 of float), flattened.</summary>
    public float[] ReadFloats(int accessor)
    {
        Accessor a = _accessors[accessor];
        if (a.ComponentType != 5126)
        {
            throw new InvalidDataException($"accessor {accessor} is not float");
        }

        var result = new float[a.Count * a.Components];
        BufferView view = _views[a.View];
        int stride = view.Stride > 0 ? view.Stride : a.Components * 4;
        int baseOffset = view.Offset + a.Offset;
        for (int i = 0; i < a.Count; i++)
        {
            int at = baseOffset + (i * stride);
            for (int c = 0; c < a.Components; c++)
            {
                result[(i * a.Components) + c] = BinaryPrimitives.ReadSingleLittleEndian(_bin.AsSpan(at + (c * 4)));
            }
        }

        return result;
    }

    /// <summary>Unsigned bytes of an accessor (VEC4 of normalized UNSIGNED_BYTE colours), flattened.</summary>
    public byte[] ReadBytes(int accessor)
    {
        Accessor a = _accessors[accessor];
        if (a.ComponentType != 5121)
        {
            throw new InvalidDataException($"accessor {accessor} is not unsigned byte");
        }

        var result = new byte[a.Count * a.Components];
        BufferView view = _views[a.View];
        int stride = view.Stride > 0 ? view.Stride : a.Components;
        int baseOffset = view.Offset + a.Offset;
        for (int i = 0; i < a.Count; i++)
        {
            Buffer.BlockCopy(_bin, baseOffset + (i * stride), result, i * a.Components, a.Components);
        }

        return result;
    }

    /// <summary>RGB/RGBA colour accessors converted to RGBA bytes, including float fixture meshes.</summary>
    public byte[] ReadColours(int accessor)
    {
        Accessor a = _accessors[accessor];
        if (a.Components is not (3 or 4)) throw new InvalidDataException("Colours must be RGB or RGBA.");
        int size = a.ComponentType switch { 5121 => 1, 5123 => 2, 5126 => 4, _ => throw new InvalidDataException("Unsupported colour component type.") };
        BufferView view = _views[a.View];
        int stride = view.Stride > 0 ? view.Stride : a.Components * size;
        var result = new byte[a.Count * 4];
        for (int i = 0; i < a.Count; i++)
        {
            result[i * 4 + 3] = 255;
            for (int c = 0; c < a.Components; c++)
            {
                int at = view.Offset + a.Offset + i * stride + c * size;
                float value = a.ComponentType switch
                {
                    5121 => _bin[at] / 255f,
                    5123 => BinaryPrimitives.ReadUInt16LittleEndian(_bin.AsSpan(at)) / 65535f,
                    _ => BinaryPrimitives.ReadSingleLittleEndian(_bin.AsSpan(at)),
                };
                result[i * 4 + c] = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
            }
        }
        return result;
    }

    /// <summary>Indices of an accessor (UNSIGNED_BYTE, SHORT or INT scalars).</summary>
    public int[] ReadIndices(int accessor)
    {
        Accessor a = _accessors[accessor];
        var result = new int[a.Count];
        BufferView view = _views[a.View];
        int size = a.ComponentType switch
        {
            5121 => 1,
            5123 => 2,
            5125 => 4,
            _ => throw new InvalidDataException($"accessor {accessor} has index type {a.ComponentType}"),
        };
        int stride = view.Stride > 0 ? view.Stride : size;
        int baseOffset = view.Offset + a.Offset;
        for (int i = 0; i < a.Count; i++)
        {
            int at = baseOffset + (i * stride);
            result[i] = size switch
            {
                1 => _bin[at],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(_bin.AsSpan(at)),
                _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(_bin.AsSpan(at)),
            };
        }

        return result;
    }

    public void Dispose() => Json.Dispose();

    private static int ComponentsOf(string? type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT4" => 16,
        _ => throw new InvalidDataException($"accessor type {type}"),
    };

    private readonly record struct BufferView(int Offset, int Length, int Stride);

    private readonly record struct Accessor(int View, int Offset, int ComponentType, int Count, int Components, bool Normalized);
}
