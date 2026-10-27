using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/scoring-and-stats.md and spec/game/round-travel.md.</summary>
public class ScoringAndTravelTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    private static (GameWorld W, SPlayerController[] P) Match(params string[] joins) =>
        DeathLivesRespawnTests.Match(Story, joins);

    // ---- the scoring table and AddPoint (rules 1 to 3) ---------------------------------------------------------

    [Fact]
    public void POINT_Multiple_is_the_table_the_game_uses()
    {
        var level = new World(1).Level.Info;
        level.InitArrayNbPoints("POINT_Multiple");
        Assert.Equal(new[] { 2, 2, 1, 2, 1, 2, 5, 5, 1, 0, 2, 3, 2, 2, 5, 0, 2, -2, -5 }, level.ActionPoints);
        level.InitArrayNbPoints("Anything");
        Assert.Equal(1, level.ActionPoints[LevelInfo.AP_A_TakeObjective]);
        Assert.Equal(0, level.ActionPoints[LevelInfo.AP_D_KillWithWeapon]);
        Assert.Equal(0, level.GetActionPoints(42));
    }

    [Fact]
    public void AddPoint_adds_to_the_team_counter_chosen_by_the_caller_and_to_the_player()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.AddPoint(1, p[1], LevelInfo.AP_D_KillWithWeapon);
        Assert.Equal(2, w.Level.Info.NbPointsDefense);
        Assert.Equal(2f, p[1].PlayerReplicationInfo!.Score);

        w.Game.AddPoint(0, p[1], LevelInfo.AP_D_KnockOut);
        Assert.Equal(2, w.Level.Info.NbPointsAttack);
        Assert.Equal(4f, p[1].PlayerReplicationInfo!.Score);

        w.Game.AddPoint(0, null, LevelInfo.AP_C_KillFriend);
        Assert.Equal(-3, w.Level.Info.NbPointsAttack);

        w.Game.AddPoint(0, p[0], LevelInfo.AP_A_TakeObjective);
        Assert.Equal(0f, p[0].PlayerReplicationInfo!.Score);
        Assert.False(w.Game.bGameEnded);
    }

    [Fact]
    public void AddPoint_runs_the_end_check()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController s = w.Join("?Name=S?Team=0");
        w.StartNetworkMatch(s);
        w.Game.AddPoint(0, s, LevelInfo.AP_A_Grab_Begin);
        Assert.True(w.Game.bGameEnded); // a network level without objectives ends at the first check
    }

    [Fact]
    public void InitScores_sets_both_counters()
    {
        (GameWorld w, _) = Match("?Name=S?Team=0");
        w.Game.InitScores(7, 9);
        Assert.Equal(7, w.Level.Info.NbPointsAttack);
        Assert.Equal(9, w.Level.Info.NbPointsDefense);
    }

    // ---- statistics and messages (rules 4 to 8) -----------------------------------------------------------------

    [Fact]
    public void FindDamageVolume_takes_an_active_volume_of_the_damage_type_set_off_by_someone()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S?Team=0", "?Name=M?Team=1");
        var idle = new SDamageVolume { bActive = false, DamageType = SDamageType.SDTBase_NoAnim, TriggerController = p[1] };
        var other = new SDamageVolume { bActive = true, DamageType = SDamageType.SDTBase, TriggerController = p[1] };
        var good = new SDamageVolume { bActive = true, DamageType = SDamageType.SDTBase_NoAnim, TriggerController = p[1] };
        Pawn victim = p[0].Pawn!;
        victim.Touching.Add(idle);
        victim.Touching.Add(other);
        victim.Touching.Add(good);
        Assert.Same(good, w.Game.FindDamageVolume(victim, SDamageType.SDTBase_NoAnim));
        Assert.Null(w.Game.FindDamageVolume(null, SDamageType.SDTBase_NoAnim));

        w.Game.Killed(p[1], p[0], victim, SDamageType.SDTBase_NoAnim);
        Assert.Equal(1, p[1].PlayerReplicationInfo!.Stats!.EnvironmentalKills);
    }

    [Fact]
    public void Environmental_kills_skip_teammates_outside_merc_deathmatch()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S?Team=0", "?Name=T?Team=0");
        w.Game.CountEnvironmentalKill(p[0], p[1]);
        w.Game.CountEnvironmentalKill(p[0], p[0]);
        w.Game.CountEnvironmentalKill(null, p[0]);
        Assert.Equal(0, p[0].PlayerReplicationInfo!.Stats!.EnvironmentalKills);
    }

    [Fact]
    public void A_sniper_head_shot_and_a_resigned_player_have_their_own_death_messages()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S?Team=0", "?Name=M?Team=1");
        p[0].LastDamages[0].PillTag = 1;
        w.Game.BroadcastDeathMessage(p[1], p[0], SDamageType.SDTSnipeShot);
        Assert.Equal(2, w.Host.Messages(23).Count);
        Assert.Equal(1, p[1].PlayerReplicationInfo!.Stats!.Merc_SniperHeadshots);

        p[0].LastDamages[0].PillTag = 3;
        w.Game.BroadcastDeathMessage(p[1], p[0], SDamageType.SDTSnipeShot);
        Assert.Equal(2, w.Host.Messages((int)SDamageType.SDTSnipeShot).Count);

        p[0].PlayerReplicationInfo!.bResigned = true;
        w.Game.BroadcastDeathMessage(p[0], p[0], SDamageType.Suicided);
        Assert.Equal(2, w.Host.Messages(24).Count);
    }

    [Fact]
    public void Team_messages_reach_one_side_and_team_less_players_read_side_0()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S?Team=0", "?Name=M?Team=1");
        var loner = w.Level.Spawn<SPlayerController>()!;
        w.Game.BroadcastTeamLocalizedMessage(0, new ClassRef("SBase.SGameMessages"), 56);
        List<(PlayerController Target, GameCall Call)> sent = w.Host.Messages(56);
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, m => ReferenceEquals(m.Target, p[0]));
        Assert.Contains(sent, m => ReferenceEquals(m.Target, loner));
    }

    // ---- server travel (round-travel.md rules 3 to 14) ----------------------------------------------------------

    [Fact]
    public void ServerTravel_is_ignored_while_a_travel_is_pending()
    {
        (GameWorld w, _) = Match("?Name=S?Team=0");
        w.Level.Info.ServerTravel("?First", false);
        w.Level.Info.ServerTravel("?Second", false);
        Assert.StartsWith("?First", w.Level.Info.NextURL);
    }

    [Fact]
    public void Without_game_rules_a_travel_starts_at_once()
    {
        var level = new World(1).Level.Info;
        level.NextSwitchCountdown = 5f;
        level.ServerTravel("Lobby", true);
        Assert.Equal("Lobby", level.NextURL);
        Assert.True(level.bNextItems);
        Assert.Equal(0f, level.NextSwitchCountdown);
    }

    [Fact]
    public void Clients_follow_with_their_team_and_the_host_team_goes_into_the_next_url()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=1", local: true);
        SPlayerController a = w.Join("?Name=A?Team=0");
        SPlayerController b = w.Join("?Name=B?Team=1");
        host.SwitchLevel("SteeE?GameMode=5");

        List<(PlayerController Target, GameCall Call)> travels = w.Host.Named("ClientTravel");
        Assert.Equal(2, travels.Count);
        Assert.Equal("SteeE?GameMode=5?Team=0?IdentifiedTravel", travels[0].Call.Text);
        Assert.Equal("SteeE?GameMode=5?Team=0?Team=1?IdentifiedTravel", travels[1].Call.Text);
        Assert.Equal((int)ETravelType.TRAVEL_Relative, travels[0].Call.Int);
        Assert.Single(w.Host.To(host, "PreClientTravel"));
        Assert.Equal(2, w.Level.Info.waitedClients);
        Assert.Equal("SteeE?GameMode=5?Team=1", w.Level.Info.NextURL);
        Assert.Same(a, travels[0].Target);
        Assert.Same(b, travels[1].Target);
    }

    [Fact]
    public void A_revenge_travel_switches_teams_when_its_options_parse_with_GamesInARow_below_2()
    {
        // The legacy restart URL starts with '?', so its options parse and GamesInARow 1 switches the teams.
        GameWorld w = new GameWorld().Load(Story + "?Revenge=true", MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=1", local: true);
        w.Join("?Name=A?Team=0");
        host.SwitchLevel("?GamesInARow=1");
        Assert.Equal("?GamesInARow=1?Team=1?IdentifiedTravel", w.Host.Named("ClientTravel")[0].Call.Text);
        Assert.Equal("?GamesInARow=1?Team=0", w.Level.Info.NextURL);

        // The first GamesInARow counts.
        GameWorld w2 = new GameWorld().Load(Story + "?Revenge=true", MatchLifecycleTests.TwoSides);
        SPlayerController host2 = w2.Join("?Name=H?Team=1", local: true);
        w2.Join("?Name=A?Team=0");
        host2.SwitchLevel("?GamesInARow=2?GamesInARow=1");
        Assert.Equal("?GamesInARow=2?GamesInARow=1?Team=0?IdentifiedTravel", w2.Host.Named("ClientTravel")[0].Call.Text);
    }

    [Fact]
    public void A_url_that_starts_with_the_map_name_never_switches_teams()
    {
        // GrabOption needs a leading '?': the menus' "Map?...?GamesInARow=1" parses as no options at all.
        GameWorld w = new GameWorld().Load(Story + "?Revenge=true", MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=1", local: true);
        w.Join("?Name=A?Team=0");
        host.SwitchLevel("SteeE?GamesInARow=1");
        Assert.Equal("SteeE?GamesInARow=1?Team=0?IdentifiedTravel", w.Host.Named("ClientTravel")[0].Call.Text);
        Assert.Equal("SteeE?GamesInARow=1?Team=1", w.Level.Info.NextURL);
    }

    [Fact]
    public void An_offline_travel_starts_at_once_and_a_client_cannot_switch_level()
    {
        GameWorld w = new GameWorld(ENetMode.NM_Standalone).Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController p = w.Join("?Name=S?Team=0", local: true);
        w.Level.Info.NextSwitchCountdown = 3f;
        p.SwitchLevel("Entry");
        Assert.Equal(0f, w.Level.Info.NextSwitchCountdown);

        var client = new GameWorld(ENetMode.NM_Client);
        var pc = client.Level.Spawn<SPlayerController>()!;
        pc.SwitchLevel("Elsewhere");
        Assert.Equal(string.Empty, client.Level.Info.NextURL);
    }

    [Fact]
    public void The_legacy_restart_carries_swapped_points_and_one_game_less()
    {
        GameWorld w = new GameWorld().Load(Story + "?Revenge=true?GamesInARow=2?ScoreATT=4?ScoreDEF=6", MatchLifecycleTests.TwoSides);
        w.Join("?Name=S?Team=0");
        w.Level.Info.NbPointsAttack = 11;
        w.Game.RestartGame();
        Assert.StartsWith("?ScoreDEF=11?ScoreATT=6?GamesInARow=1", w.Level.Info.NextURL);
    }

    [Fact]
    public void Lobby_and_entry_menus_are_sent_to_the_players()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        SPlayerController a = w.Join("?Name=A?Team=1");
        w.Gri.bMatchLaunching = true;
        w.Game.ForceMenuLobby();
        Assert.Single(w.Host.To(a, "CLientForceMenuLobby"));
        Assert.False(w.Gri.bMatchLaunching);
        Assert.Equal(new Name("BackToLobby"), w.Host.To(host, "ClientOpenMenu")[0].Name1);

        w.Game.ForceMenuClientEntry(host.PlayerReplicationInfo);
        Assert.Single(w.Host.To(a, "ClientForceMenuEntry"));
        Assert.Single(w.Host.To(host, "ClientOpenMenu"));
    }

    [Fact]
    public void Ready_flags_of_the_end_menus_reach_the_replication_info()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        SPlayerController a = w.Join("?Name=A?Team=1");
        a.ReadyToPlayAgain(true);
        Assert.True(a.PlayerReplicationInfo!.bReadyToPlayAgain);
        w.Game.AllPlayerReadyToPlayToServer(true);
        Assert.True(a.PlayerReplicationInfo!.bReadyToPlay);
        Assert.False(host.PlayerReplicationInfo!.bReadyToPlay);
    }
}
