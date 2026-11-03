using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The shared session form's bot modes, exact team counts and player capacity.</summary>
public sealed class MenuCreateSessionBotsTests
{
    private static CreateSessionModel Page()
    {
        var story = new USMapSettings();
        story.Modes.Add(new FGameModeInfo { GameModeId = EGameMode.GM_Multi, TimeLimit = 10, SpiesLives = 4, SentriesLives = 3, RespawnTime = 10 });
        var maps = new List<(string, string)> { ("SteeE", "ENHANCED STEEL SQUAT"), ("Other", "Other") };
        return new CreateSessionModel(maps, _ => story, LobbySetup.ForMap("SteeE", EGameMode.GM_Multi, story, 4));
    }

    [Fact]
    public void SidearmsDefaultOffAndPersistAcrossMapChanges()
    {
        var page = Page();
        Assert.Contains(SessionRow.Sidearms, CreateSessionModel.Rows);
        Assert.True(CreateSessionModel.IsCheck(SessionRow.Sidearms));
        Assert.False(page.Checked(SessionRow.Sidearms));
        var before = page.Setup;
        page.SetChecked(SessionRow.Sidearms, true);
        Assert.Contains(SessionRow.Sidearms, LobbyRoomModel.Differences(before, page.Setup));
        page.SelectMap("Other");
        Assert.True(page.Setup.Sidearms);
        page.SetChecked(SessionRow.Sidearms, false);
        Assert.False(page.Setup.Sidearms);
    }

    [Fact]
    public void TheBotsRowStepsThroughItsChoicesAndShowsItsOwnRows()
    {
        CreateSessionModel page = Page();
        Assert.Equal(("Bots", "Off"), (page.Caption(SessionRow.Bots), page.Text(SessionRow.Bots)));
        Assert.False(page.Visible(SessionRow.BotsPerSide));
        Assert.False(page.Visible(SessionRow.BotSkill));

        page.Step(SessionRow.Bots, 1);
        Assert.Equal((BotFill.PerSide, "Per Side"), (page.Setup.Bots, page.Text(SessionRow.Bots)));
        Assert.True(page.Visible(SessionRow.BotsPerSide));
        page.Step(SessionRow.BotsPerSide, 1);
        page.SetNumber(SessionRow.BotsPerSide, OnlineBotRoster.MaxPerSide + 1);
        Assert.Equal(OnlineBotRoster.MaxPerSide, page.Setup.BotsPerSide);

        page.Step(SessionRow.Bots, 1);
        Assert.Equal((BotFill.Fill, "Fill Slots"), (page.Setup.Bots, page.Text(SessionRow.Bots)));
        Assert.False(page.Visible(SessionRow.BotsPerSide));
        Assert.True(page.Visible(SessionRow.BotSkill));
        Assert.Equal("Normal", page.Text(SessionRow.BotSkill));
        page.Step(SessionRow.BotSkill, -1);
        Assert.Equal((AiSkill.Easy, "Easy"), (page.Setup.BotSkill, page.Text(SessionRow.BotSkill)));
        page.Step(SessionRow.BotSkill, -1);
        Assert.Equal(AiSkill.Expert, page.Setup.BotSkill);
        page.Step(SessionRow.Bots, 1);
        Assert.Equal((BotFill.Custom, "Custom teams"), (page.Setup.Bots, page.Text(SessionRow.Bots)));
        Assert.False(page.Visible(SessionRow.BotsPerSide));
        Assert.True(page.Visible(SessionRow.SpyBots));
        Assert.True(page.Visible(SessionRow.MercBots));
        page.Step(SessionRow.Bots, 1);
        Assert.Equal(BotFill.Off, page.Setup.Bots);
        Assert.False(page.Visible(SessionRow.SpyBots));
        Assert.False(page.Visible(SessionRow.MercBots));
    }

    [Fact]
    public void ExactTeamCountsAllowZeroAndRetainRequestedBotsWhenSlotsShrink()
    {
        var page = Page();
        page.Step(SessionRow.Bots, 3);
        Assert.Equal((0, OnlineBotRoster.MaxCustomPerTeam), page.Range(SessionRow.SpyBots));
        Assert.Equal((0, OnlineBotRoster.MaxCustomPerTeam), page.Range(SessionRow.MercBots));
        page.SetNumber(SessionRow.SpyBots, -1);
        page.SetNumber(SessionRow.MercBots, 3);
        page.SetNumber(SessionRow.MaxPlayers, 2);
        Assert.Equal((0, 3, 2), (page.Setup.SpyBots, page.Setup.MercBots, page.Setup.MaxPlayers));
        Assert.Equal("0", page.Text(SessionRow.SpyBots));
        Assert.Equal("3", page.Text(SessionRow.MercBots));
        Assert.Equal((0, 1), OnlineBotRoster.Wanted(page.Setup, 1, 0, page.Setup.MaxPlayers));

        page.SetNumber(SessionRow.MaxPlayers, 4);
        Assert.Equal((0, 3), OnlineBotRoster.Wanted(page.Setup, 1, 0, page.Setup.MaxPlayers));
        page.SetNumber(SessionRow.MercBots, 100);
        Assert.Equal(OnlineBotRoster.MaxCustomPerTeam, page.Setup.MercBots);
        Assert.Equal((0, OnlineBotRoster.MaxCustomPerTeam), page.Range(SessionRow.MercBots));
    }

    [Fact]
    public void CustomCountsSurviveMapAndModeChangesButSpiesAreHiddenInMercDeathmatch()
    {
        var page = Page();
        page.Step(SessionRow.Bots, 3);
        page.SetNumber(SessionRow.SpyBots, 2);
        page.SetNumber(SessionRow.MercBots, 5);
        page.SetNumber(SessionRow.MaxPlayers, 8);
        page.SelectMap("Other");
        Assert.Equal((BotFill.Custom, 2, 5, 8), (page.Setup.Bots, page.Setup.SpyBots, page.Setup.MercBots, page.Setup.MaxPlayers));

        page.SelectMode(EGameMode.GM_MercDeathMatch);
        Assert.False(page.Visible(SessionRow.SpyBots));
        Assert.True(page.Visible(SessionRow.MercBots));
        Assert.Equal((2, 5), (page.Setup.SpyBots, page.Setup.MercBots));
        Assert.Equal((0, 5), OnlineBotRoster.Wanted(page.Setup, 0, 1, page.Setup.MaxPlayers));
        page.SelectMode(EGameMode.GM_Multi);
        Assert.True(page.Visible(SessionRow.SpyBots));
        Assert.Equal((2, 5), (page.Setup.SpyBots, page.Setup.MercBots));

        LobbySetup before = page.Setup;
        page.SetNumber(SessionRow.SpyBots, 0);
        page.SetNumber(SessionRow.MercBots, 3);
        Assert.Contains(SessionRow.SpyBots, LobbyRoomModel.Differences(before, page.Setup));
        Assert.Contains(SessionRow.MercBots, LobbyRoomModel.Differences(before, page.Setup));
    }

    [Fact]
    public void AnotherMapKeepsTheBotsAndAClientSeesTheirRowsBlink()
    {
        CreateSessionModel page = Page();
        page.Step(SessionRow.Bots, 2);
        page.Step(SessionRow.BotSkill, 1);
        LobbySetup before = page.Setup;
        page.SelectMap("Other");
        Assert.Equal((BotFill.Fill, AiSkill.Hard), (page.Setup.Bots, page.Setup.BotSkill));

        IReadOnlyList<SessionRow> changed = LobbyRoomModel.Differences(before with { Bots = BotFill.Off }, before);
        Assert.Equal([SessionRow.Bots], changed);
        Assert.Contains(SessionRow.BotSkill, LobbyRoomModel.Differences(before, before with { BotSkill = AiSkill.Easy }));
    }
}
