using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using OpenScct.DiffTest.Golden;
using OpenScct.DiffTest.Model;
using OpenScct.DiffTest.Sim;
using Xunit;

namespace OpenScct.DiffTest.Anim;

/// <summary>
/// The pawn's animation channels against the real game, frame by frame, for every movement and traversal scenario
/// with goldens (spec/native/ANSWERS-anim.md A7). Without goldens or converted content (CI) these skip. Reports go
/// to local/difftest/anim (game-derived numbers, never tracked).
/// </summary>
public sealed class AnimDiffTests
{
    /// <summary>
    /// Channel 0 drives root motion, AnimEnd and the script's timing: it must match on every compared frame.
    /// </summary>
    private const double Channel0Gate = 1.0;

    /// <summary>
    /// Every channel drawn on either side (channel 0, or an alpha above 0) must match too. Channels at alpha 0 are
    /// reported but not gated: the game can show stale records there (a channel grown again keeps old memory).
    /// </summary>
    private const double DrawnGate = 1.0;

    private static readonly ConcurrentDictionary<string, Lazy<AnimReport>> s_reports = new();
    private static readonly Lazy<SimContent> s_content = new(SimContent.Default);

    private readonly ITestOutputHelper _out;

    public AnimDiffTests(ITestOutputHelper output)
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

    /// <summary>The report directory: <c>SVM_DIFFTEST_ANIM_OUT</c>, else local/difftest/anim.</summary>
    public static string OutputRoot
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("SVM_DIFFTEST_ANIM_OUT");
            return env is { Length: > 0 } ? env : Path.Combine(RepoPaths.Root, "local", "difftest", "anim");
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void ChannelsMatchTheGame(string id)
    {
        AnimReport report = ReportOrSkip(id);
        _out.WriteLine(Summary(report));
        Write(report);
        AnimChannelStats c0 = report.Of(0);
        double rate0 = c0.Compared == 0 ? 1.0 : (double)c0.Matched / c0.Compared;
        Assert.True(rate0 >= Channel0Gate,
            $"{id}: channel 0 matched {c0.Matched}/{c0.Compared}; first divergence {c0.First}");
        double drawn = report.VisibleFrames == 0 ? 1.0 : (double)report.VisibleFramesMatched / report.VisibleFrames;
        Assert.True(drawn >= DrawnGate,
            $"{id}: drawn channel-frames matched {report.VisibleFramesMatched}/{report.VisibleFrames}; {Summary(report)}");
    }

    private static AnimReport ReportOrSkip(string id)
    {
        Scenario scenario = RepoPaths.ScenarioFiles().Select(Scenario.Load).Single(s => s.Id == id);
        SimContent content = s_content.Value;
        if (SimGoldens.StoreFor(id) is null)
        {
            Assert.Skip($"no golden traces for {id}");
        }

        if (!content.HasMap(scenario.Map) || !content.HasCharacters)
        {
            Assert.Skip($"{scenario.Map} or the characters are not converted under {content.Root}");
        }

        return s_reports.GetOrAdd(id, _ => new Lazy<AnimReport>(() => AnimDiff.Run(content, scenario))).Value;
    }

    /// <summary>A one-scenario summary: overall and per channel matches, the first divergence of each channel.</summary>
    public static string Summary(AnimReport r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{r.ScenarioId} vs {r.Reference} ({(r.Full ? "all channels 0-15" : "drawn channels")}): ");
        sb.Append(CultureInfo.InvariantCulture, $"{r.FramesMatched}/{r.Frames} frames, {r.ChannelFramesMatched}/{r.ChannelFrames} channel-frames ({r.MatchRate:P1}), drawn {r.VisibleFramesMatched}/{r.VisibleFrames}");
        if (r.MovementDivergence is { } cut)
        {
            sb.Append(CultureInfo.InvariantCulture, $"; movement leaves the reference at f{cut}, compared up to there");
        }

        sb.AppendLine();
        foreach ((int ch, AnimChannelStats s) in r.Channels)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  c{ch}: {s.Matched}/{s.Compared} (drawn {s.VisibleMatched}/{s.VisibleCompared})");
            if (s.FieldMismatches.Count > 0)
            {
                sb.Append(" [").Append(string.Join(", ", s.FieldMismatches.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"))).Append(']');
            }

            if (s.First is { } d)
            {
                sb.Append(CultureInfo.InvariantCulture, $" first f{d.Frame} {d.Field}: game {d.Reference}, sim {d.Sim}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Writes the scenario's report and refreshes the summary of every report written so far.</summary>
    private static void Write(AnimReport r)
    {
        string dir = OutputRoot;
        Directory.CreateDirectory(dir);
        var json = new JsonObject
        {
            ["scenario"] = r.ScenarioId,
            ["reference"] = r.Reference,
            ["full"] = r.Full,
            ["frames"] = r.Frames,
            ["framesMatched"] = r.FramesMatched,
            ["channelFrames"] = r.ChannelFrames,
            ["channelFramesMatched"] = r.ChannelFramesMatched,
            ["drawnFrames"] = r.VisibleFrames,
            ["drawnFramesMatched"] = r.VisibleFramesMatched,
            ["summary"] = Summary(r),
        };
        var channels = new JsonObject();
        foreach ((int ch, AnimChannelStats s) in r.Channels)
        {
            var fields = new JsonObject();
            foreach ((AnimField f, int n) in s.FieldMismatches)
            {
                fields[f.ToString()] = n;
            }

            channels[ch.ToString(CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["compared"] = s.Compared,
                ["matched"] = s.Matched,
                ["mismatches"] = fields,
                ["first"] = s.First is { } d ? $"f{d.Frame} {d.Field}: game {d.Reference}, sim {d.Sim}" : null,
            };
        }

        json["channels"] = channels;
        File.WriteAllText(Path.Combine(dir, r.ScenarioId + ".json"), json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var md = new StringBuilder("# Animation channels vs game (local report)\n\n| Scenario | Records | Frames all matching | Channel-frames matching | Drawn channel-frames matching |\n|---|---|---|---|---|\n");
        var details = new StringBuilder();
        foreach (string file in Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            JsonObject o = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            int frames = o["frames"]!.GetValue<int>();
            int cf = o["channelFrames"]!.GetValue<int>();
            int cm = o["channelFramesMatched"]!.GetValue<int>();
            int df = o["drawnFrames"]?.GetValue<int>() ?? 0;
            int dm = o["drawnFramesMatched"]?.GetValue<int>() ?? 0;
            md.Append(CultureInfo.InvariantCulture,
                $"| {o["scenario"]} | {(o["full"]!.GetValue<bool>() ? "ach 0-15" : "anim")} | {o["framesMatched"]}/{frames} | {cm}/{cf} ({(cf == 0 ? 1.0 : (double)cm / cf):P1}) | {dm}/{df} ({(df == 0 ? 1.0 : (double)dm / df):P1}) |\n");
            details.Append("\n```\n").Append(o["summary"]!.GetValue<string>()).Append("```\n");
        }

        File.WriteAllText(Path.Combine(dir, "report.md"), md.Append(details).ToString());
    }
}
