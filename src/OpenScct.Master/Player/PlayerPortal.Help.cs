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
            PageHeader("OPENSCCT", "Help & feedback", "Installation, gameplay and editor guides, bug reports and contributions.") +
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
              <details><summary>Do I need Discord or a GitHub account?</summary><p>You can browse the site, play offline and join servers that welcome guests without an account. Discord sign-in reserves your player name and lets you use account features. The website and game have separate sign-ins. Bug reports here use Discord; suggestions and code contributions use GitHub.</p></details>
              <details><summary>Why are my stats or leaderboard position missing?</summary><p>Progression only counts qualifying rounds accepted from trusted servers. Practice and other uncounted rounds do not add career stats. Public leaderboards exclude private, unnamed and actively banned profiles. Win-rate rankings also need at least ten counted rounds. <a href="/wiki/progression-and-stats">Read how stats work →</a></p></details>
              <details><summary>A map is missing or a download stopped</summary><p>Use the launcher's Maps screen to manage additional maps. The game can offer a missing-map download when you join a lobby. Completed launcher downloads are kept when setup is interrupted; reopen the launcher and use the next install or update action to continue.</p></details>
              <details><summary>How do I install or update the map editor?</summary><p>On Windows, use the launcher's blue Editor tab. Choose Set up Editor to install it, or update from that tab or from the editor's Help menu. <a href="/wiki/editor-getting-started">Read the editor setup guide →</a></p></details>
              <details><summary>How do I switch graphics drivers on Windows?</summary><p>In OpenSCCT 0.8.99 or later, use Options → Performance → Graphics driver. Choose Vulkan or Direct3D 12, apply the change and restart the game. <a href="/wiki/controls-and-hud#graphics">Read the graphics settings guide →</a></p></details>
              <details><summary>How do I report a problem clearly?</summary><p>Try the current release, then include the exact steps, what you expected and what happened. Opening Help from the game or launcher can prefill useful version and session details. Review the report before sending it.</p></details>
            </section>
            """ +
            "<section class=\"section\" aria-labelledby=\"help-feedback\"><h2 id=\"help-feedback\">Bug reports and suggestions</h2>" +
            "<p>Search existing reports first. You can report a bug here using your usual Discord sign-in. " +
            "You will review the report before it is published on GitHub; you do not need a GitHub account.</p>" +
            Link(ProjectLinks.Url(ProjectDestination.Issues), "Search existing issues ↗", "text-link") +
            "<div class=\"grid-two help-actions\"><article class=\"panel\"><h3>Report a bug</h3>" +
            "<p>Include what happened, what you expected and steps someone else can repeat. For game problems, add your game version, " +
            "operating system, map, mode, side and whether you were hosting or joining.</p>" +
            Link(ReportReturnPath, "Report a bug →", "button") +
            "</article><article class=\"panel\"><h3>Suggest a feature</h3>" +
            "<p>Describe the problem and the change you would like. Suggestions open on GitHub and currently need a GitHub account.</p>" +
            Link(ProjectLinks.Url(ProjectDestination.FeatureRequest, context), "Suggest a feature ↗", "button") +
            "</article></div><p class=\"help-privacy\">Reports are public. Remove passwords, tokens and personal information from anything you share. " +
            "Do not upload original game files, converted asset packs or decompiled output.</p></section>" +
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
