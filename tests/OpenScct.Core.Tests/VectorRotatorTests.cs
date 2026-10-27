using System;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class VectorRotatorTests
{
    [Fact]
    public void VectorArithmetic()
    {
        var a = new FVector(1f, 2f, 3f);
        var b = new FVector(4f, -5f, 6f);
        Assert.Equal(new FVector(5f, -3f, 9f), a + b);
        Assert.Equal(new FVector(-3f, 7f, -3f), a - b);
        Assert.Equal(new FVector(-1f, -2f, -3f), -a);
        Assert.Equal(new FVector(2f, 4f, 6f), a * 2f);
        Assert.Equal(new FVector(2f, 4f, 6f), 2f * a);
        Assert.Equal(new FVector(4f, -10f, 18f), a * b);
        Assert.Equal(1f * 4f + 2f * -5f + 3f * 6f, a | b);
        Assert.Equal(a | b, a.Dot(b));
        Assert.Equal(new FVector(2f * 6f - 3f * -5f, 3f * 4f - 1f * 6f, 1f * -5f - 2f * 4f), a ^ b);
        Assert.Equal(a ^ b, a.Cross(b));
        Assert.Equal(0f, (a ^ b) | a);
    }

    [Fact]
    public void DivisionUsesTheReciprocalLikeUe2()
    {
        var v = new FVector(1f, 10f, 7f);
        float r = 1f / 3f;
        Assert.Equal(new FVector(v.X * r, v.Y * r, v.Z * r), v / 3f);

        // 10 / 3 and 10 * (1/3) differ in the last bit, so this really is the reciprocal form.
        Assert.NotEqual(10f / 3f, (v / 3f).Y);
    }

    [Fact]
    public void SizesAndNormals()
    {
        var v = new FVector(3f, 4f, 12f);
        Assert.Equal(13f, v.Size());
        Assert.Equal(169f, v.SizeSquared());
        Assert.Equal(5f, v.Size2D());
        Assert.Equal(25f, v.SizeSquared2D());

        FVector n = v.SafeNormal();
        Assert.True(MathF.Abs(n.Size() - 1f) < 1e-6f);
        Assert.Equal(FVector.Zero, new FVector(1e-5f, 0f, 0f).SafeNormal());

        var tiny = new FVector(1e-5f, 0f, 0f);
        Assert.False(tiny.Normalize());
        Assert.Equal(1e-5f, tiny.X);

        var big = new FVector(0f, 10f, 0f);
        Assert.True(big.Normalize());
        Assert.Equal(new FVector(0f, 1f, 0f), big);

        Assert.True(new FVector(1e-5f, -1e-5f, 0f).IsNearlyZero());
        Assert.True(default(FVector).IsZero());
    }

    [Fact]
    public void MirrorAndBound()
    {
        var v = new FVector(1f, -1f, 0f);
        Assert.Equal(new FVector(1f, 1f, 0f), v.MirrorByVector(new FVector(0f, 1f, 0f)));
        Assert.Equal(new FVector(2f, -2f, 1f), new FVector(5f, -9f, 1f).BoundToCube(2f));
    }

    [Fact]
    public void VectorToRotator()
    {
        Assert.Equal(new FRotator(0, 0, 0), new FVector(1f, 0f, 0f).Rotation());
        Assert.Equal(new FRotator(0, 16384, 0), new FVector(0f, 1f, 0f).Rotation());
        Assert.Equal(new FRotator(0, -16384, 0), new FVector(0f, -1f, 0f).Rotation());
        Assert.Equal(new FRotator(16384, 0, 0), new FVector(0f, 0f, 1f).Rotation());

        // 65535 / 2: ties round to even.
        Assert.Equal(32768, new FVector(-1f, 0f, 0f).Rotation().Yaw);
        Assert.Equal(8192, new FVector(1f, 1f, 0f).Rotation().Yaw);
    }

    [Fact]
    public void RotatorToVector()
    {
        Assert.Equal(new FVector(1f, 0f, 0f), new FRotator(0, 0, 0).Vector());
        FVector left = new FRotator(0, 16384, 0).Vector();
        Assert.Equal(1f, left.Y);
        Assert.True(MathF.Abs(left.X) < 1e-6f);
        FVector up = new FRotator(16384, 0, 0).Vector();
        Assert.Equal(1f, up.Z);

        var rng = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            var r = new FRotator(rng.Next(-16000, 16000), rng.Next(0, 65536), 0);
            FVector v = r.Vector();
            Assert.True(MathF.Abs(v.Size() - 1f) < 1e-5f);
            FRotator back = v.Rotation();
            Assert.True(Math.Abs(FRotator.NormalizeAxis(back.Yaw - r.Yaw)) <= 8, $"{r} -> {back}");
            Assert.True(Math.Abs(back.Pitch - r.Pitch) <= 8, $"{r} -> {back}");
        }
    }

    [Fact]
    public void RotatorAxes()
    {
        var rng = new Random(11);
        for (int i = 0; i < 2000; i++)
        {
            var r = new FRotator(rng.Next(-65536, 65536), rng.Next(-65536, 65536), rng.Next(-65536, 65536));
            r.GetAxes(out FVector x, out FVector y, out FVector z);
            Assert.Equal(r.Vector(), x);
            Assert.True(MathF.Abs(x | y) < 1e-5f);
            Assert.True(MathF.Abs(y | z) < 1e-5f);
            Assert.True(MathF.Abs(x | z) < 1e-5f);
            FVector cross = x ^ y;
            Assert.True((cross - z).Size() < 1e-5f, $"{r}: x^y={cross} z={z}");
        }

        new FRotator(0, 0, 0).GetAxes(out FVector x0, out FVector y0, out FVector z0);
        Assert.Equal(new FVector(1f, 0f, 0f), x0);
        Assert.Equal(new FVector(0f, 1f, 0f), y0);
        Assert.Equal(new FVector(0f, 0f, 1f), z0);
    }

    [Fact]
    public void CoordsTransform()
    {
        var r = new FRotator(1000, 20000, -3000);
        FCoords c = FCoords.FromRotator(r);
        c.Origin = new FVector(10f, 20f, 30f);
        var p = new FVector(5f, -7f, 11f);
        FVector local = p.TransformPointBy(c);
        FVector world = c.Origin + c.XAxis * local.X + c.YAxis * local.Y + c.ZAxis * local.Z;
        Assert.True((world - p).Size() < 1e-4f);

        FVector back = local.TransformPointBy(c.Transpose());
        Assert.True((back - p).Size() < 1e-4f, $"{back} vs {p}");
    }

    [Fact]
    public void RotatorWrapping()
    {
        Assert.Equal(65535, FRotator.ClampAxis(-1));
        Assert.Equal(0, FRotator.ClampAxis(65536));
        Assert.Equal(-25536, FRotator.NormalizeAxis(40000));
        Assert.Equal(32767, FRotator.NormalizeAxis(32767));
        Assert.Equal(-32768, FRotator.NormalizeAxis(32768));
        Assert.Equal(new FRotator(-1, 1, -32768), new FRotator(65535, 65537, 32768).Normalize());
        Assert.Equal(new FRotator(65535, 1, 32768), new FRotator(-1, 65537, -32768).Clamp());
        Assert.True(new FRotator(65536, 0, -131072).IsZero());
        Assert.False(new FRotator(1, 0, 0).IsZero());
    }

    [Fact]
    public void RotatorArithmetic()
    {
        var a = new FRotator(100, 200, 300);
        var b = new FRotator(1, 2, 3);
        Assert.Equal(new FRotator(101, 202, 303), a + b);
        Assert.Equal(new FRotator(99, 198, 297), a - b);
        Assert.Equal(new FRotator(-100, -200, -300), -a);
        Assert.Equal(new FRotator(50, 100, 150), a * 0.5f);
        Assert.Equal(new FRotator(2, 2, 4), new FRotator(5, 3, 7) * 0.5f); // 2.5 -> 2, 1.5 -> 2, 3.5 -> 4
        Assert.Equal(new FRotator(101, 200, 299), a.Add(1, 0, -1));
    }

    [Fact]
    public void HashesAreDeterministic()
    {
        Assert.Equal(new FVector(1f, 2f, 3f).GetHashCode(), new FVector(1f, 2f, 3f).GetHashCode());
        Assert.Equal(new FVector(0f, 0f, 0f).GetHashCode(), new FVector(-0f, 0f, -0f).GetHashCode());
        Assert.Equal(0, new FVector(0f, 0f, 0f).GetHashCode());
        Assert.Equal((100 * 31 + 200) * 31 + 300, new FRotator(100, 200, 300).GetHashCode());
    }
}
