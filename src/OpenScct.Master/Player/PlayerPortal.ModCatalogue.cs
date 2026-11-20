using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenScct.Master.Web;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private readonly CommunityMapCatalogue _communityMaps = new();

    private static readonly (string Name, string Description, string Url)[] PcMods =
    [
        ("Enhanced SCCT Versus", "Community fixes and quality-of-life improvements for original Windows Versus.", EnhancedSource),
        ("SCCT Versus Reloaded", "Gameplay and map updates for PC Versus. Check the project guide for supported versions.", ReloadedFeatures),
    ];

    private IEnumerable<(string Url, string Name, string Detail)> ModSearchEntries() =>
        _communityMaps.Current().Select(map => (MapPage(map), map.Title, "Community map · Godot and Unreal · " + string.Join(", ", map.Modes)))
            .Concat(ArchiveMaps.Select(map => (map.Url, map.Title, map.SearchText)))
            .Concat(PcMods.Select(mod => (mod.Url, mod.Name, mod.Description)));

    private static string MapPage(CommunityMap map) => "/mods/maps/" + U(map.Code);

    private static string MapImage(CommunityMap map, bool lazy)
    {
        string path = "/web/assets/showcase-map-" + map.Code.ToLowerInvariant() + ".jpg";
        string url = AssetUrl(path);
        return url == path ? "" :
            $"<img class=\"mod-shot\" src=\"{H(url)}\" alt=\"View of {H(map.Title)}\" width=\"640\" height=\"360\"{(lazy ? " loading=\"lazy\"" : "")}>";
    }

    private static string MapSize(long bytes) => bytes >= 1024 * 1024
        ? (bytes / (1024d * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MiB"
        : Math.Max(1, (bytes + 1023) / 1024).ToString(CultureInfo.InvariantCulture) + " KiB";

    private IResult Mods(HttpContext c)
    {
        string edition = c.Request.Query["edition"].ToString();
        if (!Editions.Any(e => e.Id == edition)) edition = "";
        string query = c.Request.Query["q"].ToString().Trim();
        if (query.Length > 96) query = query[..96];
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Matches(string terms) => words.All(word => terms.Contains(word, StringComparison.OrdinalIgnoreCase));

        var body = new StringBuilder("<div class=\"project-site mod-library\">");
        body.Append(ProjectHeading("Community", "Mods", "Find optional Versus mods for the PC original and community maps for the Modernisations."))
            .Append("<div class=\"mod-controls\"><nav class=\"mod-filters\" aria-label=\"Filter by edition\">");
        (string Id, string Name)[] filters = [("", "All editions"), ("ps2", "PlayStation 2"), ("pc", "PC / Steam"), ("godot", "Godot"), ("unreal", "Unreal")];
        foreach (var filter in filters)
        {
            string url = "/mods?edition=" + U(filter.Id) + (query.Length > 0 ? "&q=" + U(query) : "");
            body.Append($"<a id=\"{(filter.Id.Length > 0 ? filter.Id : "all-editions")}\" href=\"{H(url)}\"{(edition == filter.Id ? " aria-current=\"page\"" : "")}>{H(filter.Name)}</a>");
        }
        body.Append($"""
            </nav><form class="mod-search" method="get" action="/mods" role="search" aria-label="Search mods">
              <input type="hidden" name="edition" value="{H(edition)}">
              <label class="sr-only" for="mod-query">Search mods</label>
              <input id="mod-query" name="q" type="search" maxlength="96" placeholder="Search mods" value="{H(query)}">
              <button type="submit">Search</button>
            </form></div>
            """);
        int shown = 0;
        if (edition is "" or "pc")
            shown += AppendArchiveMaps(body, Matches, query.Length > 0);
        if (edition is "" or "godot" or "unreal")
        {
            var maps = _communityMaps.Current().Where(map => Matches(map.Title + " " + map.Code + " " + string.Join(" ", map.Modes) + " Godot Unreal")).ToArray();
            if (maps.Length > 0)
            {
                shown += maps.Length;
                body.Append("<section class=\"mod-section\" aria-labelledby=\"community-map-heading\"><h2 id=\"community-map-heading\">Modernisation maps</h2><p>Build and play these maps with your Steam copy in Godot or Unreal.</p><div class=\"mod-grid\">");
                foreach (CommunityMap map in maps)
                    body.Append($"""
                        <article class="mod-card">{MapImage(map, lazy: true)}<div class="mod-card-copy">
                          <h3>{Link(MapPage(map), map.Title)}</h3><p>{H(string.Join(" · ", map.Modes))}</p>
                          <small>Godot &amp; Unreal <span>{MapSize(map.DownloadBytes)} source download</span></small>
                        </div></article>
                        """);
                body.Append("</div></section>");
            }
        }
        if (edition is "" or "pc")
        {
            var pcMods = PcMods.Where(mod => Matches(mod.Name + " " + mod.Description)).ToArray();
            if (pcMods.Length > 0)
            {
                shown += pcMods.Length;
                body.Append("<section class=\"mod-section\" aria-labelledby=\"pc-mod-heading\"><h2 id=\"pc-mod-heading\">Original PC mods</h2><div class=\"mod-grid\">");
                foreach (var mod in pcMods)
                    body.Append($"<article class=\"mod-card mod-card-copy\"><h3>{Link(mod.Url, mod.Name)}</h3><p>{H(mod.Description)}</p><small>PC / Steam · Versus</small></article>");
                body.Append("</div></section>");
            }
        }
        if (shown == 0 && query.Length > 0)
            body.Append("<p class=\"mod-empty\">No mods match your search. Try another name or edition.</p>");
        else if (edition == "ps2")
            body.Append("<section class=\"mod-empty\"><h2>PlayStation 2 mods</h2><p>Select a separately patched image made from your own disc in the PS2 setup.</p>")
                .Append(Link("/projects/ps2", "See PS2 setup", "hub-secondary")).Append("</section>");
        return Show(c, "Mods", "mods", body.Append("</div>").ToString());
    }

    private IResult ModMap(HttpContext c)
    {
        string? code = c.Request.RouteValues["code"]?.ToString();
        var map = _communityMaps.Current().FirstOrDefault(candidate => string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase));
        if (map is null)
            return Show(c, "Map unavailable", "mods", "<div class=\"project-site mod-library\">" +
                ProjectHeading("Mods", "Map unavailable", "Choose a map from the current community catalogue.") +
                Link("/mods", "Browse mods", "hub-secondary") + "</div>", status: 404);
        return Show(c, map.Title, "mods", $"""
            <div class="project-site mod-library">
              {Link("/mods", "All mods", "hub-back")}
              {ProjectHeading("Community maps", map.Title, string.Join(" · ", map.Modes))}
              <div class="mod-detail">
                {MapImage(map, lazy: false)}
                <section class="mod-install" aria-labelledby="map-setup">
                  <h2 id="map-setup">Install this map</h2>
                  <p>Requires your Steam copy of Chaos Theory and OpenSCCT Godot or Unreal. The launcher downloads {MapSize(map.DownloadBytes)} of map source and builds it using your local game files.</p>
                  <div class="hub-actions">{Link("openscct://maps/" + U(map.Code), "Open map in launcher", "hub-primary")}</div>
                  <p>With launcher 0.9.4 or newer, the button selects the map for review. You can also open Maps in the launcher and select {H(map.Title)}.</p>
                  {Link("/download", "Get the launcher", "hub-secondary")}
                </section>
              </div>
              <section class="mod-section"><h2>Credits</h2><p>{H(map.Credits)}</p>{Link(map.Source, "View the map source record", "hub-secondary")}</section>
            </div>
            """);
    }
}
