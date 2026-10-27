using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScctGame.Smoke;

/// <summary>The frame times of a run: the median, the spread and the slow frames.</summary>
public readonly record struct FrameTimeStats(int Frames, double MedianMs, double P1Ms, double P99Ms, double StdDevMs, double Spread, int Slow)
{
    /// <summary>The median frame rate.</summary>
    public double Fps => MedianMs > 0 ? 1000.0 / MedianMs : 0;
}

/// <summary>
/// How evenly one drawn value moves from frame to frame while it moves steadily: frames where it held still, the
/// spread of its per-frame speed, and the step jitter, the error of each frame's step against the step the frame's
/// length asks for at the mean speed, as a share of the mean step (an interpolation alpha error of 0.1 tick at 144
/// fps is about 0.24 here). <see cref="Steps"/> counts the still frames in short runs between moving frames (at most
/// <see cref="FrameMotion.StepRun"/> long): a value that moves only at the tick rate, while a stop (a wall) is a long
/// run.
/// </summary>
public readonly record struct MotionStats(int Frames, int Still, double MeanStep, double Spread, double Jitter, int Steps = 0)
{
    public bool Moved => Frames > 1 && MeanStep > 0;
}

/// <summary>The measures of the frame-rate smoke (Godot-free, so the logic tests check them too).</summary>
public static class FrameMotion
{
    /// <summary>A frame longer than this many medians counts as slow.</summary>
    public const double SlowFactor = 1.5;

    /// <summary>The longest run of still frames between moving frames counted as stepping (240 fps holds 3 of 4).</summary>
    public const int StepRun = 4;

    /// <summary>The frame times between the samples at <paramref name="timesMs"/> (the frames' starts).</summary>
    public static FrameTimeStats Times(IReadOnlyList<double> timesMs)
    {
        ArgumentNullException.ThrowIfNull(timesMs);
        var dt = new List<double>();
        for (int i = 1; i < timesMs.Count; i++)
        {
            dt.Add(timesMs[i] - timesMs[i - 1]);
        }

        if (dt.Count == 0)
        {
            return default;
        }

        double[] sorted = dt.OrderBy(d => d).ToArray();
        double median = sorted[sorted.Length / 2];
        double mean = dt.Average();
        double sd = Math.Sqrt(dt.Sum(d => (d - mean) * (d - mean)) / dt.Count);
        double p10 = sorted[sorted.Length / 10], p90 = sorted[sorted.Length * 9 / 10];
        return new FrameTimeStats(dt.Count, median, sorted[sorted.Length / 100], sorted[Math.Min(sorted.Length - 1, sorted.Length * 99 / 100)], sd,
            p10 > 0 ? p90 / p10 : double.PositiveInfinity, dt.Count(d => d > SlowFactor * median));
    }

    /// <summary>
    /// The motion of <paramref name="values"/> drawn at <paramref name="timesMs"/>; a step no larger than
    /// <paramref name="stillEpsilon"/> is a frame that held still; still runs up to <paramref name="stepRun"/> frames
    /// between moving ones count as <see cref="MotionStats.Steps"/>.
    /// </summary>
    public static MotionStats Motion(IReadOnlyList<double> timesMs, IReadOnlyList<double> values, double stillEpsilon, int stepRun = StepRun)
    {
        ArgumentNullException.ThrowIfNull(timesMs);
        ArgumentNullException.ThrowIfNull(values);
        int n = Math.Min(timesMs.Count, values.Count);
        if (n < 3)
        {
            return new MotionStats(n, 0, 0, 0, 0);
        }

        double span = timesMs[n - 1] - timesMs[0];
        double speed = span > 0 ? (values[n - 1] - values[0]) / span : 0;
        int still = 0;
        var speeds = new List<double>();
        double steps = 0, errors = 0;
        for (int i = 1; i < n; i++)
        {
            double d = values[i] - values[i - 1];
            double dt = timesMs[i] - timesMs[i - 1];
            if (Math.Abs(d) <= stillEpsilon)
            {
                still++;
            }

            if (dt > 0)
            {
                speeds.Add(Math.Abs(d) / dt);
            }

            steps += Math.Abs(d);
            double e = d - (speed * dt);
            errors += e * e;
        }

        int stepping = 0, run = 0;
        bool movedBefore = false;
        for (int i = 1; i < n; i++)
        {
            if (Math.Abs(values[i] - values[i - 1]) <= stillEpsilon)
            {
                run++;
                continue;
            }

            if (movedBefore && run > 0 && run <= stepRun)
            {
                stepping += run;
            }

            movedBefore = true;
            run = 0;
        }

        double meanStep = steps / (n - 1);
        double jitter = meanStep > 0 ? Math.Sqrt(errors / (n - 1)) / meanStep : 0;
        speeds.Sort();
        double low = speeds.Count > 0 ? speeds[speeds.Count / 10] : 0;
        double spread = speeds.Count == 0 ? 0 : low > 0 ? speeds[speeds.Count * 9 / 10] / low : double.PositiveInfinity;
        return new MotionStats(n, still, meanStep, spread, jitter, stepping);
    }
}
