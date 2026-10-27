using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The Create Session page's rules (spec/game/lobby-and-match-creation.md rules 1 to 9).</summary>
public sealed class MenuCreateSessionTests
{
    /// <summary>A map like SteeE: Story 10 min 4/3 lives 10 s, Disk Hunt 20 of 30 disks (10 to take), Deathmatch 8/4 lives 5 s.</summary>
    private static USMapSettings SteeELike()
    {
        var s = new USMapSettings();
        s.Modes.Add(new FGameModeInfo { GameModeId = EGameMode.GM_Multi, TimeLimit = 10, SpiesLives = 4, SentriesLives = 3, RespawnTime = 10 });
        s.Modes.Add(new FGameModeInfo
        {
            GameModeId = EGameMode.GM_DiskHunt, TimeLimit = 10, ScoreLimit = 10, SpiesLives = 4, SentriesLives = 3, RespawnTime = 10, Handicap = 20,
            NumDisksMax = 30,
        });
        s.Modes.Add(new FGameModeInfo { GameModeId = EGameMode.GM_DeathMatch, TimeLimit = 10, SpiesLives = 8, SentriesLives = 4, RespawnTime = 5 });
        return s;
    }

    /// <summary>A map with Story only, rules out of the page's ranges.</summary>
    private static USMapSettings Wild()
    {
        var s = new USMapSettings();
        s.Modes.Add(new FGameModeInfo { GameModeId = EGameMode.GM_Multi, TimeLimit = 45, SpiesLives = 30, SentriesLives = -2, RespawnTime = 2 });
        return s;
    }

    private static CreateSessionModel Page(LobbySetup? start = null)
    {
        var maps = new List<(string, string)> { ("SteeE", "ENHANCED STEEL SQUAT"), ("WildX", "Wild") };
        var settings = new Dictionary<string, USMapSettings> { ["SteeE"] = SteeELike(), ["WildX"] = Wild() };
        return new CreateSessionModel(maps, m => settings.GetValueOrDefault(m),
            start ?? LobbySetup.ForMap("SteeE", EGameMode.GM_Multi, settings["SteeE"], 0));
    }

    [Fact]
    public void ThePageOpensWithTheMapsRulesAndTheProfileDefaults()
    {
        CreateSessionModel page = Page();
        Assert.Equal((10, 4, 3, 10), (page.Setup.TimeLimit, page.Setup.SpyLives, page.Setup.MercLives, page.Setup.RespawnSeconds));
        Assert.Equal(LobbySetup.DefaultMaxPlayers, page.Setup.MaxPlayers);
        Assert.Equal("Modern", page.Text(SessionRow.VoiceChat));
        Assert.Equal("Team always - hackable coms", page.VoiceHint);
        Assert.Equal("Yes", page.Text(SessionRow.VoiceInLobby));
        Assert.Equal([EGameMode.GM_Multi, EGameMode.GM_DiskHunt, EGameMode.GM_DeathMatch, EGameMode.GM_MercDeathMatch], page.Modes);
        Assert.Equal("ENHANCED STEEL SQUAT (SteeE)", page.Text(SessionRow.Map));
        Assert.False(page.Visible(SessionRow.ActiveDisks));
        Assert.Equal("Spy Lives", page.Caption(SessionRow.SpyLives));
    }

    [Fact]
    public void ModesReloadTheMapsValuesAndShowTheirOwnRows()
    {
        CreateSessionModel page = Page();
        page.Step(SessionRow.Mode, 1);
        Assert.Equal(EGameMode.GM_DiskHunt, page.Setup.Mode);
        Assert.True(page.Visible(SessionRow.ActiveDisks) && page.Visible(SessionRow.DisksToTake));
        Assert.Equal((20, 10, 30), (page.Setup.Disks, page.Setup.DisksToWin, page.Setup.DisksMax));

        // Disks to Take never exceeds Active Disks; Active Disks stops at the map's most.
        page.SetNumber(SessionRow.ActiveDisks, 6);
        Assert.Equal((6, 6), (page.Setup.Disks, page.Setup.DisksToWin));
        page.SetNumber(SessionRow.DisksToTake, 9);
        Assert.Equal(6, page.Setup.DisksToWin);
        page.SetNumber(SessionRow.ActiveDisks, 99);
        Assert.Equal(30, page.Setup.Disks);

        // Merc Deathmatch: the spy lives row is "Kills to Win" (1 to 100, 20 by default), merc lives 0 and respawn 1.
        page.SelectMode(EGameMode.GM_MercDeathMatch);
        Assert.Equal("Kills to Win", page.Caption(SessionRow.SpyLives));
        Assert.Equal((20, 0, 1), (page.Number(SessionRow.SpyLives), page.Setup.MercLives, page.Setup.RespawnSeconds));
        Assert.Equal((1, 100), page.Range(SessionRow.SpyLives));
        page.SetNumber(SessionRow.SpyLives, 250);
        Assert.Equal(100, page.Setup.ScoreLimit);
        Assert.Equal("Inf.", page.Text(SessionRow.MercLives));
    }

    [Fact]
    public void ANewMapClampsItsRulesAndKeepsTheSessionChoices()
    {
        CreateSessionModel page = Page();
        page.Step(SessionRow.VoiceChat, 1);
        page.SetChecked(SessionRow.RandomMap, true);
        page.SetNumber(SessionRow.MaxPlayers, 6);
        page.Step(SessionRow.Map, 1);
        Assert.Equal("WildX", page.Setup.Map);

        // Time 0..20, lives 0..10, respawn at least 5 from the map (match-options.md rule 26).
        Assert.Equal((20, 10, 0, 5), (page.Setup.TimeLimit, page.Setup.SpyLives, page.Setup.MercLives, page.Setup.RespawnSeconds));
        Assert.Equal((EGameMode.GM_Multi, 6, true), (page.Setup.Mode, page.Setup.MaxPlayers, page.Setup.RandomMap));
        Assert.Equal("Classic", page.Text(SessionRow.VoiceChat));
        Assert.Equal("Living team - hackable coms", page.VoiceHint);
        Assert.Equal([EGameMode.GM_Multi, EGameMode.GM_MercDeathMatch], page.Modes);

        // Edited, the respawn row takes 1 to 15 in every mode; time 0 shows as unlimited.
        page.SetNumber(SessionRow.Respawn, 0);
        Assert.Equal(1, page.Setup.RespawnSeconds);
        page.SetNumber(SessionRow.TimeLimit, -3);
        Assert.Equal("Inf.", page.Text(SessionRow.TimeLimit));
    }

    [Fact]
    public void RevengePlaysTwoGamesAndHidesTheCount()
    {
        CreateSessionModel page = Page();
        page.SetNumber(SessionRow.GamesInARow, 5);
        page.SetChecked(SessionRow.Revenge, true);
        Assert.Equal(2, page.Setup.GamesInARow);
        Assert.False(page.Visible(SessionRow.GamesInARow));
        page.SetChecked(SessionRow.Revenge, false);
        Assert.Equal(5, page.Setup.GamesInARow);
        page.SetNumber(SessionRow.GamesInARow, 40);
        Assert.Equal(10, page.Setup.GamesInARow);
        page.SetNumber(SessionRow.MaxPlayers, 1);
        Assert.Equal(2, page.Setup.MaxPlayers);
    }

    [Fact]
    public void ThePasswordKeepsSixteenAllowedCharacters()
    {
        Assert.Equal("abc_DEF-1.2", CreateSessionModel.FilterPassword("abc _DEF-1.2!?"));
        Assert.Equal(16, CreateSessionModel.FilterPassword(new string('x', 30)).Length);
        CreateSessionModel page = Page();
        page.SetPassword("se cret");
        Assert.Equal("secret", page.Password);
        Assert.Equal("******", page.Text(SessionRow.Password));
    }

    [Fact]
    public void AStartOutsideTheMaxPlayersRangeBecomesFour()
    {
        CreateSessionModel page = Page(new LobbySetup { Map = "SteeE", MaxPlayers = 20 });
        Assert.Equal(4, page.Setup.MaxPlayers);
    }
}
