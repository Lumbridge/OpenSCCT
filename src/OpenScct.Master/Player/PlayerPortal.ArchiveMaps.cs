using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private sealed record ArchiveMap(string Title, string Code, string Category, string Path, string Image, string Kind)
    {
        public string Url => "https://github.com/OpenSCCT/SCCT-Maps/" +
            (Path.EndsWith(".sdc", StringComparison.OrdinalIgnoreCase) ? "blob/" : "tree/") +
            "main/" + string.Join("/", Path.Split('/').Select(U));

        public string SearchText => $"{Title} {Code} {Category} PC Steam Versus";
    }

    private static readonly ArchiveMap[] ArchiveMaps = LoadArchiveMaps();

    private static ArchiveMap[] LoadArchiveMaps()
    {
        using Stream stream = typeof(PlayerPortal).Assembly.GetManifestResourceStream("archive-maps.json")
            ?? throw new InvalidDataException("Missing original PC map catalogue.");
        return JsonSerializer.Deserialize<ArchiveMap[]>(stream) ?? throw new InvalidDataException("Invalid map catalogue.");
    }

    private static int AppendArchiveMaps(StringBuilder body, Func<string, bool> matches, bool searching)
    {
        var maps = ArchiveMaps.Where(map => matches(map.SearchText)).ToArray();
        if (maps.Length == 0) return 0;
        body.Append("<section class=\"mod-section map-archive\"><h2>Original PC map library</h2><p>")
            .Append(N(maps.Length)).Append(maps.Length == 1 ? " map" : " maps")
            .Append(" for PC Versus and the Reloaded Editor. Each entry links to its package or installation notes. ")
            .Append(Link("https://github.com/OpenSCCT/SCCT-Map-Manager/releases/latest", "Get SCCT Map Manager"))
            .Append("</p>");
        foreach (var group in maps.GroupBy(map => map.Category).OrderBy(group => group.Key switch
        {
            "Community maps" => 1, "Enhanced variants" => 2, "Recovered editor maps" => 3,
            _ when group.Key.StartsWith("Ports", StringComparison.Ordinal) => 4, _ => 5,
        }).ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            body.Append("<details").Append(searching || maps.Length < 20 ? " open" : "").Append("><summary>")
                .Append(H(group.Key)).Append("<small>").Append(N(group.Count())).Append("</small></summary><div class=\"mod-grid\">");
            foreach (var map in group.OrderBy(map => map.Title, StringComparer.OrdinalIgnoreCase))
            {
                body.Append($"""
                    <article class="mod-card"><a href="{H(map.Url)}" tabindex="-1" aria-hidden="true"><img class="mod-shot" loading="lazy" decoding="async" width="480" height="270" src="{H(AssetUrl("/web/assets/" + map.Image))}" alt=""></a>
                      <div class="mod-card-copy"><h4>{Link(map.Url, map.Title)}</h4><small>{H(map.Code)} <span>{H(map.Kind)}</span></small></div></article>
                    """);
            }
            body.Append("</div></details>");
        }
        body.Append("</section>");
        return maps.Length;
    }
}

