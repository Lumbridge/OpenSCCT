using System;

namespace OpenScct.Core.Tests;

internal static class MathTestUtil
{
    /// <summary>Distance in representable floats between a and b (0 when equal, +0 and -0 count as equal).</summary>
    public static long UlpDistance(float a, float b)
    {
        if (float.IsNaN(a) || float.IsNaN(b))
        {
            return float.IsNaN(a) && float.IsNaN(b) ? 0 : long.MaxValue;
        }

        return Math.Abs(Ordered(a) - Ordered(b));
    }

    /// <summary>Distance in representable doubles between a and b.</summary>
    public static long UlpDistance(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return double.IsNaN(a) && double.IsNaN(b) ? 0 : long.MaxValue;
        }

        Int128 d = Int128.Abs(Ordered(a) - Ordered(b));
        return d > long.MaxValue ? long.MaxValue : (long)d;
    }

    private static long Ordered(float f)
    {
        int bits = BitConverter.SingleToInt32Bits(f);
        return bits >= 0 ? bits : (long)int.MinValue - bits;
    }

    private static Int128 Ordered(double f)
    {
        long bits = BitConverter.DoubleToInt64Bits(f);
        return bits >= 0 ? bits : (Int128)long.MinValue - bits;
    }
}
