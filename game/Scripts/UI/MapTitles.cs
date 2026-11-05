using OpenScct.Server;

namespace OpenScctGame.UI;

/// <summary>
/// The one name every page shows for a map: the catalog's name of the original's map list (ENHANCED STEEL SQUAT,
/// docs/MAPS.md "The map catalog"), else the map's id for a map the catalog does not list (fixtures, TestRange).
/// </summary>
public static class MapTitles
{
    /// <summary>
    /// The map's name with its id after it (scoreboards): the name of <paramref name="maps"/> (the round's own catalog,
    /// which knows editor and downloaded maps and their identity names), else of the default catalog.
    /// </summary>
    public static string WithCode(MapCatalog? maps, string? id)
    {
        string name = maps is null ? id ?? string.Empty : Of(maps, id);
        if (string.Equals(name, id, System.StringComparison.OrdinalIgnoreCase) && !ReferenceEquals(maps, MapImages.Default))
        {
            name = Of(MapImages.Default, id);
        }

        return string.Equals(name, id, System.StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(id)
            ? name : $"{name} ({id})";
    }

    public static string Of(MapCatalog maps, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return string.Empty;
        }

        return maps?.GetCatalogEntry(id)?.DisplayName is { Length: > 0 } name ? name : id;
    }
}
