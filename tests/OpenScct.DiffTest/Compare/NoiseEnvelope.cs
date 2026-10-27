using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.DiffTest.Model;

namespace OpenScct.DiffTest.Compare;

/// <summary>
/// How much the original disagrees with itself: every pair of golden runs of a scenario compared with
/// <see cref="RunMetrics.Compare"/>, the worst value of each metric kept. The reference run for comparisons is the
/// medoid, the run with the smallest summed location RMS to the others.
/// </summary>
public sealed class NoiseEnvelope
{
    private NoiseEnvelope(RunMetrics worst, TraceRun reference, IReadOnlyList<(string A, string B, RunMetrics M)> pairs)
    {
        Worst = worst;
        Reference = reference;
        Pairs = pairs;
    }

    public RunMetrics Worst { get; }

    public TraceRun Reference { get; }

    public IReadOnlyList<(string A, string B, RunMetrics M)> Pairs { get; }

    public static NoiseEnvelope Of(IReadOnlyList<TraceRun> runs)
    {
        if (runs.Count == 0)
        {
            throw new ArgumentException("no runs", nameof(runs));
        }

        var pairs = new List<(string, string, RunMetrics)>();
        var total = new double[runs.Count];
        for (int i = 0; i < runs.Count; i++)
        {
            for (int j = i + 1; j < runs.Count; j++)
            {
                RunMetrics m = RunMetrics.Compare(runs[i], runs[j]);
                pairs.Add((runs[i].Name, runs[j].Name, m));
                total[i] += m.LocRms;
                total[j] += m.LocRms;
            }
        }

        RunMetrics worst = pairs.Count == 0
            ? RunMetrics.Zero
            : new RunMetrics(
                pairs.Max(p => p.Item3.LocRms),
                pairs.Max(p => p.Item3.LocMax),
                pairs.Max(p => p.Item3.LocEnd),
                pairs.Max(p => p.Item3.VelRms),
                pairs.Min(p => p.Item3.Samples),
                pairs.Min(p => p.Item3.Seconds));
        int best = 0;
        for (int i = 1; i < runs.Count; i++)
        {
            if (total[i] < total[best])
            {
                best = i;
            }
        }

        return new NoiseEnvelope(worst, runs[best], pairs);
    }
}
