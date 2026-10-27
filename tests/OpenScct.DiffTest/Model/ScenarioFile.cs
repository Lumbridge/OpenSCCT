using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace OpenScct.DiffTest.Model;

/// <summary>A setup step run before the inputs start (teleport, stance, view, writes, waits).</summary>
public sealed record SetupOp(string Op, JsonObject Raw);

/// <summary>A frame-indexed input event of a scenario.</summary>
public sealed record InputEvent(int Frame, IReadOnlyDictionary<string, int> Buttons, bool Jump, Rot3? View, bool? FastSpeed);

/// <summary>
/// A scenario (svm.scenario/1, tests/scenarios/README.md): map, team, spot, setup, settle frames, frame-indexed
/// inputs, recording length, repeats, covered units and what to measure.
/// </summary>
public sealed class Scenario
{
    public const string Format = "svm.scenario/1";

    private Scenario(JsonObject doc, string path)
    {
        Doc = doc;
        Path = path;
        Id = doc["id"]!.GetValue<string>();
        Title = doc["title"]!.GetValue<string>();
        Map = doc["map"]!.GetValue<string>();
        Team = doc["players"]![0]!["team"]!.GetValue<string>();
        JsonArray floor = doc["spawn"]!["floor"]!.AsArray();
        SpawnFloor = new Vec3(floor[0]!.GetValue<double>(), floor[1]!.GetValue<double>(), floor[2]!.GetValue<double>());
        SpawnYaw = doc["spawn"]!["yaw"]!.GetValue<int>();
        SettleFrames = doc["settleFrames"]!.GetValue<int>();
        DurationFrames = doc["durationFrames"]!.GetValue<int>();
        Repeats = doc["repeats"]!.GetValue<int>();
        Profile = doc["profile"]?.GetValue<string>() ?? "locomotion";
        Covers = doc["covers"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Setup = (doc["setup"] as JsonArray ?? []).Select(n => new SetupOp(n!["op"]!.GetValue<string>(), n.AsObject())).ToList();
        Inputs = doc["inputs"]!.AsArray().Select(n => ParseEvent(n!.AsObject())).ToList();
        Measure = doc["measure"] as JsonObject ?? [];
    }

    public JsonObject Doc { get; }

    public string Path { get; }

    public string Id { get; }

    public string Title { get; }

    public string Map { get; }

    /// <summary>spy or merc.</summary>
    public string Team { get; }

    /// <summary>The spot: X, Y and the floor's Z (the recorder puts the cylinder 2 uu above it).</summary>
    public Vec3 SpawnFloor { get; }

    public int SpawnYaw { get; }

    public int SettleFrames { get; }

    public int DurationFrames { get; }

    public int Repeats { get; }

    public string Profile { get; }

    public IReadOnlyList<string> Covers { get; }

    public IReadOnlyList<SetupOp> Setup { get; }

    public IReadOnlyList<InputEvent> Inputs { get; }

    public JsonObject Measure { get; }

    public static Scenario Load(string path)
    {
        JsonObject doc = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException($"{path}: not a JSON object");
        if (doc["format"]?.GetValue<string>() != Format)
        {
            throw new InvalidDataException($"{path}: format is not {Format}");
        }

        return new Scenario(doc, path);
    }

    /// <summary>
    /// The input state before each frame's tick, frames 0..DurationFrames-1: held buttons carry over, a jump,
    /// a view and a speed-mode write apply to their own frame only. The same expansion as the recorder's.
    /// </summary>
    public IReadOnlyList<FrameInput> Timeline()
    {
        var byFrame = Inputs.GroupBy(e => e.Frame).ToDictionary(g => g.Key, g => g.ToList());
        var held = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var list = new List<FrameInput>(DurationFrames);
        for (int f = 0; f < DurationFrames; f++)
        {
            bool jump = false;
            Rot3? view = null;
            bool? fast = null;
            if (byFrame.TryGetValue(f, out List<InputEvent>? events))
            {
                foreach (InputEvent e in events)
                {
                    foreach (KeyValuePair<string, int> b in e.Buttons)
                    {
                        held[b.Key] = b.Value;
                    }

                    jump |= e.Jump;
                    view = e.View ?? view;
                    fast = e.FastSpeed ?? fast;
                }
            }

            var buttons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> b in held.Where(b => b.Value != 0))
            {
                buttons[b.Key] = b.Value;
            }

            list.Add(new FrameInput(buttons, jump, view, fast));
        }

        return list;
    }

    private static InputEvent ParseEvent(JsonObject o)
    {
        var buttons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (o["buttons"] is JsonObject b)
        {
            foreach (KeyValuePair<string, JsonNode?> kv in b)
            {
                buttons[kv.Key] = kv.Value!.GetValue<int>();
            }
        }

        Rot3? view = o["view"] is JsonObject v ? new Rot3(v["pitch"]!.GetValue<int>(), v["yaw"]!.GetValue<int>(), 0) : null;
        return new InputEvent(o["frame"]!.GetValue<int>(), buttons, o["jump"]?.GetValue<bool>() == true, view,
            o["fastSpeed"]?.GetValue<bool>());
    }
}
