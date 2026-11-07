using System.Globalization;
using System.Linq;
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
        Task<ChangelogResponse> gameTask = _releases.GetAsync(c.RequestAborted);
        Task<ChangelogResponse> editorTask = _editorReleases.GetAsync(c.RequestAborted);
        await Task.WhenAll(gameTask, editorTask).ConfigureAwait(false);
        ChangelogResponse game = await gameTask.ConfigureAwait(false);
        ChangelogResponse editor = await editorTask.ConfigureAwait(false);
        var entries = game.Entries.Select((release, index) => (Release: release, Editor: false, Latest: index == 0, game.Unavailable))
            .Concat(editor.Entries.Select((release, index) => (Release: release, Editor: true, Latest: index == 0, editor.Unavailable)))
            .OrderByDescending(entry => entry.Release.Published).ToArray();
        var body = new StringBuilder(PageHeader("OPENSCCT", "Updates", "Features, changes and fixes in each published version of OpenSCCT and OpenSCCT Editor.",
            Link("/download", "Download the latest version →", "button secondary")));
        foreach (var product in new[] { (Feed: game, Name: "game", Url: ReleaseChangelog.ReleasesUrl), (Feed: editor, Name: "Editor", Url: ReleaseChangelog.EditorReleasesUrl) })
            if (product.Feed.Unavailable)
                body.Append("<p class=\"notice\" role=\"status\">We could not check for new ").Append(product.Name).Append(" updates. ")
                    .Append(product.Feed.Entries.Count > 0 ? "The last available notes are shown below. " : "")
                    .Append(Link(product.Url, "Read " + product.Name + " updates on GitHub ↗")).Append("</p>");
        if (entries.Length == 0 && !game.Unavailable && !editor.Unavailable)
            body.Append(Empty("No updates to show", "Notes will appear here when a game or Editor update is published."));

        body.Append("<nav class=\"button-row changelog-products\" aria-label=\"Latest updates\">");
        if (game.Entries.Count > 0) body.Append(Link("#game-latest", "Latest game update ↓", "button secondary"));
        if (editor.Entries.Count > 0) body.Append(Link("#editor-latest", "Latest Editor update ↓", "button secondary editor-theme"));
        body.Append("</nav>");

        body.Append("<div class=\"changelog-layout\"><aside class=\"changelog-search panel\" aria-label=\"Search updates\" hidden>" +
            "<label for=\"changelog-query\">Search updates</label><input id=\"changelog-query\" type=\"search\" maxlength=\"120\" placeholder=\"Feature, fix, or version…\" aria-describedby=\"changelog-search-help\" aria-controls=\"changelog-results\">" +
            "<p id=\"changelog-search-help\" class=\"muted\">Search the update notes on this page. Select a result to read it.</p>" +
            "<p id=\"changelog-search-status\" role=\"status\"></p><ol id=\"changelog-results\"></ol></aside><div class=\"release-list\">");
        foreach (var entry in entries)
        {
            ChangelogEntry release = entry.Release;
            body.Append("<article class=\"release-card panel").Append(entry.Editor ? " editor-theme" : "").Append("\" id=\"")
                .Append(H(entry.Editor ? "editor-" + release.Tag : release.Tag)).Append("\"><div class=\"release-heading\"");
            if (entry.Latest) body.Append(" id=\"").Append(entry.Editor ? "editor-latest" : "game-latest").Append("\" tabindex=\"-1\"");
            body.Append("><div><p class=\"eyebrow release-product\">").Append(entry.Editor ? "Editor changelog" : "Game changelog")
                .Append("</p><div class=\"release-meta\">")
                .Append(Badge(release.Tag, "ok"));
            if (entry.Latest) body.Append(Badge(entry.Unavailable ? "Last available update" : "Latest version"));
            body.Append("<time datetime=\"").Append(release.Published.ToString("O", CultureInfo.InvariantCulture)).Append("\">")
                .Append(H(release.Published.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)))
                .Append("</time></div><h2>").Append(H(release.Name)).Append("</h2></div>")
                .Append(Link(release.Url, "View on GitHub ↗", "text-link")).Append("</div>");
            if (!entry.Latest) body.Append("<details class=\"release-details\"><summary>Read update notes<span class=\"sr-only\"> for ").Append(H(release.Name)).Append("</span></summary>");
            body.Append("<div class=\"release-notes\">").Append(string.IsNullOrWhiteSpace(release.Notes)
                ? "<p>No update notes were provided for this version.</p>" : ReleaseNotesHtml.Render(release.Notes)).Append("</div>");
            if (!entry.Latest) body.Append("</details>");
            body.Append("</article>");
        }
        body.Append("</div></div><div class=\"section button-row\">")
            .Append(Link(ReleaseChangelog.ReleasesUrl, "All game versions on GitHub ↗", "button secondary"))
            .Append(Link(ReleaseChangelog.EditorReleasesUrl, "All Editor versions on GitHub ↗", "button secondary editor-theme")).Append("</div>");
        body.Append("<p class=\"help-context\">Problem after an update? ").Append(Link("/help", "Get help or report a bug →", "text-link")).Append("</p>");
        return Show(c, "Updates", "changelog", body.ToString());
    }
}
