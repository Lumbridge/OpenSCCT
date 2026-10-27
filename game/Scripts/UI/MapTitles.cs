using OpenScct.Server;

namespace OpenScctGame.UI;

/// <summary>
/// The one name every page shows for a map: the catalog's name of the original's map list (ENHANCED STEEL SQUAT,
/// docs/MAPS.md "The map catalog"), else the map's id for a map the catalog does not list (fixtures, TestRange).
/// </summary>
public static class MapTitles
{
    public static string Of(MapCatalog maps, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return string.Empty;
        }

        return maps?.GetCatalogEntry(id)?.DisplayName is { Length: > 0 } name ? name : id;
    }
}
