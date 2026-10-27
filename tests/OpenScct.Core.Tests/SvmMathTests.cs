using System;
using OpenScct.Core;
using Xunit;
using static OpenScct.Core.Tests.MathTestUtil;

namespace OpenScct.Core.Tests;

public sealed class SvmMathTests
{
    [Fact]
    public void SineTableMatchesMathSinWithinOneUlpForEveryEntry()
    {
        ReadOnlySpan<float> table = SvmMath.SinTable;
        Assert.Equal(SvmMath.NUM_ANGLES, table.Length);

        int exact = 0;
        long worst = 0;
        for (int i = 0; i < table.Length; i++)
        {
            float expected = (float)Math.Sin(SvmMath.SinTableArgument(i));
            long ulps = UlpDistance(table[i], expected);
            Assert.True(ulps <= 1, $"entry {i}: {table[i]:R} vs {expected:R} ({ulps} ulp)");
            worst = Math.Max(worst, ulps);
            if (ulps == 0)
            {
                exact++;
            }
        }

        // In practice every entry rounds the same way as the C runtime.
        Assert.True(exact >= table.Length - 4, $"only {exact} of {table.Length} entries are exact");
    }

    [Fact]
    public void SineTableKeyEntries()
    {
        ReadOnlySpan<float> t = SvmMath.SinTable;
        Assert.Equal(0f, t[0]);
        Assert.Equal(1f, t[4096]);
        Assert.Equal(-1f, t[12288]);

        // sin(PI as a double) is not zero, and the table keeps that.
        Assert.Equal((float)1.2246467991473532e-16, t[8192]);
    }

    [Fact]
    public void TableSinCosIndexLikeUe2()
    {
        Assert.Equal(0f, SvmMath.Sin(0));
        Assert.Equal(1f, SvmMath.Cos(0));
        Assert.Equal(1f, SvmMath.Sin(16384));
        Assert.Equal(-1f, SvmMath.Sin(-16384));
        Assert.Equal(-1f, SvmMath.Cos(32768));
        Assert.Equal(SvmMath.Sin(1000), SvmMath.Sin(1000 + 65536));
        Assert.Equal(SvmMath.Sin(1000), SvmMath.Sin(1003)); // low two bits are dropped
        Assert.Equal(SvmMath.Cos(5000), SvmMath.Sin(5000 + 16384));

        for (int a = -200000; a < 200000; a += 37)
        {
            Assert.Equal(SvmMath.SinTable[(a >> 2) & 16383], SvmMath.Sin(a));
            Assert.Equal(SvmMath.SinTable[((a + 16384) >> 2) & 16383], SvmMath.Cos(a));
        }
    }

    [Fact]
    public void RadianTrigMatchesRuntimeToTheNearestFloat()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 200000; i++)
        {
            float x = (float)((rng.NextDouble() * 2.0 - 1.0) * (i % 4 == 0 ? 2000.0 : 10.0));
            AssertUlp(SvmMath.Sin(x), (float)Math.Sin(x), 1, "sin", x);
            AssertUlp(SvmMath.Cos(x), (float)Math.Cos(x), 1, "cos", x);
            AssertUlp(SvmMath.Tan(x), (float)Math.Tan(x), 1, "tan", x);
            AssertUlp(SvmMath.Atan(x), (float)Math.Atan(x), 1, "atan", x);
        }
    }

    [Fact]
    public void DoubleKernelsAreAccurate()
    {
        var rng = new Random(99);
        long worstSin = 0;
        long worstAtan = 0;
        for (int i = 0; i < 100000; i++)
        {
            double x = (rng.NextDouble() * 2.0 - 1.0) * 1000.0;
            worstSin = Math.Max(worstSin, UlpDistance(SvmMath.SinD(x), Math.Sin(x)));
            double y = (rng.NextDouble() * 2.0 - 1.0) * 50.0;
            worstAtan = Math.Max(worstAtan, UlpDistance(SvmMath.AtanD(y), Math.Atan(y)));
        }

        Assert.True(worstSin <= 4, $"sin worst {worstSin} ulp");
        Assert.True(worstAtan <= 4, $"atan worst {worstAtan} ulp");
    }

    [Fact]
    public void Atan2MatchesRuntime()
    {
        var rng = new Random(5);
        for (int i = 0; i < 200000; i++)
        {
            float y = (float)((rng.NextDouble() * 2.0 - 1.0) * Math.Pow(10, rng.Next(-6, 6)));
            float x = (float)((rng.NextDouble() * 2.0 - 1.0) * Math.Pow(10, rng.Next(-6, 6)));
            AssertUlp(SvmMath.Atan2(y, x), (float)Math.Atan2(y, x), 1, "atan2", y, x);
        }

        float[] special = [0f, -0f, 1f, -1f, float.PositiveInfinity, float.NegativeInfinity, 1e-30f, -1e30f];
        foreach (float y in special)
        {
            foreach (float x in special)
            {
                float expected = (float)Math.Atan2(y, x);
                float actual = SvmMath.Atan2(y, x);
                Assert.True(UlpDistance(actual, expected) <= 1, $"atan2({y}, {x}) = {actual:R}, expected {expected:R}");
                Assert.Equal(float.IsNegative(expected), float.IsNegative(actual));
            }
        }

        Assert.True(float.IsNaN(SvmMath.Atan2(float.NaN, 1f)));
    }

    [Fact]
    public void AcosAsinMatchRuntime()
    {
        var rng = new Random(77);
        for (int i = 0; i < 200000; i++)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            AssertUlp(SvmMath.Acos(x), (float)Math.Acos(x), 1, "acos", x);
            AssertUlp(SvmMath.Asin(x), (float)Math.Asin(x), 1, "asin", x);
        }

        Assert.Equal(0f, SvmMath.Acos(1f));
        Assert.Equal((float)Math.PI, SvmMath.Acos(-1f));
        Assert.Equal((float)(Math.PI / 2), SvmMath.Asin(1f));
        Assert.True(float.IsNaN(SvmMath.Acos(1.0000001f)));
        Assert.True(float.IsNaN(SvmMath.Asin(-1.5f)));
    }

    [Fact]
    public void ExpLogPowMatchRuntime()
    {
        var rng = new Random(31);
        for (int i = 0; i < 200000; i++)
        {
            float e = (float)((rng.NextDouble() * 2.0 - 1.0) * 85.0);
            AssertUlp(SvmMath.Exp(e), (float)Math.Exp(e), 1, "exp", e);

            float l = (float)(rng.NextDouble() * Math.Pow(10, rng.Next(-30, 30)));
            AssertUlp(SvmMath.Loge(l), (float)Math.Log(l), 1, "log", l);

            float b = (float)(rng.NextDouble() * 100.0 + 0.001);
            float p = (float)((rng.NextDouble() * 2.0 - 1.0) * 10.0);
            AssertUlp(SvmMath.Pow(b, p), (float)Math.Pow(b, p), 1, "pow", b, p);
        }

        Assert.Equal(1f, SvmMath.Pow(0f, 0f));
        Assert.Equal(1f, SvmMath.Pow(float.NaN, 0f));
        Assert.Equal(-8f, SvmMath.Pow(-2f, 3f));
        Assert.Equal(16f, SvmMath.Pow(-2f, 4f));
        Assert.True(float.IsNaN(SvmMath.Pow(-2f, 0.5f)));
        Assert.Equal(float.PositiveInfinity, SvmMath.Pow(0f, -1f));
        Assert.Equal(float.NegativeInfinity, SvmMath.Loge(0f));
        Assert.True(float.IsNaN(SvmMath.Loge(-1f)));
        Assert.Equal(0f, SvmMath.Loge(1f));
        Assert.Equal(1f, SvmMath.Exp(0f));
        Assert.Equal(float.PositiveInfinity, SvmMath.Exp(1000f));
        Assert.Equal(0f, SvmMath.Exp(-1000f));
    }

    [Fact]
    public void SqrtIsIeee()
    {
        Assert.Equal(3f, SvmMath.Sqrt(9f));
        Assert.Equal(MathF.Sqrt(2f), SvmMath.Sqrt(2f));
        Assert.Equal(0.5f, SvmMath.InvSqrt(4f));
    }

    [Fact]
    public void RoundingFollowsX87()
    {
        Assert.Equal(0, SvmMath.Round(0.5f));
        Assert.Equal(2, SvmMath.Round(1.5f));
        Assert.Equal(2, SvmMath.Round(2.5f));
        Assert.Equal(0, SvmMath.Round(-0.5f));
        Assert.Equal(-2, SvmMath.Round(-1.5f));
        Assert.Equal(3, SvmMath.Round(2.6f));
        Assert.Equal(int.MinValue, SvmMath.Round(float.NaN));
        Assert.Equal(int.MinValue, SvmMath.Round(3e9f));
        Assert.Equal(int.MinValue, SvmMath.Round(-3e9f));

        Assert.Equal(-1, SvmMath.Trunc(-1.7f));
        Assert.Equal(1, SvmMath.Trunc(1.7f));
        Assert.Equal(int.MinValue, SvmMath.Trunc(float.PositiveInfinity));
        Assert.Equal(-2, SvmMath.Floor(-1.2f));
        Assert.Equal(2, SvmMath.Ceil(1.2f));
    }

    [Fact]
    public void Ue2MinMaxClamp()
    {
        Assert.Equal(2f, SvmMath.Max(1f, 2f));
        Assert.Equal(1f, SvmMath.Min(1f, 2f));
        Assert.Equal(5f, SvmMath.Clamp(7f, 0f, 5f));
        Assert.Equal(0f, SvmMath.Clamp(-7f, 0f, 5f));

        // UE2 Max(NaN, x) returns x because NaN >= x is false.
        Assert.Equal(3f, SvmMath.Max(float.NaN, 3f));
        Assert.Equal(15f, SvmMath.Lerp(10f, 20f, 0.5f));
    }

    private static void AssertUlp(float actual, float expected, long max, string what, params float[] args)
    {
        long ulps = UlpDistance(actual, expected);
        if (ulps > max)
        {
            Assert.Fail($"{what}({string.Join(", ", Array.ConvertAll(args, a => a.ToString("R")))}) = {actual:R}, expected {expected:R} ({ulps} ulp)");
        }
    }
}
