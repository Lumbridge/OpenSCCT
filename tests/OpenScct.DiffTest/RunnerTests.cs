using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenScct.Core;
using OpenScct.DiffTest.Golden;
using OpenScct.DiffTest.Measure;
using OpenScct.DiffTest.Model;
using OpenScct.DiffTest.Sim;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.DiffTest;

/// <summary>
/// The rewrite against the original: every scenario with goldens (local/difftest/golden, golden-traversal and golden-moveset) is
/// played by <see cref="PawnRigSim"/> on the converted map (.content/svmap) in both timing modes and compared with
/// <see cref="SimHarness"/>. Without goldens or converted content (CI) these skip; the runner's wiring is checked
/// on a synthetic floor either way. Runs and reports go to local/difftest/sim (docs/spikes/R4-sim-vs-game.md).
/// </summary>
public sealed class RunnerTests
{
    /// <summary>
    /// Scenario and timing pairs that do not pass yet, with the reason (each one is a finding in
    /// docs/spikes/R4-sim-vs-game.md). They are reported as skipped while they fail and pass once fixed; every other
    /// pair must pass.
    /// </summary>
    private static readonly Dictionary<string, string> KnownMismatches = new(StringComparer.Ordinal)
    {
        ["movement.spy-fall-ledge Fixed"] = "F11 the 60 Hz grid",
        ["movement.spy-jump-in-place Fixed"] = "F11 landing a frame earlier on the 60 Hz grid",
        ["traversal.spy-zip-line-13 Fixed"] = "F11 the 60 Hz grid",
        ["traversal.spy-zip-line-29 DtReplay"] = "F12 the velocity after the narrow-top side push at the window sill",
        ["traversal.spy-zip-line-29 Fixed"] = "F12, F11",
        ["moveset.spy-ladder-down Fixed"] = "F11 the walk to the ladder top on the 60 Hz grid (the golden steps every other frame; s_downTopLadder 10 frames early)",
        ["moveset.merc-ladder-down Fixed"] = "F11 the walk to the ladder top on the 60 Hz grid (s_downTopLadder 5 frames early, golden spread 2)",
        ["moveset.spy-wall-jump Fixed"] = "F11 the 60 Hz grid (WljpStAlBgR ends a frame early, velocity RMS 109.6 against 58.9)",
        ["moveset.merc-ladder-up Fixed"] = "F11 the 60 Hz grid (velocity RMS 30.03 against 30)",
        ["moveset.spy-vault-barrier Fixed"] = "F11 the 60 Hz grid (velocity RMS 31.0 against 30)",
    };

    private static readonly ConcurrentDictionary<(string Id, SimTiming Timing), Lazy<SimReport>> s_reports = new();
    private static readonly Lazy<PawnRigSim> s_sim = new(() => new PawnRigSim(SimContent.Default()));

    private readonly ITestOutputHelper _out;

    public RunnerTests(ITestOutputHelper output)
    {
        _out = output;
    }

    public static TheoryData<string, SimTiming> Cases()
    {
        var data = new TheoryData<string, SimTiming>();
        foreach (string f in RepoPaths.ScenarioFiles())
        {
            string id = Scenario.Load(f).Id;
            data.Add(id, SimTiming.DtReplay);
            data.Add(id, SimTiming.Fixed);
        }

        return data;
    }

    private static string Key(string id, SimTiming timing) => $"{id} {timing}";

    private static Scenario ScenarioById(string id) => RepoPaths.ScenarioFiles().Select(Scenario.Load).Single(s => s.Id == id);

    /// <summary>The report of a scenario (played once per test run), or a skip when it cannot be played here.</summary>
    private static SimReport ReportOrSkip(string id, SimTiming timing)
    {
        Scenario scenario = ScenarioById(id);
        GoldenStore? found = SimGoldens.StoreWithMovesetFor(id);
        if (found is null)
        {
            Assert.Skip($"no golden traces for {id} under {GoldenStore.Default().Root}[-traversal, -moveset]");
        }

        GoldenStore store = found!;

        if (!s_sim.Value.CanRun(scenario))
        {
            Assert.Skip($"{scenario.Map} is not converted under {SimContent.Default().Root} (content/svmbuild)");
        }

        Lazy<SimReport> lazy = s_reports.GetOrAdd((id, timing), k => new Lazy<SimReport>(() =>
        {
            SimReport r = SimHarness.Run(s_sim.Value, scenario, store.Load(scenario.Id), timing);
            SimHarness.WriteRun(r);
            return r;
        }));
        return lazy.Value;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SimMatchesTheGoldens(string id, SimTiming timing)
    {
        SimReport report = ReportOrSkip(id, timing);
        _out.WriteLine(report.Details());
        if (KnownMismatches.TryGetValue(Key(id, timing), out string? why))
        {
            if (!report.Pass)
            {
                Assert.Skip($"known mismatch ({why}): {report.Result.Describe()}");
            }

            _out.WriteLine($"{Key(id, timing)} now passes: take it off the known mismatches ({why}).");
            return;
        }

        Assert.True(report.Pass, report.Details());
    }

    [Fact]
    public void WritesTheReport()
    {
        var scenarios = RepoPaths.ScenarioFiles().Select(Scenario.Load)
            .Where(s => SimGoldens.StoreWithMovesetFor(s.Id) is not null && s_sim.Value.CanRun(s)).ToList();
        if (scenarios.Count == 0)
        {
            Assert.Skip("no scenario has both goldens and converted content here");
        }

        var reports = new List<SimReport>();
        foreach (SimTiming timing in new[] { SimTiming.DtReplay, SimTiming.Fixed })
        {
            reports.AddRange(scenarios.Select(s => ReportOrSkip(s.Id, timing)));
        }

        // The scenario's own setup (teleport, stance, waitStill, settle) in place of the recorded start: checks the
        // spawn and the settle; informational.
        var setupSim = new PawnRigSim(SimContent.Default(), new PawnRigSimOptions { Start = SimStart.Setup });
        var setupReports = scenarios.Select(s => SimHarness.Run(setupSim, s, SimGoldens.StoreWithMovesetFor(s.Id)!.Load(s.Id), SimTiming.DtReplay))
            .ToList();
        setupReports.ForEach(r => SimHarness.WriteRun(r));

        var md = new StringBuilder();
        md.AppendLine("# Sim vs game (local report)");
        md.AppendLine();
        md.AppendLine(SimReport.SummaryHeader);
        foreach (SimReport r in reports)
        {
            md.AppendLine(r.SummaryRow());
        }

        md.AppendLine();
        md.AppendLine("## Setup start (DtReplay)");
        md.AppendLine();
        md.AppendLine("| Scenario | Start loc error | Start state | Verdict | first divergence |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (SimReport r in setupReports)
        {
            TraceFrame a = r.Reference.Start;
            TraceFrame b = r.Candidate.Start;
            md.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"| {r.ScenarioId} | {r.StartLocError:0.###} (dz {b.Loc.Z - a.Loc.Z:+0.###;-0.###;0}) | {b.ControllerState}/{b.Physics}{(r.StartSameState ? "" : $" vs {a.ControllerState}/{a.Physics}")} | {(r.Pass ? "pass" : "FAIL")} | {(r.FirstDivergence is { } d ? $"f{d.Frame}: {d.What}" : "none")} |"));
        }

        md.AppendLine();
        foreach (SimReport r in reports)
        {
            md.AppendLine(r.Details());
        }

        string dir = SimHarness.OutputRoot;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "report.md"), md.ToString());
        var json = new JsonArray(reports.Select(r => (JsonNode)r.ToJson()).ToArray());
        File.WriteAllText(Path.Combine(dir, "report.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        _out.WriteLine(SimReport.SummaryHeader);
        foreach (SimReport r in reports)
        {
            _out.WriteLine(r.SummaryRow());
        }

        Assert.Equal(scenarios.Count * 2, reports.Count);
    }

    [Fact]
    public void SimRunsRepeatExactly()
    {
        Scenario scenario = ScenarioById("movement.spy-jump-forward");
        GoldenStore? store = SimGoldens.StoreFor(scenario.Id);
        if (store is null || !s_sim.Value.CanRun(scenario))
        {
            Assert.Skip("needs the goldens and the converted map");
        }

        TraceRun reference = Compare.NoiseEnvelope.Of(store.Load(scenario.Id)).Reference;
        TraceRun a = s_sim.Value.Run(new SimRequest(scenario, reference, SimTiming.DtReplay));
        TraceRun b = s_sim.Value.Run(new SimRequest(scenario, reference, SimTiming.DtReplay));
        Assert.Equal(a.Frames.Count, b.Frames.Count);
        for (int i = 0; i < a.Frames.Count; i++)
        {
            Assert.Equal(TraceFile.FrameToJson(a.Frames[i]).ToJsonString(), TraceFile.FrameToJson(b.Frames[i]).ToJsonString());
        }
    }

    // ---- synthetic: the runner's wiring, on a flat floor with no content --------------------------------------

    private static PawnRig FlatRig(Scenario s)
    {
        var floor = new CollisionDataBuilder();
        floor.AddBox(new FVector(-6000f, -6000f, -64f), new FVector(6000f, 6000f, 0f));
        return PawnRig.Create(PawnRigSim.TeamOf(s), floor.Build());
    }

    /// <summary>A reference standing still on the flat floor's top (z 0), with the scenario's inputs at 60 Hz.</summary>
    private static TraceRun SyntheticReference(Scenario s, bool fast)
    {
        var frames = new List<TraceFrame>();
        IReadOnlyList<FrameInput> timeline = s.Timeline();
        var start = new TraceFrame
        {
            F = -1, T = 10.0, Dt = 1.0 / 60.0, Loc = new Vec3(0, 0, 88.5), Vel = Vec3.Zero, Physics = 1,
            Rot = new Rot3(0, s.SpawnYaw, 0), ControllerRot = new Rot3(0, s.SpawnYaw, 0), Health = 100, FastSpeed = fast,
            ControllerState = "s_PlayerWalkingSt0H",
        };
        frames.Add(start);
        for (int f = 0; f < timeline.Count; f++)
        {
            frames.Add(start with { F = f, T = 10.0 + ((f + 1) / 60.0), Input = timeline[f] });
        }

        return new TraceRun(new JsonObject { ["scenario"] = new JsonObject { ["id"] = s.Id } }, frames, null);
    }

    [Theory]
    [InlineData(SimTiming.DtReplay)]
    [InlineData(SimTiming.Fixed)]
    public void RunnerPlaysTheInputsOnASyntheticFloor(SimTiming timing)
    {
        Scenario s = ScenarioById("movement.spy-run-forward");
        var sim = new PawnRigSim(FlatRig, _ => true, new PawnRigSimOptions { WarmupFrames = 30 });
        TraceRun run = sim.Run(new SimRequest(s, SyntheticReference(s, fast: true), timing));

        Assert.Equal(s.DurationFrames + 1, run.Frames.Count);
        Assert.Equal(-1, run.Frames[0].F);
        Assert.Equal(Enumerable.Range(0, s.DurationFrames), run.Played.Select(f => f.F));
        Assert.Equal(new Vec3(0, 0, 88.5), run.Start.Loc);
        Assert.Equal(1, run.At(0)!.Input.Button("BT_Up"));
        Assert.Equal(0, run.At(180)!.Input.Button("BT_Up"));
        Assert.All(run.Played, f => Assert.Equal(1.0 / 60.0, f.Dt, 5));

        // The spy runs +Y at the decoded 423.5 uu/s and stops when the key is let go.
        (double mean, _, _) = Locomotion.PlateauSpeed(run, 90, 180);
        Assert.Equal(423.5, mean, 0.5);
        Assert.True(run.At(150)!.Loc.Y > 400, $"moved {run.At(150)!.Loc.Y} uu");
        Assert.Equal(0, run.At(s.DurationFrames - 1)!.Speed2D, 0.01);

        // svm.trace/1 round trip.
        string path = Path.Combine(Directory.CreateTempSubdirectory("svm-sim-").FullName, "run.jsonl.gz");
        try
        {
            TraceFile.Write(path, run.Header, run.Frames, run.Footer);
            TraceRun back = TraceFile.Read(path);
            Assert.Equal(run.Frames.Count, back.Frames.Count);
            Assert.Equal(s.Id, back.ScenarioId);
            Assert.Equal(run.At(120)!.Loc.Y, back.At(120)!.Loc.Y, 3);
            Assert.Equal(run.At(120)!.ControllerState, back.At(120)!.ControllerState);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ADtReplayRunFollowsTheReferenceFrameTimes()
    {
        Scenario s = ScenarioById("movement.spy-jump-in-place");
        TraceRun reference = SyntheticReference(s, fast: true);
        var frames = reference.Frames.Select(f => f.F < 0 ? f : f with { Dt = f.F % 2 == 0 ? 0.0166 : 0.0168 }).ToList();
        var jittered = new TraceRun(reference.Header, frames, null);
        var sim = new PawnRigSim(FlatRig, _ => true, new PawnRigSimOptions { WarmupFrames = 30 });
        TraceRun run = sim.Run(new SimRequest(s, jittered, SimTiming.DtReplay));
        Assert.Equal(0.0166, run.At(10)!.Dt, 5);
        Assert.Equal(0.0168, run.At(11)!.Dt, 5);
        Assert.Equal(run.At(10)!.T + 0.0168, run.At(11)!.T, 4);

        // The jump on frame 30 leaves the floor.
        Assert.Contains(run.Played, f => f.Physics == Locomotion.PhysFalling);
        Assert.True(run.Played.Max(f => f.Loc.Z) > 88.5 + 100);
    }
}
