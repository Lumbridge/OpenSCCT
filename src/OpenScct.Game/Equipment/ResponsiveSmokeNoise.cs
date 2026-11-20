using System;
using OpenScct.Core;

namespace OpenScct.Game.Equipment;

/// <summary>
/// Shared periodic macro-noise lattice. Its bytes are uploaded unchanged as an R8 texture by the renderer;
/// hardware trilinear sampling evaluates the same field as the CPU sampler below.
/// </summary>
public static class ResponsiveSmokeNoise
{
    public const int Dimension = 64;
    public const int TexelsPerFeature = 8;
    private const float NormalizeByte = 1f / 255f;
    public static ReadOnlySpan<byte> Lattice => Data.Values;
    public static void Warmup() => _ = Data.Values;

    // Fixed simulation data, shared by every field. Loading 256 KiB avoids generating the volume
    // on the first gameplay sample. The original generator verifies every byte in ResponsiveSmokeNoiseTests.
    private static class Data
    {
        internal static readonly byte[] Values = LoadLattice();
        static Data() { }
        private static byte[] LoadLattice()
        {
            using var stream = typeof(ResponsiveSmokeNoise).Assembly.GetManifestResourceStream("OpenScct.ResponsiveSmokeNoise.r8")
                ?? throw new System.IO.InvalidDataException("Missing responsive smoke noise lattice.");
            var values = new byte[Dimension * Dimension * Dimension];
            stream.ReadExactly(values);
            return values;
        }
    }

    public static float Sample(FVector coordinate)
    {
        coordinate *= TexelsPerFeature;
        int x = (int)MathF.Floor(coordinate.X), y = (int)MathF.Floor(coordinate.Y), z = (int)MathF.Floor(coordinate.Z);
        float fx = coordinate.X - x, fy = coordinate.Y - y, fz = coordinate.Z - z;
        int x0 = x & 63, x1 = (x + 1) & 63;
        int y0 = (y & 63) * Dimension, y1 = ((y + 1) & 63) * Dimension;
        int z0 = (z & 63) * Dimension * Dimension, z1 = ((z + 1) & 63) * Dimension * Dimension;
        ReadOnlySpan<byte> bytes = Lattice;
        float a = Mix(Mix(bytes[x0 + y0 + z0] * NormalizeByte, bytes[x1 + y0 + z0] * NormalizeByte, fx),
            Mix(bytes[x0 + y1 + z0] * NormalizeByte, bytes[x1 + y1 + z0] * NormalizeByte, fx), fy);
        float b = Mix(Mix(bytes[x0 + y0 + z1] * NormalizeByte, bytes[x1 + y0 + z1] * NormalizeByte, fx),
            Mix(bytes[x0 + y1 + z1] * NormalizeByte, bytes[x1 + y1 + z1] * NormalizeByte, fx), fy);
        return Mix(a, b, fz);
    }

    private static float Mix(float a, float b, float t) => a + (b - a) * t;
}
