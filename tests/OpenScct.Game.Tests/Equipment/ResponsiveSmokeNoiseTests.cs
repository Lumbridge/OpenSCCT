using System;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ResponsiveSmokeNoiseTests
{
    [Fact]
    public void PublishedLatticeHasStableR8LayoutAndExactTexelCenters()
    {
        ReadOnlySpan<byte> bytes = ResponsiveSmokeNoise.Lattice;
        Assert.Equal(64 * 64 * 64, bytes.Length);
        Assert.Equal(8, ResponsiveSmokeNoise.TexelsPerFeature);
        byte minimum = byte.MaxValue, maximum = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            minimum = Math.Min(minimum, bytes[i]); maximum = Math.Max(maximum, bytes[i]);
            float sample = ResponsiveSmokeNoise.Sample(new FVector(i % 64, i / 64 % 64, i / 4096) / 8);
            Assert.InRange(MathF.Abs(sample - bytes[i] / 255f), 0f, .000001f);
        }
        Assert.InRange(minimum, (byte)0, (byte)32); Assert.InRange(maximum, (byte)225, byte.MaxValue);
    }

    [Fact]
    public void LatticeWrapsContinuouslyIncludingNegativeCoordinates()
    {
        FVector point = new(-.37f, 8.63f, 31.91f);
        float reference = ResponsiveSmokeNoise.Sample(point);
        Assert.InRange(MathF.Abs(reference - ResponsiveSmokeNoise.Sample(point + new FVector(8, 0, 0))), 0, .00001f);
        Assert.InRange(MathF.Abs(reference - ResponsiveSmokeNoise.Sample(point + new FVector(0, -8, 0))), 0, .00001f);
        Assert.InRange(MathF.Abs(reference - ResponsiveSmokeNoise.Sample(point + new FVector(0, 0, 8))), 0, .00001f);
        float before = ResponsiveSmokeNoise.Sample(new FVector(-.00001f, 8.63f, 31.91f));
        float after = ResponsiveSmokeNoise.Sample(new FVector(.00001f, 8.63f, 31.91f));
        Assert.InRange(MathF.Abs(before - after), 0, .0002f);
    }

    [Fact]
    public void TurbulentNoiseRetainsCoherentDetailAcrossMultipleScales()
    {
        float fine = 0, broad = 0;
        int count = 0;
        for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        {
            FVector point = new FVector(x, y, z) + new FVector(.31f, .57f, .83f);
            float value = ResponsiveSmokeNoise.Sample(point);
            fine += MathF.Abs(value - ResponsiveSmokeNoise.Sample(point + new FVector(.125f, 0, 0)));
            broad += MathF.Abs(value - ResponsiveSmokeNoise.Sample(point + new FVector(1, 0, 0)));
            count++;
        }
        Assert.True(fine/count > .005f, $"fine detail={fine/count}");
        Assert.True(broad/count > .08f && broad > fine*1.5f, $"fine={fine/count}, broad={broad/count}");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    [InlineData(5f)]
    [InlineData(9f)]
    public void MacroShapeRetainsFullCoreAndStrictOuterBoundAtEveryAge(float age)
    {
        for (int y = -4; y <= 4; y++) for (int x = -4; x <= 4; x++)
        {
            FVector direction = new FVector(x, y, 2).SafeNormal();
            Assert.Equal(1f, ResponsiveSmokeField.Envelope(direction * 149.99f, age));
            Assert.Equal(0f, ResponsiveSmokeField.Envelope(direction * 300.01f, age));
            Assert.InRange(ResponsiveSmokeField.Envelope(direction * 210, age), .54999f, 1f);
        }
    }

    [Fact]
    public void MacroRimHasBroadContrastAndMovesWhileCoreStaysOpaque()
    {
        float minimum = 1, maximum = 0, motion = 0;
        for (int i = 0; i < 128; i++)
        {
            float angle = i * (SvmMath.PI * 2f / 128);
            FVector point = new FVector(SvmMath.Cos(angle), SvmMath.Sin(angle), 0) * 270;
            float density = ResponsiveSmokeField.Envelope(point, 2);
            minimum = Math.Min(minimum, density); maximum = Math.Max(maximum, density);
            motion += MathF.Abs(density - ResponsiveSmokeField.Envelope(point, 3));
        }
        Assert.True(minimum < .05f);
        Assert.True(maximum > .75f);
        Assert.True(motion / 128 > .01f);
        Assert.Equal(1f, ResponsiveSmokeField.Envelope(default, 2));
        Assert.Equal(1f, ResponsiveSmokeField.Envelope(default, 3));
    }

    [Fact]
    public void CentralStandingHeightRayRemainsOpaqueAcrossChangingLobes()
    {
        for (float age = 1; age <= 7; age += .5f)
        {
            float opticalDepth = 0f;
            for (int x = -300; x < 300; x++)
                opticalDepth += ResponsiveSmokeField.Envelope(new FVector(x + .5f, 0, 55), age) * ResponsiveSmokeField.Extinction;
            Assert.True(opticalDepth > 6f, $"age={age}, opticalDepth={opticalDepth}");
        }
    }
}
