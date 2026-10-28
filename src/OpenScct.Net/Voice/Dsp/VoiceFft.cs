using System;

namespace OpenScct.Net.Voice.Dsp;

/// <summary>
/// A plain radix-2 complex FFT for the microphone clean-up (<see cref="VoiceDsp"/>). The twiddles come from the
/// half-angle formula and complex powers in double precision, so no trigonometric library call is needed.
/// </summary>
internal sealed class VoiceFft
{
    private readonly float[] _cos;
    private readonly float[] _sin;
    private readonly int[] _reverse;

    public VoiceFft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "The size must be a power of two.");
        }

        Size = size;
        (double[] c, double[] s) = UnitRoots(size);
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (int k = 0; k < size / 2; k++)
        {
            _cos[k] = (float)c[k];
            _sin[k] = (float)s[k];
        }

        _reverse = new int[size];
        int bits = 0;
        while ((1 << bits) < size)
        {
            bits++;
        }

        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++)
            {
                r |= ((i >> b) & 1) << (bits - 1 - b);
            }

            _reverse[i] = r;
        }
    }

    public int Size { get; }

    /// <summary>
    /// cos and sin of 2 pi k / <paramref name="order"/> for k = 0 .. order - 1: the base angle by halving pi, then
    /// powers of the base root.
    /// </summary>
    public static (double[] Cos, double[] Sin) UnitRoots(int order)
    {
        double c = -1.0;
        double s = 0.0;
        int angle = 2;
        while (angle < order)
        {
            // From 2 pi / angle to 2 pi / (2 angle): the half-angle formula.
            double half = Math.Sqrt((1.0 + c) / 2.0);
            s = Math.Sqrt((1.0 - c) / 2.0);
            c = half;
            angle *= 2;
        }

        if (order == 1)
        {
            c = 1.0;
            s = 0.0;
        }

        var cos = new double[order];
        var sin = new double[order];
        double rc = 1.0;
        double rs = 0.0;
        for (int k = 0; k < order; k++)
        {
            cos[k] = rc;
            sin[k] = rs;
            double nc = rc * c - rs * s;
            rs = rc * s + rs * c;
            rc = nc;
        }

        return (cos, sin);
    }

    /// <summary>In place, e^(-i...) kernel, no scaling.</summary>
    public void Forward(float[] re, float[] im) => Transform(re, im, -1f);

    /// <summary>In place, e^(+i...) kernel, scaled by 1 / size.</summary>
    public void Inverse(float[] re, float[] im)
    {
        Transform(re, im, 1f);
        float k = 1f / Size;
        for (int i = 0; i < Size; i++)
        {
            re[i] *= k;
            im[i] *= k;
        }
    }

    private void Transform(float[] re, float[] im, float sign)
    {
        int n = Size;
        for (int i = 0; i < n; i++)
        {
            int j = _reverse[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1;
            int step = n / len;
            for (int start = 0; start < n; start += len)
            {
                for (int j = 0; j < half; j++)
                {
                    float wr = _cos[j * step];
                    float wi = sign * _sin[j * step];
                    int a = start + j;
                    int b = a + half;
                    float tr = re[b] * wr - im[b] * wi;
                    float ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
