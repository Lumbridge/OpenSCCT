using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.DiffTest.Compare;
using OpenScct.DiffTest.Model;
using OpenScct.DiffTest.Sim;

namespace OpenScct.DiffTest.Golden;

/// <summary>
/// Golden traces recorded from the original by re/tools/difftest/record.py: <c>&lt;root&gt;/&lt;scenario id&gt;/run&lt;N&gt;.jsonl.gz</c>.
/// The root is <c>SVM_DIFFTEST_GOLDEN</c> when set, else <c>local/difftest/golden</c> in the repository. Goldens are
/// game-derived and never in git, so CI has none and the tests that need them skip.
/// </summary>
public sealed class GoldenStore
{
    public GoldenStore(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public bool Exists => Directory.Exists(Root);

    public static GoldenStore Default()
    {
        string? env = Environment.GetEnvironmentVariable("SVM_DIFFTEST_GOLDEN");
        return new GoldenStore(env is { Length: > 0 } ? env : Path.Combine(RepoPaths.Root, "local", "difftest", "golden"));
    }

    public string Folder(string scenarioId) => Path.Combine(Root, scenarioId);

    public bool Has(string scenarioId) => Directory.Exists(Folder(scenarioId)) && RunFiles(scenarioId).Any();

    public IEnumerable<string> RunFiles(string scenarioId) =>
        Directory.Exists(Folder(scenarioId))
            ? Directory.EnumerateFiles(Folder(scenarioId), "run*.jsonl.gz").OrderBy(p => p, StringComparer.Ordinal)
            : [];

    public IReadOnlyList<TraceRun> Load(string scenarioId) => RunFiles(scenarioId).Select(TraceFile.Read).ToList();
}

/// <summary>Plays every scenario that has goldens through a sim runner and compares the result.</summary>
public static class DiffHarness
{
    public static IEnumerable<ComparisonResult> Run(IScenarioSim sim, GoldenStore store, IEnumerable<Scenario> scenarios,
        SimTiming timing = SimTiming.DtReplay)
    {
        foreach (Scenario scenario in scenarios)
        {
            if (!sim.CanRun(scenario) || !store.Has(scenario.Id))
            {
                continue;
            }

            IReadOnlyList<TraceRun> golden = store.Load(scenario.Id);
            NoiseEnvelope envelope = NoiseEnvelope.Of(golden);
            TraceRun candidate = sim.Run(new SimRequest(scenario, envelope.Reference, timing));
            yield return Comparator.Compare(candidate, golden, ComparisonProfile.ByName(scenario.Profile));
        }
    }
}

/// <summary>Repository locations.</summary>
public static class RepoPaths
{
    private static readonly Lazy<string> s_root = new(FindRoot);

    /// <summary>The repository root (the folder holding OpenSCCT.slnx).</summary>
    public static string Root => s_root.Value;

    public static string Scenarios => Path.Combine(Root, "tests", "scenarios");

    public static IEnumerable<string> ScenarioFiles() =>
        Directory.EnumerateFiles(Scenarios, "*.scenario.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal);

    private static string FindRoot()
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("The repository root (OpenSCCT.slnx) was not found above the test binaries.");
    }
}
