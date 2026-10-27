using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The Create Session page's bot rows (ours, not the original's): Bots, Bots per Side and Bot Skill.</summary>
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
        page.SetNumber(SessionRow.BotsPerSide, 9);
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
        Assert.Equal(BotFill.Off, page.Setup.Bots);
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
