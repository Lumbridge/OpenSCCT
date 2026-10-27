using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenScct.Coverage;

/// <summary>A difftest scenario (tests/scenarios/&lt;area&gt;/&lt;name&gt;.scenario.json) and the units it covers.</summary>
internal sealed record ScenarioInfo(string Id, string File, IReadOnlyList<string> Covers);

/// <summary>One row of the sim runner's report (local/difftest/sim/report.json).</summary>
internal sealed record SimResult(string Scenario, string Timing, bool Pass);

internal static class Scenarios
{
    /// <summary>The timing mode that gates a scenario: the sim replays the recorded frame times.</summary>
    public const string GateTiming = "DtReplay";

    public static IReadOnlyList<ScenarioInfo> ReadAll(string repoRoot, List<string> errors)
    {
        string dir = Path.Combine(repoRoot, "tests", "scenarios");
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var list = new List<ScenarioInfo>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.scenario.json", SearchOption.AllDirectories))
        {
            string rel = SpecUnits.Relative(repoRoot, file);
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement root = doc.RootElement;
                string? id = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id))
                {
                    errors.Add($"{rel}: scenario without an id");
                    continue;
                }

                var covers = new List<string>();
                if (root.TryGetProperty("covers", out JsonElement c) && c.ValueKind == JsonValueKind.Array)
                {
                    covers.AddRange(c.EnumerateArray().Select(x => x.GetString()).OfType<string>());
                }

                list.Add(new ScenarioInfo(id, rel, covers));
            }
            catch (JsonException e)
            {
                errors.Add($"{rel}: {e.Message}");
            }
        }

        return list.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The runner report, or null when there is none (CI, or no local goldens).</summary>
    public static IReadOnlyList<SimResult>? ReadReport(string path, List<string> warnings)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            var results = new List<SimResult>();
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                string? scenario = e.TryGetProperty("scenario", out JsonElement s) ? s.GetString() : null;
                string timing = e.TryGetProperty("timing", out JsonElement t) ? t.GetString() ?? "" : "";
                bool pass = e.TryGetProperty("pass", out JsonElement p) && p.ValueKind == JsonValueKind.True;
                if (scenario is not null)
                {
                    results.Add(new SimResult(scenario, timing, pass));
                }
            }

            return results;
        }
        catch (JsonException e)
        {
            warnings.Add($"sim report {path}: {e.Message}; verified status skipped");
            return null;
        }
    }

    /// <summary>
    /// True when the scenario's golden passes in the report: every row of the gate timing passes (or, with
    /// <paramref name="allTimings"/> or when the report has no gate row for it, every row passes).
    /// </summary>
    public static bool Passes(IReadOnlyList<SimResult> report, string scenarioId, bool allTimings)
    {
        var rows = report.Where(r => string.Equals(r.Scenario, scenarioId, StringComparison.Ordinal)).ToList();
        if (rows.Count == 0)
        {
            return false;
        }

        if (!allTimings)
        {
            var gate = rows.Where(r => string.Equals(r.Timing, GateTiming, StringComparison.Ordinal)).ToList();
            if (gate.Count > 0)
            {
                return gate.All(r => r.Pass);
            }
        }

        return rows.All(r => r.Pass);
    }
}
