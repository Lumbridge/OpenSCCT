using System;
using System.Collections.Generic;
using System.Text;
using OpenScct.Provenance;

namespace OpenScct.Game.GameFlow;

/// <summary>
/// The engine side of a map travel URL (spec/game/round-travel.md rules 6 and 10, match-options.md edge cases): the
/// map part, and the options as the engine hands them to the next map's InitGame and Login. The engine keeps one
/// value per key, the last one given, at the place the key first appeared; a relative travel adds the new options to
/// the ones the machine already had. The script option helpers (ParseOption, first match) are a different rule.
/// </summary>
public static class TravelUrl
{
    /// <summary>The map part (before the first '?') and the options part (from it, or "").</summary>
    public static (string Map, string Options) Split(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        int q = url.IndexOf('?', StringComparison.Ordinal);
        return q < 0 ? (url, string.Empty) : (url[..q], url[q..]);
    }

    /// <summary>
    /// The options of <paramref name="options"/> with each repeated key reduced to its last value, kept at its
    /// first position ("?GamesInARow=2?A=1?GamesInARow=1" gives "?GamesInARow=1?A=1"). Keys compare ignoring case;
    /// empty items are dropped.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 FURL::AddOption replaces the value of an existing key (match-options.md open question 1)")]
    public static string NativeOptions(string options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var keys = new List<string>();
        var items = new List<string>();
        foreach (string item in options.Split('?'))
        {
            if (item.Length == 0)
            {
                continue;
            }

            int eq = item.IndexOf('=', StringComparison.Ordinal);
            string key = eq < 0 ? item : item[..eq];
            int at = keys.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                items[at] = item;
            }
            else
            {
                keys.Add(key);
                items.Add(item);
            }
        }

        var sb = new StringBuilder();
        foreach (string item in items)
        {
            sb.Append('?').Append(item);
        }

        return sb.ToString();
    }

    /// <summary>A relative travel: <paramref name="current"/> options with <paramref name="added"/> applied on top.</summary>
    public static string Merge(string current, string added) => NativeOptions((current ?? string.Empty) + (added ?? string.Empty));

    /// <summary>
    /// The login options a player arrives with on the next map: his own options with those of the URL he was sent
    /// (a client's ClientTravel URL, or the listen host's Level.NextURL) applied on top.
    /// </summary>
    public static string NextLogin(string loginOptions, string travelUrl) => Merge(loginOptions, Split(travelUrl ?? string.Empty).Options);
}
