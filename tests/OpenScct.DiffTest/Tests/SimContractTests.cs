using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using OpenScct.DiffTest.Compare;
using OpenScct.DiffTest.Golden;
using OpenScct.DiffTest.Model;
using OpenScct.DiffTest.Sim;
using Xunit;

namespace OpenScct.DiffTest.Tests;

/// <summary>
/// The sim runner contract, exercised with stand-in runners until the OpenScct.Physics / OpenScct.Game runner exists: a
/// runner that hands back the reference run passes every scenario, one that drifts fails.
/// </summary>
public sealed class SimContractTests
{
    /// <summary>Plays back the reference run, optionally pushed sideways: a stand-in for a real runner.</summary>
    private sealed class PlaybackSim(double drift) : IScenarioSim
    {
        public string Name => drift == 0 ? "playback" : $"playback+{drift}";

        public bool CanRun(Scenario scenario) => true;

        public TraceRun Run(SimRequest request)
        {
            var frames = new List<TraceFrame> { request.Start };
            var byFrame = request.Reference.Frames.ToDictionary(f => f.F);
            foreach ((int frame, double _, FrameInput input) in request.Steps())
            {
                if (byFrame.TryGetValue(frame, out TraceFrame? f))
                {
                    frames.Add(f with { Loc = f.Loc + new Vec3(drift, 0, 0), Input = input });
                }
            }

            return new TraceRun(new JsonObject { ["scenario"] = new JsonObject { ["id"] = request.Scenario.Id } }, frames, null);
        }
    }

    [Fact]
    public void StepsFollowTheReferenceOrTheFixedTick()
    {
        Scenario s = Scenario.Load(Path.Combine(RepoPaths.Scenarios, "movement", "spy-run-forward.scenario.json"));
        TraceRun reference = Synthetic.Walk(s.Id, 1, s.DurationFrames, 423.5, 3300, 180, k => 1.0 / 60.0 + (k % 2 * 1e-4));
        var replay = new SimRequest(s, reference, SimTiming.DtReplay).Steps().ToList();
        Assert.Equal(s.DurationFrames, replay.Count);
        Assert.Equal(1.0 / 60.0 + 1e-4, replay[1].Dt, 9);
        Assert.Equal(1, replay[0].Input.Button("BT_Up"));
        var fixedSteps = new SimRequest(s, reference, SimTiming.Fixed).Steps().ToList();
        Assert.Equal(s.DurationFrames, fixedSteps.Count);
        Assert.All(fixedSteps, st => Assert.Equal(1.0 / 60.0, st.Dt, 12));
        Assert.Equal(0, fixedSteps[180].Input.Button("BT_Up"));
    }

    [Fact]
    public void HarnessPassesAFaithfulRunnerAndFailsADriftingOne()
    {
        string root = Directory.CreateTempSubdirectory("svm-difftest-").FullName;
        try
        {
            Scenario s = Scenario.Load(Path.Combine(RepoPaths.Scenarios, "movement", "spy-run-forward.scenario.json"));
            Directory.CreateDirectory(Path.Combine(root, s.Id));
            for (int run = 1; run <= 3; run++)
            {
                int r = run;
                TraceRun golden = Synthetic.Walk(s.Id, run, s.DurationFrames, 423.5, 3300, 180, k => 1.0 / 60.0 + (((k + r) % 3) * 2e-5));
                TraceFile.Write(Path.Combine(root, s.Id, $"run{run}.jsonl.gz"), golden.Header, golden.Frames);
            }

            var store = new GoldenStore(root);
            ComparisonResult good = DiffHarness.Run(new PlaybackSim(0), store, [s]).Single();
            Assert.True(good.Pass, good.Describe());
            ComparisonResult bad = DiffHarness.Run(new PlaybackSim(5.0), store, [s]).Single();
            Assert.False(bad.Pass, bad.Describe());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PlaybackOfEveryGoldenPasses()
    {
        GoldenStore store = GoldenStore.Default();
        var scenarios = RepoPaths.ScenarioFiles().Select(Scenario.Load).Where(s => store.Has(s.Id)).ToList();
        if (scenarios.Count == 0)
        {
            Assert.Skip($"no golden traces under {store.Root}");
        }

        var results = DiffHarness.Run(new PlaybackSim(0), store, scenarios).ToList();
        Assert.Equal(scenarios.Count, results.Count);
        Assert.All(results, r => Assert.True(r.Pass, r.Describe()));
    }
}
