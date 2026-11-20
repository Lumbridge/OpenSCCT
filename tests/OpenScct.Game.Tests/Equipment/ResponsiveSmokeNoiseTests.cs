using System;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public class ResponsiveSmokeNoiseTests
{
    private const int Dimension = ResponsiveSmokeNoise.Dimension;
    private const int TexelsPerFeature = ResponsiveSmokeNoise.TexelsPerFeature;
    private const int FeatureDimension = Dimension / TexelsPerFeature;

    [Fact]
    public void ShippedLatticeMatchesTheOriginalPeriodicNoise()
    {
        Assert.True(ResponsiveSmokeNoise.Lattice.SequenceEqual(Build()));
    }

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
            // Warp once while baking the shared lattice, so connected eddies replace regular round
            // cellular balls without adding texture samples to the per-frame density shader.
            FVector warp = new(Perlin(point * .5f + new FVector(1.3f, 2.1f, .7f), gradients),
                Perlin(point * .5f + new FVector(2.7f, .9f, 3.1f), gradients),
                Perlin(point * .5f + new FVector(.4f, 3.3f, 1.8f), gradients));
            point += warp * .85f;
            int cx = (int)MathF.Floor(point.X), cy = (int)MathF.Floor(point.Y), cz = (int)MathF.Floor(point.Z);
            float nearest = float.PositiveInfinity;
            for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int fx = cx + dx, fy = cy + dy, fz = cz + dz;
                FVector feature = new FVector(fx, fy, fz) + features[(fx & 7) + 8 * ((fy & 7) + 8 * (fz & 7))];
                nearest = Math.Min(nearest, FVector.DistSquared(point, feature));
            }
            float clump = 1f - ResponsiveSmokeField.Smooth(.12f, .85f, MathF.Sqrt(nearest));
            float turbulence = .50f * Perlin(point, gradients)
                + .32f * Perlin(point * 2f + new FVector(1.7f, 2.3f, .9f), gradients)
                + .18f * Perlin(point * 4f + new FVector(3.1f, .6f, 2.7f), gradients);
            float perlin = ResponsiveSmokeField.Smooth(.15f, .85f, Math.Clamp(.5f + 1.1f * turbulence, 0f, 1f));
            float value = .30f * clump + .70f * perlin;
            result[x + Dimension * (y + Dimension * z)] = (byte)Math.Clamp((int)(value * 255f + .5f), 0, 255);
        }
        return result;
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

}
