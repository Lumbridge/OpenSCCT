using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/login-and-teams.md: capacity, Login, PickTeam, ChangeTeam, late joiners and Logout.</summary>
public class LoginAndTeamsTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    private static GameWorld Map(ENetMode mode = ENetMode.NM_ListenServer, string options = Story) =>
        new GameWorld(mode).Load(options, MatchLifecycleTests.TwoSides);

    // ---- capacity (rules 1 to 5) ------------------------------------------------------------------------------

    [Fact]
    public void AtCapacity_follows_the_net_mode_and_the_caps()
    {
        GameWorld standalone = Map(ENetMode.NM_Standalone);
        standalone.Game.NumPlayers = 99;
        Assert.False(standalone.Game.AtCapacity(false));

        GameWorld listen = Map();
        SGameInfo g = listen.Game;
        g.MaxPlayers = 2;
        g.NumPlayers = 2;
        Assert.True(g.AtCapacity(false));
        g.MaxPlayers = 0;
        Assert.False(g.AtCapacity(false));

        g.MaxSpectators = 2;
        g.NumSpectators = 2;
        g.NumPlayers = 0;
        Assert.False(g.AtCapacity(true)); // a listen server with no player yet always takes a spectator
        g.NumPlayers = 1;
        Assert.True(g.AtCapacity(true));
    }

    [Fact]
    public void A_full_server_refuses_the_login_with_the_capacity_message()
    {
        Assert.SkipUnless(UcText.HasText, "the game text (shared/classes.json) is not in .content");
        GameWorld w = Map();
        w.Game.MaxPlayers = 1;
        w.Join("?Name=A?Team=0");
        (PlayerController? pc, string error) = w.TryJoin("?Name=B?Team=1");
        Assert.Null(pc);
        Assert.Equal("Server is already at capacity.", error);
    }

    // ---- Login (rules 6 to 21) --------------------------------------------------------------------------------

    [Fact]
    public void Login_cuts_the_name_to_20_characters_and_numbers_players()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=ABCDEFGHIJKLMNOPQRSTUVWXYZ?Team=0");
        SPlayerController b = w.Join("?Name=Bob?Team=1");
        Assert.Equal("ABCDEFGHIJKLMNOPQRST", a.PlayerReplicationInfo!.PlayerName);
        Assert.Equal(0, a.PlayerReplicationInfo!.PlayerID);
        Assert.Equal(1, b.PlayerReplicationInfo!.PlayerID);
        Assert.Equal(2, w.Game.NumPlayers);
    }

    [Fact]
    public void The_team_option_sets_side_FOV_and_pawn_class()
    {
        GameWorld w = Map();
        SPlayerController spy = w.Join("?Name=S");
        SPlayerController merc = w.Join("?Name=M?Team=1");
        Assert.Same(w.Spies, spy.PlayerReplicationInfo!.Team);
        Assert.Equal(PawnType.SPAWNOFFENSE, spy.SPawnType);
        Assert.Equal(75f, spy.DefaultFOV);
        Assert.Equal(75f, spy.DesiredFOV);
        Assert.Equal(Classes.SpyPawn, spy.PawnClass);

        Assert.Same(w.Mercs, merc.PlayerReplicationInfo!.Team);
        Assert.Equal(PawnType.SPAWNDEFENSE, merc.SPawnType);
        Assert.Equal(95f, merc.DefaultFOV);
        Assert.Equal(Classes.MercPawn, merc.PawnClass);
    }

    [Fact]
    public void The_login_controller_stands_at_a_spy_start_whatever_the_team()
    {
        GameWorld w = Map();
        SPlayerController merc = w.Join("?Name=M?Team=1");
        Assert.Equal(0, merc.StartSpot is null ? 0 : 1); // ChangeTeam cleared the login start
        Assert.True(merc.Location.X < 1000f);
        Assert.Equal(0, merc.MyDesiredRotation.Yaw);
    }

    [Fact]
    public void A_map_without_any_start_refuses_every_playing_login()
    {
        Assert.SkipUnless(UcText.HasText, "the game text (shared/classes.json) is not in .content");
        GameWorld w = new GameWorld().Load(Story);
        (PlayerController? pc, string error) = w.TryJoin("?Name=M?Team=1");
        Assert.Null(pc);
        Assert.Equal("Could not find a starting spot.", error);
    }

    [Fact]
    public void Without_a_spy_start_the_login_uses_the_first_start_of_the_set()
    {
        // The distance pass rates a start of the other side 0, which still beats the initial -1.
        GameWorld w = new GameWorld().Load(Story, x => x.AddStart(new FVector(5000f, 0f, 0f), 1));
        SPlayerController pc = w.Join("?Name=M?Team=1");
        Assert.Equal(5000f, pc.Location.X);
    }

    [Fact]
    public void The_spy_camera_offsets_turn_the_login_rotation_and_wrap_once()
    {
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            PlayerStart s = x.AddStart(default, 0, yaw: 30000);
            s.iSpyCam_InitAngle_Hor = 5000;
            s.iSpyCam_InitAngle_Ver = -40000;
        });
        SPlayerController p = w.Join("?Name=A");
        Assert.Equal(35000 - 65536, p.Rotation.Yaw);
        Assert.Equal(-40000 + 65536, p.Rotation.Pitch);
        Assert.Equal(30000, p.MyDesiredRotation.Yaw);
    }

    [Theory]
    [InlineData("?Team=0", 0)]
    [InlineData("?Team=0?GamesInARow=1", 1)]
    [InlineData("?Team=1?GamesInARow=0", 0)]
    [InlineData("?Team=0?GamesInARow=2", 0)]
    public void The_second_game_of_a_revenge_pair_swaps_the_requested_team(string playerOptions, int expectedTeam)
    {
        GameWorld w = Map(options: Story + "?Revenge=true");
        SPlayerController p = w.Join("?Name=A" + playerOptions);
        Assert.Equal(expectedTeam, p.PlayerReplicationInfo!.Team!.TeamIndex);
    }

    [Fact]
    public void The_non_playing_host_is_a_far_away_visitor_on_no_team()
    {
        GameWorld w = Map();
        SPlayerController host = w.Join("?Name=Host?ServerOnly=true", local: true);
        Assert.Equal(new FVector(100000f, 100000f, 100000f), host.Location);
        Assert.True(host.PlayerReplicationInfo!.bOnlySpectator);
        Assert.True(host.PlayerReplicationInfo!.bIsSpectator);
        Assert.Null(host.PlayerReplicationInfo!.Team);
        Assert.True(host.IsInState("s_Spectating_Visitor"));
        Assert.Equal(1, w.Game.NumSpectators);
        Assert.Equal(0, w.Game.NumPlayers);
        Assert.True(w.Game.bWelcomePending);
    }

    [Fact]
    public void After_the_start_a_story_joiner_watches_until_the_next_map()
    {
        GameWorld w = Map();
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        w.StartNetworkMatch(host);
        SPlayerController late = w.Join("?Name=Late?Team=1");
        Assert.True(late.PlayerReplicationInfo!.bOnlySpectator);
        Assert.True(late.IsInState("s_Spectating_Visitor"));
        Assert.Null(late.Pawn);
    }

    [Fact]
    public void After_the_start_a_merc_deathmatch_joiner_waits_and_joins_with_merc_lives()
    {
        GameWorld w = Map(options: "?GameMode=7?LivesATT=0?LivesDEF=6");
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        w.StartNetworkMatch(host);
        SPlayerController late = w.Join("?Name=Late?Team=0");
        Assert.False(late.PlayerReplicationInfo!.bOnlySpectator);
        Assert.True(late.IsInState("s_PlayerWaiting"));
        Assert.Equal(1, late.PlayerReplicationInfo!.Team!.TeamIndex);

        late.PlayerWaitingToPlayToServer(true);
        Assert.NotNull(late.Pawn);
        Assert.Equal(6, late.PlayerReplicationInfo!.NumLives);
        Assert.Contains(w.Host.Named("resetConnectionPing"), c => ReferenceEquals(c.Target, late));
    }

    [Fact]
    public void A_mesh_out_of_range_keeps_the_previous_pawn_class()
    {
        GameWorld w = Map();
        SPlayerController p = w.Join("?Name=A?Team=0?Mesh=3");
        Assert.Equal(3, p.PlayerReplicationInfo!.MyMeshNum);
        Assert.True(p.PawnClass.IsNone);
    }

    [Fact]
    public void Play_level_hands_a_disconnected_player_his_living_pawn_back()
    {
        GameWorld w = Map(ENetMode.NM_Standalone, "?GameMode=5?Editeur=true");
        SPlayerController first = w.Join("?Name=Ed?Team=0?Editeur=true", local: true);
        Pawn pawn = first.Pawn!;
        first.Player = null;
        int controllers = w.Level.AllActors<SPlayerController>().Count();

        SPlayerController again = w.Join("?Name=ed?Team=0?Editeur=true", local: true);
        Assert.Same(first, again);
        Assert.Same(pawn, again.Pawn);
        Assert.Equal(1, w.Ports.Count("PlayWaiting"));
        Assert.Equal(controllers, w.Level.AllActors<SPlayerController>().Count());
    }

    // ---- PickTeam (rules 22 to 29) ----------------------------------------------------------------------------

    [Fact]
    public void PickTeam_honours_the_request_until_the_team_is_full()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=1");
        Assert.Equal(1, w.Game.PickTeam(1, a));
        w.Game.MaxTeamSize = 1;
        Assert.Equal(0, w.Game.PickTeam(1, a));
        Assert.Equal(0, w.Game.PickTeam(7, a));
    }

    [Fact]
    public void PickTeam_sends_players_to_the_smaller_team_once_the_match_is_launching()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=1");
        Assert.Equal(1, w.Game.PickTeam(1, a));
        w.Gri.bMatchLaunching = true;
        Assert.Equal(0, w.Game.PickTeam(1, a));
    }

    [Fact]
    public void Auto_balance_applies_only_on_the_lobby_map()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=1");
        w.Game.PersistentData.bAutoBalance = true;
        Assert.Equal(1, w.Game.PickTeam(1, a));
        Assert.False(w.Game.PersistentData.bAutoBalance);

        w.Host.URLMap = "Lobby";
        w.Game.PersistentData.bAutoBalance = true;
        Assert.Equal(0, w.Game.PickTeam(1, a));
        Assert.True(w.Game.bPlayersBalanceTeams);
    }

    [Fact]
    public void Merc_deathmatch_puts_everyone_on_the_mercs()
    {
        GameWorld w = Map(options: "?GameMode=7");
        SPlayerController a = w.Join("?Name=A?Team=0");
        Assert.Equal(1, a.PlayerReplicationInfo!.Team!.TeamIndex);
        Assert.Equal(1, w.Game.PickTeam(0, a));
    }

    // ---- ChangeTeam and TeamInfo (rules 30 to 35) -------------------------------------------------------------

    [Fact]
    public void ChangeTeam_moves_the_player_and_clears_its_start()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=0");
        a.StartSpot = w.Level.AllActors<PlayerStart>().ToList()[0];
        Assert.True(w.Game.eventChangeTeam(a, 1, true));
        Assert.Same(w.Mercs, a.PlayerReplicationInfo!.Team);
        Assert.Null(a.StartSpot);
        Assert.Equal(0, w.Spies.Size);
        Assert.Equal(1, w.Mercs.Size);
        Assert.False(w.Game.eventChangeTeam(a, 1, true));
        Assert.True(w.Ports.Count("UpdateVoiceChatMuting") >= 2);
    }

    [Fact]
    public void The_team_cap_holds_in_network_games_but_not_offline()
    {
        GameWorld net = Map();
        SPlayerController a = net.Join("?Name=A?Team=0");
        net.Game.MaxTeamSize = 0;
        Assert.False(net.Game.eventChangeTeam(a, 1, false));

        GameWorld offline = Map(ENetMode.NM_Standalone);
        SPlayerController b = offline.Join("?Name=B?Team=0", local: true);
        offline.Game.MaxTeamSize = 0;
        Assert.True(offline.Game.eventChangeTeam(b, 1, false));
    }

    [Fact]
    public void Team_ids_are_the_lowest_free_number_on_the_team()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=0");
        SPlayerController b = w.Join("?Name=B?Team=0");
        SPlayerController c = w.Join("?Name=C?Team=1");
        Assert.Equal(0, a.PlayerReplicationInfo!.TeamID);
        Assert.Equal(1, b.PlayerReplicationInfo!.TeamID);
        Assert.Equal(0, c.PlayerReplicationInfo!.TeamID);
    }

    [Fact]
    public void MoveEveryoneToMercs_passes_the_cap_and_restores_it()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=0");
        SPlayerController b = w.Join("?Name=B?Team=0");
        w.Game.MaxTeamSize = 1;
        w.Game.MoveEveryoneToMercs(true);
        Assert.Equal(1, a.PlayerReplicationInfo!.Team!.TeamIndex);
        Assert.Equal(1, b.PlayerReplicationInfo!.Team!.TeamIndex);
        Assert.Equal(1, w.Game.MaxTeamSize);
        Assert.Equal(2, w.Host.Named("ForceUnreadyOnClient").Count);
    }

    // ---- waiting and readiness (rules 36 to 41) ---------------------------------------------------------------

    [Fact]
    public void Readiness_and_only_spectator_flags_reach_the_server()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=0");
        a.PlayerReadyToPlayToServer(true);
        Assert.True(a.PlayerReplicationInfo!.bReadyToPlay);
        a.PlayerOnlySpectatorToServer(true);
        Assert.True(a.PlayerReplicationInfo!.bOnlySpectator);
        Assert.Equal(0, w.Game.NumPlayers);
        Assert.Equal(1, w.Game.NumSpectators);
        Assert.Equal(1, w.Spies.Size);
    }

    [Fact]
    public void A_waiting_player_marks_itself_ready_while_the_match_waits_and_restarts_otherwise()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=0");
        w.Game.bWaitingToStartMatch = true;
        a.eventServerReStartPlayer();
        Assert.True(a.PlayerReplicationInfo!.bReadyToPlay);
        Assert.Null(a.Pawn);
        w.Game.bWaitingToStartMatch = false;
        a.eventServerReStartPlayer();
        Assert.NotNull(a.Pawn);
    }

    // ---- Logout (rules 42 to 46) ------------------------------------------------------------------------------

    [Fact]
    public void A_playing_leaver_is_announced_once_and_still_counted_by_its_team()
    {
        GameWorld w = Map();
        SPlayerController a = w.Join("?Name=A?Team=0");
        SPlayerController b = w.Join("?Name=B?Team=1");
        w.Game.Logout(b);
        Assert.True(b.PlayerReplicationInfo!.bQuittedMatch);
        Assert.Equal(1, w.Game.NumPlayers);
        Assert.Equal(2, w.Host.Messages(4).Count); // one broadcast, to both players
        Assert.Equal(1, w.Mercs.Size);
        Assert.Contains(w.Ports.Voice, v => ReferenceEquals(v.Listener, a) && ReferenceEquals(v.Speaker, b) && v.Mute);
    }

    [Fact]
    public void A_spectator_leaver_is_announced_once_and_offline_leavers_not_at_all()
    {
        GameWorld w = Map();
        SPlayerController host = w.Join("?Name=Host?ServerOnly=true", local: true);
        w.Game.Logout(host);
        Assert.Equal(0, w.Game.NumSpectators);
        Assert.Single(w.Host.Messages(4));

        GameWorld offline = Map(ENetMode.NM_Standalone);
        SPlayerController solo = offline.Join("?Name=Solo?Team=0", local: true);
        offline.Game.Logout(solo);
        Assert.Empty(offline.Host.Messages(4));
    }

    [Fact]
    public void Leaving_the_lobby_map_runs_no_end_check()
    {
        GameWorld w = Map();
        w.Host.URLMap = "Lobby";
        SPlayerController a = w.Join("?Name=A?Team=0");
        w.Game.Logout(a);
        Assert.Empty(w.Host.Messages(4));
    }
}
