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

    [Fact]
    public void CustomRosterPreviewDistinguishesHumanBotAndOpenSlots()
    {
        var setup = new LobbySetup { MaxPlayers = 5, Bots = BotFill.Custom, SpyBots = 0, MercBots = 3 };
        string preview = LobbyPresentation.PlayerSlots(setup, 1, 0);
        Assert.Contains("1 human + 3 bots", preview);
        Assert.Contains("1 open / 5 slots", preview);
        Assert.Contains("Spies: 1 human + 0 bots", preview);
        Assert.Contains("Mercenaries: 0 human + 3 bots", preview);
        Assert.DoesNotContain("requested bots fit", preview);
    }

    [Fact]
    public void RosterPreviewExplainsWhenHumansLeaveTooLittleRoomForRequestedBots()
    {
        var setup = new LobbySetup { MaxPlayers = 4, Bots = BotFill.Custom, SpyBots = 0, MercBots = 3 };
        string preview = LobbyPresentation.PlayerSlots(setup, 1, 1);
        Assert.Contains("2 humans + 2 bots", preview);
        Assert.Contains("0 open / 4 slots", preview);
        Assert.Contains("Only 2 of 3 requested bots fit", preview);
        Assert.Contains("Increase player slots", preview);

        preview = LobbyPresentation.PlayerSlots(setup with { MaxPlayers = 2 }, 2, 1);
        Assert.Contains("3 humans + 0 bots", preview);
        Assert.Contains("0 open / 3 slots", preview);
        Assert.Contains("Connected players need at least 3 slots", preview);
    }

    [Fact]
    public void MercDeathmatchPreviewMovesHumansToMercsAndIgnoresStoredSpyBots()
    {
        var setup = new LobbySetup { Mode = EGameMode.GM_MercDeathMatch, MaxPlayers = 5,
            Bots = BotFill.Custom, SpyBots = 4, MercBots = 2 };
        string preview = LobbyPresentation.PlayerSlots(setup, 1, 1);
        Assert.Contains("2 humans + 2 bots", preview);
        Assert.Contains("1 open / 5 slots", preview);
        Assert.Contains("Mercenaries: 2 human + 2 bots", preview);
        Assert.DoesNotContain("Spies:", preview);
        Assert.DoesNotContain("requested bots fit", preview);
    }
}
