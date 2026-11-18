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
    public void FeatureSuggestionsFillOnlyDeclaredFieldsAndEscapeCustomMapNames()
    {
        ReportContext context = Game() with { Map = "a & + # ? / 雨\nSecond line", Mode = "Story & Disk", PlayerLocation = "private position" };
        var query = Query(ProjectLinks.Url(ProjectDestination.FeatureRequest, context));
        Assert.Equal(["environment", "template"], query.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("feature_request.yml", query["template"]);
        Assert.Contains("private position", ProjectLinks.EnvironmentDetails(context));
        Assert.Equal(ProjectLinks.EnvironmentDetails(context with { PlayerLocation = null }), query["environment"]);
        Assert.Contains("Map: a & + # ? / 雨 Second line", query["environment"]);
        Assert.Contains("Session: Online host", query["environment"]);
        Assert.DoesNotContain("\nSecond line", query["environment"]);
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
        Assert.Equal(details, Query(ProjectLinks.Url(ProjectDestination.FeatureRequest, launcher))["environment"]);
        var website = new ReportContext { Source = ReportSource.Website, Version = "3", Build = "20261103.1200", Map = "omitted" };
        details = ProjectLinks.EnvironmentDetails(website);
        Assert.Contains("Website version: 3", details);
        Assert.Contains("Website build: 20261103.1200", details);
        Assert.DoesNotContain("Game version", details);
        Assert.DoesNotContain("Platform:", details);
        Assert.DoesNotContain("omitted", details);
        Assert.Equal(details, Query(ProjectLinks.Url(ProjectDestination.FeatureRequest, website))["environment"]);
    }

    [Fact]
    public void OversizedFeatureContextFallsBackToThePlainForm()
    {
        string longValue = string.Concat(Enumerable.Repeat("🛰", 1000));
        ReportContext context = Game() with { Map = longValue, Mode = longValue, Side = longValue,
            Version = longValue, Platform = longValue, Build = longValue, Commit = longValue };
        Assert.Equal(ProjectLinks.Url(ProjectDestination.FeatureRequest), ProjectLinks.Url(ProjectDestination.FeatureRequest, context));
        string details = ProjectLinks.EnvironmentDetails(context);
        Assert.DoesNotContain(longValue, details);
        Assert.DoesNotContain("\ufffd", details);
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
    internal void FeatureSuggestionRetainsSessionContext(ReportSession session, string label)
    {
        var query = Query(ProjectLinks.Url(ProjectDestination.FeatureRequest, Game() with { Session = session }));
        Assert.Contains("Session: " + label, query["environment"]);
        Assert.Contains("Map: SteeE", query["environment"]);
        Assert.Contains("Mode: Story", query["environment"]);
        Assert.Contains("Side: Spy", query["environment"]);
    }

    [Fact]
    public void ContextHasOnlyExplicitProductFields()
    {
        string[] names = typeof(ReportContext).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Build", "Commit", "InstalledGameVersion", "LauncherVersion", "Map", "Mode", "Platform", "PlayerLocation",
            "Session", "Side", "Source", "Version" }, names);
    }

    [Fact]
    public void BrowserFailureRetainsCopyableTargetAndCanRecover()
    {
        bool fail = true;
        string copied = "";
        var actions = new ProjectLinkActions(_ => fail ? throw new Win32Exception("private OS error") : true, s => copied = s);
        Assert.False(actions.Open(ProjectDestination.FeatureRequest, Game()));
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
