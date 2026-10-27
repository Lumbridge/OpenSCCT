using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.DiffTest.Compare;
using OpenScct.DiffTest.Golden;
using OpenScct.DiffTest.Model;

namespace OpenScct.DiffTest.Sim;

/// <summary>
/// Comparison profiles by scenario profile name. Locomotion is <see cref="ComparisonProfile.Locomotion"/>.
/// Traversal is provisional until the comparator has its own (spec: Track R, R4, "traversal exact state/anim
/// sequence +-2 frames"): same controller and Physics sequences, every phase starting within
/// <see cref="TraversalPhaseFrames"/> frames of the reference, and location limits loose enough for the one-frame
/// snaps of a grab or the end of a root-motion move.
/// </summary>
public static class SimProfiles
{
    public const int TraversalPhaseFrames = 2;

    public static readonly ComparisonProfile Traversal = new(
        "traversal",
        new Dictionary<MetricKind, double>
        {
            [MetricKind.LocRms] = 5.0,
            [MetricKind.LocEnd] = 10.0,
            [MetricKind.VelRms] = 30.0,
        },
        ComparisonProfile.DefaultNoiseFactor,
        RequireSameStates: true);

    public static ComparisonProfile ByName(string name) => name == "traversal" ? Traversal : ComparisonProfile.ByName(name);
}

/// <summary>
/// Where the goldens are: <see cref="GoldenStore.Default"/> (the locomotion goldens) and the same root with
/// <c>-traversal</c> appended (the traversal goldens, kept apart while GoldenTraceTests checks every golden under
/// the first root by locomotion rules; docs/spikes/R4-traversal.md).
/// </summary>
public static class SimGoldens
{
    public static IReadOnlyList<GoldenStore> Stores()
    {
        GoldenStore main = GoldenStore.Default();
        return [main, new GoldenStore(main.Root.TrimEnd('\\', '/') + "-traversal")];
    }

    public static GoldenStore? StoreFor(string scenarioId) => Stores().FirstOrDefault(s => s.Has(scenarioId));

    /// <summary>
    /// The store of a scenario including the moveset goldens (golden-moveset, docs/spikes/R4-moveset.md), which only
    /// the movement runner compares so far.
    /// </summary>
    public static GoldenStore? StoreWithMovesetFor(string scenarioId)
    {
        if (StoreFor(scenarioId) is { } store)
        {
            return store;
        }

        var moveset = new GoldenStore(GoldenStore.Default().Root.TrimEnd('\\', '/') + "-moveset");
        return moveset.Has(scenarioId) ? moveset : null;
    }
}

/// <summary>
/// Plays scenarios through a sim runner against their goldens and reports per scenario (<see cref="SimReport"/>).
/// The same steps as <see cref="DiffHarness"/>, with the traversal profile, measurements and phase timing added.
/// </summary>
public static class SimHarness
{
    /// <summary>Every scenario with goldens and a runnable setup, in file order.</summary>
    public static IEnumerable<Scenario> Scenarios(IScenarioSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return RepoPaths.ScenarioFiles().Select(Scenario.Load).Where(s => SimGoldens.StoreFor(s.Id) is not null && sim.CanRun(s));
    }

    /// <summary>Plays one scenario against its goldens.</summary>
    public static SimReport Run(IScenarioSim sim, Scenario scenario, SimTiming timing)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(scenario);
        GoldenStore store = SimGoldens.StoreFor(scenario.Id)
            ?? throw new InvalidOperationException($"no goldens for {scenario.Id}");
        return Run(sim, scenario, store.Load(scenario.Id), timing);
    }

    public static SimReport Run(IScenarioSim sim, Scenario scenario, IReadOnlyList<TraceRun> golden, SimTiming timing)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(golden);
        NoiseEnvelope envelope = NoiseEnvelope.Of(golden);
        TraceRun candidate = sim.Run(new SimRequest(scenario, envelope.Reference, timing));
        return SimReport.Build(sim.Name, scenario, golden, envelope, candidate, timing);
    }

    /// <summary>
    /// The folder sim runs and reports are written to: <c>local/difftest/sim</c> (game-derived numbers, never
    /// tracked), or <c>SVM_DIFFTEST_SIM_OUT</c> when set.
    /// </summary>
    public static string OutputRoot
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("SVM_DIFFTEST_SIM_OUT");
            return env is { Length: > 0 } ? env : Path.Combine(RepoPaths.Root, "local", "difftest", "sim");
        }
    }

    /// <summary>Writes the sim run as svm.trace/1 under <see cref="OutputRoot"/>; returns the path.</summary>
    public static string WriteRun(SimReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string dir = Path.Combine(OutputRoot, report.SimName, report.Timing.ToString());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, report.ScenarioId + ".jsonl.gz");
        TraceFile.Write(path, report.Candidate.Header, report.Candidate.Frames, report.Candidate.Footer);
        return path;
    }
}
