using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenScct.Coverage;

/// <summary>Coverage status of a unit. The first four are ordered: each implies the ones before it.</summary>
internal enum UnitStatus
{
    Unported = 0,
    Specced = 1,
    Ported = 2,
    Verified = 3,
    NotApplicable = 4,
}

internal static class UnitStatusText
{
    public static string Text(UnitStatus s) => s switch
    {
        UnitStatus.Unported => "unported",
        UnitStatus.Specced => "specced",
        UnitStatus.Ported => "ported",
        UnitStatus.Verified => "verified",
        UnitStatus.NotApplicable => "n-a",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static bool TryParse(string text, out UnitStatus status)
    {
        switch (text.Trim())
        {
            case "unported": status = UnitStatus.Unported; return true;
            case "specced": status = UnitStatus.Specced; return true;
            case "ported": status = UnitStatus.Ported; return true;
            case "verified": status = UnitStatus.Verified; return true;
            case "n-a": status = UnitStatus.NotApplicable; return true;
            default: status = UnitStatus.Unported; return false;
        }
    }
}

/// <summary>The computed status of one unit and the evidence for it.</summary>
internal sealed class UnitState
{
    public UnitState(string id, InventoryUnit? unit)
    {
        Id = id;
        Unit = unit;
    }

    public string Id { get; }

    /// <summary>The inventory entry, or null for engine, format and Reloaded units (eng:, fmt:, rl:).</summary>
    public InventoryUnit? Unit { get; }

    public UnitStatus Status { get; set; }

    /// <summary>Spec references, <c>spec/movement/walking.md:3</c>.</summary>
    public SortedSet<string> Specs { get; } = new(StringComparer.Ordinal);

    /// <summary>C# members that port the unit, <c>OpenScct.Game: OpenScct.Game.SBase.SInterrupteur.Trigger</c>.</summary>
    public SortedSet<string> Ports { get; } = new(StringComparer.Ordinal);

    /// <summary>Scenarios covering the unit whose golden passes in the runner report.</summary>
    public SortedSet<string> PassingScenarios { get; } = new(StringComparer.Ordinal);

    /// <summary>True when the def unit's class defaults come from the class skeleton generator.</summary>
    public bool Generated { get; set; }

    /// <summary>Set by coverage/status.yaml (single-unit entry).</summary>
    public string? StatusFileNote { get; set; }

    public string? NaReason { get; set; }

    /// <summary>The n-a entry of coverage/status.yaml that matched (1-based), or null.</summary>
    public int? NaEntry { get; set; }

    public void Raise(UnitStatus s)
    {
        if (Status != UnitStatus.NotApplicable && s > Status)
        {
            Status = s;
        }
    }
}

/// <summary>Everything the report is computed from.</summary>
internal sealed class CoverageInputs
{
    public required string RepoRoot { get; init; }

    public required Inventory Inventory { get; init; }

    public StatusFile Status { get; init; } = StatusFile.Empty;

    public IReadOnlyList<SpecUnitRef> Specs { get; init; } = [];

    public IReadOnlyList<ScannedAssembly> Assemblies { get; init; } = [];

    public IReadOnlyList<ScenarioInfo> Scenarios { get; init; } = [];

    /// <summary>The sim runner report, or null when there is none (then nothing is verified by scenarios).</summary>
    public IReadOnlyList<SimResult>? Report { get; init; }

    public string? ReportPath { get; init; }

    public bool AllTimings { get; init; }

    /// <summary>Problems found while reading the inputs; they are reported with the rest.</summary>
    public IReadOnlyList<string> InputErrors { get; init; } = [];

    public IReadOnlyList<string> InputWarnings { get; init; } = [];
}

/// <summary>Result of <see cref="CoverageModel.Compute"/>.</summary>
internal sealed class CoverageResult
{
    public required CoverageInputs Inputs { get; init; }

    /// <summary>Every inventory unit, sorted by id.</summary>
    public required IReadOnlyList<UnitState> Units { get; init; }

    /// <summary>Engine, format and Reloaded units named by specs or ports (outside the inventory), sorted by id.</summary>
    public required IReadOnlyList<UnitState> Extra { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Spec <c>Units:</c> ids that are not in the inventory, as "spec/file.md:line: id".</summary>
    public required IReadOnlyList<string> UnknownSpecIds { get; init; }

    /// <summary>Units matched by each n-a entry (by entry index).</summary>
    public required IReadOnlyDictionary<int, int> NaMatches { get; init; }

    /// <summary>Per scenario: true (passes), false (fails) or null (no result in the report).</summary>
    public required IReadOnlyDictionary<string, bool?> ScenarioResults { get; init; }

    public int AttributeCount { get; init; }
}

/// <summary>Joins the inputs into one status per unit.</summary>
internal static partial class CoverageModel
{
    public static CoverageResult Compute(CoverageInputs input)
    {
        Inventory inv = input.Inventory;
        var errors = new List<string>(input.InputErrors);
        var warnings = new List<string>(input.InputWarnings);
        var unknownSpec = new List<string>();
        var units = new Dictionary<string, UnitState>(StringComparer.Ordinal);
        foreach (InventoryUnit u in inv.Units)
        {
            units[u.Id] = new UnitState(u.Id, u);
        }

        var extra = new Dictionary<string, UnitState>(StringComparer.Ordinal);
        UnitState? State(string id)
        {
            if (units.TryGetValue(id, out UnitState? s))
            {
                return s;
            }

            if (Inventory.IsInventoryKind(id))
            {
                return null;
            }

            if (!extra.TryGetValue(id, out s))
            {
                s = new UnitState(id, null);
                extra[id] = s;
            }

            return s;
        }

        // Spec Units lists: specced.
        foreach (SpecUnitRef r in input.Specs)
        {
            string where = $"{r.SpecFile}:{r.Line}";
            Resolved res = UnitIds.Normalize(inv, r.Id, where);
            if (res.Id is null)
            {
                unknownSpec.Add($"{where}: {r.Id}");
                continue;
            }

            UnitState? s = State(res.Id);
            if (s is null)
            {
                unknownSpec.Add($"{where}: {r.Id}");
                continue;
            }

            s.Specs.Add(where);
            s.Raise(UnitStatus.Specced);
        }

        // Generated class skeletons: their defaults are ported, and their states are known even when empty.
        foreach (GeneratedClass g in input.Assemblies.SelectMany(a => a.GeneratedClasses))
        {
            string? cls = GeneratedClassPath(inv, g.Namespace, g.Name);
            if (cls is null)
            {
                continue;
            }

            foreach (string st in g.States)
            {
                inv.AddState(cls, st);
            }

            if (units.TryGetValue("def:" + cls, out UnitState? d))
            {
                d.Generated = true;
                d.Raise(UnitStatus.Ported);
            }
        }

        // Provenance attributes: ported.
        var anchorCache = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
        int attributeCount = 0;
        foreach (ScannedAssembly asm in input.Assemblies)
        {
            foreach (FoundAttribute a in asm.Attributes)
            {
                attributeCount++;
                string where = $"{a.Assembly}: {a.Location}";
                Resolved res;
                switch (a.Kind)
                {
                    case ProvenanceAttributeKind.PortOf:
                        res = UnitIds.Normalize(inv, a.Value, $"[PortOf] on {where}");
                        break;
                    case ProvenanceAttributeKind.Uc:
                        res = UnitIds.FromUcPath(inv, a.Value);
                        if (res.Error is { } e)
                        {
                            res = res with { Error = $"{e} on {where}" };
                        }

                        break;
                    default:
                        res = new Resolved(UnitIds.FromUcNative(inv, a.Value));
                        break;
                }

                if (res.Error is not null)
                {
                    errors.Add(res.Error);
                }

                if (res.Warning is not null)
                {
                    warnings.Add(res.Warning);
                }

                if (a.Kind == ProvenanceAttributeKind.PortOf)
                {
                    CheckSpecAnchor(input.RepoRoot, a, where, errors, warnings, anchorCache);
                }

                if (res.Id is null)
                {
                    continue;
                }

                UnitState? s = State(res.Id);
                if (s is null)
                {
                    continue;
                }

                if (a.Kind == ProvenanceAttributeKind.Uc && a.Sha1 is { Length: > 0 } sha && s.Unit?.Sha1 is { Length: > 0 } invSha &&
                    !string.Equals(sha, invSha, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"[Uc(\"{a.Value}\")] on {where}: Sha1 {sha} differs from the inventory's {invSha} (the original changed since the port)");
                }

                s.Ports.Add(where);
                s.Raise(UnitStatus.Ported);
            }
        }

        // Single-unit entries of status.yaml.
        var explicitIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (StatusUnit su in input.Status.Units)
        {
            Resolved res = UnitIds.Normalize(inv, su.Id, "coverage/status.yaml units");
            if (res.Error is not null)
            {
                errors.Add(res.Error);
                continue;
            }

            UnitState? s = res.Id is null ? null : State(res.Id);
            if (s is null)
            {
                continue;
            }

            explicitIds.Add(s.Id);
            s.StatusFileNote = UnitStatusText.Text(su.Status) + (su.Evidence is { Length: > 0 } ev ? $" ({ev})" : "");
            if (su.Spec is { Length: > 0 } spec)
            {
                string file = spec.Split('#')[0];
                if (!File.Exists(Path.Combine(input.RepoRoot, file)))
                {
                    errors.Add($"coverage/status.yaml units.{su.Id}: spec {spec} does not exist");
                }
                else
                {
                    s.Specs.Add(spec);
                }
            }

            if (su.Status == UnitStatus.NotApplicable)
            {
                if (s.Ports.Count > 0)
                {
                    warnings.Add($"coverage/status.yaml marks {s.Id} n-a but it is ported ({s.Ports.First()})");
                }

                s.Status = UnitStatus.NotApplicable;
                s.NaReason = su.Reason;
            }
            else
            {
                s.Raise(su.Status);
            }
        }

        // Scenarios: verified when the golden passes and the unit is ported.
        var scenarioResults = new SortedDictionary<string, bool?>(StringComparer.Ordinal);
        foreach (ScenarioInfo sc in input.Scenarios)
        {
            bool? pass = input.Report is null ? null : input.Report.Any(r => string.Equals(r.Scenario, sc.Id, StringComparison.Ordinal))
                ? Scenarios.Passes(input.Report, sc.Id, input.AllTimings)
                : null;
            scenarioResults[sc.Id] = pass;
            foreach (string cover in sc.Covers)
            {
                Resolved res = UnitIds.Normalize(inv, cover, $"{sc.File} covers");
                if (res.Error is not null)
                {
                    errors.Add(res.Error);
                    continue;
                }

                UnitState? s = res.Id is null ? null : State(res.Id);
                if (s is null || pass != true)
                {
                    continue;
                }

                s.PassingScenarios.Add(sc.Id);
                if (s.Status == UnitStatus.NotApplicable)
                {
                    continue;
                }

                if (s.Status >= UnitStatus.Ported)
                {
                    s.Raise(UnitStatus.Verified);
                }
                else
                {
                    warnings.Add($"{s.Id} is covered by the passing scenario {sc.Id} but has no port ([PortOf] or [Uc]); left {UnitStatusText.Text(s.Status)}");
                }
            }
        }

        // Out-of-scope globs: n-a unless the unit is ported or has its own entry.
        var naMatches = new SortedDictionary<int, int>();
        foreach (NaEntry e in input.Status.Na)
        {
            naMatches[e.Index] = 0;
        }

        foreach (UnitState s in units.Values)
        {
            if (explicitIds.Contains(s.Id))
            {
                continue;
            }

            NaEntry? hit = input.Status.Na.FirstOrDefault(e => e.Globs.Any(g => g.IsMatch(s.Id)));
            if (hit is null)
            {
                continue;
            }

            naMatches[hit.Index]++;

            // Generated defaults alone do not count against n-a: the generator emits every class.
            if (s.Status >= UnitStatus.Ported && s.Ports.Count > 0)
            {
                warnings.Add($"{s.Id} matches n-a entry {hit.Index} but is {UnitStatusText.Text(s.Status)}; kept {UnitStatusText.Text(s.Status)}");
                continue;
            }

            s.Status = UnitStatus.NotApplicable;
            s.NaReason = hit.Reason;
            s.NaEntry = hit.Index;
        }

        foreach (NaEntry e in input.Status.Na)
        {
            foreach (IdGlob g in e.Globs)
            {
                if (!units.Keys.Any(g.IsMatch))
                {
                    warnings.Add($"coverage/status.yaml n-a entry {e.Index}: glob {g.Pattern} matches no unit");
                }
            }
        }

        return new CoverageResult
        {
            Inputs = input,
            Units = units.Values.OrderBy(s => s.Id, StringComparer.Ordinal).ToList(),
            Extra = extra.Values.OrderBy(s => s.Id, StringComparer.Ordinal).ToList(),
            Errors = errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            UnknownSpecIds = unknownSpec.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            NaMatches = naMatches,
            ScenarioResults = scenarioResults,
            AttributeCount = attributeCount,
        };
    }

    /// <summary>The script class of a generated skeleton type (docs/CODEGEN.md, Names), or null.</summary>
    public static string? GeneratedClassPath(Inventory inv, string ns, string name)
    {
        string? pkg = ns switch
        {
            "OpenScct.Core" => "Core",
            "OpenScct.Core.Engine" => "Engine",
            "OpenScct.Game.SBase" => "SBase",
            _ => null,
        };
        if (pkg is null)
        {
            return null;
        }

        if (pkg == "Core" && name == "UObject")
        {
            return "Core.Object";
        }

        if (inv.HasClass(pkg + "." + name))
        {
            return pkg + "." + name;
        }

        if (name.Length > 1 && name[0] == 'U' && inv.HasClass(pkg + "." + name[1..]))
        {
            return pkg + "." + name[1..];
        }

        return null;
    }

    private static void CheckSpecAnchor(
        string repoRoot,
        FoundAttribute a,
        string where,
        List<string> errors,
        List<string> warnings,
        Dictionary<string, HashSet<string>?> cache)
    {
        if (a.Spec is not { Length: > 0 } spec)
        {
            warnings.Add($"[PortOf(\"{a.Value}\")] on {where} has no Spec anchor");
            return;
        }

        string[] parts = spec.Split('#', 2);
        string file = parts[0];
        if (!cache.TryGetValue(file, out HashSet<string>? anchors))
        {
            string full = Path.Combine(repoRoot, file);
            anchors = File.Exists(full) ? HeadingAnchors(File.ReadAllText(full)) : null;
            cache[file] = anchors;
        }

        if (anchors is null)
        {
            errors.Add($"[PortOf(\"{a.Value}\")] on {where}: spec {file} does not exist");
            return;
        }

        if (parts.Length == 2 && parts[1].Length > 0 && !anchors.Contains(parts[1]))
        {
            warnings.Add($"[PortOf(\"{a.Value}\")] on {where}: {file} has no heading for #{parts[1]}");
        }
    }

    /// <summary>The anchors a Markdown renderer gives the file's headings (GitHub rules: lower case, punctuation
    /// dropped, spaces to hyphens, repeats numbered).</summary>
    public static HashSet<string> HeadingAnchors(string markdown)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool fence = false;
        foreach (string raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                fence = !fence;
                continue;
            }

            Match m = HeadingPattern().Match(raw);
            if (fence || !m.Success)
            {
                continue;
            }

            string slug = Slug(m.Groups[1].Value);
            if (counts.TryGetValue(slug, out int n))
            {
                counts[slug] = n + 1;
                anchors.Add($"{slug}-{n}");
            }
            else
            {
                counts[slug] = 1;
                anchors.Add(slug);
            }
        }

        return anchors;
    }

    internal static string Slug(string heading)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                sb.Append(c);
            }
            else if (c == ' ')
            {
                sb.Append('-');
            }
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"^#{1,6}\s+(.*?)\s*#*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();
}
