using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/match-options.md: the URL option grammar, SMatchInfo and SGameInfo.InitGame.</summary>
public class MatchOptionsTests
{
    private const string SteeEStory =
        "?MapName=SteeE?MaxPlayers=4?TimeLimit=10?ScoreLimit=0?GameMode=5?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3";

    // ---- option grammar (rules 1 to 5) ----------------------------------------------------------------------

    [Fact]
    public void GrabOption_takes_the_first_pair_and_needs_a_leading_question_mark()
    {
        string options = "?A=1?B=2";
        string pair = string.Empty;
        Assert.True(UcOptions.GrabOption(ref options, ref pair));
        Assert.Equal("A=1", pair);
        Assert.Equal("?B=2", options);
        Assert.True(UcOptions.GrabOption(ref options, ref pair));
        Assert.Equal("B=2", pair);
        Assert.Equal(string.Empty, options);
        Assert.False(UcOptions.GrabOption(ref options, ref pair));

        string bad = "A=1?B=2";
        Assert.False(UcOptions.GrabOption(ref bad, ref pair));
    }

    [Fact]
    public void GetKeyValue_splits_at_the_first_equals_sign()
    {
        string key = string.Empty;
        string value = string.Empty;
        UcOptions.GetKeyValue("Name=a=b", ref key, ref value);
        Assert.Equal(("Name", "a=b"), (key, value));
        UcOptions.GetKeyValue("Flag", ref key, ref value);
        Assert.Equal(("Flag", string.Empty), (key, value));
    }

    [Fact]
    public void ParseOption_returns_the_first_match_ignoring_case()
    {
        Assert.Equal("2", UcOptions.ParseOption("?GamesInARow=2?gamesinarow=1", "GAMESINAROW"));
        Assert.Equal(string.Empty, UcOptions.ParseOption("?A=1", "B"));
        Assert.Equal(string.Empty, UcOptions.ParseOption("?A=", "A"));
        Assert.Equal(string.Empty, UcOptions.ParseOption("A=1", "A"));
    }

    [Fact]
    public void GetIntOption_uses_the_default_for_absent_or_empty_values_and_converts_like_UnrealScript()
    {
        Assert.Equal(7, UcOptions.GetIntOption("?A=", "A", 7));
        Assert.Equal(7, UcOptions.GetIntOption("?B=1", "A", 7));
        Assert.Equal(12, UcOptions.GetIntOption("?A=12abc", "A", 7));
        Assert.Equal(0, UcOptions.GetIntOption("?A=abc", "A", 7));
        Assert.Equal(-5, UcOptions.GetIntOption("?A=-5", "A", 7));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("yes", false)]
    public void Bool_conversion_reads_true_false_or_a_number(string text, bool expected) =>
        Assert.Equal(expected, UcStrings.ToBool(text));

    // ---- SMatchInfo.SetOptionString (data table) ------------------------------------------------------------

    [Fact]
    public void SetOptionString_without_options_gives_the_documented_defaults()
    {
        var m = new USMatchInfo { Handicap = 9 };
        m.SetOptionString(string.Empty);
        Assert.Equal(string.Empty, m.LevelName);
        Assert.Equal(4, m.MaxPlayers);
        Assert.Equal(0, m.TimeLimit);
        Assert.Equal(0, m.ScoreLimit);
        Assert.Equal(EGameMode.GM_Extraction, m.GameMode);
        Assert.Equal(999999, m.NumLivesATT);
        Assert.Equal(999999, m.NumLivesDEF);
        Assert.Equal(15, m.TimesRespawn);
        Assert.False(m.BalanceTeams);
        Assert.False(m.Revenge);
        Assert.Equal(0, m.GamesInARow);
        Assert.Equal(1, m.TimesReinitFlag);
        Assert.Equal(1, m.TimeToExplode);
        Assert.False(m.GameWithRanking);
        Assert.Equal(0, m.LevelGap);
        Assert.Equal(9, m.Handicap);
    }

    [Fact]
    public void SetOptionString_reads_the_SteeE_story_url()
    {
        var m = new USMatchInfo();
        m.SetOptionString(SteeEStory);
        Assert.Equal("SteeE", m.LevelName);
        Assert.Equal(10, m.TimeLimit);
        Assert.Equal(EGameMode.GM_Multi, m.GameMode);
        Assert.Equal(4, m.NumLivesATT);
        Assert.Equal(3, m.NumLivesDEF);
        Assert.Equal(10, m.TimesRespawn);
    }

    [Fact]
    public void Lives_of_zero_are_unlimited_and_negative_lives_are_kept()
    {
        var m = new USMatchInfo();
        m.SetOptionString("?LivesATT=0?LivesDEF=-2");
        Assert.Equal(999999, m.NumLivesATT);
        Assert.Equal(-2, m.NumLivesDEF);
    }

    [Fact]
    public void Booleans_are_true_only_for_the_text_true()
    {
        var m = new USMatchInfo();
        m.SetOptionString("?BalanceTeams=TRUE?Revenge=1?GameWithRanking=true");
        Assert.True(m.BalanceTeams);
        Assert.False(m.Revenge);
        Assert.True(m.GameWithRanking);
    }

    [Fact]
    public void MaxPlayers_clamps_to_0_and_32()
    {
        var m = new USMatchInfo();
        m.SetOptionString("?MaxPlayers=99");
        Assert.Equal(32, m.MaxPlayers);
        m.SetOptionString("?MaxPlayers=-1");
        Assert.Equal(0, m.MaxPlayers);
    }

    // ---- GetOptionsString (rule 23) ---------------------------------------------------------------------------

    [Fact]
    public void GetOptionsString_writes_every_setting_in_order_and_round_trips()
    {
        var m = new USMatchInfo();
        m.SetOptionString(SteeEStory + "?Revenge=true?GamesInARow=2");
        string url = m.GetOptionsString();
        Assert.Equal(
            "?MapName=SteeE?MaxPlayers=4?TimeLimit=10?ScoreLimit=0?GameMode=5?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3" +
            "?BalanceTeams=False?Revenge=True?GamesInARow=2?TimesReinitFlag=1?TimeToExplode=1?GameWithRanking=False?LevelGap=0",
            url);

        var again = new USMatchInfo();
        again.SetOptionString(url);
        Assert.Equal(url, again.GetOptionsString());
    }

    [Fact]
    public void Unlimited_lives_are_written_back_as_999999()
    {
        var m = new USMatchInfo();
        m.SetOptionString(string.Empty);
        Assert.Contains("?LivesATT=999999", m.GetOptionsString());
    }

    // ---- SMatchInfo helpers (rules 19 to 22) and SMapSettings (rule 24) --------------------------------------

    [Fact]
    public void Static_helpers_follow_the_mode_rules()
    {
        Assert.Equal(100, USMatchInfo.MaxKillLimit());
        Assert.Equal(20, USMatchInfo.DefaultKillLimit());
        Assert.Equal(1, USMatchInfo.RespawnFloor(EGameMode.GM_MercDeathMatch));
        Assert.Equal(5, USMatchInfo.RespawnFloor(EGameMode.GM_Multi));
        Assert.True(USMatchInfo.IsDeathMatch(EGameMode.GM_DeathMatch));
        Assert.True(USMatchInfo.IsDeathMatch(EGameMode.GM_MercDeathMatch));
        Assert.False(USMatchInfo.IsDeathMatch(EGameMode.GM_Multi));
        Assert.Equal(EGameMode.GM_DeathMatch, USMatchInfo.RulesModeFor(EGameMode.GM_MercDeathMatch));
        Assert.Equal(EGameMode.GM_DiskHunt, USMatchInfo.RulesModeFor(EGameMode.GM_DiskHunt));
    }

    [Fact]
    public void SearchForGameMode_finds_the_first_entry_of_a_mode()
    {
        var settings = new USMapSettings();
        settings.Modes.Add(new FGameModeInfo { GameModeId = EGameMode.GM_Multi, TimeLimit = 10 });
        settings.Modes.Add(new FGameModeInfo { GameModeId = EGameMode.GM_DiskHunt });
        settings.Modes.Add(new FGameModeInfo { GameModeId = EGameMode.GM_Multi, TimeLimit = 99 });
        Assert.Equal(0, settings.SearchForGameMode(EGameMode.GM_Multi));
        Assert.Equal(1, settings.SearchForGameMode(EGameMode.GM_DiskHunt));
        Assert.Equal(-1, settings.SearchForGameMode(EGameMode.GM_DeathMatch));
    }

    // ---- SGameInfo.InitGame (rules 6 to 18) -------------------------------------------------------------------

    [Fact]
    public void InitGame_reads_caps_waiting_players_scoreboard_and_scoring_table()
    {
        GameWorld w = new GameWorld().Load("?MapName=SteeE?GameMode=5?NBPlayers=4?MaxPlayers=40");
        SGameInfo g = w.Game;
        Assert.Equal(32, g.MaxPlayers);
        Assert.Equal(4, g.PlayersToWait);
        Assert.Equal(new Name("SScoreBoardStory"), g.ScoreBoardType);
        Assert.Equal(32, g.MaxTeamSize);
        Assert.False(g.bEnableStatLogging);
        Assert.Equal(19, w.Level.Info.ActionPoints.Count);
        Assert.Equal(2, w.Level.Info.ActionPoints[LevelInfo.AP_D_KillWithWeapon]);
        Assert.NotNull(g.BroadcastHandler);
        Assert.NotNull(g.AccessControl);
        Assert.Equal(EGameMode.GM_Multi, g.MatchInfo!.GameMode);
    }

    [Fact]
    public void InitGame_defaults_without_options()
    {
        GameWorld w = new GameWorld().Load(string.Empty);
        Assert.Equal(16, w.Game.MaxPlayers);
        Assert.Equal(1, w.Game.PlayersToWait);
        Assert.Equal(new Name("SScoreBoardStory"), w.Game.ScoreBoardType); // GameMode defaults to Extraction (defined)
    }

    [Fact]
    public void InitGame_keeps_the_scoreboard_for_an_undefined_mode_and_parses_speed_and_throwing()
    {
        GameWorld w = new GameWorld().Load("?GameMode=0?GameSpeed=0?AllowThrowing=False?GameStats=true");
        Assert.Equal(new Name("SScoreBoard"), w.Game.ScoreBoardType);
        Assert.Equal(0.1f, w.Level.Info.TimeDilation);
        Assert.False(w.Game.bAllowWeaponThrowing);
        Assert.True(w.Game.bEnableStatLogging);
    }

    [Fact]
    public void InitGame_on_the_console_build_caps_teams_at_two()
    {
        var w = new GameWorld();
        w.Host.Psx2 = true;
        w.Load(string.Empty);
        Assert.Equal(2, w.Game.MaxTeamSize);
    }

    [Fact]
    public void A_revenge_game_carries_the_team_points_and_a_plain_game_does_not()
    {
        GameWorld revenge = new GameWorld().Load("?Revenge=true?ScoreATT=12?ScoreDEF=7");
        Assert.Equal(12, revenge.Level.Info.NbPointsAttack);
        Assert.Equal(7, revenge.Level.Info.NbPointsDefense);

        GameWorld plain = new GameWorld().Load("?ScoreATT=12?ScoreDEF=7");
        Assert.Equal(0, plain.Level.Info.NbPointsAttack);
        Assert.Equal(0, plain.Level.Info.NbPointsDefense);
    }

    [Fact]
    public void Play_level_url_gives_unlimited_lives_15_s_respawn_and_no_time_limit()
    {
        GameWorld w = new GameWorld(ENetMode.NM_Standalone).Load("?GameMode=5?Team=0?Editeur=true");
        USMatchInfo m = w.Game.MatchInfo!;
        Assert.Equal(999999, m.NumLivesATT);
        Assert.Equal(999999, m.NumLivesDEF);
        Assert.Equal(15, m.TimesRespawn);
        Assert.Equal(0, m.TimeLimit);
        Assert.Equal(EGameMode.GM_Multi, m.GameMode);
    }
}
