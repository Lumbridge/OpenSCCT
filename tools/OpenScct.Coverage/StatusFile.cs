using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace OpenScct.Coverage;

/// <summary>A single-unit entry of coverage/status.yaml.</summary>
/// <param name="Evidence">Free text naming the test or trace that verified the unit (status verified).</param>
internal sealed record StatusUnit(string Id, UnitStatus Status, string? Spec, string? Reason, string? Evidence);

/// <summary>An out-of-scope entry: every unit matching one of the globs is n-a for the reason.</summary>
internal sealed record NaEntry(int Index, IReadOnlyList<IdGlob> Globs, string Reason);

/// <summary>coverage/status.yaml: explicit unit statuses and out-of-scope globs.</summary>
internal sealed class StatusFile
{
    public int Version { get; private init; }

    public IReadOnlyList<StatusUnit> Units { get; private init; } = [];

    public IReadOnlyList<NaEntry> Na { get; private init; } = [];

    /// <summary>Problems in the file itself (an n-a entry without a reason, an unknown status...).</summary>
    public IReadOnlyList<string> Errors { get; private init; } = [];

    public static StatusFile Empty { get; } = new();

    public static StatusFile Load(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(reader, path);
    }

    public static StatusFile Parse(TextReader text, string name = "coverage/status.yaml")
    {
        var errors = new List<string>();
        var yaml = new YamlStream();
        try
        {
            yaml.Load(text);
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            return new StatusFile { Errors = [$"{name}: {e.Message}"] };
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new StatusFile { Errors = [$"{name}: expected a mapping at the top"] };
        }

        int version = 0;
        var units = new List<StatusUnit>();
        var na = new List<NaEntry>();
        foreach (KeyValuePair<YamlNode, YamlNode> kv in root.Children)
        {
            string key = Scalar(kv.Key) ?? "";
            switch (key)
            {
                case "version":
                    if (!int.TryParse(Scalar(kv.Value), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out version))
                    {
                        errors.Add($"{name}: version is not a number");
                    }

                    break;
                case "units":
                    ReadUnits(kv.Value, units, errors, name);
                    break;
                case "na":
                    ReadNa(kv.Value, na, errors, name);
                    break;
                default:
                    errors.Add($"{name}: unknown top-level key {key}");
                    break;
            }
        }

        if (version != 1)
        {
            errors.Add($"{name}: version must be 1");
        }

        return new StatusFile
        {
            Version = version,
            Units = units.OrderBy(u => u.Id, StringComparer.Ordinal).ToList(),
            Na = na,
            Errors = errors,
        };
    }

    private static void ReadUnits(YamlNode node, List<StatusUnit> units, List<string> errors, string name)
    {
        if (node is YamlScalarNode s && string.IsNullOrEmpty(s.Value))
        {
            return;
        }

        if (node is not YamlMappingNode map)
        {
            errors.Add($"{name}: units must be a mapping of unit id to entry");
            return;
        }

        foreach (KeyValuePair<YamlNode, YamlNode> kv in map.Children)
        {
            string id = Scalar(kv.Key) ?? "";
            if (kv.Value is not YamlMappingNode entry)
            {
                errors.Add($"{name}: units.{id} must be a mapping with a status");
                continue;
            }

            string? statusText = Get(entry, "status");
            if (statusText is null || !UnitStatusText.TryParse(statusText, out UnitStatus status))
            {
                errors.Add($"{name}: units.{id} has an unknown status \"{statusText}\" (unported, specced, ported, verified, n-a)");
                continue;
            }

            string? reason = Get(entry, "reason");
            if (status == UnitStatus.NotApplicable && string.IsNullOrWhiteSpace(reason))
            {
                errors.Add($"{name}: units.{id} is n-a without a reason");
            }

            units.Add(new StatusUnit(id, status, Get(entry, "spec"), reason, Get(entry, "evidence") ?? Get(entry, "test")));
        }
    }

    private static void ReadNa(YamlNode node, List<NaEntry> na, List<string> errors, string name)
    {
        if (node is not YamlSequenceNode seq)
        {
            errors.Add($"{name}: na must be a list");
            return;
        }

        int index = 0;
        foreach (YamlNode item in seq.Children)
        {
            index++;
            if (item is not YamlMappingNode entry)
            {
                errors.Add($"{name}: na entry {index} must be a mapping with globs and a reason");
                continue;
            }

            var globs = new List<IdGlob>();
            if (entry.Children.TryGetValue(new YamlScalarNode("globs"), out YamlNode? g) && g is YamlSequenceNode gs)
            {
                foreach (YamlNode gn in gs.Children)
                {
                    string? pattern = Scalar(gn);
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        errors.Add($"{name}: na entry {index} has an empty glob");
                        continue;
                    }

                    globs.Add(new IdGlob(pattern));
                }
            }

            if (globs.Count == 0)
            {
                errors.Add($"{name}: na entry {index} has no globs");
            }

            string? reason = Get(entry, "reason");
            if (string.IsNullOrWhiteSpace(reason))
            {
                errors.Add($"{name}: na entry {index} ({string.Join(", ", globs.Select(x => x.Pattern))}) has no reason");
                reason = "";
            }

            na.Add(new NaEntry(index, globs, reason.Trim()));
        }
    }

    private static string? Get(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? v) ? Scalar(v) : null;

    private static string? Scalar(YamlNode node) => node is YamlScalarNode s ? s.Value : null;
}
