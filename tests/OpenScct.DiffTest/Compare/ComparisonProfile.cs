using System;
using System.Collections.Generic;

namespace OpenScct.DiffTest.Compare;

/// <summary>
/// Pass limits for one kind of scenario. A metric passes when it is at most
/// <c>max(profile limit, NoiseFactor x the golden runs' noise envelope)</c>: the original does not repeat itself
/// exactly (frame times jitter), so a limit tighter than the original's own spread would fail the original.
/// </summary>
public sealed record ComparisonProfile(string Name, IReadOnlyDictionary<MetricKind, double> Limits, double NoiseFactor,
    bool RequireSameStates)
{
    /// <summary>The noise envelope multiplier of the plan (Track R, R4).</summary>
    public const double DefaultNoiseFactor = 1.5;

    /// <summary>Walking, running, crouching, jumping and falling: location RMS 2 uu, end 10 uu, velocity RMS 10 uu/s.</summary>
    public static readonly ComparisonProfile Locomotion = new(
        "locomotion",
        new Dictionary<MetricKind, double>
        {
            [MetricKind.LocRms] = 2.0,
            [MetricKind.LocEnd] = 10.0,
            [MetricKind.VelRms] = 10.0,
        },
        DefaultNoiseFactor,
        RequireSameStates: false);

    public static ComparisonProfile ByName(string name) => name switch
    {
        "locomotion" => Locomotion,
        _ => throw new ArgumentException($"unknown comparison profile {name}", nameof(name)),
    };

    /// <summary>The limit actually applied to a metric given the noise envelope.</summary>
    public double Tolerance(MetricKind kind, RunMetrics envelope) =>
        Math.Max(Limits[kind], NoiseFactor * envelope[kind]);
}
