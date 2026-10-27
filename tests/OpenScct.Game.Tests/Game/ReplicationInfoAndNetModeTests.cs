using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/replication-info.md and spec/game/netmode-branches.md.</summary>
public class ReplicationInfoAndNetModeTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    // ---- the GRI clock (rules 1 to 5) ---------------------------------------------------------------------------

    [Fact]
    public void Before_the_start_the_clock_counts_elapsed_time_but_not_the_remaining_time()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        w.Run(5.5f);
        Assert.InRange(w.Gri.ElapsedTime, 4, 5);
        Assert.Equal(0, w.Gri.RemainingTime);
        Assert.True(w.Gri.bStopCountDown);
    }

    [Fact]
    public void Reset_seeds_the_countdown_and_each_counted_second_moves_both_clocks()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        w.Run(2.5f);
        GameReplicationInfo gri = w.Gri;
        gri.ElapsedTime = 9;
        gri.Reset();
        float now = w.Level.TimeSeconds;
        Assert.Equal(now, gri.SecondCount);
        Assert.Equal(0, gri.ElapsedTime);
        Assert.Equal(600, gri.RemainingMinute);
        Assert.False(gri.bStopCountDown);
        Assert.Equal(1f, gri.TimerRate);

        gri.eventTimer();
        Assert.Equal(0, gri.ElapsedTime); // less than a game second since the reset

        gri.SecondCount = now - 1f;
        gri.eventTimer();
        Assert.Equal(1, gri.ElapsedTime);
        Assert.Equal(599, gri.RemainingTime);
        Assert.Equal(0, gri.RemainingMinute);
        Assert.Equal(now, gri.SecondCount);
    }

    [Fact]
    public void The_clock_counts_at_most_one_second_per_timer_call()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        GameReplicationInfo gri = w.Gri;
        gri.Reset();
        gri.SecondCount = w.Level.TimeSeconds - 5f;
        gri.eventTimer();
        Assert.Equal(1, gri.ElapsedTime);
        Assert.Equal(w.Level.TimeSeconds - 4f, gri.SecondCount);
    }

    [Fact]
    public void In_a_running_match_the_remaining_time_is_the_limit_minus_the_elapsed_time()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController s = w.Join("?Name=S?Team=0");
        w.StartNetworkMatch(s);
        w.Run(10.5f);
        Assert.InRange(w.Gri.ElapsedTime, 9, 10);
        Assert.Equal(600 - w.Gri.ElapsedTime, w.Gri.RemainingTime);
    }

    [Fact]
    public void Without_a_time_limit_the_remaining_time_goes_negative_and_a_stopped_clock_freezes_it()
    {
        GameWorld w = new GameWorld().Load("?GameMode=5", MatchLifecycleTests.TwoSides);
        SPlayerController s = w.Join("?Name=S?Team=0");
        w.StartNetworkMatch(s);
        w.Run(3.5f);
        Assert.True(w.Gri.RemainingTime < 0);
        Assert.Equal(-w.Gri.ElapsedTime, w.Gri.RemainingTime);
        int frozen = w.Gri.RemainingTime;
        int elapsed = w.Gri.ElapsedTime;
        w.Gri.bStopCountDown = true;
        w.Run(2f);
        Assert.Equal(frozen, w.Gri.RemainingTime);
        Assert.Equal(elapsed + 2, w.Gri.ElapsedTime);
    }

    [Fact]
    public void PreciseElapsedTime_adds_the_fraction_of_the_current_second()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        w.Run(1.5f);
        w.Gri.ElapsedTime = 7;
        w.Gri.SecondCount = w.Level.TimeSeconds - 0.25f;
        Assert.Equal(7.25f, w.Gri.PreciseElapsedTime(), 4);
    }

    [Fact]
    public void A_client_copy_blanks_the_server_strings_and_never_counts()
    {
        var w = new GameWorld(ENetMode.NM_Client);
        var gri = new GameReplicationInfo { Role = ENetRole.ROLE_SimulatedProxy, ServerName = "X", AdminName = "Y" };
        w.Level.SpawnActor(gri, default, default);
        Assert.Equal(string.Empty, gri.ServerName);
        Assert.Equal(string.Empty, gri.AdminName);
        w.Run(2.1f);
        Assert.Equal(0, gri.ElapsedTime);
    }

    // ---- the player list (rule 6) and the ability switches (rule 7) --------------------------------------------

    [Fact]
    public void Replication_infos_join_and_leave_the_player_list()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController a = w.Join("?Name=A?Team=0");
        SPlayerController b = w.Join("?Name=B?Team=1");
        Assert.Equal(new[] { a.PlayerReplicationInfo, b.PlayerReplicationInfo }, w.Gri.PRIArray);
        a.PlayerReplicationInfo!.Destroy();
        Assert.Equal(new[] { b.PlayerReplicationInfo }, w.Gri.PRIArray);
        w.Gri.RemovePRI(a.PlayerReplicationInfo!);
        Assert.Single(w.Gri.PRIArray);

        var copy = new List<PlayerReplicationInfo> { a.PlayerReplicationInfo! };
        w.Gri.GetPRIArray(copy);
        Assert.Equal(new[] { b.PlayerReplicationInfo }, copy);
    }

    [Fact]
    public void SetBridage_toggles_one_ability_switch()
    {
        var gri = new GameReplicationInfo();
        gri.SetBridage(1);
        Assert.True(gri.bAvlb_SPY_NightVision);
        gri.SetBridage(1);
        Assert.False(gri.bAvlb_SPY_NightVision);
        gri.SetBridage(12);
        Assert.True(gri.bAvlb_MERC_Charge);
        gri.SetBridage(13);
    }

    // ---- PRI (rules 8 to 13) ------------------------------------------------------------------------------------

    [Fact]
    public void A_new_replication_info_has_statistics_its_join_time_and_a_location_timer()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        w.Run(4.05f);
        SPlayerController a = w.Join("?Name=A?Team=0");
        PlayerReplicationInfo pri = a.PlayerReplicationInfo!;
        Assert.NotNull(pri.Stats);
        Assert.True(pri.StartTime > 0);
        Assert.Equal(w.Gri.ElapsedTime, pri.StartTime);
        Assert.InRange(pri.TimerRate, 1.5f, 2.5f);
        Assert.True(pri.bTimerLoop);
        Assert.Same(a, pri.Owner);
    }

    [Fact]
    public void The_replication_info_timer_refreshes_the_ping_about_a_third_of_the_time()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController a = w.Join("?Name=A?Team=0");
        w.Run(120f);
        Assert.Equal(42, a.PlayerReplicationInfo!.Ping);

        SPlayerController b = w.Join("?Name=B?Team=1");
        b.PlayerReplicationInfo!.bReceivedPing = true;
        w.Run(120f);
        Assert.Equal(0, b.PlayerReplicationInfo!.Ping);
    }

    [Fact]
    public void Reset_and_the_small_setters_change_only_their_fields()
    {
        var pri = new PlayerReplicationInfo { Score = 5f, Deaths = 2, NumLives = 3, bReadyToPlay = true, PlayerName = "Old" };
        pri.Reset();
        Assert.Equal(0f, pri.Score);
        Assert.Equal(0, pri.Deaths);
        Assert.Equal(0, pri.NumLives);
        Assert.False(pri.bReadyToPlay);
        pri.SetWaitingPlayer(true);
        Assert.True(pri.bIsSpectator);
        Assert.True(pri.bWaitingPlayer);
        pri.SetPlayerName("New");
        Assert.Equal("Old", pri.OldName);
        Assert.Equal("New", pri.PlayerName);
        pri.playerQuitMatch();
        Assert.True(pri.bQuittedMatch);
    }

    [Fact]
    public void A_team_never_takes_a_messaging_spectator_and_RemoveFromTeam_only_changes_the_size()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController a = w.Join("?Name=A?Team=0");
        Assert.False(w.Spies.AddToTeam(null));
        w.Spies.RemoveFromTeam(a);
        Assert.Equal(0, w.Spies.Size);
        Assert.Same(w.Spies, a.PlayerReplicationInfo!.Team);
    }

    // ---- NetMode branches and state mirroring (rules 1 to 4, 32) ------------------------------------------------

    [Fact]
    public void IAmOnClient_is_true_for_the_player_this_machine_controls()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        SPlayerController remote = w.Join("?Name=R?Team=1");
        Assert.True(host.IAmOnClient());
        Assert.False(remote.IAmOnClient());
    }

    [Fact]
    public void MirrorState_tells_a_remote_client_with_a_refreshed_acknowledgement_time()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        SPlayerController remote = w.Join("?Name=R?Team=1");
        w.Run(2f);
        host.eventMirrorState("s_Dead");
        remote.eventMirrorState("s_Dead");
        Assert.True(host.IsInState("s_Dead"));
        Assert.True(remote.IsInState("s_Dead"));
        Assert.Empty(w.Host.To(host, "PCChangeClientState"));
        GameCall call = Assert.Single(w.Host.To(remote, "PCChangeClientState"));
        Assert.Equal(w.Level.TimeSeconds, call.Float1);
        Assert.Equal(w.Level.TimeSeconds, call.Float2);
        Assert.Equal(new Name("s_Dead"), call.Name1);

        w.Run(0.5f);
        remote.eventMirrorState("s_WaitRespawn");
        Assert.Equal(call.Float1, w.Host.To(remote, "PCChangeClientState")[1].Float1);
    }

    [Fact]
    public void PCChangeClientState_ignores_orders_older_than_the_last_one()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController p = w.Join("?Name=P?Team=0");
        p.PCChangeClientState(1f, 5f, "s_Dead");
        Assert.True(p.IsInState("s_Dead"));
        p.PCChangeClientState(1f, 4f, "s_WaitRespawn");
        Assert.True(p.IsInState("s_Dead"));
        Assert.Equal(5f, p.MirrorStateLastTime);
    }

    [Fact]
    public void ChangeState_asks_the_server_only_from_a_client()
    {
        var client = new GameWorld(ENetMode.NM_Client);
        var pc = client.Level.Spawn<SPlayerController>()!;
        pc.eventChangeState("s_Dead");
        Assert.True(pc.IsInState("s_Dead"));
        Assert.Single(client.Host.ServerCalls);

        GameWorld listen = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController host = listen.Join("?Name=H?Team=0", local: true);
        host.eventChangeState("s_Dead");
        Assert.Empty(listen.Host.ServerCalls);
    }

    [Fact]
    public void PCChangeServerState_follows_only_a_matching_acknowledgement()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController p = w.Join("?Name=P?Team=0");
        p.MirrorStateAcknowledgementTime = 3f;
        p.PCChangeServerState(2.5f, "s_Dead");
        Assert.False(p.IsInState("s_Dead"));
        p.PCChangeServerState(3.005f, "s_Dead");
        Assert.True(p.IsInState("s_Dead"));
    }

    [Fact]
    public void A_controller_state_change_remembers_the_previous_state_unless_asked_not_to()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController p = w.Join("?Name=P?Team=0");
        p.GotoState("s_Dead");
        Assert.Equal(new Name("s_PlayerWaiting"), p.PreviousState);
        p.GotoState("s_WaitRespawn", default, true);
        Assert.Equal(new Name("s_PlayerWaiting"), p.PreviousState);
    }

    // ---- ports keep their manifest -------------------------------------------------------------------------------

    [Fact]
    public void Every_game_flow_port_names_its_unit_with_the_right_bytecode_hash()
    {
        var index = new UcClassIndex(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);
        IReadOnlyList<UcManifestCheck.Problem> problems = UcManifestCheck.Check(index, typeof(Actor).Assembly, typeof(SGameInfo).Assembly);
        string[] mine =
        [
            "OpenScct.Game.SBase.SGameInfo", "OpenScct.Game.SBase.SPlayerController", "OpenScct.Game.SBase.USMatchInfo",
            "OpenScct.Game.SBase.USMapSettings", "OpenScct.Game.SBase.STriggerPlayerStart", "OpenScct.Core.Engine.GameInfo",
            "OpenScct.Core.Engine.GameReplicationInfo", "OpenScct.Core.Engine.PlayerReplicationInfo", "OpenScct.Core.Engine.TeamInfo",
            "OpenScct.Core.Engine.LevelInfo", "OpenScct.Core.Engine.Controller", "OpenScct.Core.Engine.PlayerController", "OpenScct.Core.Engine.Pawn",
        ];
        var bad = problems.Where(p => mine.Any(m => p.Member.StartsWith(m + ".", StringComparison.Ordinal) ||
            p.Member.StartsWith(m + "+", StringComparison.Ordinal))).ToList();
        Assert.Empty(bad);
    }
}
