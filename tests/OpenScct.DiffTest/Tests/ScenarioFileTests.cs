using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using OpenScct.DiffTest.Golden;
using OpenScct.DiffTest.Model;
using Xunit;

namespace OpenScct.DiffTest.Tests;

/// <summary>The tracked scenario files: they load, their ids are unique and match their file names, the units they
/// cover exist in the coverage inventory, and their frame windows fit their length.</summary>
public sealed class ScenarioFileTests
{
    private static readonly Lazy<HashSet<string>> s_units = new(() =>
    {
        string path = Path.Combine(RepoPaths.Root, "re", "inventory", "inventory.json");
        JsonObject inv = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return inv["units"]!.AsArray().Select(u => u!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
    });

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (string f in RepoPaths.ScenarioFiles())
        {
            data.Add(Path.GetRelativePath(RepoPaths.Root, f).Replace('\\', '/'));
        }

        return data;
    }

    [Fact]
    public void TheLocomotionSetIsPresent()
    {
        var ids = RepoPaths.ScenarioFiles().Select(Scenario.Load).Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(ids.Count(i => i.StartsWith("movement.", StringComparison.Ordinal)) >= 10, string.Join(", ", ids));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void ScenarioIsWellFormed(string relPath)
    {
        Scenario s = Scenario.Load(Path.Combine(RepoPaths.Root, relPath));
        string area = Path.GetFileName(Path.GetDirectoryName(Path.Combine(RepoPaths.Root, relPath)))!;
        string stem = Path.GetFileName(relPath).Replace(".scenario.json", "", StringComparison.Ordinal);
        Assert.Equal($"{area}.{stem}", s.Id);
        Assert.Contains(s.Team, new[] { "spy", "merc" });
        Assert.InRange(s.Repeats, 1, 10);
        Assert.InRange(s.DurationFrames, 1, 60 * 60);
        Assert.Equal("playlevel", s.Doc["mode"]!.GetValue<string>());
        Assert.NotEmpty(s.Covers);
        foreach (string unit in s.Covers)
        {
            Assert.True(s_units.Value.Contains(unit), $"{s.Id}: unit {unit} is not in re/inventory/inventory.json");
        }

        Assert.DoesNotContain(s.Setup, op => op.Raw["field"]?.GetValue<string>() == "Physics");
        Assert.All(s.Inputs, e => Assert.InRange(e.Frame, 0, s.DurationFrames - 1));
        foreach (KeyValuePair<string, JsonNode?> m in s.Measure)
        {
            foreach (string key in new[] { "from", "to" })
            {
                if (m.Value?[key] is JsonNode n)
                {
                    Assert.InRange(n.GetValue<int>(), 0, s.DurationFrames);
                }
            }
        }
    }

    [Fact]
    public void TimelineHoldsButtonsAndPulsesJumps()
    {
        Scenario s = Scenario.Load(Path.Combine(RepoPaths.Scenarios, "movement", "spy-jump-forward.scenario.json"));
        IReadOnlyList<FrameInput> t = s.Timeline();
        Assert.Equal(s.DurationFrames, t.Count);
        Assert.Equal(1, t[0].Button("BT_Up"));
        Assert.Equal(1, t[59].Button("BT_Up"));
        Assert.True(t[60].Jump);
        Assert.False(t[61].Jump);
        Assert.Equal(1, t[149].Button("BT_Up"));
        Assert.Equal(0, t[150].Button("BT_Up"));
        Assert.Empty(t[150].Buttons);
    }
}
