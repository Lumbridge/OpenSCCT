using System;
using System.IO;
using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenScct.Content.Materials;

/// <summary>Lossless 16-bit PNG samples for data textures, independent of the display-image decoder.</summary>
public static class PngData
{
    public sealed record Samples(int Width, int Height, float[] Rgba);
    public static Samples? Read16(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || !png[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) || png[24] != 16) return null;
        int width = BinaryPrimitives.ReadInt32BigEndian(png[16..]), height = BinaryPrimitives.ReadInt32BigEndian(png[20..]);
        int channels = png[25] switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0 || png[28] != 0 || width <= 0 || height <= 0 || (long)width * height > 67108864)
            throw new InvalidDataException("16-bit data PNG must be non-interlaced grayscale, RGB or RGBA, within 64 million pixels.");
        using var compressed = new MemoryStream();
        for (int at = 8; at + 12 <= png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png[at..]);
            if (length < 0 || length > png.Length - at - 12) throw new InvalidDataException("Truncated data PNG.");
            if (png.Slice(at + 4, 4).SequenceEqual("IDAT"u8)) compressed.Write(png.Slice(at + 8, length));
            at += length + 12;
        }
        compressed.Position = 0;
        using var z = new ZLibStream(compressed, CompressionMode.Decompress);
        int stride = checked(width * channels * 2), bpp = channels * 2;
        var previous = new byte[stride]; var row = new byte[stride]; var encoded = new byte[stride]; var pixels = new float[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int filter = z.ReadByte(); if (filter < 0 || filter > 4) throw new InvalidDataException("Invalid data PNG filter.");
            z.ReadExactly(encoded);
            for (int x = 0; x < stride; x++)
            {
                int a = x < bpp ? 0 : row[x - bpp], b = previous[x], c = x < bpp ? 0 : previous[x - bpp], p = a + b - c;
                int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                int prediction = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, _ => pa <= pb && pa <= pc ? a : pb <= pc ? b : c };
                row[x] = unchecked((byte)(encoded[x] + prediction));
            }
            for (int x = 0; x < width; x++) for (int c = 0; c < 4; c++)
            {
                int channel = channels <= 2 ? (c == 3 && channels == 2 ? 1 : 0) : c;
                pixels[(y * width + x) * 4 + c] = c == 3 && channels is 1 or 3 ? 1 : BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan((x * channels + channel) * 2)) / 65535f;
            }
            (previous, row) = (row, previous);
        }
        return new(width, height, pixels);
    }
}
