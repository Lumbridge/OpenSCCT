using System;
using OpenScct.Core;
using OpenScct.Server.Online;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class LobbyPresentationTests
{
    [Theory]
    [InlineData(true, false, false, "Start match")]
    [InlineData(true, false, true, "Cancel countdown")]
    [InlineData(false, false, false, "Ready")]
    [InlineData(false, true, false, "Not ready")]
    public void PrimaryActionReflectsRoleAndPhase(bool host, bool ready, bool countdown, string expected) =>
        Assert.Equal(expected, LobbyPresentation.MainAction(host, ready, countdown));

    [Fact]
    public void CountdownDownloadSpectatorAndReadyStatesHaveActionableText()
    {
        string State(bool host = false, bool spectator = false, bool ready = true, bool countdown = false, string? download = null) =>
            LobbyPresentation.Preparation(host, true, spectator, ready, countdown, 3, Array.Empty<string>(), Array.Empty<string>(), download);
        Assert.Contains("Starting in 3", State(countdown: true));
        Assert.Contains("Downloading", State(download: "Downloading map — 64%"));
        Assert.Contains("join a team", State(spectator: true));
        Assert.Contains("select Ready", State(ready: false));
        Assert.Contains("Waiting for the host", State());
        Assert.Contains("can start", State(host: true));
    }

    [Fact]
    public void ProblemsIdentifyPlayersWithoutInventingTeamPopulationRules()
    {
        Assert.Contains("Alex", LobbyPresentation.Preparation(true, true, false, true, false, 0, new[] { "Alex" }, Array.Empty<string>()));
        Assert.Contains("different map", LobbyPresentation.Preparation(true, true, false, true, false, 0, Array.Empty<string>(), new[] { "Alex: different map" }));
        Assert.Contains("can start", LobbyPresentation.Preparation(true, true, false, true, false, 0, Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void ChangeNoticesDescribeAppliedSettingsAndIgnoreUnchangedSetups()
    {
        var before = new LobbySetup { Map = "Old", TimeLimit = 10 };
        Assert.Equal("", LobbyPresentation.ChangeNotice(before, before, "Old"));
        string notice = LobbyPresentation.ChangeNotice(before, before with { Map = "New", TimeLimit = 8 }, "New map");
        Assert.Contains("New map", notice);
        Assert.Contains("1 match rule", notice);
        Assert.DoesNotContain("Old", notice);
    }
}
