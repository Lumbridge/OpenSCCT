using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using OpenScct.Project;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class ProjectLinksTests
{
    [Theory]
    [InlineData(ProjectDestination.FeatureRequest, "/issues/new?template=feature_request.yml")]
    [InlineData(ProjectDestination.Issues, "/issues")]
    [InlineData(ProjectDestination.Documentation, "/blob/main/docs/README.md")]
    [InlineData(ProjectDestination.Contributing, "/blob/main/CONTRIBUTING.md")]
    [InlineData(ProjectDestination.Development, "/blob/main/docs/DEVELOPMENT.md")]
    [InlineData(ProjectDestination.PullRequests, "/pulls")]
    [InlineData(ProjectDestination.NewPullRequest, "/compare?expand=1")]
    internal void FixedGitHubDestinations(ProjectDestination destination, string path) =>
        Assert.Equal(ProjectLinks.Repository + path, ProjectLinks.Url(destination));

    [Fact]
    public void HelpUsesOfficialSiteWithoutContext() =>
        Assert.Equal("https://master.lumbridge.co.uk/help", ProjectLinks.Url(ProjectDestination.Help, Game()));

    [Fact]
    public void BugReportsUseTheWebsiteWithOrWithoutContext()
    {
        const string destination = "https://master.lumbridge.co.uk/help/report-bug";
        Assert.Equal(destination, ProjectLinks.Url(ProjectDestination.BugReport));
        Assert.Equal(destination, new Uri(ProjectLinks.Url(ProjectDestination.BugReport, Game())).GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public void FormsFillOnlyDeclaredFieldsAndEscapeCustomMapNames()
    {
        ReportContext context = Game() with { Map = "a & + # ? / 雨\nSecond line", Mode = "Story & Disk" };
        Dictionary<string, string> query = Query(ProjectLinks.Url(ProjectDestination.BugReport, context));
        Assert.Equal(["area", "environment", "platform", "version"], query.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("game", query["area"]);
        Assert.Equal("OpenSCCT game 1.2.3", query["version"]);
        Assert.Equal("Windows", query["platform"]);
        Assert.Equal(ProjectLinks.EnvironmentDetails(context), query["environment"]);
        Assert.Contains("Map: a & + # ? / 雨 Second line", query["environment"]);
        Assert.Contains("Session: Online host", query["environment"]);
        Assert.DoesNotContain("\nSecond line", query["environment"]);
        var suggestion = Query(ProjectLinks.Url(ProjectDestination.FeatureRequest, context));
        Assert.Equal(["environment", "template"], suggestion.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("feature_request.yml", suggestion["template"]);
    }

    [Fact]
    public void LauncherVersionsAndWebsiteBuildAreNotLabelledAsGameVersions()
    {
        var launcher = new ReportContext { Source = ReportSource.Launcher, Version = "9", LauncherVersion = "2",
            InstalledGameVersion = "1", Platform = "Windows", Map = "must not appear", Session = ReportSession.OnlineClient };
        string details = ProjectLinks.EnvironmentDetails(launcher);
        Assert.Contains("Launcher version: 2", details);
        Assert.Contains("Installed game version: 1", details);
        Assert.DoesNotContain("must not appear", details);
        Assert.DoesNotContain("Session:", details);
        Assert.Contains("launcher 2", Query(ProjectLinks.Url(ProjectDestination.BugReport, launcher))["version"]);
        Assert.Equal("launcher", Query(ProjectLinks.Url(ProjectDestination.BugReport, launcher))["area"]);
        Assert.Equal(details, Query(ProjectLinks.Url(ProjectDestination.BugReport, launcher))["environment"]);
        var website = new ReportContext { Source = ReportSource.Website, Version = "3", Build = "20261103.1200", Map = "omitted" };
        details = ProjectLinks.EnvironmentDetails(website);
        Assert.Contains("Website version: 3", details);
        Assert.Contains("Website build: 20261103.1200", details);
        Assert.DoesNotContain("Game version", details);
        Assert.DoesNotContain("Platform:", details);
        Assert.DoesNotContain("omitted", details);
        Assert.Equal("website", Query(ProjectLinks.Url(ProjectDestination.BugReport, website))["area"]);
    }

    [Fact]
    public void MissingDetailsAndOversizedUnicodeStillOpenValidForms()
    {
        string empty = ProjectLinks.Url(ProjectDestination.BugReport, new ReportContext());
        Assert.Contains("version unavailable", Query(empty)["version"]);
        string longValue = string.Concat(Enumerable.Repeat("🛰", 1000));
        ReportContext context = Game() with { Map = longValue, Mode = longValue, Side = longValue,
            Version = longValue, Platform = longValue, Build = longValue, Commit = longValue };
        string url = ProjectLinks.Url(ProjectDestination.BugReport, context);
        Assert.True(url.Length <= 4096);
        var query = Query(url);
        Assert.Equal("game", query["area"]);
        Assert.InRange(query["version"].Length, 1, 120);
        Assert.InRange(query["platform"].Length, 1, 80);
        Assert.InRange(query["environment"].Length, 1, 2000);
        foreach (string field in new[] { "Source", "Game version", "Build", "Commit", "Platform", "Map", "Mode", "Side", "Session" })
            Assert.Contains(field + ": ", query["environment"]);
        Assert.Contains("Session: Online host", query["environment"]);
        Assert.DoesNotContain("\ufffd", Uri.UnescapeDataString(url));
        string details = ProjectLinks.EnvironmentDetails(context);
        Assert.DoesNotContain(longValue, details);
        Assert.Contains("Map:", details);
    }

    [Theory]
    [InlineData(ReportSession.Menu, "Menus")]
    [InlineData(ReportSession.Offline, "Offline")]
    [InlineData(ReportSession.OnlineHost, "Online host")]
    [InlineData(ReportSession.OnlineClient, "Online client")]
    [InlineData(ReportSession.HubHost, "Hub host")]
    [InlineData(ReportSession.HubClient, "Hub client")]
    [InlineData(ReportSession.Replay, "Replay")]
    internal void WebsitePrefillRetainsSessionContext(ReportSession session, string label)
    {
        var query = Query(ProjectLinks.Url(ProjectDestination.BugReport, Game() with { Session = session }));
        Assert.Contains("Session: " + label, query["environment"]);
        Assert.Contains("Map: SteeE", query["environment"]);
        Assert.Contains("Mode: Story", query["environment"]);
        Assert.Contains("Side: Spy", query["environment"]);
    }

    [Fact]
    public void ContextHasOnlyExplicitProductFields()
    {
        string[] names = typeof(ReportContext).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Build", "Commit", "InstalledGameVersion", "LauncherVersion", "Map", "Mode", "Platform",
            "Session", "Side", "Source", "Version" }, names);
    }

    [Fact]
    public void BrowserFailureRetainsCopyableTargetAndCanRecover()
    {
        bool fail = true;
        string copied = "";
        var actions = new ProjectLinkActions(_ => fail ? throw new Win32Exception("private OS error") : true, s => copied = s);
        Assert.False(actions.Open(ProjectDestination.BugReport, Game()));
        Assert.Contains("Copy the link", actions.Message);
        Assert.DoesNotContain("private", actions.Message);
        Assert.True(actions.CopyLink());
        Assert.Equal(actions.TargetUrl, copied);
        fail = false;
        Assert.True(actions.Open(ProjectDestination.Documentation));
        Assert.Empty(actions.Message);
        Assert.True(actions.CopyDetails(Game()));
        Assert.Equal(ProjectLinks.EnvironmentDetails(Game()), copied);
    }

    [Fact]
    public void FailedDispatchOrClipboardDoesNotLoseUrlOrDetails()
    {
        var actions = new ProjectLinkActions(_ => false, _ => throw new ExternalException("Clipboard unavailable"));
        Assert.False(actions.CopyLink());
        Assert.Equal("Choose a link first.", actions.Message);
        Assert.False(actions.Open(ProjectDestination.FeatureRequest, Game()));
        string target = actions.TargetUrl;
        Assert.False(actions.CopyLink());
        Assert.Contains("manually", actions.Message);
        Assert.False(actions.CopyDetails(Game()));
        Assert.Equal(target, actions.TargetUrl);
    }

    private static ReportContext Game() => new() { Source = ReportSource.Game, Version = "1.2.3", Build = "20261103.1200",
        Commit = "abc1234", Platform = "Windows", Map = "SteeE", Mode = "Story", Side = "Spy", Session = ReportSession.OnlineHost };

    private static Dictionary<string, string> Query(string url) => new Uri(url).Query.TrimStart('?').Split('&')
        .Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
}
