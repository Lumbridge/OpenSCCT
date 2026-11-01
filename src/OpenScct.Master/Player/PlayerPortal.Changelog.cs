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
        var body = new StringBuilder(PageHeader("WHAT'S NEW", "Changelog", "New features, improvements, and fixes in OpenSCCT. Release notes appear here automatically when an update is published.",
            Link("/download", "Get the latest version →", "button secondary")));
        if (feed.Unavailable)
            body.Append("<p class=\"notice\" role=\"status\">").Append(feed.Entries.Count > 0
                ? "Updates are temporarily unavailable. Showing the last release notes we received. "
                : "Release notes are temporarily unavailable. ")
                .Append(Link(ReleaseChangelog.ReleasesUrl, "Read the changelog on GitHub ↗")).Append("</p>");
        else if (feed.Entries.Count == 0)
            body.Append(Empty("No releases yet", "Published OpenSCCT updates will appear here."));

        body.Append("<div class=\"release-list\">");
        for (int i = 0; i < feed.Entries.Count; i++)
        {
            ChangelogEntry release = feed.Entries[i];
            body.Append("<article class=\"release-card panel\" id=\"").Append(H(release.Tag)).Append("\"><div class=\"release-heading\"><div><div class=\"release-meta\">")
                .Append(Badge(release.Tag, "ok"));
            if (i == 0) body.Append(Badge(feed.Unavailable ? "Last received" : "Latest release"));
            body.Append("<time datetime=\"").Append(release.Published.ToString("O", CultureInfo.InvariantCulture)).Append("\">")
                .Append(H(release.Published.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)))
                .Append("</time></div><h2>").Append(H(release.Name)).Append("</h2></div>")
                .Append(Link(release.Url, "View on GitHub ↗", "text-link")).Append("</div>");
            if (i > 0) body.Append("<details class=\"release-details\"><summary>Read release notes<span class=\"sr-only\"> for ").Append(H(release.Tag)).Append("</span></summary>");
            body.Append("<div class=\"release-notes\">").Append(string.IsNullOrWhiteSpace(release.Notes)
                ? "<p>No release notes were provided for this version.</p>" : ReleaseNotesHtml.Render(release.Notes)).Append("</div>");
            if (i > 0) body.Append("</details>");
            body.Append("</article>");
        }
        body.Append("</div><div class=\"section button-row\">").Append(Link(ReleaseChangelog.ReleasesUrl, "Browse all releases on GitHub ↗", "button secondary")).Append("</div>");
        return Show(c, "Changelog", "changelog", body.ToString());
    }
}
