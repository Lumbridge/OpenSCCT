using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScctGame.Options;

/// <summary>
/// Stable per-action display order independent of the gesture dictionary's physical ordering. The metadata
/// changes only after an explicit edit; reading an older profile never dirties the options draft.
/// </summary>
public static class PadBindingOrder
{
    public static IReadOnlyList<string> Order(PadSettings settings, bool merc, string entryId, IEnumerable<string> available)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        ArgumentNullException.ThrowIfNull(available);
        string[] inputs = available.Distinct(StringComparer.Ordinal).ToArray();
        var remaining = new HashSet<string>(inputs, StringComparer.Ordinal);
        var ordered = new List<string>(inputs.Length);
        if (settings.BindingOrderOf(merc).TryGetValue(entryId, out List<string>? saved) && saved is not null)
            foreach (string input in saved)
                if (remaining.Remove(input)) ordered.Add(input);
        foreach (string input in inputs)
            if (remaining.Remove(input)) ordered.Add(input);
        return ordered;
    }

    public static void RememberOrder(PadSettings settings, bool merc, string entryId, IEnumerable<string> ordered)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        ArgumentNullException.ThrowIfNull(ordered);
        List<string> inputs = ordered.Where(PadSettings.Controls.Contains).Distinct(StringComparer.Ordinal).ToList();
        Dictionary<string, List<string>> metadata = settings.BindingOrderOf(merc);
        if (inputs.Count == 0) metadata.Remove(entryId);
        else metadata[entryId] = inputs;
    }
}
