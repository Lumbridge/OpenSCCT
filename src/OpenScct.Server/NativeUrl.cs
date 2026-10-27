using System;
using System.Collections.Generic;
using System.Text;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// The engine's URL options as the game rules receive them. The native URL parser keeps one entry per key: a key
/// given again replaces the value at the place of its first appearance (spec/game/match-options.md, "Duplicate
/// keys"; spec/game/round-travel.md rule 10). The round URLs of a series carry GamesInARow twice, and the second
/// game of a revenge pair only swaps its sides because InitGame and Login then see the last value.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "UE2 FURL::AddOption replaces a repeated key; not yet confirmed in this build (match-options.md open question)")]
public static class NativeUrl
{
    /// <summary>The options of <paramref name="options"/> (<c>?Key=Value...</c>) with each key once, holding its last value.</summary>
    public static string Rebuild(string options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var keys = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in options.Split('?', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            string key = eq < 0 ? pair : pair[..eq];
            if (!values.ContainsKey(key))
            {
                keys.Add(key);
            }

            values[key] = pair;
        }

        var sb = new StringBuilder(options.Length);
        foreach (string key in keys)
        {
            sb.Append('?').Append(values[key]);
        }

        return sb.ToString();
    }
}
