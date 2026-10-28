using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Maps;

namespace OpenScct.Launcher.Content;

/// <summary>
/// The game files of a player without Splinter Cell Chaos Theory: downloaded from OpenSCCT-Content
/// (docs/CONTENT-PACKS.md) instead of converted from an install. The shared packs are needed to play at all; the
/// stock maps come with the first download, every other map from the Maps window or in game when a lobby picks it.
/// </summary>
public static class PackedContent
{
    /// <summary>The shared packs the game cannot start a round without.</summary>
    public static IReadOnlyList<string> SharedIds { get; } = ["core", "chars", "audio"];

    /// <summary>The category whose maps come with the first download.</summary>
    public const string FirstCategory = "stock";

    /// <summary>Every shared pack is installed (any version): enough to play.</summary>
    public static bool Installed(ContentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        IReadOnlyList<InstalledContent> installed = store.Installed();
        return SharedIds.All(id => installed.Any(c => c.IsShared && string.Equals(c.Code, id, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The shared packs that are missing or older than the index's.</summary>
    public static IReadOnlyList<ContentSharedPack> SharedToFetch(ContentIndex index, ContentStore store)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(store);
        return index.Shared.Where(p => !store.HasShared(p)).ToList();
    }

    /// <summary>
    /// The maps to fetch: with <paramref name="firstDownload"/> every playable stock map not installed as the index
    /// has it; otherwise only installed maps the index has a newer copy of (a map the player removed stays removed).
    /// </summary>
    public static IReadOnlyList<ContentMap> MapsToFetch(ContentIndex index, ContentStore store, bool firstDownload)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(store);
        return index.PlayableMaps()
            .Where(m => !store.Has(m))
            .Where(m => firstDownload
                ? string.Equals(m.Category, FirstCategory, StringComparison.Ordinal)
                : store.InstalledMap(m.Code) is not null)
            .ToList();
    }

    /// <summary>Bytes the fetch would download.</summary>
    public static long DownloadSize(IEnumerable<ContentSharedPack> shared, IEnumerable<ContentMap> maps) =>
        shared.Sum(p => p.Download.Size) + maps.Sum(m => m.Full.Size);
}
