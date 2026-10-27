using System;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.Server;

namespace OpenScct.MapSweep;

/// <summary><c>--catalog</c>: prints the map catalog the menus show (docs/MAPS.md) as a Markdown table, then the playlists.</summary>
public static class CatalogTable
{
    public static int Print()
    {
        MapCatalog catalog = MapCatalog.CreateDefault();
        Console.WriteLine("| # | Map | Shown as | Reloaded name | Kind | Orig | Modes that play | Starts spy/merc/MDM | Players | Picture | Playlists |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (MapCatalogEntry e in catalog.GetCatalog())
        {
            string modes = string.Join(", ", e.Modes.Where(m => m.Playable).Select(m => m.Mode switch
            {
                EGameMode.GM_Multi => string.Create(CultureInfo.InvariantCulture, $"Story {m.ObjectivesNeeded}/{m.Objectives}"),
                EGameMode.GM_DiskHunt => string.Create(CultureInfo.InvariantCulture, $"Disk Hunt {m.ObjectivesNeeded}/{m.Objectives}"),
                EGameMode.GM_DeathMatch => "DM",
                _ => "MDM",
            }));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {(e.HostOrder >= 0 ? e.HostOrder.ToString(CultureInfo.InvariantCulture) : "-")} | {e.Id} | {e.DisplayName} | {e.Name} | {e.Kind} | {(e.OriginalOrder >= 0 ? e.OriginalOrder.ToString(CultureInfo.InvariantCulture) : "-")} | {modes} | {e.SpyStarts}/{e.MercStarts}/{e.MercDeathmatchStarts} | {e.RecommendedPlayers} | {(e.PreviewImage is null ? "none" : "yes")} | {string.Join(", ", e.Playlists)} |"));
        }

        Console.WriteLine();
        foreach (MapPlaylist p in catalog.GetPlaylists())
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"- {p.Name} ({MapCatalog.ModeName(p.Mode)}, {p.Maps.Count} maps{(p.Missing.Count > 0 ? ", not installed: " + string.Join(", ", p.Missing) : string.Empty)}): {string.Join(", ", p.Maps)}"));
        }

        return 0;
    }
}
