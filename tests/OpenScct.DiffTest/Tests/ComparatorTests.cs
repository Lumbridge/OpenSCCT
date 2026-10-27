using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.DiffTest.Compare;
using OpenScct.DiffTest.Model;
using Xunit;

namespace OpenScct.DiffTest.Tests;

public sealed class ComparatorTests
{
    private const double Dt = 1.0 / 60.0;

    private static TraceRun Walk(int run, double yOffset = 0, Func<int, double>? dt = null, int release = 180) =>
        Synthetic.Walk("movement.synthetic", run, 240, 423.5, 3300, release, dt ?? (_ => Dt), yOffset);

    [Fact]
    public void IdenticalRunsHaveZeroDistance()
    {
        RunMetrics m = RunMetrics.Compare(Walk(1), Walk(2));
        Assert.Equal(0, m.LocRms, 9);
        Assert.Equal(0, m.LocEnd, 9);
        Assert.Equal(0, m.VelRms, 9);
        Assert.Equal(240, m.Samples);
    }

    [Fact]
    public void ConstantOffsetShowsInEveryLocationMetric()
    {
        RunMetrics m = RunMetrics.Compare(Walk(1), Walk(2, yOffset: 1.5));
        Assert.Equal(1.5, m.LocRms, 6);
        Assert.Equal(1.5, m.LocMax, 6);
        Assert.Equal(1.5, m.LocEnd, 6);
        Assert.Equal(0, m.VelRms, 6);
    }

    [Fact]
    public void RunsAreComparedOnTheirOwnClocks()
    {
        // Same motion, different frame times: interpolation on the common clock keeps the distance small.
        TraceRun a = Walk(1);
        TraceRun b = Walk(2, dt: k => k % 2 == 0 ? Dt * 0.9 : Dt * 1.1);
        RunMetrics m = RunMetrics.Compare(a, b);
        Assert.True(m.LocRms < 1.0, $"locRms {m.LocRms}");
        Assert.True(m.Seconds > 3.9 && m.Seconds < 4.1, $"seconds {m.Seconds}");
    }

    [Fact]
    public void ToleranceIsTheProfileLimitOrOneAndAHalfTimesTheNoise()
    {
        ComparisonProfile p = ComparisonProfile.Locomotion;
        Assert.Equal(2.0, p.Tolerance(MetricKind.LocRms, new RunMetrics(0.5, 0, 0, 0, 1, 1)));
        Assert.Equal(4.5, p.Tolerance(MetricKind.LocRms, new RunMetrics(3.0, 0, 0, 0, 1, 1)), 9);
        Assert.Equal(10.0, p.Tolerance(MetricKind.LocEnd, new RunMetrics(0, 0, 6.0, 0, 1, 1)));
        Assert.Equal(15.0, p.Tolerance(MetricKind.VelRms, new RunMetrics(0, 0, 0, 10.0, 1, 1)), 9);
        Assert.Equal(2.0, p.Limits[MetricKind.LocRms]);
        Assert.Equal(10.0, p.Limits[MetricKind.LocEnd]);
        Assert.Equal(10.0, p.Limits[MetricKind.VelRms]);
    }

    [Fact]
    public void ACandidateWithinTheProfilePasses()
    {
        var golden = new List<TraceRun> { Walk(1), Walk(2), Walk(3) };
        ComparisonResult r = Comparator.Compare(Walk(9, yOffset: 1.0), golden, ComparisonProfile.Locomotion);
        Assert.True(r.Pass, r.Describe());
        Assert.True(r.SameStates);
    }

    [Fact]
    public void ACandidateOutsideTheProfileFails()
    {
        var golden = new List<TraceRun> { Walk(1), Walk(2), Walk(3) };
        ComparisonResult r = Comparator.Compare(Walk(9, yOffset: 3.0), golden, ComparisonProfile.Locomotion);
        Assert.False(r.Pass, r.Describe());
        Assert.Contains(r.Verdicts, v => v.Kind == MetricKind.LocRms && !v.Pass);
    }

    [Fact]
    public void NoisyGoldensWidenTheTolerance()
    {
        // Goldens 0, 2.5 and 5 uu apart: the envelope's locRms is 5, so 1.5 x 5 = 7.5 uu is tolerated.
        var golden = new List<TraceRun> { Walk(1), Walk(2, yOffset: 2.5), Walk(3, yOffset: 5.0) };
        NoiseEnvelope env = NoiseEnvelope.Of(golden);
        Assert.Equal(5.0, env.Worst.LocRms, 6);
        Assert.Equal("run2", env.Reference.Name);
        ComparisonResult r = Comparator.Compare(Walk(9, yOffset: 8.0), golden, ComparisonProfile.Locomotion);
        Assert.True(r.Pass, r.Describe());
        ComparisonResult far = Comparator.Compare(Walk(9, yOffset: 12.0), golden, ComparisonProfile.Locomotion);
        Assert.False(far.Pass, far.Describe());
        Assert.Equal(new[] { MetricKind.LocRms }, r.NoiseDominated.ToArray());
    }

    [Fact]
    public void ALateStopShowsInTheEndDistanceAndVelocity()
    {
        RunMetrics m = RunMetrics.Compare(Walk(1), Walk(2, release: 182));
        Assert.Equal(2 * 423.5 * Dt, m.LocEnd, 3);
        Assert.True(m.VelRms > 10, $"velRms {m.VelRms}");
    }

    [Fact]
    public void StateSequencesAreCompared()
    {
        TraceRun a = Synthetic.Jump("movement.synthetic", 120, 30, 610, -1500, Dt);
        TraceRun b = Walk(2);
        List<string> diffs = Comparator.StateDifferences(a, b);
        Assert.Equal(2, diffs.Count);
        Assert.Empty(Comparator.StateDifferences(a, a));
        Assert.Equal(new[] { "s_PlayerWalkingSt0H", "s_PlayerJump", "s_PlayerWalkingSt0H" },
            Comparator.Sequence(a, f => f.ControllerState).ToArray());
    }
}
