using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.DiffTest.Compare;
using OpenScct.DiffTest.Golden;
using OpenScct.DiffTest.Model;
using OpenScct.DiffTest.Sim;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;

namespace OpenScct.DiffTest.Anim;

/// <summary>A channel field the animation difftest compares.</summary>
public enum AnimField
{
    Presence,
    Sequence,
    Frame,
    Rate,
    Alpha,
    Tween,
    TweenRate,
}

/// <summary>Where a channel first differs from the reference.</summary>
public sealed record AnimDivergence(int Channel, int Frame, AnimField Field, string Reference, string Sim);

/// <summary>Per channel counts of one scenario.</summary>
public sealed class AnimChannelStats
{
    public int Compared { get; set; }

    /// <summary>Frames where every compared field of the channel matched.</summary>
    public int Matched { get; set; }

    /// <summary>Frames where the channel was drawn (channel 0, or an alpha above 0 on either side).</summary>
    public int VisibleCompared { get; set; }

    public int VisibleMatched { get; set; }

    public Dictionary<AnimField, int> FieldMismatches { get; } = new();

    public AnimDivergence? First { get; set; }
}

/// <summary>The result of one scenario: per channel and per field matches of the sim's channels against the reference run.</summary>
public sealed class AnimReport
{
    public required string ScenarioId { get; init; }

    public required string Reference { get; init; }

    public required bool Full { get; init; }

    /// <summary>The frame the sim's movement leaves the reference (controller state or physics), or null.</summary>
    public int? MovementDivergence { get; init; }

    public int Frames { get; set; }

    /// <summary>Frames where every channel matched.</summary>
    public int FramesMatched { get; set; }

    public SortedDictionary<int, AnimChannelStats> Channels { get; } = new();

    public int ChannelFrames => Channels.Values.Sum(c => c.Compared);

    public int ChannelFramesMatched => Channels.Values.Sum(c => c.Matched);

    public double MatchRate => ChannelFrames == 0 ? 1.0 : (double)ChannelFramesMatched / ChannelFrames;

    /// <summary>Channel-frames where the channel was drawn on either side.</summary>
    public int VisibleFrames => Channels.Values.Sum(c => c.VisibleCompared);

    public int VisibleFramesMatched => Channels.Values.Sum(c => c.VisibleMatched);

    public AnimChannelStats Of(int channel)
    {
        if (!Channels.TryGetValue(channel, out AnimChannelStats? s))
        {
            s = new AnimChannelStats();
            Channels[channel] = s;
        }

        return s;
    }
}

/// <summary>
/// The animation difftest (spec/native/ANSWERS-anim.md A7): a scenario is replayed by <see cref="PawnRigSim"/> on the
/// reference run's frame times (DtReplay) from the recorded start, with the pawn's channels set to the reference's
/// frame -1 record, and every later frame's channels are compared field by field with the record: sequence, frame,
/// AnimRate, blend alpha, and in full records the tween weight and tween rate.
/// </summary>
public static class AnimDiff
{
    /// <summary>Frame and alpha tolerance (normalised).</summary>
    public const double FrameTolerance = 1e-3;

    /// <summary>AnimRate tolerance (relative above 1).</summary>
    public const double RateTolerance = 1e-3;

    /// <summary>Tween weight tolerance.</summary>
    public const double TweenTolerance = 2e-3;

    /// <summary>Plays <paramref name="scenario"/> against its goldens and compares the channels.</summary>
    public static AnimReport Run(SimContent content, Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scenario);
        GoldenStore store = SimGoldens.StoreFor(scenario.Id) ?? throw new InvalidOperationException($"no goldens for {scenario.Id}");
        IReadOnlyList<TraceRun> golden = store.Load(scenario.Id);
        TraceRun reference = NoiseEnvelope.Of(golden).Reference;
        IReadOnlyList<AnimFrame> recorded = AnimFrames.Read(reference.Path ?? throw new InvalidOperationException("reference without a path"));
        Dictionary<int, AnimFrame> byFrame = recorded.ToDictionary(f => f.F);
        AnimFrame start = recorded.FirstOrDefault(f => f.F == -1) ?? recorded[0];
        bool full = start.Full;
        var sampled = new Dictionary<int, AnimFrame>();
        var sim = new PawnRigSim(content, new PawnRigSimOptions
        {
            Started = (rig, _) => Seed(rig.Pawn, start),
            Sampled = (rig, f) => sampled[f] = AnimFrames.Sample(rig.Pawn.PawnAnim, f, full),
        });
        TraceRun run = sim.Run(new SimRequest(scenario, reference, SimTiming.DtReplay));
        int? cutoff = FirstMovementDivergence(reference, run);
        var report = new AnimReport { ScenarioId = scenario.Id, Reference = reference.Name, Full = full, MovementDivergence = cutoff };
        if (Environment.GetEnvironmentVariable("SVM_DIFFTEST_ANIM_DUMP") is { Length: > 0 } dump &&
            (dump == "*" || dump.Split(',').Contains(scenario.Id)))
        {
            Dump(scenario.Id, byFrame, sampled);
        }

        AnimFrame? previous = null;
        foreach ((int f, AnimFrame simFrame) in sampled.OrderBy(kv => kv.Key))
        {
            if (f >= 0 && (cutoff is null || f < cutoff) && byFrame.TryGetValue(f, out AnimFrame? gFrame))
            {
                report.Frames++;
                if (CompareFrame(report, gFrame, simFrame, previous))
                {
                    report.FramesMatched++;
                }
            }

            previous = simFrame;
        }

        return report;
    }

    /// <summary>
    /// The first frame where the sim's movement leaves the reference (controller state or physics mode): the channels
    /// follow the movement, so they are compared up to there only.
    /// </summary>
    public static int? FirstMovementDivergence(TraceRun reference, TraceRun sim)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(sim);
        foreach (TraceFrame s in sim.Played)
        {
            if (reference.At(s.F) is not { } g)
            {
                continue;
            }

            if (g.Physics != s.Physics || !string.Equals(g.ControllerState, s.ControllerState, StringComparison.OrdinalIgnoreCase))
            {
                return s.F;
            }
        }

        return null;
    }

    /// <summary>
    /// The pawn's channels set to a recorded frame: every recorded channel restored (the walk channels 2 to 13 as
    /// loops without notifies but channel 5, channel 0 as its wait loop), every other channel emptied, and the walk
    /// blender marked as initialised.
    /// </summary>
    public static void Seed(SBasePawn pawn, AnimFrame start)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        ArgumentNullException.ThrowIfNull(start);
        PawnAnimation anim = pawn.PawnAnim;
        for (int ch = 0; ch < PawnAnimation.ChannelCount; ch++)
        {
            bool walk = ch is >= 2 and <= 13;
            bool loop = ch == 0 || walk;
            bool ignore = walk && ch != 5;
            if (start.Of(ch) is { } c)
            {
                PawnClip clip = PawnActions.Clip(pawn, new Name(c.Sequence));
                float alpha = ch == 0 ? 0f : (float)c.Alpha;
                anim.Restore(ch, clip, (float)c.Frame, (float)c.Rate, alpha, (float)(c.Tween ?? 0.0), (float)(c.TweenRate ?? 0.0), loop, ignore);
            }
            else
            {
                anim.Restore(ch, null, 0f, 0f, 0f, 0f, 0f, loop, ignore);
            }
        }

        pawn.bInitializeAnimation = true;
    }

    /// <summary>Writes both channel lists of every frame to local/difftest/anim/&lt;id&gt;.dump.txt (debugging).</summary>
    private static void Dump(string id, Dictionary<int, AnimFrame> recorded, Dictionary<int, AnimFrame> sampled)
    {
        string dir = AnimDiffTests.OutputRoot;
        System.IO.Directory.CreateDirectory(dir);
        var sb = new System.Text.StringBuilder();
        foreach ((int f, AnimFrame s) in sampled.OrderBy(kv => kv.Key))
        {
            sb.Append(CultureInfo.InvariantCulture, $"f{f}").AppendLine();
            recorded.TryGetValue(f, out AnimFrame? g);
            var channels = new SortedSet<int>(s.Channels.Select(c => c.Channel));
            if (g is not null)
            {
                channels.UnionWith(g.Channels.Select(c => c.Channel));
            }

            foreach (int ch in channels)
            {
                AnimChannel? gc = g?.Of(ch);
                AnimChannel? sc = s.Of(ch);
                string mark = Compare(gc, sc, s.Full) is null ? " " : "*";
                sb.Append(CultureInfo.InvariantCulture, $" {mark}c{ch} game {Line(gc)} | sim {Line(sc)}").AppendLine();
            }
        }

        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, id + ".dump.txt"), sb.ToString());
    }

    private static string Line(AnimChannel? c) =>
        c is null ? "-" : string.Create(CultureInfo.InvariantCulture,
            $"{c.Sequence} f{c.Frame:0.######} r{c.Rate:0.#####} a{c.Alpha:0.#####} w{c.Tween:0.#####} tr{c.TweenRate:0.###}");

    private static bool CompareFrame(AnimReport report, AnimFrame g, AnimFrame s, AnimFrame? previousSim)
    {
        bool all = true;
        var channels = new SortedSet<int>(g.Channels.Select(c => c.Channel));
        channels.UnionWith(s.Channels.Select(c => c.Channel));
        foreach (int ch in channels)
        {
            AnimChannel? gc = g.Of(ch);
            AnimChannel? sc = s.Of(ch);
            AnimChannelStats stats = report.Of(ch);
            bool visible = ch == 0 || (gc?.Alpha ?? 0.0) > 0.0 || (sc?.Alpha ?? 0.0) > 0.0;
            stats.Compared++;
            if (visible)
            {
                stats.VisibleCompared++;
            }

            (AnimField Field, string Reference, string Sim)? miss = Compare(gc, sc, g.Full);

            // The recorder can sample a frame before the pose build updates the tween weight: a weight equal to the
            // sim's previous frame is that frame's value seen early.
            if (miss is { Field: AnimField.Tween } && previousSim?.Of(ch) is { Tween: { } before } && gc?.Tween is { } seen &&
                Math.Abs(seen - before) <= TweenTolerance && Compare(gc with { Tween = sc!.Tween }, sc, g.Full) is null)
            {
                miss = null;
            }

            if (miss is null)
            {
                stats.Matched++;
                if (visible)
                {
                    stats.VisibleMatched++;
                }

                continue;
            }

            all = false;
            AnimField field = miss.Value.Field;
            stats.FieldMismatches[field] = stats.FieldMismatches.GetValueOrDefault(field) + 1;
            stats.First ??= new AnimDivergence(ch, g.F, field, miss.Value.Reference, miss.Value.Sim);
        }

        return all;
    }

    /// <summary>The first differing field of a channel, or null when it matches.</summary>
    public static (AnimField Field, string Reference, string Sim)? Compare(AnimChannel? g, AnimChannel? s, bool full)
    {
        if (g is null || s is null)
        {
            return (AnimField.Presence, Describe(g), Describe(s));
        }

        if (!string.Equals(g.Sequence, s.Sequence, StringComparison.OrdinalIgnoreCase))
        {
            return (AnimField.Sequence, g.Sequence, s.Sequence);
        }

        if (Math.Abs(g.Frame - s.Frame) > FrameTolerance)
        {
            return (AnimField.Frame, F(g.Frame), F(s.Frame));
        }

        if (Math.Abs(g.Rate - s.Rate) > RateTolerance * Math.Max(1.0, Math.Abs(g.Rate)))
        {
            return (AnimField.Rate, F(g.Rate), F(s.Rate));
        }

        if (Math.Abs(g.Alpha - s.Alpha) > FrameTolerance)
        {
            return (AnimField.Alpha, F(g.Alpha), F(s.Alpha));
        }

        if (g.Tween is { } gt && s.Tween is { } st && Math.Abs(gt - st) > TweenTolerance)
        {
            return (AnimField.Tween, F(gt), F(st));
        }

        if (full && g.TweenRate is { } gr && s.TweenRate is { } sr && Math.Abs(gr - sr) > RateTolerance * Math.Max(1.0, Math.Abs(gr)))
        {
            return (AnimField.TweenRate, F(gr), F(sr));
        }

        return null;
    }

    private static string Describe(AnimChannel? c) =>
        c is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{c.Sequence} f{c.Frame:0.####} a{c.Alpha:0.###}");

    private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
