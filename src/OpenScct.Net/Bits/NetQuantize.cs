using System;
using OpenScct.Core;

namespace OpenScct.Net;

/// <summary>
/// Quantisation used on the wire. Replication compares values in quantised form, so a change smaller than one
/// step is not sent and the client ends up with exactly the value the server's shadow holds.
/// </summary>
public static class NetQuantize
{
    /// <summary>Vectors are sent in 1/16 uu steps.</summary>
    public const int DefaultVectorScale = 16;

    /// <summary>Largest bit count per packed vector component (sign included): about +-33 million uu at 1/16.</summary>
    public const int DefaultVectorBits = 30;

    /// <summary>
    /// Maps <paramref name="value"/> from [min, max] onto 0..2^bits-1, rounding to nearest. NaN maps to min.
    /// At most 24 bits, the float mantissa.
    /// </summary>
    public static uint QuantizeFloat(float value, float min, float max, int bits)
    {
        if (bits is < 1 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(bits));
        }

        if (!(max > min))
        {
            throw new ArgumentOutOfRangeException(nameof(max), "max must be above min.");
        }

        uint steps = (1u << bits) - 1;
        float t = (value - min) / (max - min);
        if (!(t > 0f))
        {
            return 0;
        }

        if (t >= 1f)
        {
            return steps;
        }

        long q = SvmMath.Round(t * steps);
        return q < 0 ? 0 : q > steps ? steps : (uint)q;
    }

    /// <summary>Inverse of <see cref="QuantizeFloat"/>.</summary>
    public static float DequantizeFloat(uint quantized, float min, float max, int bits)
    {
        uint steps = (1u << bits) - 1;
        return min + (max - min) * ((float)quantized / steps);
    }

    /// <summary>Rounds a component to 1/<paramref name="scale"/> uu and clamps it to what fits in the given bits.</summary>
    public static int QuantizeComponent(float value, int scale, int maxBitsPerComponent = DefaultVectorBits)
    {
        int limit = (1 << (maxBitsPerComponent - 1)) - 1;
        float scaled = value * scale;
        if (float.IsNaN(scaled))
        {
            return 0;
        }

        if (scaled >= limit)
        {
            return limit;
        }

        if (scaled <= -limit)
        {
            return -limit;
        }

        return SvmMath.Round(scaled);
    }

    public static void QuantizeVector(FVector value, int scale, int maxBitsPerComponent, out int qx, out int qy, out int qz)
    {
        qx = QuantizeComponent(value.X, scale, maxBitsPerComponent);
        qy = QuantizeComponent(value.Y, scale, maxBitsPerComponent);
        qz = QuantizeComponent(value.Z, scale, maxBitsPerComponent);
    }

    public static float DequantizeComponent(int quantized, int scale) => (float)quantized / scale;

    public static FVector DequantizeVector(int qx, int qy, int qz, int scale) =>
        new(DequantizeComponent(qx, scale), DequantizeComponent(qy, scale), DequantizeComponent(qz, scale));

    /// <summary>The vector a client receives for <paramref name="value"/>.</summary>
    public static FVector RoundTripVector(FVector value, int scale = DefaultVectorScale, int maxBitsPerComponent = DefaultVectorBits)
    {
        QuantizeVector(value, scale, maxBitsPerComponent, out int qx, out int qy, out int qz);
        return DequantizeVector(qx, qy, qz, scale);
    }
}
