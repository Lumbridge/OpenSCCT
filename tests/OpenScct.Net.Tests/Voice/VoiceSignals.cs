using System;
using OpenScct.Net.Voice;

namespace OpenScct.Net.Tests.Voice;

/// <summary>Synthetic signals and measurements for the voice tests.</summary>
internal static class VoiceSignals
{
    /// <summary>A sine tone of <paramref name="seconds"/> at <paramref name="rate"/>, -1 to 1.</summary>
    public static float[] Tone(double hz, double seconds, int rate = VoiceK.SampleRate, float amplitude = 0.3f)
    {
        var samples = new float[(int)(seconds * rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = amplitude * (float)Math.Sin(2 * Math.PI * hz * i / rate);
        }

        return samples;
    }

    /// <summary>A two-tone vowel-like test signal (a fundamental and a weaker overtone), so the codec sees voice-like content.</summary>
    public static float[] Voice(double seconds, int rate = VoiceK.SampleRate)
    {
        var samples = new float[(int)(seconds * rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / rate;
            samples[i] = (float)(0.25 * Math.Sin(2 * Math.PI * 220 * t) + 0.1 * Math.Sin(2 * Math.PI * 660 * t));
        }

        return samples;
    }

    public static short[] ToPcm(ReadOnlySpan<float> samples)
    {
        var pcm = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            pcm[i] = (short)Math.Clamp(samples[i] * 32767f, -32768f, 32767f);
        }

        return pcm;
    }

    /// <summary>Power of <paramref name="hz"/> in <paramref name="samples"/> (Goertzel), normalised by the length.</summary>
    public static double Power(ReadOnlySpan<float> samples, double hz, int rate = VoiceK.SampleRate)
    {
        double w = 2 * Math.PI * hz / rate;
        double coeff = 2 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        foreach (float x in samples)
        {
            double s0 = x + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }

        double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
        return power / ((double)samples.Length * samples.Length);
    }

    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (float x in samples)
        {
            sum += (double)x * x;
        }

        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>The frequency of a clean tone from its zero crossings.</summary>
    public static double ZeroCrossingHz(ReadOnlySpan<float> samples, int rate = VoiceK.SampleRate)
    {
        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] < 0f) != (samples[i] < 0f))
            {
                crossings++;
            }
        }

        return crossings / 2.0 * rate / samples.Length;
    }

    /// <summary>
    /// Best normalised correlation of <paramref name="decoded"/> with <paramref name="original"/> over delays of 0 to
    /// <paramref name="maxDelay"/> samples (the codec's look-ahead), and that delay.
    /// </summary>
    public static (double Correlation, int Delay) BestCorrelation(ReadOnlySpan<float> original, ReadOnlySpan<float> decoded, int maxDelay)
    {
        double best = double.MinValue;
        int bestDelay = 0;
        for (int d = 0; d <= maxDelay; d++)
        {
            int n = Math.Min(original.Length, decoded.Length - d);
            double xy = 0, xx = 0, yy = 0;
            for (int i = 0; i < n; i++)
            {
                double x = original[i];
                double y = decoded[i + d];
                xy += x * y;
                xx += x * x;
                yy += y * y;
            }

            double c = xx > 0 && yy > 0 ? xy / Math.Sqrt(xx * yy) : 0;
            if (c > best)
            {
                best = c;
                bestDelay = d;
            }
        }

        return (best, bestDelay);
    }
}
