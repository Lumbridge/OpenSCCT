using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Server;

namespace OpenScct.Integration.Tests;

/// <summary>Input patterns for the test walkers: two circles, a weave and a strafing zig-zag.</summary>
internal static class WalkPatterns
{
    public static CubeInput For(int client, long tick) => (client % 4) switch
    {
        0 => new CubeInput(127, 0, (ushort)(tick * 200)),
        1 => new CubeInput(127, 0, (ushort)(16384 - tick * 150)),
        2 => new CubeInput(127, 0, (ushort)(32768 + (int)(8000f * SvmMath.Sin((int)(tick * 273))))),
        _ => new CubeInput(80, (sbyte)(SvmMath.Sin((int)(tick * 364)) >= 0f ? 127 : -127), 49152),
    };
}

/// <summary>Distance samples and their summary.</summary>
internal sealed class ErrorStats
{
    private readonly List<float> _samples = new();

    public int Count => _samples.Count;

    public void Add(float error) => _samples.Add(error);

    public float Max
    {
        get
        {
            float max = 0f;
            foreach (float s in _samples)
            {
                max = Math.Max(max, s);
            }

            return max;
        }
    }

    public float Mean
    {
        get
        {
            double sum = 0;
            foreach (float s in _samples)
            {
                sum += s;
            }

            return _samples.Count == 0 ? 0f : (float)(sum / _samples.Count);
        }
    }

    public float Percentile(double p)
    {
        if (_samples.Count == 0)
        {
            return 0f;
        }

        var sorted = new List<float>(_samples);
        sorted.Sort();
        int index = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    /// <summary>Share of samples at or below <paramref name="limit"/>, in percent.</summary>
    public double WithinPercent(float limit)
    {
        if (_samples.Count == 0)
        {
            return 100.0;
        }

        int within = 0;
        foreach (float s in _samples)
        {
            if (s <= limit)
            {
                within++;
            }
        }

        return 100.0 * within / _samples.Count;
    }

    public override string ToString() =>
        FormattableString.Invariant($"n={Count} mean={Mean:F3} p50={Percentile(50):F3} p95={Percentile(95):F3} p99={Percentile(99):F3} max={Max:F3} uu");
}
