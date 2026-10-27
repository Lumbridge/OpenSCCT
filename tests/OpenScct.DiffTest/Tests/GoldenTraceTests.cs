using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using OpenScct.DiffTest.Compare;
using OpenScct.DiffTest.Golden;
using OpenScct.DiffTest.Measure;
using OpenScct.DiffTest.Model;
using Xunit;

namespace OpenScct.DiffTest.Tests;

/// <summary>
/// Checks the golden traces recorded from the original (local only; skipped when none are present, as in CI):
/// every run is internally consistent, the runs of a scenario agree within the locomotion profile, and the
/// measured numbers match the decoded ones the scenario expects. The numbers are printed and written to
/// local/difftest/measurements/&lt;id&gt;.json.
/// </summary>
public sealed class GoldenTraceTests
{
    /// <summary>The scenario fields that decide what the original played (title, covers and measure do not).</summary>
    private static readonly string[] PlayedKeys =
        ["map", "mode", "players", "spawn", "setup", "settleFrames", "durationFrames", "inputs"];

    private readonly ITestOutputHelper _out;

    public GoldenTraceTests(ITestOutputHelper output)
    {
        _out = output;
    }

    public static TheoryData<string> ScenarioIds()
    {
        var data = new TheoryData<string>();
        foreach (string f in RepoPaths.ScenarioFiles())
        {
            data.Add(Scenario.Load(f).Id);
        }

        return data;
    }

    private static Scenario ScenarioById(string id) =>
        RepoPaths.ScenarioFiles().Select(Scenario.Load).Single(s => s.Id == id);

    private static IReadOnlyList<TraceRun> GoldenOrSkip(string id)
    {
        GoldenStore store = GoldenStore.Default();
        if (!store.Has(id))
        {
            Assert.Skip($"no golden traces for {id} under {store.Root} (record them with re/tools/difftest/record.py)");
        }

        return store.Load(id);
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void RunsAreInternallyConsistent(string id)
    {
        Scenario scenario = ScenarioById(id);
        IReadOnlyList<TraceRun> runs = GoldenOrSkip(id);
        Assert.Equal(scenario.Repeats, runs.Count);
        foreach (TraceRun run in runs)
        {
            string where = $"{id} {run.Name}";
            JsonObject h = run.Header;
            Assert.Equal(id, run.ScenarioId);
            Assert.Equal(scenario.Map, h["map"]!["name"]!.GetValue<string>());
            Assert.Equal(scenario.Team, h["team"]!.GetValue<string>());
            Assert.False(string.IsNullOrEmpty(h["map"]!["autoplaySha256"]?.GetValue<string>()), $"{where}: no map hash");
            Assert.False(string.IsNullOrEmpty(h["binaries"]!["System/SCCT Versus"]?.GetValue<string>()), $"{where}: no exe hash");
            foreach (string key in PlayedKeys)
            {
                Assert.True(JsonNode.DeepEquals(h["scenario"]!["doc"]![key], scenario.Doc[key]),
                    $"{where}: '{key}' differs from {scenario.Path}: the golden was recorded from another version; record it again");
            }
            Assert.Equal(-1, run.Frames[0].F);
            Assert.Equal(scenario.DurationFrames, run.Played.Count() + run.Played.Sum(f => f.Gap));

            TraceFrame? prev = null;
            foreach (TraceFrame f in run.Frames)
            {
                Assert.InRange(f.Dt, 0.004, 0.1);
                Assert.True(f.Vel.Length < 3000, $"{where} f{f.F}: speed {f.Vel.Length}");
                Assert.InRange(f.Physics, 1, 2);
                Assert.InRange(f.Health, 1, 100);
                if (prev is not null)
                {
                    Assert.Equal(prev.F + 1 + f.Gap, f.F);
                    Assert.True(f.T > prev.T, $"{where} f{f.F}: time does not move on");
                    if (f.Gap == 0)
                    {
                        Assert.True(Math.Abs(f.T - prev.T - f.Dt) < 2e-4, $"{where} f{f.F}: TimeSeconds step {f.T - prev.T} vs m_dT {f.Dt}");
                    }

                    // A frame moves the pawn by about its velocity times dt (never a jump in place).
                    double step = (f.Loc - prev.Loc).Length;
                    double bound = (Math.Max(f.Vel.Length, prev.Vel.Length) * (f.T - prev.T) * 1.05) + 1.0;
                    Assert.True(step <= bound, $"{where} f{f.F}: moved {step:0.00} uu, at most {bound:0.00} expected");
                }

                prev = f;
            }
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void RunsAgreeWithinTheProfile(string id)
    {
        Scenario scenario = ScenarioById(id);
        IReadOnlyList<TraceRun> runs = GoldenOrSkip(id);
        NoiseEnvelope env = NoiseEnvelope.Of(runs);
        ComparisonProfile profile = ComparisonProfile.ByName(scenario.Profile);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"{id}: reference {env.Reference.Name}; envelope locRms {env.Worst.LocRms:0.###} locEnd {env.Worst.LocEnd:0.###} locMax {env.Worst.LocMax:0.###} velRms {env.Worst.VelRms:0.###}");
        foreach (TraceRun run in runs.Where(r => r != env.Reference))
        {
            ComparisonResult r = Comparator.Compare(run, runs, profile);
            sb.AppendLine(r.Describe());
            Assert.True(r.Pass, r.Describe());
            Assert.True(r.SameStates, r.Describe());
        }

        _out.WriteLine(sb.ToString());

        // The recorder's envelope.json uses the same metric definitions.
        string envPath = Path.Combine(GoldenStore.Default().Folder(id), "envelope.json");
        if (File.Exists(envPath))
        {
            JsonObject py = JsonNode.Parse(File.ReadAllText(envPath))!["envelope"]!.AsObject();
            Assert.Equal(env.Worst.LocRms, py["locRms"]!.GetValue<double>(), 0.001);
            Assert.Equal(env.Worst.LocEnd, py["locEnd"]!.GetValue<double>(), 0.001);
            Assert.Equal(env.Worst.VelRms, py["velRms"]!.GetValue<double>(), 0.001);
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void MeasuredNumbersMatchTheDecodedOnes(string id)
    {
        Scenario scenario = ScenarioById(id);
        IReadOnlyList<TraceRun> runs = GoldenOrSkip(id);
        var report = new JsonObject { ["scenario"] = id, ["team"] = scenario.Team };
        var perRun = new JsonObject();
        var sb = new StringBuilder();
        foreach (TraceRun run in runs)
        {
            IReadOnlyList<Measured> ms = Locomotion.MeasureAll(scenario, run);
            sb.AppendLine(CultureInfo.InvariantCulture, $"{id} {run.Name}: {string.Join("; ", ms)}");
            var o = new JsonObject();
            foreach (Measured m in ms)
            {
                o[m.Name] = Math.Round(m.Value, 4);
            }

            perRun[run.Name] = o;
            foreach (Measured m in ms)
            {
                Check(run, m);
            }

            if (scenario.Measure["fallDamage"] is not null)
            {
                Locomotion.FallResult fall = Locomotion.Fall(run, 0);
                Assert.Equal(Locomotion.SpecFallDamage(scenario.Team, fall.Drop), fall.Damage);
            }
        }

        report["runs"] = perRun;
        _out.WriteLine(sb.ToString());
        string dir = Path.Combine(RepoPaths.Root, "local", "difftest", "measurements");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".json"), report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        void Check(TraceRun run, Measured m)
        {
            if (m.Expected is not double e)
            {
                return;
            }

            string what = $"{id} {run.Name}: {m}";
            switch (m.Name)
            {
                case "plateauSpeed":
                case "groundSpeed":
                    Assert.True(Math.Abs(m.Value - e) <= 0.005 * e, what);
                    break;
                case "jumpApex":
                    Assert.True(Math.Abs(m.Value - e) <= 1.5, what);
                    break;
                case "airtime":
                    Assert.True(Math.Abs(m.Value - e) <= 0.04, what);
                    break;
                case "gravity":
                    Assert.True(Math.Abs(m.Value - e) <= 0.01 * Math.Abs(e), what);
                    break;
                case "fallDrop":
                    Assert.True(Math.Abs(m.Value - e) <= 3.0, what);
                    break;
                case "fallDamage":
                    Assert.Equal(e, m.Value);
                    break;
                default:
                    break;
            }
        }
    }
}
