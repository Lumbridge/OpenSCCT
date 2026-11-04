using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;

namespace OpenScct.Server;

public sealed partial class MapCatalog
{
    public const string EnhancedMapsFilter = "Enhanced maps";
    public const string StockMapsFilter = "Stock maps";
    public const string CommunityMapsFilter = "Community maps";
    public const string ThreeVsThreeMapsFilter = "3v3 optimised maps";

    // The eleven versus maps shipped at launch in 2005. Reloaded's Classic playlist also includes the later
    // Polar Base and Steel Squat downloads; OriginalOrder is the installed file list, not the launch roster.
    private static readonly HashSet<string> StockMapIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "AquaD", "BankD", "ClubD", "DeftD", "FactD", "MissD", "MuseD", "OrphD", "RiveD", "StatD", "WareD",
    };

    /// <summary>
    /// The map browser's groups, available even without the optional Reloaded playlists. Enhanced excludes 3v3
    /// variants; Community excludes stock maps and the enhanced family, including its 3v3 variants. Maps stay in host order.
    /// </summary>
    public IReadOnlyList<MapPlaylist> GetMapFilters()
    {
        IReadOnlyList<MapCatalogEntry> host = GetHostMaps();
        MapPlaylist Group(string name, string description, Func<MapCatalogEntry, bool> include) =>
            new(name, description, EGameMode.GM_Multi, host.Where(include).Select(map => map.Id).ToArray(), []);

        return
        [
            Group(AllMapsPlaylist, "Every installed map.", _ => true),
            Group(EnhancedMapsFilter, "Enhanced versions of maps, excluding 3v3 variants.", map => IsEnhancedMap(map) && !IsThreeVsThreeMap(map)),
            Group(StockMapsFilter, "The eleven versus maps shipped with the original 2005 game.", map => StockMapIds.Contains(map.Id)),
            Group(CommunityMapsFilter, "Maps outside the Stock and Enhanced families, including community 3v3 maps.", map => !StockMapIds.Contains(map.Id) && !IsEnhancedMap(map)),
            Group(ThreeVsThreeMapsFilter, "Maps made for three spies versus three mercenaries.", IsThreeVsThreeMap),
        ];
    }

    private static bool IsEnhancedMap(MapCatalogEntry map) =>
        !StockMapIds.Contains(map.Id) && (HasEnhancedPrefix(map.Name) || HasEnhancedPrefix(map.DisplayName));

    private static bool HasEnhancedPrefix(string name)
    {
        string trimmed = name.TrimStart();
        return StartsWithWord(trimmed, "Enhanced") || StartsWithWord(trimmed, "Enh");
    }

    private static bool StartsWithWord(string name, string word) =>
        name.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
        (name.Length == word.Length || !char.IsLetterOrDigit(name[word.Length]));

    // A map's own identity counts too: a 3v3 tag, or exactly six recommended players (a map made for 8 or 24 is not a
    // 3v3 map).
    private static bool IsThreeVsThreeMap(MapCatalogEntry map) =>
        map.Playlists.Any(name => string.Equals(name, "3v3", StringComparison.OrdinalIgnoreCase)) ||
        map.Tags.Contains("3v3") || map.RecommendedPlayers == 6 ||
        HasThreeVsThreeTag(map.Name) || HasThreeVsThreeTag(map.DisplayName) || map.Id.Contains("3v3", StringComparison.OrdinalIgnoreCase);

    private static bool HasThreeVsThreeTag(string name)
    {
        int start = 0;
        while ((start = name.IndexOf("3v3", start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = start + 3;
            if ((start == 0 || !char.IsLetterOrDigit(name[start - 1])) &&
                (end == name.Length || !char.IsLetterOrDigit(name[end]))) return true;
            start = end;
        }
        return false;
    }
}
