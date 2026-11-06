using System;
using OpenScct.Core;

namespace OpenScct.Game.Equipment;

/// <summary>
/// Shared periodic macro-noise lattice. Its bytes are uploaded unchanged as an R8 texture by the renderer;
/// hardware trilinear sampling evaluates the same field as the CPU sampler below. Expensive cellular and
/// gradient-noise generation happens once during an enabled match's loading phase.
/// </summary>
public static class ResponsiveSmokeNoise
{
    public const int Dimension = 64;
    public const int TexelsPerFeature = 8;
    private const int FeatureDimension = Dimension / TexelsPerFeature;
    private const float NormalizeByte = 1f / 255f;
    public static ReadOnlySpan<byte> Lattice => Data.Values;
    public static void Warmup() => _ = Data.Values;

    // Nested initialization keeps the lattice out of disabled matches and ordinary legacy smoke.
    private static class Data
    {
        internal static readonly byte[] Values = Build();
        static Data() { }
        private static byte[] Build()
        {
            var features = new FVector[FeatureDimension * FeatureDimension * FeatureDimension];
            for (int i = 0; i < features.Length; i++)
                features[i] = new(Jitter(Hash((uint)i + 0x9e3779b9u)), Jitter(Hash((uint)i + 0x85ebca6bu)), Jitter(Hash((uint)i + 0xc2b2ae35u)));
            var gradients = new FVector[4 * 4 * 4];
            for (int i = 0; i < gradients.Length; i++) gradients[i] = Gradient(Hash((uint)i + 0x6a09e667u) % 12);
            var result = new byte[Dimension * Dimension * Dimension];
            for (int z = 0; z < Dimension; z++) for (int y = 0; y < Dimension; y++) for (int x = 0; x < Dimension; x++)
            {
                // Values live at feature-coordinate x/8, so texture(q*8+.5)/64 samples these exact nodes.
                FVector point = new FVector(x, y, z) / TexelsPerFeature;
                int cx = x / TexelsPerFeature, cy = y / TexelsPerFeature, cz = z / TexelsPerFeature;
                float nearest = float.PositiveInfinity;
                for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int fx = cx + dx, fy = cy + dy, fz = cz + dz;
                    FVector feature = new FVector(fx, fy, fz) + features[(fx & 7) + 8 * ((fy & 7) + 8 * (fz & 7))];
                    nearest = Math.Min(nearest, FVector.DistSquared(point, feature));
                }
                float clump = 1f - ResponsiveSmokeField.Smooth(.12f, .85f, MathF.Sqrt(nearest));
                float perlin = Math.Clamp(.5f + .5f * Perlin(point * .5f, gradients), 0f, 1f);
                float value = .85f * clump + .15f * perlin;
                result[x + Dimension * (y + Dimension * z)] = (byte)Math.Clamp((int)(value * 255f + .5f), 0, 255);
            }
            return result;
        }
    }

    private static uint Hash(uint hash)
    {
        hash = unchecked((hash ^ (hash >> 16)) * 0x7feb352du);
        hash = unchecked((hash ^ (hash >> 15)) * 0x846ca68bu);
        return hash ^ (hash >> 16);
    }
    // +/- .20 jitter guarantees a 27-cell search: the own-cell seed is at most sqrt(3)*.7 away,
    // whereas a seed outside the neighboring cell cube is at least 1.3 away.
    private static float Jitter(uint hash) => .3f + .4f * ((hash >> 8) / 16777215f);
    private static FVector Gradient(uint index) => index switch
    {
        0 => new(1, 1, 0), 1 => new(-1, 1, 0), 2 => new(1, -1, 0), 3 => new(-1, -1, 0),
        4 => new(1, 0, 1), 5 => new(-1, 0, 1), 6 => new(1, 0, -1), 7 => new(-1, 0, -1),
        8 => new(0, 1, 1), 9 => new(0, -1, 1), 10 => new(0, 1, -1), _ => new(0, -1, -1),
    };
    private static float Perlin(FVector point, FVector[] gradients)
    {
        int x = (int)MathF.Floor(point.X), y = (int)MathF.Floor(point.Y), z = (int)MathF.Floor(point.Z);
        FVector f = point - new FVector(x, y, z);
        float tx = Fade(f.X), ty = Fade(f.Y), tz = Fade(f.Z), result = 0f;
        for (int dz = 0; dz <= 1; dz++) for (int dy = 0; dy <= 1; dy++) for (int dx = 0; dx <= 1; dx++)
        {
            FVector gradient = gradients[((x + dx) & 3) + 4 * (((y + dy) & 3) + 4 * ((z + dz) & 3))];
            float value = gradient | (f - new FVector(dx, dy, dz));
            result += value * (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty) * (dz == 0 ? 1 - tz : tz);
        }
        return result;
    }
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

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
