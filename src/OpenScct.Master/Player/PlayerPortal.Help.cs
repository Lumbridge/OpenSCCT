using Microsoft.AspNetCore.Http;
using OpenScct.Project;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private static ReportContext WebsiteReportContext() => new()
    {
        Source = ReportSource.Website,
        Version = MasterBuildInfo.Version,
        Build = MasterBuildInfo.BuildNumber,
        Commit = MasterBuildInfo.Commit,
    };

    private IResult Help(HttpContext c)
    {
        ReportContext context = WebsiteReportContext();
        return Show(c, "Help & feedback", "help",
            PageHeader("OPENSCCT", "Help & feedback", "Installation, gameplay and editor guides, feature suggestions and contributions.") +
            "<div class=\"help-page\"><section class=\"panel help-start\" aria-labelledby=\"help-guides\"><h2 id=\"help-guides\">Guides</h2>" +
            "<p>The wiki covers setup, gameplay, hosting and map making. Pick a topic below or search all the guides. " +
            "You can read everything without signing in.</p><div class=\"button-row\">" +
            Link("/wiki", "Browse the wiki →", "button") +
            Link("/download", "Download and setup →", "text-link") +
            Link("/changelog", "Recent updates →", "text-link") + "</div></section>" +
            "<section class=\"section\" aria-labelledby=\"help-topics\"><h2 id=\"help-topics\">Help topics</h2><div class=\"grid-three help-topics\">" +
            Feature("01", "Install or update", "Installation and updates for Windows and Linux. Steam Deck support is experimental.", "/wiki/getting-started", "Installation guide →") +
            Feature("02", "Join or host a game", "Server discovery, guest access, passwords and counted rounds.", "/wiki/hosting", "Online play guide →") +
            Feature("03", "Controls and display", "Role-specific bindings, crosshair settings, controllers and the HUD editor.", "/wiki/controls-and-hud", "Controls and HUD guide →") +
            Feature("04", "Gameplay", "Spy and mercenary roles, game modes, objectives and practice sessions.", "/wiki/modes-and-objectives", "Gameplay guides →") +
            Feature("05", "Stats and account", "Counted rounds, career statistics and profile privacy.", "/wiki/progression-and-stats", "Stats and progression →") +
            Feature("06", "Map editing", "Editor setup, objectives, lighting and playtesting.", "/wiki/editor-getting-started", "Map editing guide →") + "</div></section>" +
            """
            <section class="section help-faq" aria-labelledby="help-common">
              <div class="section-heading"><h2 id="help-common">Quick answers</h2><a class="text-link" href="/wiki/troubleshooting">Troubleshooting guide →</a></div>
              <details><summary>The website's Join button does not open the game</summary><p>Run the Windows launcher once to register join links. On native Linux, run <code>install.sh</code> from the launcher archive; Flatpak also registers the handler. Then try Join again and allow your browser to open OpenSCCT. You can also join from the in-game server browser. An active match waits until you return to the main menu.</p></details>
              <details><summary>Do I need Discord or a GitHub account?</summary><p>You can browse the site, play offline and join servers that welcome guests without an account. Discord sign-in reserves your player name and lets you use account features. The website and game have separate sign-ins. Feature suggestions and code contributions use GitHub.</p></details>
              <details><summary>Why are my stats or leaderboard position missing?</summary><p>Progression only counts qualifying rounds accepted from trusted servers. Practice and other uncounted rounds do not add career stats. Public leaderboards exclude private, unnamed and actively banned profiles. Win-rate rankings also need at least ten counted rounds. <a href="/wiki/progression-and-stats">Read how stats work →</a></p></details>
              <details><summary>A map is missing or a download stopped</summary><p>For a Godot map supplied with Steam, open <strong>OpenSCCT Godot</strong> under Modernisations and choose <strong>Build from Steam</strong>. Use <strong>Maps</strong> for Enhanced variants and community maps. Reopen the launcher to resume an interrupted setup.</p></details>
              <details><summary>How do I install or update the map editor?</summary><p>On Windows, use <strong>OpenSCCT Editor</strong> under Tools. Choose Set up Editor to install it, or update from that tab or from the editor's Help menu. <a href="/wiki/editor-getting-started">Read the editor setup guide →</a></p></details>
              <details><summary>Which graphics driver does OpenSCCT use on Windows?</summary><p>Vulkan. The Direct3D 12 option of versions 0.8.99 to 0.8.125 was removed: a profile that used it now starts on Vulkan. <a href="/wiki/controls-and-hud#graphics">Read the graphics settings guide →</a></p></details>
            </section>
            """ +
            "<section class=\"section panel\" aria-labelledby=\"help-feedback\"><h2 id=\"help-feedback\">Suggest a feature</h2>" +
            "<p>Check existing issues for your idea. To suggest a change, sign in to GitHub and describe how it would help.</p>" +
            "<div class=\"button-row\">" +
            Link(ProjectLinks.Url(ProjectDestination.FeatureRequest, context), "Suggest a feature ↗", "button") +
            Link(ProjectLinks.Url(ProjectDestination.Issues), "Search existing issues ↗", "text-link") + "</div></section>" +
            "<section class=\"section panel\" aria-labelledby=\"help-contribute\"><h2 id=\"help-contribute\">Contributing</h2>" +
            "<p>You can help by testing a release, improving a guide or contributing code. Read the contribution guide, check existing work and keep changes focused.</p>" +
            "<div class=\"button-row\">" +
            Link("/wiki/contributing", "Contribution guide →", "button secondary") +
            Link("/wiki/editor-getting-started", "Map making →", "text-link") +
            Link(ProjectLinks.Url(ProjectDestination.PullRequests), "View pull requests ↗", "text-link") +
            Link(ProjectLinks.Url(ProjectDestination.NewPullRequest), "Open a pull request ↗", "text-link") +
            "</div><p class=\"account-side-note\">To open a pull request, push your changes to a branch or fork, then choose it on GitHub's comparison page.</p></section></div>");
    }
}
