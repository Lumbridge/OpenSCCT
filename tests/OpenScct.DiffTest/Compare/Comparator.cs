using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenScct.DiffTest.Model;

namespace OpenScct.DiffTest.Compare;

/// <summary>One metric's result.</summary>
public sealed record MetricVerdict(MetricKind Kind, double Value, double ProfileLimit, double Noise, double Tolerance)
{
    public bool Pass => Value <= Tolerance;
}

/// <summary>The result of comparing a candidate run (the rewrite's) with a scenario's golden runs.</summary>
public sealed record ComparisonResult(
    string ScenarioId,
    ComparisonProfile Profile,
    RunMetrics Metrics,
    NoiseEnvelope Envelope,
    IReadOnlyList<MetricVerdict> Verdicts,
    bool SameStates,
    IReadOnlyList<string> StateDifferences)
{
    public bool Pass => Verdicts.All(v => v.Pass) && (!Profile.RequireSameStates || SameStates);

    /// <summary>
    /// The metrics where the original's own spread, not the profile, sets the tolerance: the gate is only as
    /// tight as the original repeats itself (a landing that falls on either side of a frame, for example).
    /// </summary>
    public IReadOnlyList<MetricKind> NoiseDominated => Verdicts.Where(v => v.Tolerance > v.ProfileLimit).Select(v => v.Kind).ToList();

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{ScenarioId} [{Profile.Name}] vs {Envelope.Reference.Name}: ");
        sb.Append(Pass ? "pass" : "FAIL");
        foreach (MetricVerdict v in Verdicts)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"; {v.Kind} {v.Value:0.###} <= {v.Tolerance:0.###} (profile {v.ProfileLimit:0.###}, noise {v.Noise:0.###})");
        }

        if (!SameStates)
        {
            sb.Append("; states differ: ").Append(string.Join(" | ", StateDifferences));
        }

        if (NoiseDominated.Count > 0)
        {
            sb.Append("; noise-dominated: ").Append(string.Join(", ", NoiseDominated));
        }

        return sb.ToString();
    }
}

/// <summary>Compares a candidate run with the golden runs of its scenario under a profile.</summary>
public static class Comparator
{
    public static ComparisonResult Compare(TraceRun candidate, IReadOnlyList<TraceRun> golden, ComparisonProfile profile)
    {
        NoiseEnvelope envelope = NoiseEnvelope.Of(golden);
        RunMetrics metrics = RunMetrics.Compare(envelope.Reference, candidate);
        var verdicts = profile.Limits.Keys
            .OrderBy(k => k)
            .Select(k => new MetricVerdict(k, metrics[k], profile.Limits[k], envelope.Worst[k], profile.Tolerance(k, envelope.Worst)))
            .ToList();
        List<string> diffs = StateDifferences(envelope.Reference, candidate);
        return new ComparisonResult(candidate.ScenarioId.Length > 0 ? candidate.ScenarioId : envelope.Reference.ScenarioId,
            profile, metrics, envelope, verdicts, diffs.Count == 0, diffs);
    }

    /// <summary>The sequences of controller states and of Physics modes (consecutive repeats removed).</summary>
    public static IReadOnlyList<string> Sequence(TraceRun run, Func<TraceFrame, string?> key)
    {
        var list = new List<string>();
        foreach (TraceFrame f in run.Played)
        {
            string k = key(f) ?? "";
            if (list.Count == 0 || list[^1] != k)
            {
                list.Add(k);
            }
        }

        return list;
    }

    public static List<string> StateDifferences(TraceRun reference, TraceRun candidate)
    {
        var diffs = new List<string>();
        Compare("controller", f => f.ControllerState);
        Compare("physics", f => f.Physics.ToString(CultureInfo.InvariantCulture));
        return diffs;

        void Compare(string label, Func<TraceFrame, string?> key)
        {
            IReadOnlyList<string> a = Sequence(reference, key);
            IReadOnlyList<string> b = Sequence(candidate, key);
            if (!a.SequenceEqual(b))
            {
                diffs.Add($"{label}: [{string.Join(", ", a)}] vs [{string.Join(", ", b)}]");
            }
        }
    }
}
