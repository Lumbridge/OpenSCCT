using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/end-of-round.md: win conditions, sEndGame, the match-over states and the end menu.</summary>
public class EndOfRoundTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    /// <summary>A begun match with one objective per entry of <paramref name="taken"/>.</summary>
    private static (GameWorld W, SPlayerController[] P, SObjective[] O) Round(ENetMode mode, string options, bool[] taken,
        params string[] joins)
    {
        var objectives = new SObjective[taken.Length];
        GameWorld w = new GameWorld(mode).Load(options, x =>
        {
            MatchLifecycleTests.TwoSides(x);
            for (int i = 0; i < objectives.Length; i++)
            {
                objectives[i] = x.Level.SpawnActor(new SObjective(), default, default)!;
            }
        });
        for (int i = 0; i < taken.Length; i++)
        {
            objectives[i].ObjectiveTaken = taken[i];
        }

        var players = new SPlayerController[joins.Length];
        for (int i = 0; i < joins.Length; i++)
        {
            players[i] = w.Join(joins[i], local: mode == ENetMode.NM_Standalone && i == 0);
        }

        w.Game.bDelayedStart = false;
        w.Gri.bMatchHasBegun = true;
        w.Game.StartMatch();
        return (w, players, objectives);
    }

    private static (GameWorld W, SPlayerController[] P, SObjective[] O) Net(bool[] taken, params string[] joins) =>
        Round(ENetMode.NM_ListenServer, Story, taken, joins);

    // ---- sCheckEndGame (rules 2 to 10) --------------------------------------------------------------------------

    [Fact]
    public void Nothing_ends_before_the_match_has_begun()
    {
        GameWorld w = new GameWorld(ENetMode.NM_Standalone).Load("?GameMode=5?Editeur=true", MatchLifecycleTests.TwoSides);
        SPlayerController p = w.Join("?Name=Ed?Team=0?Editeur=true", local: true);
        p.PlayerReplicationInfo!.NumLives = 0;
        Assert.False(w.Game.sCheckEndGame());
        Assert.Equal(255, w.Game.WinnerTeam);
    }

    [Fact]
    public void Time_out_wins_for_the_mercs_before_anything_else()
    {
        (GameWorld w, SPlayerController[] p, _) = Net([true], "?Name=S?Team=0", "?Name=M?Team=1");
        p[1].PlayerReplicationInfo!.NumLives = 0;
        w.Gri.ElapsedTime = 600;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(1, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TIMEOUT, w.Gri.WinningReason);
    }

    [Fact]
    public void All_objectives_taken_win_for_the_spies()
    {
        (GameWorld w, _, SObjective[] o) = Net([false, true], "?Name=S?Team=0", "?Name=M?Team=1");
        Assert.False(w.Game.sCheckEndGame());
        Assert.Equal(255, w.Gri.WinningTeam);
        o[0].ObjectiveTaken = true;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(0, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_STORY, w.Gri.WinningReason);
    }

    [Fact]
    public void A_network_level_without_objectives_ends_with_a_spy_win_at_the_first_check()
    {
        (GameWorld w, _, _) = Net([], "?Name=S?Team=0", "?Name=M?Team=1");
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(0, w.Gri.WinningTeam);
    }

    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(4, 0, 0)]
    [InlineData(0, 0, 1)]
    public void In_a_network_game_a_side_out_of_lives_loses(int spyLives, int mercLives, int winner)
    {
        (GameWorld w, SPlayerController[] p, _) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1");
        p[0].PlayerReplicationInfo!.NumLives = spyLives;
        p[1].PlayerReplicationInfo!.NumLives = mercLives;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(winner, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TEAMDEATH, w.Gri.WinningReason);
    }

    [Fact]
    public void A_dead_player_with_lives_left_still_counts_and_quitters_and_spectators_do_not()
    {
        (GameWorld w, SPlayerController[] p, _) = Net([false], "?Name=S?Team=0", "?Name=T?Team=0", "?Name=M?Team=1");
        p[0].UnPossess();
        Assert.False(w.Game.sCheckEndGame());

        p[0].PlayerReplicationInfo!.NumLives = 0;
        p[1].PlayerReplicationInfo!.bQuittedMatch = true;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(1, w.Gri.WinningTeam);
    }

    [Fact]
    public void A_side_whose_players_all_quit_is_not_dead()
    {
        (GameWorld w, SPlayerController[] p, _) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1");
        p[0].PlayerReplicationInfo!.bQuittedMatch = true;
        p[0].PlayerReplicationInfo!.NumLives = 0;
        Assert.False(w.Game.sCheckEndGame());
    }

    [Fact]
    public void Offline_only_both_sides_out_of_lives_ends_the_round()
    {
        (GameWorld w, SPlayerController[] p, _) = Round(ENetMode.NM_Standalone, Story, [false], "?Name=S?Team=0", "?Name=M?Team=1");
        p[0].PlayerReplicationInfo!.NumLives = 0;
        Assert.False(w.Game.sCheckEndGame());
        p[1].PlayerReplicationInfo!.NumLives = 0;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(1, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TEAMDEATH, w.Gri.WinningReason);
    }

    [Fact]
    public void Offline_a_level_without_objectives_never_ends_on_objectives()
    {
        (GameWorld w, _, _) = Round(ENetMode.NM_Standalone, Story, [], "?Name=S?Team=0", "?Name=M?Team=1");
        Assert.False(w.Game.sCheckEndGame());
    }

    [Fact]
    public void Merc_deathmatch_ends_on_the_kill_limit_or_the_last_merc_standing()
    {
        (GameWorld w, SPlayerController[] p, _) = Round(ENetMode.NM_ListenServer, "?GameMode=7?ScoreLimit=3?LivesDEF=2",
            [false], "?Name=A", "?Name=B", "?Name=C");
        Assert.False(w.Game.sCheckEndGame());
        p[0].PlayerReplicationInfo!.Kills = 3;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(1, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_STORY, w.Gri.WinningReason);

        p[0].PlayerReplicationInfo!.Kills = 0;
        p[1].PlayerReplicationInfo!.NumLives = 0;
        p[1].PlayerReplicationInfo!.Deaths = 2;
        Assert.False(w.Game.sCheckEndGame());
        p[2].PlayerReplicationInfo!.NumLives = 0;
        p[2].PlayerReplicationInfo!.Deaths = 2;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal((byte)EEndReason.END_TEAMDEATH, w.Gri.WinningReason);
        Assert.Equal(1, w.Game.CountMercsStillIn());
    }

    [Fact]
    public void The_default_kill_limit_is_20()
    {
        (GameWorld w, SPlayerController[] p, _) = Round(ENetMode.NM_ListenServer, "?GameMode=7", [false], "?Name=A", "?Name=B");
        p[0].PlayerReplicationInfo!.Kills = 19;
        Assert.False(w.Game.sCheckEndGame());
        p[0].PlayerReplicationInfo!.Kills = 20;
        Assert.True(w.Game.sCheckEndGame());
        Assert.Equal(20, w.Game.GetTopKills());
    }

    // ---- sEndGame and s_MatchOver (rules 11 to 15) --------------------------------------------------------------

    [Fact]
    public void sEndGame_freezes_the_clock_fires_EndGame_and_enters_s_MatchOver()
    {
        EventProbe? probe = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            MatchLifecycleTests.TwoSides(x);
            probe = x.Level.SpawnActor(new EventProbe { Tag = "EndGame" }, default, default);
        });
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        SPlayerController remote = w.Join("?Name=R?Team=1");
        w.StartNetworkMatch(host);
        w.Game.sEndGame();

        Assert.True(w.Gri.bStopCountDown);
        Assert.True(w.Game.bGameEnded);
        Assert.Equal(1, probe!.Triggers);
        Assert.True(w.Game.IsInState("s_MatchOver"));
        Assert.Equal(255, w.Gri.WinningTeam);
        Assert.True(host.IsInState("GameEnded"));
        Assert.True(remote.IsInState("GameEnded"));
        Assert.Single(w.Host.To(remote, "ClientGameEnded"));
        Assert.Empty(w.Host.To(host, "ClientGameEnded"));
        Assert.True(host.Pawn!.bNoWeaponFiring);
        Assert.Equal(1f, w.Game.TimerRate);
    }

    [Fact]
    public void After_the_end_the_rules_timer_stops_checking_the_time_limit()
    {
        (GameWorld w, _, _) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.sEndGame();
        w.Gri.ElapsedTime = 9999;
        w.Run(2.2f);
        Assert.Equal(255, w.Gri.WinningTeam);
    }

    [Fact]
    public void The_time_limit_ends_the_round_at_the_first_rules_tick_after_it()
    {
        GameWorld w = new GameWorld().Load("?GameMode=5?TimeLimit=1", MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        w.StartNetworkMatch(host);
        w.Run(59.5f);
        Assert.False(w.Game.bGameEnded);
        w.Run(2.6f);
        Assert.True(w.Game.bGameEnded);
        Assert.Equal(1, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TIMEOUT, w.Gri.WinningReason);
    }

    [Fact]
    public void In_s_MatchOver_team_changes_are_refused_and_restarts_do_nothing()
    {
        (GameWorld w, SPlayerController[] p, _) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.sEndGame();
        Assert.False(w.Game.eventChangeTeam(p[0], 1, true));
        p[0].UnPossess();
        w.Game.eventRestartPlayer(p[0]);
        Assert.Null(p[0].Pawn);
    }

    [Fact]
    public void Every_player_asking_to_play_again_restarts_the_game()
    {
        (GameWorld w, SPlayerController[] p, _) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.sEndGame();
        p[0].ServerReadyToPlayAgain(true);
        w.Run(1.1f);
        Assert.Equal(string.Empty, w.Level.Info.NextURL);
        p[1].ServerReadyToPlayAgain(true);
        w.Run(1.1f);
        Assert.Equal("?Restart", w.Level.Info.NextURL);
    }

    [Fact]
    public void The_result_can_be_rewritten_after_the_end()
    {
        (GameWorld w, SPlayerController[] p, SObjective[] o) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1", "?Name=N?Team=1");
        o[0].ObjectiveTaken = true;
        w.Game.NotifyObjectiveTaken(o[0]);
        Assert.Equal(0, w.Gri.WinningTeam);
        w.Gri.ElapsedTime = 600;
        w.Game.Logout(p[2]);
        Assert.Equal(1, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TIMEOUT, w.Gri.WinningReason);
    }

    // ---- other entry points (rules 20 and 21) -------------------------------------------------------------------

    [Fact]
    public void A_taken_objective_plays_the_jingle_for_both_sides_and_checks_the_end()
    {
        (GameWorld w, SPlayerController[] p, SObjective[] o) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.NotifyObjectiveTaken(o[0]);
        Assert.Equal(2, w.Host.Named("ClientSetMusic").Count);
        Assert.Equal("Interface.Objectifs.Jingl_objectifTaken", w.Host.To(p[1], "ClientSetMusic")[0].Asset.Path);
        Assert.False(w.Game.bGameEnded);
        Assert.Equal(w.Level.TimeSeconds + 2f, w.Game.BroadcastScoreTime);
    }

    [Fact]
    public void A_dog_tag_is_counted_and_checks_the_end()
    {
        (GameWorld w, _, _) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.NotifyDogTag();
        Assert.Equal(1, w.Gri.NumDogTagsTaken);
    }

    [Fact]
    public void The_engine_end_path_is_disabled()
    {
        (GameWorld w, _, _) = Net([false], "?Name=S?Team=0");
        Assert.False(w.Game.CheckEndGame(null, string.Empty));
        w.Game.EndGame(null, string.Empty);
        Assert.False(w.Game.bGameEnded);
    }

    // ---- the player end state (rules 16 to 18) ------------------------------------------------------------------

    [Fact]
    public void A_player_in_GameEnded_cannot_change_state()
    {
        (GameWorld w, SPlayerController[] p, _) = Net([false], "?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.sEndGame();
        p[0].GotoState("s_Dead");
        p[0].eventMirrorState("s_Dead");
        p[0].eventChangeState("s_Dead");
        p[0].eventAntiBugTellServerIAmDead();
        Assert.True(p[0].IsInState("GameEnded"));
        Assert.True(p[0].bGameEnded);
        Assert.False(p[0].PlayerReplicationInfo!.bReadyToPlayAgain);
    }

    [Fact]
    public void The_end_music_follows_the_winner_and_the_end_menu_opens_10_s_later()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        w.Host.Net = NetworkType.NET_LAN;
        SPlayerController spy = w.Join("?Name=S?Team=0", local: true);
        w.StartNetworkMatch(spy);
        w.Game.sEndGame();
        w.RunWithPlayerTick(3f, spy);
        Assert.Empty(w.Host.To(spy, "ClientSetMusic"));

        w.Game.ReplicateWinningReason(0, EEndReason.END_STORY);
        w.RunWithPlayerTick(9.5f, spy);
        GameCall music = Assert.Single(w.Host.To(spy, "ClientSetMusic"));
        Assert.Equal("Interface.Objectifs.Jingle_Victoire1", music.Asset.Path);
        Assert.Empty(w.Host.To(spy, "ClientOpenMenu"));
        w.RunWithPlayerTick(0.7f, spy);
        GameCall menu = Assert.Single(w.Host.To(spy, "ClientOpenMenu"));
        Assert.Equal(new Name("Ending_Menu"), menu.Name1);
    }

    [Fact]
    public void A_loser_hears_the_defeat_music_and_offline_returns_to_the_entry_map()
    {
        GameWorld w = new GameWorld(ENetMode.NM_Standalone).Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController spy = w.Join("?Name=S?Team=0", local: true);
        w.Game.bDelayedStart = false;
        w.Game.StartMatch();
        w.Game.ReplicateWinningReason(1, EEndReason.END_TIMEOUT);
        w.Game.sEndGame();
        w.RunWithPlayerTick(10.2f, spy);
        Assert.Equal("Interface.Objectifs.Jingl_YouLose", Assert.Single(w.Host.To(spy, "ClientSetMusic")).Asset.Path);
        GameCall menu = Assert.Single(w.Host.To(spy, "ClientOpenMenu"));
        Assert.Equal(new Name("loading"), menu.Name1);
        Assert.Equal("VisitMapReturn", menu.Text);
    }
}
