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
    [InlineData(ProjectDestination.BugReport, "/issues/new?template=bug_report.yml")]
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
    public void FormsFillOnlyDeclaredFieldsAndEscapeCustomMapNames()
    {
        ReportContext context = Game() with { Map = "a & + # ? / 雨\nSecond line", Mode = "Story & Disk" };
        Dictionary<string, string> query = Query(ProjectLinks.Url(ProjectDestination.BugReport, context));
        Assert.Equal(["environment", "template", "version"], query.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("bug_report.yml", query["template"]);
        Assert.Equal("OpenSCCT game 1.2.3 / Windows", query["version"]);
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
        var website = new ReportContext { Source = ReportSource.Website, Version = "3", Build = "20261103.1200", Map = "omitted" };
        details = ProjectLinks.EnvironmentDetails(website);
        Assert.Contains("Website version: 3", details);
        Assert.Contains("Website build: 20261103.1200", details);
        Assert.DoesNotContain("Game version", details);
        Assert.DoesNotContain("Platform:", details);
        Assert.DoesNotContain("omitted", details);
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
        Assert.Equal("bug_report.yml", Query(url)["template"]);
        Assert.DoesNotContain("environment", Query(url).Keys);
        Assert.DoesNotContain("\ufffd", Uri.UnescapeDataString(url));
        string details = ProjectLinks.EnvironmentDetails(context);
        Assert.DoesNotContain(longValue, details);
        Assert.Contains("Map:", details);
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
