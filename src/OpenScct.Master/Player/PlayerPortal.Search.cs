using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenScct.Master.Web;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private IResult ProjectSearch(HttpContext c)
    {
        string query = c.Request.Query["q"].ToString().Trim();
        if (query.Length > 96) query = query[..96].TrimEnd();
        var body = new StringBuilder("<div class=\"project-site search-page\">");
        body.Append(ProjectHeading("OpenSCCT", "Search", "Find a project, a tool or a guide."))
            .Append("<form method=\"get\" action=\"/search\" role=\"search\"><label class=\"sr-only\" for=\"site-query\">Search OpenSCCT</label><input id=\"site-query\" type=\"search\" name=\"q\" maxlength=\"96\" value=\"")
            .Append(H(query)).Append("\"><button class=\"button\" type=\"submit\">Search</button></form>");
        if (query.Length > 0)
        {
            string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var catalog = new List<(string Url, string Name, string Detail)>
            {
                ("/download", "Download the launcher", "Windows, Linux and Steam Deck installation"),
                ("/mods", "Mods", "Optional community maps and Enhanced SCCT Versus, including Reloaded"),
                ("/progress", "Progress reports", "Exact matches, verified retail code and release notes"),
                ("/tools#decomp-tools", "Decompilation tools", "ByteMatch, decomp.me, objdiff, splat, spimdisasm, m2c, Ghidra and PCSX2"),
                ("/tools#maps", "Map editors and tools", "OpenSCCT Editor and SCCT Map Manager"),
                ("/projects/ps2/decomp", "PlayStation 2 matching guide", "PS2 disc, disassembly and MIPS executables"),
                ("/projects/pc/decomp", "PC / Steam matching guide", "Original Windows x86 campaign, co-op and Versus decompilation"),
                ("/community", "Player hub", "Online servers, profiles and matches"),
                ("/changelog", "Release notes", "Godot, launcher and Editor updates"),
                ("/legal", "Legal", "Project notice and ownership"),
            };
            catalog.AddRange(Editions.Select(e => ("/projects/" + e.Id, e.Name, e.Group + " · " + e.Platform + " · " + e.Summary)));
            var results = catalog.Where(p => words.All(word => (p.Name + " " + p.Detail).Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Concat(WikiContent.Search(query, "").Select(a => (Url: "/wiki/" + a.Slug, Name: a.Title, Detail: a.Summary))).ToList();
            if (results.Count == 0)
                body.Append("<p class=\"search-count\">No results for “").Append(H(query)).Append("”. Try a shorter search.</p>");
            else
            {
                body.Append("<p class=\"search-count\">").Append(N(results.Count)).Append(results.Count == 1 ? " result" : " results").Append(" for “").Append(H(query)).Append("”</p><div class=\"search-results\">");
                foreach (var result in results) body.Append(Resource(result.Url, result.Name, result.Detail));
                body.Append("</div>");
            }
        }
        return Show(c, "Search", "search", body.Append("</div>").ToString());
    }
}
