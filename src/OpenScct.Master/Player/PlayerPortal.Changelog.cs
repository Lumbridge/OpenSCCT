using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using OpenScct.Master.Web;
using OpenScct.Net.Discovery;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private async Task<IResult> Changelog(HttpContext c)
    {
        ChangelogResponse feed = await _releases.GetAsync(c.RequestAborted).ConfigureAwait(false);
        var body = new StringBuilder(PageHeader("OPENSCCT", "Updates", "Features, changes and fixes in each published version of OpenSCCT.",
            Link("/download", "Download the latest version →", "button secondary")));
        if (feed.Unavailable)
            body.Append("<p class=\"notice\" role=\"status\">").Append(feed.Entries.Count > 0
                ? "We could not check for new updates. The last available update notes are shown below. "
                : "We could not load the update notes. ")
                .Append(Link(ReleaseChangelog.ReleasesUrl, "Read updates on GitHub ↗")).Append("</p>");
        else if (feed.Entries.Count == 0)
            body.Append(Empty("No updates to show", "Notes will appear here when an OpenSCCT update is published."));

        body.Append("<div class=\"changelog-layout\"><aside class=\"changelog-search panel\" aria-label=\"Search updates\" hidden>" +
            "<label for=\"changelog-query\">Search updates</label><input id=\"changelog-query\" type=\"search\" maxlength=\"120\" placeholder=\"Feature, fix, or version…\" aria-describedby=\"changelog-search-help\" aria-controls=\"changelog-results\">" +
            "<p id=\"changelog-search-help\" class=\"muted\">Search the update notes on this page. Select a result to read it.</p>" +
            "<p id=\"changelog-search-status\" role=\"status\"></p><ol id=\"changelog-results\"></ol></aside><div class=\"release-list\">");
        for (int i = 0; i < feed.Entries.Count; i++)
        {
            ChangelogEntry release = feed.Entries[i];
            body.Append("<article class=\"release-card panel\" id=\"").Append(H(release.Tag)).Append("\"><div class=\"release-heading\"><div><div class=\"release-meta\">")
                .Append(Badge(release.Tag, "ok"));
            if (i == 0) body.Append(Badge(feed.Unavailable ? "Last available update" : "Latest version"));
            body.Append("<time datetime=\"").Append(release.Published.ToString("O", CultureInfo.InvariantCulture)).Append("\">")
                .Append(H(release.Published.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)))
                .Append("</time></div><h2>").Append(H(release.Name)).Append("</h2></div>")
                .Append(Link(release.Url, "View on GitHub ↗", "text-link")).Append("</div>");
            if (i > 0) body.Append("<details class=\"release-details\"><summary>Read update notes<span class=\"sr-only\"> for ").Append(H(release.Tag)).Append("</span></summary>");
            body.Append("<div class=\"release-notes\">").Append(string.IsNullOrWhiteSpace(release.Notes)
                ? "<p>No update notes were provided for this version.</p>" : ReleaseNotesHtml.Render(release.Notes)).Append("</div>");
            if (i > 0) body.Append("</details>");
            body.Append("</article>");
        }
        body.Append("</div></div><div class=\"section button-row\">").Append(Link(ReleaseChangelog.ReleasesUrl, "All versions on GitHub ↗", "button secondary")).Append("</div>");
        body.Append("<p class=\"help-context\">Problem after an update? ").Append(Link("/help", "Get help or report a bug →", "text-link")).Append("</p>");
        return Show(c, "Updates", "changelog", body.ToString());
    }
}
