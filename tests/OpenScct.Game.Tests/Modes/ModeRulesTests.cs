using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Modes;

/// <summary>
/// spec/game/game-modes.md: the settings each mode starts with on SteeE, what the generic rules do per mode
/// (missions, lives, end conditions, late joiners) and a revenge pair played over two maps.
/// </summary>
public class ModeRulesTests
{
    // ---- the untouched menu per mode (rules 1 to 5) -------------------------------------------------------------

    [Fact]
    public void SteeE_Story_and_Deathmatch_start_with_their_map_entries()
    {
        USMatchInfo story = SteeE.Setup(EGameMode.GM_Multi).Match;
        Assert.Equal((10, 4, 3, 10, 0), (story.TimeLimit, story.NumLivesATT, story.NumLivesDEF, story.TimesRespawn, story.ScoreLimit));

        MatchSetup dm = SteeE.Setup(EGameMode.GM_DeathMatch);
        Assert.Equal((10, 8, 4, 5, 0), (dm.Match.TimeLimit, dm.Match.NumLivesATT, dm.Match.NumLivesDEF, dm.Match.TimesRespawn, dm.Match.ScoreLimit));
        Assert.Equal((0, 0), (dm.NumDisks, dm.NumDisksToWin));
        Assert.Contains("?GameMode=6?LivesATT=8?TimesRespawnsATT=5?LivesDEF=4", dm.GameOptions(1));
    }

    [Fact]
    public void Merc_deathmatch_reads_the_deathmatch_entry_with_its_own_defaults()
    {
        MatchSetup mdm = SteeE.Setup(EGameMode.GM_MercDeathMatch);
        Assert.Equal(10, mdm.Match.TimeLimit);
        Assert.Equal(20, mdm.Match.ScoreLimit);
        Assert.Equal(0, mdm.Match.NumLivesATT);
        Assert.Equal(0, mdm.Match.NumLivesDEF);
        Assert.Equal(1, mdm.Match.TimesRespawn);
        Assert.Equal(20, mdm.NumDisksToWin);
        Assert.Contains("?TimeLimit=10?ScoreLimit=20?GameMode=7?LivesATT=0?TimesRespawnsATT=1?LivesDEF=0", mdm.GameOptions(1));
    }

    [Fact]
    public void A_map_without_an_entry_for_the_mode_starts_from_the_menu_fallbacks()
    {
        MatchSetup setup = MatchSetup.Defaults(new USMapSettings(), EGameMode.GM_Multi, "Map");
        Assert.Equal((0, 0, 0, 5), (setup.Match.TimeLimit, setup.Match.NumLivesATT, setup.Match.NumLivesDEF, setup.Match.TimesRespawn));
        Assert.Equal((1, 1), (setup.NumDisks, setup.NumDisksToWin));
    }

    [Fact]
    public void Revenge_makes_a_pair_of_games()
    {
        USMatchInfo m = SteeE.Setup(EGameMode.GM_Multi, revenge: true, gamesInARow: 5).Match;
        Assert.True(m.Revenge);
        Assert.Equal(2, m.GamesInARow);
    }

    // ---- per-mode match start and lives (rules 6 to 9) ----------------------------------------------------------

    [Theory]
    [InlineData(EGameMode.GM_Multi, 4, 3)]
    [InlineData(EGameMode.GM_DiskHunt, 4, 3)]
    [InlineData(EGameMode.GM_DeathMatch, 8, 4)]
    public void Only_the_mode_mission_is_live_and_each_side_gets_its_lives(EGameMode mode, int spyLives, int mercLives)
    {
        var m = new ModeWorld(mode);
        SPlayerController[] p = m.Start("?Name=S?Team=0", "?Name=M?Team=1");
        Assert.Equal(mode != EGameMode.GM_Multi, m.Story.ObjectiveTaken);
        Assert.Equal(mode != EGameMode.GM_DiskHunt, m.DiskHunt.ObjectiveTaken);
        Assert.Equal(mode != EGameMode.GM_DeathMatch, m.Deathmatch.ObjectiveTaken);
        Assert.Equal(spyLives, p[0].PlayerReplicationInfo!.NumLives);
        Assert.Equal(mercLives, p[1].PlayerReplicationInfo!.NumLives);
        Assert.Equal(mode, m.Gri.GameMode);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("?GameMode=1", 0)]
    [InlineData("?GameMode=2", 1)]
    [InlineData("?GameMode=3", 2)]
    public void Extraction_Sabotage_and_Neutralization_select_their_own_mission(string modeOption, int live)
    {
        // Rule 16: no stock map has these missions; a level with one mission per mode plays only the selected one,
        // and an absent GameMode is Extraction.
        EGameMode[] modes = [EGameMode.GM_Extraction, EGameMode.GM_Sabotage, EGameMode.GM_Neutralization];
        var missions = new SMission[modes.Length];
        var objectives = new SObjective[modes.Length];
        GameWorld w = new GameWorld().Load("?TimeLimit=10?LivesATT=4?LivesDEF=3" + modeOption, x =>
        {
            MatchLifecycleTests.TwoSides(x);
            for (int i = 0; i < modes.Length; i++)
            {
                objectives[i] = x.Level.SpawnActor(new SObjective(), default, default)!;
                missions[i] = x.Level.SpawnActor(new SMission { GameMode = modes[i], bChained = false, MinimumObjectives = 1 },
                    default, default)!;
                missions[i].Objectives.Add(objectives[i]);
            }
        });
        SPlayerController spy = w.Join("?Name=S?Team=0");
        w.Join("?Name=M?Team=1");
        w.StartNetworkMatch(spy);
        w.Gri.bMatchHasBegun = true;

        Assert.Equal(modes[live], w.Gri.GameMode);
        for (int i = 0; i < modes.Length; i++)
        {
            Assert.Equal(i != live, objectives[i].ObjectiveTaken);
        }

        // The mission is an objective too: the round ends once both are taken.
        Assert.False(w.Game.sCheckEndGame());
        objectives[live].ObjectiveTaken = true;
        missions[live].ObjectiveTaken = true;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(0, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_STORY, w.Gri.WinningReason);
    }

    [Fact]
    public void Merc_deathmatch_puts_everyone_on_the_mercs_with_unlimited_lives_and_no_mission()
    {
        var m = new ModeWorld(EGameMode.GM_MercDeathMatch);
        SPlayerController[] p = m.Start("?Name=A?Team=0", "?Name=B?Team=0", "?Name=C?Team=1");
        foreach (SPlayerController pc in p)
        {
            Assert.Equal(1, pc.PlayerReplicationInfo!.Team!.TeamIndex);
            Assert.Equal(GameFlow.K.UnlimitedLives, pc.PlayerReplicationInfo!.NumLives);
        }

        Assert.True(m.Story.ObjectiveTaken);
        Assert.True(m.DiskHunt.ObjectiveTaken);
        Assert.True(m.Deathmatch.ObjectiveTaken);
        Assert.Empty(m.Active());
    }

    // ---- per-mode end conditions (rules 10 to 13) ---------------------------------------------------------------

    [Fact]
    public void Deathmatch_never_ends_on_objectives_and_ends_on_time_or_a_side_out_of_lives()
    {
        var m = new ModeWorld(EGameMode.GM_DeathMatch);
        SPlayerController[] p = m.Start("?Name=S?Team=0", "?Name=M?Team=1");
        m.Game.Killed(p[1], p[0], p[0].Pawn, SDamageType.SDTBase);
        Assert.False(m.Game.bGameEnded);
        Assert.Equal(7, p[0].PlayerReplicationInfo!.NumLives);

        p[0].PlayerReplicationInfo!.NumLives = 1;
        m.Game.Killed(p[1], p[0], p[0].Pawn, SDamageType.SDTBase);
        Assert.True(m.Game.bGameEnded);
        Assert.Equal(1, m.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TEAMDEATH, m.Gri.WinningReason);
    }

    [Theory]
    [InlineData(EGameMode.GM_DeathMatch)]
    [InlineData(EGameMode.GM_DiskHunt)]
    [InlineData(EGameMode.GM_Multi)]
    public void Every_mode_gives_the_mercs_the_time_out(EGameMode mode)
    {
        var m = new ModeWorld(mode);
        m.Start("?Name=S?Team=0", "?Name=M?Team=1");
        m.Gri.ElapsedTime = 600;
        Assert.True(m.Game.sCheckEndGame());
        Assert.Equal(1, m.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TIMEOUT, m.Gri.WinningReason);
    }

    [Fact]
    public void Merc_deathmatch_ends_at_the_default_kill_limit()
    {
        var m = new ModeWorld(EGameMode.GM_MercDeathMatch);
        SPlayerController[] p = m.Start("?Name=A?Team=0", "?Name=B?Team=1");
        for (int i = 0; i < 19; i++)
        {
            m.Game.Killed(p[0], p[1], p[1].Pawn, SDamageType.SDTBase);
        }

        Assert.False(m.Game.bGameEnded);
        m.Game.Killed(p[0], p[1], p[1].Pawn, SDamageType.SDTBase);
        Assert.True(m.Game.bGameEnded);
        Assert.Equal((byte)EEndReason.END_STORY, m.Gri.WinningReason);
        Assert.Equal(20, m.Gri.NumDisks);
    }

    // ---- late joiners (rule 14) ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(EGameMode.GM_Multi)]
    [InlineData(EGameMode.GM_DiskHunt)]
    [InlineData(EGameMode.GM_DeathMatch)]
    public void A_late_joiner_watches_until_the_next_map_outside_merc_deathmatch(EGameMode mode)
    {
        var m = new ModeWorld(mode);
        m.Start("?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController late = m.W.Join("?Name=Late?Team=0");
        Assert.True(late.PlayerReplicationInfo!.bOnlySpectator);
        Assert.True(late.IsInState("s_Spectating_Visitor"));
        Assert.Null(late.Pawn);
    }

    [Fact]
    public void A_merc_deathmatch_late_joiner_plays_once_ready_but_not_after_the_end()
    {
        var m = new ModeWorld(EGameMode.GM_MercDeathMatch);
        m.Start("?Name=A?Team=0", "?Name=B?Team=1");
        SPlayerController late = m.W.Join("?Name=Late?Team=0");
        Assert.True(late.IsInState("s_PlayerWaiting"));
        late.PlayerWaitingToPlayToServer(true);
        Assert.NotNull(late.Pawn);
        Assert.Equal(GameFlow.K.UnlimitedLives, late.PlayerReplicationInfo!.NumLives);

        m.Game.sEndGame();
        SPlayerController after = m.W.Join("?Name=After?Team=1");
        Assert.True(after.PlayerReplicationInfo!.bOnlySpectator);
    }

    // ---- rounds in a row and revenge (round-travel.md) ----------------------------------------------------------

    /// <summary>The next map of a travel, as the engine starts it: server options and each player's login options.</summary>
    private static (string Options, Dictionary<string, string> Logins) NextMap(GameWorld w, (SPlayerController Pc, string Login)[] players)
    {
        (string _, string serverOptions) = TravelUrl.Split(w.Level.Info.NextURL);
        var logins = new Dictionary<string, string>();
        List<(PlayerController Target, GameCall Call)> travels = w.Host.Named("ClientTravel");
        foreach ((SPlayerController pc, string login) in players)
        {
            string url = w.Level.Info.NextURL;
            foreach ((PlayerController target, GameCall call) in travels)
            {
                if (ReferenceEquals(target, pc))
                {
                    url = call.Text ?? string.Empty;
                }
            }

            logins[pc.PlayerReplicationInfo!.PlayerName] = TravelUrl.NextLogin(login, url);
        }

        return (TravelUrl.NativeOptions(serverOptions), logins);
    }

    [Fact]
    public void A_revenge_pair_swaps_the_sides_and_carries_the_points_swapped()
    {
        MatchSetup setup = SteeE.Setup(EGameMode.GM_Multi, revenge: true);
        var round1 = new GameWorld().Load(setup.GameOptions(2), MatchLifecycleTests.TwoSides);
        round1.Game.PersistentData.LevelName = SteeE.Map;
        SPlayerController host = round1.Join("?Name=H?Team=0", local: true);
        SPlayerController guest = round1.Join("?Name=G?Team=1");
        round1.StartNetworkMatch(host);
        round1.Level.Info.NbPointsAttack = 7;
        round1.Level.Info.NbPointsDefense = 3;
        round1.Game.sEndGame();

        string next = EndingMenuRules.NextRoundUrl(round1.Game, 1);
        host.SwitchLevel(next);
        (string options, Dictionary<string, string> logins) = NextMap(round1, [(host, "?Name=H?Team=0"), (guest, "?Name=G?Team=1")]);
        Assert.Contains("?GamesInARow=1", options);
        Assert.DoesNotContain("?GamesInARow=2", options);

        var round2 = new GameWorld().Load(options, MatchLifecycleTests.TwoSides);
        SPlayerController host2 = round2.Join(logins["H"], local: true);
        SPlayerController guest2 = round2.Join(logins["G"]);
        Assert.Equal(1, host2.PlayerReplicationInfo!.Team!.TeamIndex);
        Assert.Equal(0, guest2.PlayerReplicationInfo!.Team!.TeamIndex);
        Assert.Equal(3, round2.Level.Info.NbPointsAttack);
        Assert.Equal(7, round2.Level.Info.NbPointsDefense);
        Assert.Equal(1, round2.Game.MatchInfo!.GamesInARow);
        Assert.Equal(1, round2.Gri.GamesInARow);

        // The pair is over: the end menu goes back to the lobby.
        round2.StartNetworkMatch(host2);
        round2.Game.sEndGame();
        Assert.StartsWith("Lobby?", EndingMenuRules.NextRoundUrl(round2.Game, 1));
    }

    [Fact]
    public void Games_in_a_row_count_down_on_the_same_map_without_swapping()
    {
        MatchSetup setup = SteeE.Setup(EGameMode.GM_DeathMatch, gamesInARow: 3);
        string options = setup.GameOptions(2);
        var logins = new Dictionary<string, string> { ["H"] = "?Name=H?Team=0", ["G"] = "?Name=G?Team=1" };
        for (int game = 3; game >= 2; game--)
        {
            var w = new GameWorld().Load(options, MatchLifecycleTests.TwoSides);
            w.Game.PersistentData.LevelName = SteeE.Map;
            SPlayerController host = w.Join(logins["H"], local: true);
            SPlayerController guest = w.Join(logins["G"]);
            Assert.Equal(game, w.Game.MatchInfo!.GamesInARow);
            Assert.Equal(0, host.PlayerReplicationInfo!.Team!.TeamIndex);
            Assert.Equal(1, guest.PlayerReplicationInfo!.Team!.TeamIndex);
            w.StartNetworkMatch(host);
            w.Game.sEndGame();
            host.SwitchLevel(EndingMenuRules.NextRoundUrl(w.Game, 1));
            (options, logins) = NextMap(w, [(host, logins["H"]), (guest, logins["G"])]);
            Assert.Contains("?GameMode=6", options);
        }

        var last = new GameWorld().Load(options, MatchLifecycleTests.TwoSides);
        last.Join(logins["H"], local: true);
        Assert.Equal(1, last.Game.MatchInfo!.GamesInARow);
        Assert.StartsWith("Lobby?", EndingMenuRules.NextRoundUrl(last.Game, 1));
    }

    [Fact]
    public void The_engine_keeps_the_last_value_of_a_repeated_option_at_its_first_place()
    {
        Assert.Equal("?GamesInARow=1?A=1?Team=0", TravelUrl.NativeOptions("?GamesInARow=2?A=1?GamesInARow=1?Team=1?Team=0"));
        Assert.Equal("?team=1?B", TravelUrl.NativeOptions("?Team=0??B?team=1"));
        Assert.Equal(("SteeE", "?A=1"), TravelUrl.Split("SteeE?A=1"));
        Assert.Equal(("Lobby", ""), TravelUrl.Split("Lobby"));
        Assert.Equal("?Name=P?Team=1?X=2", TravelUrl.Merge("?Name=P?Team=0", "?Team=1?X=2"));
    }
}
