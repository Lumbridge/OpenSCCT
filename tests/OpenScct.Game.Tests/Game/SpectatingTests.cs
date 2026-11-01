using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/spectating.md: the spectating states, target choice and the spectator view rotation.</summary>
public class SpectatingTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    [Theory]
    [InlineData("s_MultiSpectating", true)]
    [InlineData("s_MultiSpectating_WhenKilled", true)]
    [InlineData("s_Spectating_Visitor", true)]
    [InlineData("s_Dead", false)]
    [InlineData("s_PlayerWalkingCr0H", false)]
    public void RealControllerStatesExposeTheSpectatorStatusUsedByTheHud(string state, bool expected)
    {
        GameWorld world = new GameWorld().Load(Story);
        var pc = world.Level.Spawn<SPlayerController>()!;
        pc.GotoState(state);
        Assert.Equal(expected, pc.IsSpectating());
    }

    private static (GameWorld W, SPlayerController[] P) Match(params string[] joins) =>
        DeathLivesRespawnTests.Match(Story, joins);

    // ---- s_BaseSpectating (rules 1 to 3) ------------------------------------------------------------------------

    [Fact]
    public void Every_new_controller_starts_in_s_BaseSpectating_with_world_collision()
    {
        GameWorld w = new GameWorld().Load(Story);
        var pc = w.Level.Spawn<SPlayerController>()!;
        Assert.True(pc.IsInState("s_BaseSpectating"));
        Assert.True(pc.bCollideWorld);
    }

    [Fact]
    public void An_only_spectator_moves_on_to_the_visitor_state()
    {
        GameWorld w = new GameWorld().Load(Story);
        var pc = w.Level.Spawn<SPlayerController>()!;
        pc.eventPlayerTick(Sim.DeltaTime);
        Assert.True(pc.IsInState("s_BaseSpectating"));
        pc.PlayerReplicationInfo!.bOnlySpectator = true;
        pc.eventPlayerTick(Sim.DeltaTime);
        Assert.True(pc.IsInState("s_Spectating_Visitor"));
    }

    [Fact]
    public void Leaving_spectating_drops_the_target_and_world_collision()
    {
        GameWorld w = new GameWorld().Load(Story);
        var pc = w.Level.Spawn<SPlayerController>()!;
        pc.SpecTarget = w.SpawnPawn(true, default);
        pc.bForceReplicationForSpecTarget = false;
        pc.GotoState("s_Dead");
        Assert.Null(pc.SpecTarget);
        Assert.False(pc.bCollideWorld);
        Assert.True(pc.bForceReplicationForSpecTarget);
    }

    // ---- the spectator view (rules 8 and 9) ---------------------------------------------------------------------

    [Theory]
    [InlineData(false, false, 1f, 1f, 0, 33)]
    [InlineData(true, false, 1f, 1f, 0, -33)]
    [InlineData(false, true, 1f, 1f, 0, 33)]
    public void The_look_rate_is_50_per_unit_of_input_per_second_truncated(bool invertSpyYaw, bool invertPitch,
        float turn, float lookUp, int startPitch, int expectedYaw)
    {
        GameWorld w = new GameWorld().Load(Story);
        var pc = w.Level.Spawn<SPlayerController>()!;
        pc.GotoState("s_MultiSpectating");
        pc.Rotation = new FRotator(startPitch, 0, 0);
        pc.SPawnType = PawnType.SPAWNOFFENSE;
        pc.bInvertHorizontalAxisCamSpy = invertSpyYaw;
        pc.bInvertVerticalAxis = invertPitch;
        pc.aTurn = turn * 40f;
        pc.aLookUp = lookUp * 40f;
        pc.eventPlayerTick(1f / 60f);
        Assert.Equal(expectedYaw, pc.Rotation.Yaw);
        Assert.Equal(invertPitch ? -33 : 33, pc.Rotation.Pitch);
    }

    [Fact]
    public void Spectator_pitch_is_clamped_to_15000()
    {
        GameWorld w = new GameWorld().Load(Story);
        var pc = w.Level.Spawn<SPlayerController>()!;
        pc.GotoState("s_MultiSpectating");
        pc.Rotation = new FRotator(14990, 0, 0);
        pc.aLookUp = 6000f;
        pc.eventPlayerTick(1f / 60f);
        Assert.Equal(15000, pc.Rotation.Pitch);
        pc.Rotation = new FRotator(-14990 + 65536, 0, 0);
        pc.aLookUp = -6000f;
        pc.eventPlayerTick(1f / 60f);
        Assert.Equal(-15000, pc.Rotation.Pitch);
    }

    // ---- dead players watch teammates (rules 11 to 18) ----------------------------------------------------------

    [Fact]
    public void A_dead_player_watches_a_living_teammate_and_its_body_goes()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S1?Team=0", "?Name=M?Team=1", "?Name=S2?Team=0");
        SPlayerController dead = p[0];
        Pawn body = dead.Pawn!;
        dead.GotoState("s_MultiSpectating_WhenKilled");
        Assert.Same(p[2], dead.PC_spectated);
        Assert.Same(p[2].SBPawn, dead.SpecTarget);
        Assert.Same(p[2].SBPawn, dead.ViewTarget);
        Assert.True(body.bDeleteMe);
        Assert.Single(w.Host.To(dead, "ClientSetViewTarget"));
    }

    [Fact]
    public void Without_a_living_teammate_the_dead_player_keeps_its_own_view()
    {
        (GameWorld _, SPlayerController[] p) = Match("?Name=S1?Team=0", "?Name=M?Team=1");
        SPlayerController dead = p[0];
        Pawn body = dead.Pawn!;
        dead.GotoState("s_MultiSpectating_WhenKilled");
        Assert.Null(dead.SpecTarget);
        Assert.False(body.bDeleteMe);
    }

    [Fact]
    public void Fire_cycles_to_the_next_teammate_after_the_current_one()
    {
        (GameWorld _, SPlayerController[] p) = Match("?Name=S1?Team=0", "?Name=S2?Team=0", "?Name=S3?Team=0", "?Name=M?Team=1");
        SPlayerController dead = p[0];
        dead.GotoState("s_MultiSpectating_WhenKilled");
        SPlayerController first = (SPlayerController)dead.PC_spectated!;
        var state = (SPlayerController.s_MultiSpectating_WhenKilled)dead.StateNode;
        state.Fire(dead, 0f);
        SPlayerController second = (SPlayerController)dead.PC_spectated!;
        Assert.NotSame(first, second);
        Assert.Equal(PawnType.SPAWNOFFENSE, second.SPawnType);
        state.Fire(dead, 0f);
        Assert.Same(first, dead.PC_spectated);
    }

    [Fact]
    public void The_Fire_key_reaches_the_spectating_state()
    {
        (GameWorld _, SPlayerController[] p) = Match("?Name=S1?Team=0", "?Name=S2?Team=0", "?Name=S3?Team=0", "?Name=M?Team=1");
        SPlayerController dead = p[0];
        dead.GotoState("s_MultiSpectating_WhenKilled");
        PlayerController? first = dead.PC_spectated;
        dead.eventFire(1f);
        Assert.NotSame(first, dead.PC_spectated);

        // The plain spectating states ignore it.
        SPlayerController watcher = p[1];
        watcher.GotoState("s_MultiSpectating");
        watcher.eventFire(1f);
        watcher.eventAltFire(1f);
        Assert.Null(watcher.PC_spectated);
    }

    [Fact]
    public void The_target_is_checked_every_second_and_replaced_when_its_owner_dies()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S1?Team=0", "?Name=S2?Team=0", "?Name=S3?Team=0", "?Name=M?Team=1");
        SPlayerController dead = p[0];
        dead.GotoState("s_MultiSpectating_WhenKilled");
        var watched = (SPlayerController)dead.PC_spectated!;
        watched.GotoState("s_MultiSpectating_WhenKilled");
        Assert.Null(dead.SpecTarget); // the watched body left: its watchers lose it

        w.RunWithPlayerTick(1.1f, dead);
        Assert.NotSame(watched, dead.PC_spectated);
        Assert.NotNull(dead.SpecTarget);
    }

    [Fact]
    public void Spectators_of_a_body_that_starts_spectating_lose_it()
    {
        (GameWorld _, SPlayerController[] p) = Match("?Name=S1?Team=0", "?Name=S2?Team=0", "?Name=M?Team=1");
        SPlayerController watcher = p[1];
        SPlayerController dead = p[0];
        watcher.SpecTarget = dead.SBPawn;
        dead.GotoState("s_MultiSpectating_WhenKilled");
        Assert.Null(watcher.SpecTarget);
        Assert.Same(watcher, watcher.PC_spectated);
        Assert.True(watcher.bForceReplicationForSpecTarget);
    }

    [Fact]
    public void Server_FindNextFriend_does_nothing_outside_the_killed_spectating_state()
    {
        (GameWorld _, SPlayerController[] p) = Match("?Name=S1?Team=0", "?Name=S2?Team=0", "?Name=M?Team=1");
        p[0].Server_FindNextFriend(false);
        Assert.Null(p[0].PC_spectated);
    }

    // ---- visitors (rules 19 to 22) ------------------------------------------------------------------------------

    [Fact]
    public void A_visitor_watches_players_of_either_side_and_is_muted_for_everyone()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController visitor = w.Join("?Name=V?Team=0");
        Assert.True(visitor.IsInState("s_Spectating_Visitor"));
        Assert.NotNull(visitor.SpecTarget);
        Assert.Contains(w.Ports.Voice, v => ReferenceEquals(v.Speaker, visitor) && v.Mute);

        var seen = new System.Collections.Generic.HashSet<PlayerController>();
        var state = (SPlayerController.s_Spectating_Visitor)visitor.StateNode;
        for (int i = 0; i < 4; i++)
        {
            seen.Add(visitor.PC_spectated!);
            state.Fire(visitor, 0f);
        }

        Assert.Contains(p[0], seen);
        Assert.Contains(p[1], seen);
    }

    [Fact]
    public void The_visitor_target_is_rechecked_every_second()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController visitor = w.Join("?Name=V?Team=0");
        var watched = (SPlayerController)visitor.PC_spectated!;
        watched.UnPossess();
        w.RunWithPlayerTick(1.1f, visitor);
        Assert.NotSame(watched, visitor.PC_spectated);
    }

    [Fact]
    public void The_non_playing_host_in_spying_mode_returns_to_its_menu_when_the_target_is_lost()
    {
        (GameWorld w, SPlayerController[] p) = Match("?Name=S?Team=0", "?Name=M?Team=1");
        w.Host.DedicatedServerMode = true;
        SPlayerController host = w.Join("?Name=Host?ServerOnly=true", local: true);
        Assert.Null(host.SpecTarget);
        host.bDedicatedServerinSpyingMode = true;
        w.RunWithPlayerTick(1.1f, host);
        Assert.False(host.bDedicatedServerinSpyingMode);
        Assert.Single(w.Host.To(host, "ShowMenu"));
    }

    [Fact]
    public void More_than_twelve_controllers_overflow_the_candidate_list()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        for (int i = 0; i < 12; i++)
        {
            w.Level.Spawn<SPlayerController>();
        }

        SPlayerController s = w.Join("?Name=S?Team=0");
        w.StartNetworkMatch(s);
        Assert.NotNull(s.Pawn);
        SPlayerController visitor = w.Join("?Name=V?Team=0");
        Assert.True(visitor.IsInState("s_Spectating_Visitor"));
        Assert.Null(visitor.SpecTarget); // the only player with a pawn is the 13th candidate, which is dropped
    }
}
