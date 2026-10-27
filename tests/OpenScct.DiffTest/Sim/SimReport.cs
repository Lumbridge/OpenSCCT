using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using OpenScct.DiffTest.Compare;
using OpenScct.DiffTest.Measure;
using OpenScct.DiffTest.Model;

namespace OpenScct.DiffTest.Sim;

/// <summary>A measured number on the reference and on the sim run, with the spread of the golden runs.</summary>
public sealed record MeasureDelta(string Name, string Unit, double Reference, double Sim, double Noise, string? Note = null)
{
    public double Error => Sim - Reference;
}

/// <summary>One phase (controller state and Physics) and where it starts in the reference and in the sim run.</summary>
public sealed record PhaseTiming(string Phase, int ReferenceFrame, int SimFrame, double OffsetSeconds, int OffsetFrames, int Noise);

/// <summary>The first frame where the sim run leaves the reference: location, velocity, Physics or state.</summary>
public sealed record FrameDivergence(int Frame, string What, TraceFrame Reference, Vec3 SimLoc, Vec3 SimVel, TraceFrame Sim);

/// <summary>
/// A sim run against a scenario's goldens: the comparator's verdicts (profile metrics with the noise envelope),
/// the scenario's measurements on both runs, the phase sequence and timing, the channel-0 clip sequence, the start
/// state and the first divergence.
/// </summary>
public sealed class SimReport
{
    /// <summary>Location distance, uu, from which a frame counts as diverged.</summary>
    public const double DivergeLoc = 0.5;

    /// <summary>Velocity difference, uu/s, from which a frame counts as diverged.</summary>
    public const double DivergeVel = 5.0;

    private static readonly string[] KeyMeasures =
        ["plateauSpeed", "accelTime90", "accelTimeFull", "decelTimeZero", "jumpApex", "airtime", "fallDrop", "fallDamage", "landingHold"];

    private SimReport(string simName, Scenario scenario, SimTiming timing, ComparisonProfile profile, NoiseEnvelope envelope,
        TraceRun candidate, ComparisonResult result)
    {
        SimName = simName;
        Scenario = scenario;
        Timing = timing;
        Profile = profile;
        Envelope = envelope;
        Candidate = candidate;
        Result = result;
    }

    public string SimName { get; }

    public Scenario Scenario { get; }

    public string ScenarioId => Scenario.Id;

    public SimTiming Timing { get; }

    public ComparisonProfile Profile { get; }

    public NoiseEnvelope Envelope { get; }

    public TraceRun Reference => Envelope.Reference;

    public TraceRun Candidate { get; }

    public ComparisonResult Result { get; }

    public IReadOnlyList<MeasureDelta> Measures { get; private set; } = [];

    /// <summary>Phases (controller state / Physics) of the reference with their start in both runs; empty when the sequences differ.</summary>
    public IReadOnlyList<PhaseTiming> Phases { get; private set; } = [];

    public IReadOnlyList<string> ReferencePhases { get; private set; } = [];

    public IReadOnlyList<string> SimPhases { get; private set; } = [];

    public bool SamePhases => ReferencePhases.SequenceEqual(SimPhases);

    public bool SameAnim0 { get; private set; }

    public string? Anim0Difference { get; private set; }

    public double StartLocError { get; private set; }

    public bool StartSameState { get; private set; }

    public FrameDivergence? FirstDivergence { get; private set; }

    /// <summary>Largest phase start offset beyond the golden runs' own spread, in frames (0 when the sequences differ).</summary>
    public int WorstPhaseOffset => Phases.Count == 0 ? 0 : Phases.Max(p => Math.Max(0, Math.Abs(p.OffsetFrames) - p.Noise));

    public bool PhasesInTime => SamePhases && Phases.All(p => Math.Abs(p.OffsetFrames) <= Math.Max(SimProfiles.TraversalPhaseFrames, p.Noise));

    /// <summary>The profile's verdict, with the traversal phase timing when the profile asks for the same states.</summary>
    public bool Pass => Result.Pass && (!Profile.RequireSameStates || PhasesInTime);

    public static SimReport Build(string simName, Scenario scenario, IReadOnlyList<TraceRun> golden, NoiseEnvelope envelope,
        TraceRun candidate, SimTiming timing)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(golden);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(candidate);
        ComparisonProfile profile = SimProfiles.ByName(scenario.Profile);
        ComparisonResult result = Comparator.Compare(candidate, golden, profile);
        var report = new SimReport(simName, scenario, timing, profile, envelope, candidate, result);
        report.Measures = MeasureDeltas(scenario, golden, envelope.Reference, candidate);
        report.ComparePhases(golden);
        report.CompareAnim0();
        report.CompareStart();
        report.FirstDivergence = Diverge(envelope.Reference, candidate);
        return report;
    }

    /// <summary>The measurement named <paramref name="name"/>, or null.</summary>
    public MeasureDelta? Measure(string name) => Measures.FirstOrDefault(m => m.Name == name);

    // ---- pieces ------------------------------------------------------------------------------------------------

    private static List<MeasureDelta> MeasureDeltas(Scenario scenario, IReadOnlyList<TraceRun> golden, TraceRun reference, TraceRun candidate)
    {
        var list = new List<MeasureDelta>();
        IReadOnlyList<Measured> refMs;
        try
        {
            refMs = Locomotion.MeasureAll(scenario, reference);
        }
        catch (ArgumentException)
        {
            return list;
        }

        var goldenMs = golden.Select(g => Safe(() => Locomotion.MeasureAll(scenario, g))).Where(m => m is not null).ToList();
        IReadOnlyList<Measured>? simMs = null;
        string? note = null;
        try
        {
            simMs = Locomotion.MeasureAll(scenario, candidate);
        }
        catch (ArgumentException e)
        {
            note = e.Message;
        }

        foreach (Measured r in refMs)
        {
            var values = goldenMs.Select(ms => ms!.FirstOrDefault(m => m.Name == r.Name)?.Value ?? double.NaN).Where(v => !double.IsNaN(v)).ToList();
            double noise = values.Count < 2 ? 0 : values.Max() - values.Min();
            double sim = simMs?.FirstOrDefault(m => m.Name == r.Name)?.Value ?? double.NaN;
            list.Add(new MeasureDelta(r.Name, r.Unit, r.Value, sim, noise, double.IsNaN(sim) ? note ?? "not measurable on the sim run" : null));
        }

        return list;
    }

    private static IReadOnlyList<Measured>? Safe(Func<IReadOnlyList<Measured>> f)
    {
        try
        {
            return f();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string PhaseKey(TraceFrame f) => $"{f.ControllerState}/{f.Physics.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The phases of a run (consecutive repeats removed) with the first frame of each.</summary>
    public static List<(string Phase, TraceFrame First)> PhasesOf(TraceRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var list = new List<(string, TraceFrame)>();
        foreach (TraceFrame f in run.Played)
        {
            string k = PhaseKey(f);
            if (list.Count == 0 || list[^1].Item1 != k)
            {
                list.Add((k, f));
            }
        }

        return list;
    }

    private void ComparePhases(IReadOnlyList<TraceRun> golden)
    {
        List<(string Phase, TraceFrame First)> a = PhasesOf(Reference);
        List<(string Phase, TraceFrame First)> b = PhasesOf(Candidate);
        ReferencePhases = a.Select(p => p.Phase).ToList();
        SimPhases = b.Select(p => p.Phase).ToList();
        if (!SamePhases)
        {
            Phases = [];
            return;
        }

        double a0 = Reference.Played.First().T;
        double b0 = Candidate.Played.First().T;

        // The golden runs' own spread of each phase start, in frames, where they go through the same phases.
        var spread = new int[a.Count];
        foreach (TraceRun g in golden.Where(g => g != Reference))
        {
            List<(string Phase, TraceFrame First)> c = PhasesOf(g);
            if (c.Select(p => p.Phase).SequenceEqual(ReferencePhases))
            {
                for (int i = 0; i < a.Count; i++)
                {
                    spread[i] = Math.Max(spread[i], Math.Abs(c[i].First.F - a[i].First.F));
                }
            }
        }

        var list = new List<PhaseTiming>();
        for (int i = 0; i < a.Count; i++)
        {
            double offset = (b[i].First.T - b0) - (a[i].First.T - a0);
            int frames = Timing == SimTiming.DtReplay ? b[i].First.F - a[i].First.F : (int)Math.Round(offset * 60.0);
            list.Add(new PhaseTiming(a[i].Phase, a[i].First.F, b[i].First.F, offset, frames, spread[i]));
        }

        Phases = list;
    }

    private static List<string> Anim0Sequence(TraceRun run) =>
        Comparator.Sequence(run, f => f.Anim.FirstOrDefault(c => c.Channel == 0)?.Sequence ?? "").ToList();

    private void CompareAnim0()
    {
        List<string> a = Anim0Sequence(Reference);
        List<string> b = Anim0Sequence(Candidate);
        SameAnim0 = a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
        if (!SameAnim0)
        {
            int i = 0;
            while (i < a.Count && i < b.Count && string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
            {
                i++;
            }

            Anim0Difference = string.Create(CultureInfo.InvariantCulture,
                $"clip {i}: {(i < a.Count ? a[i] : "end")} vs {(i < b.Count ? b[i] : "end")} (reference [{string.Join(", ", a)}], sim [{string.Join(", ", b)}])");
        }
    }

    private void CompareStart()
    {
        TraceFrame a = Reference.Start;
        TraceFrame b = Candidate.Start;
        StartLocError = Vec3.Distance(a.Loc, b.Loc);
        StartSameState = a.ControllerState == b.ControllerState && a.Physics == b.Physics && a.Crouched == b.Crouched;
    }

    /// <summary>The first reference frame the sim run leaves (location, velocity on the common clock; state, Physics).</summary>
    public static FrameDivergence? Diverge(TraceRun reference, TraceRun candidate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);
        List<TraceFrame> a = reference.Played.ToList();
        List<TraceFrame> b = candidate.Played.ToList();
        if (a.Count == 0 || b.Count == 0)
        {
            return null;
        }

        double a0 = a[0].T;
        double b0 = b[0].T;
        double[] bt = b.Select(f => f.T - b0).ToArray();
        int j = 0;
        foreach (TraceFrame fa in a)
        {
            double t = fa.T - a0;
            if (t > bt[^1] + 1e-6)
            {
                break;
            }

            // The sim frame nearest in time (the same frame in DtReplay; the clocks differ by float rounding).
            while (j + 1 < b.Count && Math.Abs(bt[j + 1] - t) < Math.Abs(bt[j] - t))
            {
                j++;
            }

            TraceFrame fb = b[j];
            (Vec3 loc, Vec3 vel) = RunMetrics.Sample(b, bt, t);
            var what = new List<string>();
            double dl = Vec3.Distance(fa.Loc, loc);
            double dv = Vec3.Distance(fa.Vel, vel);
            if (dl > DivergeLoc)
            {
                what.Add(string.Create(CultureInfo.InvariantCulture, $"location {dl:0.###} uu"));
            }

            if (dv > DivergeVel)
            {
                what.Add(string.Create(CultureInfo.InvariantCulture, $"velocity {dv:0.###} uu/s"));
            }

            if (fa.Physics != fb.Physics)
            {
                what.Add(string.Create(CultureInfo.InvariantCulture, $"Physics {fb.Physics} vs {fa.Physics}"));
            }

            if (!string.Equals(fa.ControllerState, fb.ControllerState, StringComparison.OrdinalIgnoreCase))
            {
                what.Add($"state {fb.ControllerState} vs {fa.ControllerState}");
            }

            if (what.Count > 0)
            {
                return new FrameDivergence(fa.F, string.Join(", ", what), fa, loc, vel, fb);
            }
        }

        return null;
    }

    // ---- output ------------------------------------------------------------------------------------------------

    private static string N(double v, string format = "0.###") =>
        double.IsNaN(v) ? "n/a" : v.ToString(format, CultureInfo.InvariantCulture);

    private static string V(Vec3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##})");

    private string Verdict(MetricKind kind)
    {
        MetricVerdict? v = Result.Verdicts.FirstOrDefault(x => x.Kind == kind);
        return v is null ? "" : $"{N(v.Value, "0.##")} / {N(v.Tolerance, "0.##")}{(v.Pass ? "" : " **x**")}";
    }

    private string Err(string name, string format = "+0.###;-0.###;0")
    {
        MeasureDelta? m = Measure(name);
        return m is null ? "" : double.IsNaN(m.Sim) ? "n/a" : m.Error.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>The header of the summary table of <see cref="SummaryRow"/>.</summary>
    public const string SummaryHeader =
        "| Scenario | Timing | Verdict | loc RMS / tol | loc end / tol | vel RMS / tol | states | phase off | plateau err | accel 90% err s | apex err | first divergence |\n" +
        "|---|---|---|---|---|---|---|---|---|---|---|---|";

    /// <summary>One row of the markdown summary table.</summary>
    public string SummaryRow()
    {
        string states = Result.SameStates ? "same" : "differ";
        if (Profile.RequireSameStates && SamePhases && !PhasesInTime)
        {
            states += " (late)";
        }

        string div = FirstDivergence is { } d ? string.Create(CultureInfo.InvariantCulture, $"f{d.Frame}: {d.What}") : "none";
        string phase = SamePhases && Phases.Count > 0 ? Phases.Max(p => Math.Abs(p.OffsetFrames)).ToString(CultureInfo.InvariantCulture) : "-";
        return $"| {ScenarioId} | {Timing} | {(Pass ? "pass" : "FAIL")} | {Verdict(MetricKind.LocRms)} | {Verdict(MetricKind.LocEnd)} | " +
            $"{Verdict(MetricKind.VelRms)} | {states} | {phase} | {Err("plateauSpeed")} | {Err("accelTime90")} | {Err("jumpApex")} | {div} |";
    }

    /// <summary>The full report of the scenario as markdown.</summary>
    public string Details()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"### {ScenarioId} ({Timing}, {SimName}): {(Pass ? "pass" : "FAIL")}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Reference {Reference.Name}; profile {Profile.Name}. {Result.Describe()}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Start: location {N(StartLocError)} uu from the reference frame -1, {(StartSameState ? "same" : "different")} state.");
        if (FirstDivergence is { } d)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"First divergence: frame {d.Frame}: {d.What}. Reference loc {V(d.Reference.Loc)} vel {V(d.Reference.Vel)} {d.Reference.ControllerState}/{d.Reference.Physics}; " +
                $"sim loc {V(d.SimLoc)} vel {V(d.SimVel)} {d.Sim.ControllerState}/{d.Sim.Physics}.");
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"No divergence beyond {DivergeLoc} uu, {DivergeVel} uu/s or state.");
        }

        sb.AppendLine();
        if (Measures.Count > 0)
        {
            sb.AppendLine("| Measure | Reference | Sim | Error | Golden spread |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (MeasureDelta m in Measures)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {m.Name} ({m.Unit}) | {N(m.Reference)} | {N(m.Sim)} | {(double.IsNaN(m.Sim) ? m.Note : N(m.Error, "+0.###;-0.###;0"))} | {N(m.Noise)} |");
            }

            sb.AppendLine();
        }

        if (SamePhases)
        {
            sb.AppendLine("| Phase | Reference frame | Sim frame | Offset frames | Golden spread |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (PhaseTiming p in Phases)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {p.Phase} | {p.ReferenceFrame} | {p.SimFrame} | {p.OffsetFrames:+0;-0;0} | {p.Noise} |");
            }
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Phases differ: reference [{string.Join(", ", ReferencePhases)}]");
            sb.AppendLine(CultureInfo.InvariantCulture, $"sim [{string.Join(", ", SimPhases)}]");
        }

        sb.AppendLine();
        sb.AppendLine(SameAnim0 ? "Channel 0 clips: same sequence." : $"Channel 0 clips differ: {Anim0Difference}");
        return sb.ToString();
    }

    /// <summary>The report as JSON (numbers only, no frames).</summary>
    public JsonObject ToJson()
    {
        var measures = new JsonObject();
        foreach (MeasureDelta m in Measures)
        {
            measures[m.Name] = new JsonObject
            {
                ["reference"] = J(m.Reference),
                ["sim"] = J(m.Sim),
                ["error"] = J(m.Error),
                ["noise"] = J(m.Noise),
            };
        }

        var verdicts = new JsonObject();
        foreach (MetricVerdict v in Result.Verdicts)
        {
            verdicts[v.Kind.ToString()] = new JsonObject
            {
                ["value"] = J(v.Value),
                ["tolerance"] = J(v.Tolerance),
                ["pass"] = v.Pass,
            };
        }

        return new JsonObject
        {
            ["scenario"] = ScenarioId,
            ["sim"] = SimName,
            ["timing"] = Timing.ToString(),
            ["profile"] = Profile.Name,
            ["pass"] = Pass,
            ["verdicts"] = verdicts,
            ["sameStates"] = Result.SameStates,
            ["samePhases"] = SamePhases,
            ["worstPhaseOffset"] = WorstPhaseOffset,
            ["sameAnim0"] = SameAnim0,
            ["startLocError"] = J(StartLocError),
            ["firstDivergence"] = FirstDivergence is { } d ? new JsonObject { ["frame"] = d.Frame, ["what"] = d.What } : null,
            ["measures"] = measures,
        };

        static JsonNode? J(double v) => double.IsNaN(v) || double.IsInfinity(v) ? null : JsonValue.Create(Math.Round(v, 5));
    }
}
