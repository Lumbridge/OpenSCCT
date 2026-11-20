using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenScct.Master.Player;

/// <summary>Serves one immutable report per page, including during an atomic sidecar replacement.</summary>
internal sealed class ProjectProgressReport(string path)
{
    internal const string Filename = "project-progress.json";
    private static readonly JsonElement Embedded = ReadEmbedded();
    private readonly object _gate = new();
    private JsonElement _editions = Embedded;
    private byte[]? _previous;

    private static JsonElement ReadEmbedded()
    {
        using var stream = typeof(ProjectProgressReport).Assembly.GetManifestResourceStream(Filename)
            ?? throw new InvalidDataException("The embedded project progress report is missing.");
        using var report = JsonDocument.Parse(stream);
        return report.RootElement.GetProperty("editions").Clone();
    }

    internal JsonElement ReadEditions()
    {
        lock (_gate)
        {
            if (!File.Exists(path)) return _editions;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (_previous is not null && data.AsSpan().SequenceEqual(_previous)) return _editions;
                _previous = data;
                using JsonDocument report = JsonDocument.Parse(data);
                JsonElement root = report.RootElement;
                if (root.GetProperty("schema_version").GetInt32() == 1 && ValidEditions(root.GetProperty("editions")))
                    _editions = root.GetProperty("editions").Clone();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
            {
                // Keep serving the last complete report if an operator supplies an unreadable or malformed file.
            }
            return _editions;
        }
    }

    private static bool ValidEditions(JsonElement editions)
    {
        var remaining = new HashSet<string?> { "ps2", "pc" };
        long matchedTotal = 0;
        foreach (JsonElement edition in editions.EnumerateArray())
        {
            if (!remaining.Remove(edition.GetProperty("id").GetString()) || !HasText(edition, "stage")) return false;
            var ids = new HashSet<string?>();
            foreach (JsonElement target in edition.GetProperty("targets").EnumerateArray())
            {
                if (!HasText(target, "id") || !ids.Add(target.GetProperty("id").GetString())
                    || !HasText(target, "label") || !HasText(target, "scope") || !HasText(target, "evidence")) return false;
                long matched = target.GetProperty("matched_bytes").GetInt64();
                long candidates = target.GetProperty("candidate_bytes").GetInt64();
                if (matched < 0 || candidates < 0) return false;
                JsonElement total = target.GetProperty("total_bytes");
                if (total.ValueKind != JsonValueKind.Null && checked(matched + candidates) > total.GetInt64()) return false;
                matchedTotal = checked(matchedTotal + matched);
            }
        }
        return remaining.Count == 0;
    }

    private static bool HasText(JsonElement entry, string property) =>
        !string.IsNullOrWhiteSpace(entry.GetProperty(property).GetString());
}
