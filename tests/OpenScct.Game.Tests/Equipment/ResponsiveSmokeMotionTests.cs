using System;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ResponsiveSmokeMotionTests
{
    [Fact]
    public void ASharedMaterialFeatureFollowsACurvedContinuousUpwardPath()
    {
        FVector feature = new(37, -53, 80);
        FVector phase = new(11.17f, 7.31f, 19.83f);
        float value = ResponsiveSmokeNoise.Sample(feature / 135f + phase);
        FVector start = PositionOfFeature(feature, 1), middle = PositionOfFeature(feature, 3), end = PositionOfFeature(feature, 5);
        // The 18uu/s upward drift exceeds either extreme of the bounded 22uu vertical roll over four seconds.
        Assert.InRange(end.Z - start.Z, 28f, 116f);
        Assert.True(FVector.Dist(middle, (start + end) * .5f) > 5f);
        FVector previous = PositionOfFeature(feature, 0);
        for (int frame = 0; frame <= 8 * 144; frame++)
        {
            float age = frame / 144f;
            FVector position = PositionOfFeature(feature, age);
            FVector material = ResponsiveSmokeField.MaterialCoordinate(position, age);
            Assert.InRange(FVector.Dist(material, feature), 0f, .0001f);
            Assert.InRange(MathF.Abs(ResponsiveSmokeNoise.Sample(material / 135f + phase) - value), 0f, .00001f);
            // Ordered shear derivatives bound this trajectory below 50uu/s; visible motion cannot jump a voxel.
            Assert.InRange(FVector.Dist(previous, position), 0f, 50f / 144f + .0001f);
            previous = position;
        }
    }

    [Fact]
    public void RollingRetainsTheProtectedBodyAndStrictWorldSpaceBounds()
    {
        for (float age = 0; age <= 10; age += .25f)
        for (int z = -3; z <= 3; z++) for (int y = -3; y <= 3; y++) for (int x = -3; x <= 3; x++)
        {
            if (x == 0 && y == 0 && z == 0) continue;
            FVector direction = new FVector(x, y, z).SafeNormal();
            Assert.Equal(1f, ResponsiveSmokeField.Envelope(direction * 149.99f, age));
            Assert.Equal(0f, ResponsiveSmokeField.Envelope(direction * 450.01f, age));
        }
    }

    [Fact]
    public void RollingDoesNotMoveABulletHoleWithItsMaterial()
    {
        var field = new ResponsiveSmokeField(default, 0);
        Array.Fill(field.Cells, (byte)1); Array.Fill(field.ActivationTicks, (ushort)0);
        FVector start = new(-75, 205, 0), end = new(75, 205, 0);
        field.AddCarve(new(1, ResponsiveSmokeCarveKind.Bullet, start, end, 12, 6, 2, .1f, .4f, null));
        for (float age = 2; age < 2.1f; age += 1f / 144f)
        {
            for (int sample = 0; sample <= 15; sample++)
                Assert.Equal(0f, field.SampleDensity(start + (end - start) * (sample / 15f), age));
            Assert.True(field.SampleDensity(new FVector(0, 205, 40), age) >= .55f);
        }
        Assert.True(field.SampleDensity(new FVector(0, 205, 0), 2.51f) >= .55f);
    }

    private static FVector PositionOfFeature(FVector material, float age)
    {
        // Reverse the ordered shears to follow one fixed material feature through world space.
        material.Z += 18f * age;
        material.Y -= 18f * SvmMath.Sin(material.Z / 100f - .7f * age + 3.1f);
        material.Z -= 22f * SvmMath.Sin(material.X / 90f + .9f * age + 1.7f);
        material.X -= 24f * SvmMath.Sin(material.Z / 85f - 1.2f * age);
        return material;
    }
}
