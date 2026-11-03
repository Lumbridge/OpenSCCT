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
        return Show(c, "Help & contribute", "help",
            PageHeader("OPENSCCT", "Help & contribute", "Find a guide, report a problem or help improve OpenSCCT.") +
            "<div class=\"help-page\"><section class=\"panel help-start\" aria-labelledby=\"help-guides\"><h2 id=\"help-guides\">Start with a guide</h2>" +
            "<p>For installation, updates, hosting and common problems, start with the documentation. " +
            "You can read it without signing in.</p><div class=\"button-row\">" +
            Link(ProjectLinks.Url(ProjectDestination.Documentation), "Read the guides ↗", "button secondary") +
            Link("/download", "Download and setup →", "text-link") +
            Link("/changelog", "Recent updates →", "text-link") + "</div></section>" +
            "<section class=\"section\" aria-labelledby=\"help-feedback\"><h2 id=\"help-feedback\">Tell us what needs attention</h2>" +
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
            "<section class=\"section panel\" aria-labelledby=\"help-contribute\"><h2 id=\"help-contribute\">Help build OpenSCCT</h2>" +
            "<p>You can help by testing a release, improving a guide or contributing code. Read the contribution guide, check existing work and keep changes focused.</p>" +
            "<div class=\"button-row\">" +
            Link(ProjectLinks.Url(ProjectDestination.Contributing), "Contribution guide ↗", "button secondary") +
            Link(ProjectLinks.Url(ProjectDestination.Development), "Build from source ↗", "text-link") +
            Link(ProjectLinks.Url(ProjectDestination.PullRequests), "View pull requests ↗", "text-link") +
            Link(ProjectLinks.Url(ProjectDestination.NewPullRequest), "Open a pull request ↗", "text-link") +
            "</div><p class=\"account-side-note\">To open a pull request, push your changes to a branch or fork, then choose it on GitHub's comparison page.</p></section></div>");
    }
}
