using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/respawn-and-spawning.md: RestartPlayer, possession, the start state and the spawn shock.</summary>
public class RespawnAndSpawningTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    private static GameWorld Ready(out PlayerStart spyStart, out PlayerStart mercStart, ENetMode mode = ENetMode.NM_ListenServer)
    {
        PlayerStart? s = null;
        PlayerStart? m = null;
        GameWorld w = new GameWorld(mode).Load(Story, x =>
        {
            s = x.AddStart(new FVector(0f, 0f, 100f), 0, yaw: 1000);
            m = x.AddStart(new FVector(8000f, 0f, 100f), 1, yaw: 2000);
        });

        // Players join before the match begins (a Story joiner after the start only watches).
        spyStart = s!;
        mercStart = m!;
        return w;
    }

    // ---- RestartPlayer (rules 1 to 12) -------------------------------------------------------------------------

    [Fact]
    public void The_pawn_spawns_79_uu_above_the_floor_found_by_the_box_probe()
    {
        GameWorld w = Ready(out PlayerStart start, out _);
        var floor = new FlatFloor(0f);
        w.Level.SetService<ICollisionWorld>(floor);
        SPlayerController p = w.Join("?Name=S?Team=0");
        w.Game.eventRestartPlayer(p);

        Assert.Equal(new FVector(0f, 0f, 10f + 79f), p.Pawn!.Location);
        (FVector traceStart, FVector traceEnd, FVector extent, ETraceFlags flags) = floor.Traces[^1];
        Assert.Equal(start.Location, traceStart);
        Assert.Equal(start.Location - new FVector(0f, 0f, 200f), traceEnd);
        Assert.Equal(new FVector(35f, 35f, 10f), extent);
        Assert.Equal(ETraceFlags.TRACE_ProjTargets, flags);
    }

    [Fact]
    public void Without_a_floor_under_the_start_the_pawn_spawns_at_the_start()
    {
        GameWorld w = Ready(out PlayerStart start, out _);
        w.Level.SetService<ICollisionWorld>(new FlatFloor(-5000f));
        SPlayerController p = w.Join("?Name=S?Team=0");
        w.Game.eventRestartPlayer(p);
        Assert.Equal(start.Location, p.Pawn!.Location);
        Assert.Equal(start.Rotation, p.Pawn.Rotation);
    }

    [Fact]
    public void Possession_links_pawn_controller_and_replication_info()
    {
        GameWorld w = Ready(out PlayerStart start, out _);
        SPlayerController p = w.Join("?Name=Sam?Team=0");
        w.Run(0.5f);
        w.Game.eventRestartPlayer(p);
        Pawn pawn = p.Pawn!;
        Assert.Same(p, pawn.Controller);
        Assert.Same(p.PlayerReplicationInfo, pawn.PlayerReplicationInfo);
        Assert.Equal("Sam", pawn.OwnerName);
        Assert.Same(p, pawn.Owner);
        Assert.Same(pawn, p.SBPawn);
        Assert.False(pawn.bStasis);
        Assert.Same(start, pawn.Anchor);
        Assert.Same(start, pawn.LastStartSpot);
        Assert.Equal(w.Level.TimeSeconds, pawn.LastStartTime);
        Assert.Same(start, p.StartSpot);
        Assert.Equal(Classes.SpyPawn, p.PawnClass);
        Assert.Equal(Classes.SpyPawn, p.PreviousPawnClass);
        Assert.Equal(ENetRole.ROLE_AutonomousProxy, pawn.RemoteRole);
        Assert.NotNull(p.IManager);
        Assert.Equal(1, w.Ports.Count("PlayTeleportEffect"));
        Assert.Equal(1, w.Ports.Count("AddDefaultInventory GM_Multi"));
    }

    [Fact]
    public void The_spawn_fires_the_start_event_and_plays_the_start_sound_to_its_owner()
    {
        EventProbe? probe = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            PlayerStart s = x.AddStart(default, 0);
            s.Event = "SpawnedHere";
            probe = x.Level.SpawnActor(new EventProbe { Tag = "SpawnedHere" }, default, default);
        });
        SPlayerController p = w.Join("?Name=S?Team=0");
        w.Game.eventRestartPlayer(p);
        Assert.Equal(1, probe!.Triggers);
        GameCall sound = Assert.Single(w.Host.To(p, "ClientPlaySound"));
        Assert.Equal("Interface.Menu.Menu_start_point", sound.Asset.Path);
    }

    [Fact]
    public void A_spy_gets_the_start_rotation_with_the_camera_offsets_and_a_merc_its_pawn_rotation()
    {
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            PlayerStart s = x.AddStart(default, 0, yaw: 30000);
            s.iSpyCam_InitAngle_Hor = 5000;
            s.iSpyCam_InitAngle_Ver = 1200;
            x.AddStart(new FVector(5000f, 0f, 0f), 1, yaw: 4000);
        });
        SPlayerController spy = w.Join("?Name=S?Team=0");
        SPlayerController merc = w.Join("?Name=M?Team=1");
        w.Game.eventRestartPlayer(spy);
        w.Game.eventRestartPlayer(merc);
        GameCall spyRot = Assert.Single(w.Host.To(spy, "ClientSetRotation"));
        Assert.Equal(new FRotator(1200, 35000 - 65536, 0), spyRot.Rotation);
        GameCall mercRot = Assert.Single(w.Host.To(merc, "ClientSetRotation"));
        Assert.Equal(new FRotator(0, 4000, 0), mercRot.Rotation);

        // Possess adds the offsets without wrapping, for both sides.
        Assert.Equal(new FRotator(1200, 35000, 0), spy.Rotation);
        Assert.Equal(spy.Rotation, spy.MyDesiredRotation);
    }

    [Fact]
    public void A_local_player_applies_its_client_rotation_itself()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        w.Game.eventRestartPlayer(host);
        Assert.Empty(w.Host.To(host, "ClientSetRotation"));
        Assert.Equal(new FRotator(0, 1000, 0), host.Rotation);
    }

    [Fact]
    public void A_spectator_or_a_player_without_a_start_is_not_spawned()
    {
        GameWorld w = new GameWorld().Load(Story);
        var pc = w.Level.Spawn<SPlayerController>()!;
        pc.PawnClass = Classes.SpyPawn;
        w.Game.eventRestartPlayer(pc);
        Assert.Null(pc.Pawn);

        GameWorld w2 = Ready(out _, out _);
        SPlayerController visitor = w2.Join("?Name=V?ServerOnly=true");
        visitor.PawnClass = Classes.SpyPawn;
        w2.Game.eventRestartPlayer(visitor);
        Assert.Null(visitor.Pawn);
    }

    [Fact]
    public void A_failed_spawn_makes_the_player_a_permanent_visitor()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController p = w.Join("?Name=S?Team=0");
        p.PawnClass = default;
        int players = w.Game.NumPlayers;
        w.Game.eventRestartPlayer(p);
        Assert.Null(p.Pawn);
        Assert.True(p.PlayerReplicationInfo!.bOnlySpectator);
        Assert.True(p.PlayerReplicationInfo!.bIsSpectator);
        Assert.True(p.IsInState("s_Spectating_Visitor"));
        Assert.Equal(1, w.Game.NumSpectators);
        Assert.Equal(players, w.Game.NumPlayers);
    }

    [Fact]
    public void The_match_over_state_spawns_nobody()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController p = w.Join("?Name=S?Team=0");
        w.Game.sEndGame();
        w.Game.eventRestartPlayer(p);
        Assert.Null(p.Pawn);
    }

    // ---- the spawn shock (rule 11) -----------------------------------------------------------------------------

    [Fact]
    public void A_new_pawn_sticky_shocks_enemies_close_by_but_not_below_or_teammates()
    {
        GameWorld w = Ready(out PlayerStart start, out _);
        DamageRecordingController Enemy(FVector at, int team)
        {
            var c = w.Level.Spawn<DamageRecordingController>()!;
            w.Game.eventChangeTeam(c, team, false);
            c.Pawn = w.SpawnPawn(team == 0, start.Location + at);
            return c;
        }

        DamageRecordingController above = Enemy(new FVector(200f, 0f, 30f), 1);
        DamageRecordingController aboveFar = Enemy(new FVector(0f, 225f, 30f), 1);
        DamageRecordingController level = Enemy(new FVector(165f, 0f, 20f), 1);
        DamageRecordingController levelFar = Enemy(new FVector(171f, 0f, 0f), 1);
        DamageRecordingController below = Enemy(new FVector(10f, 0f, -21f), 1);
        DamageRecordingController mate = Enemy(new FVector(10f, 0f, 0f), 0);

        SPlayerController spy = w.Join("?Name=S?Team=0");
        w.Game.eventRestartPlayer(spy);
        Pawn spawned = spy.Pawn!;

        var hit = Assert.Single(above.Hits);
        Assert.Equal(0, hit.Damage);
        Assert.Same(spawned, hit.By);
        Assert.Equal(above.Pawn!.Location, hit.HitLocation);
        Assert.Equal(above.Pawn.Location - spawned.Location, hit.Momentum);
        Assert.Equal(SDamageType.SDTStickyShocker, hit.Type);
        Assert.Equal(-10, hit.Pill);
        Assert.Single(level.Hits);
        Assert.Empty(aboveFar.Hits);
        Assert.Empty(levelFar.Hits);
        Assert.Empty(below.Hits);
        Assert.Empty(mate.Hits);
    }

    // ---- possession, restart and the start state (rules 13 to 23) ---------------------------------------------

    [Fact]
    public void The_start_state_gives_spies_explosion_protection_their_FOV_and_the_crouched_start()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController spy = w.Join("?Name=S?Team=0");
        w.Run(1f);
        w.Game.eventRestartPlayer(spy);
        Assert.Equal(w.Level.TimeSeconds + 3f, spy.fSpawnProtectionEnd);
        Assert.Equal(75f, spy.DefaultFOV);
        Assert.Equal(75f, spy.DesiredFOV);
        Assert.Equal(75f, spy.FovAngle);
        Assert.True(spy.bCrouchWithRoll);
        Assert.Equal((spy, true), Assert.Single(w.Ports.StartStates));
        GameCall mirror = Assert.Single(w.Host.To(spy, "PCChangeClientState"));
        Assert.Equal(new Name("s_PlayerWalkingCr0H"), mirror.Name1);
        Assert.Equal(new FVector(-10000000f, 0f, -10000000f), spy.SBPawn!.LastZipP);
    }

    [Fact]
    public void The_start_state_of_a_merc_is_standing_with_its_FOV()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController merc = w.Join("?Name=M?Team=1");
        merc.bNoReticule = true;
        w.Game.eventRestartPlayer(merc);
        Assert.Equal(95f, merc.DefaultFOV);
        Assert.False(merc.bNoReticule);
        Assert.Equal((merc, false), Assert.Single(w.Ports.StartStates));
        Assert.Equal(new Name("s_PlayerWalkingSt2H"), Assert.Single(w.Host.To(merc, "PCChangeClientState")).Name1);
    }

    [Fact]
    public void A_remote_player_restarts_on_its_machine_through_the_host_and_a_local_one_here()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController remote = w.Join("?Name=R?Team=0");
        w.Game.eventRestartPlayer(remote);
        Assert.Single(w.Host.To(remote, "ClientRestart"));
        Assert.Single(w.Ports.StartStates);

        SPlayerController host = w.Join("?Name=H?Team=1", local: true);
        w.Game.eventRestartPlayer(host);
        Assert.Empty(w.Host.To(host, "ClientRestart"));
        Assert.Empty(w.Host.To(host, "PCChangeClientState"));
        Assert.Equal(3, w.Ports.StartStates.Count); // Restart, then ClientRestart run here
        Assert.Same(host.Pawn, host.ViewTarget);
    }

    [Fact]
    public void A_client_without_its_pawn_waits_for_it_and_takes_it_when_it_arrives()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController p = w.Join("?Name=H?Team=0", local: true);
        p.ClientRestart();
        Assert.True(p.IsInState("WaitingForPawn"));
        Assert.Equal(0.2f, p.TimerRate);
        w.Run(1f);
        Assert.Null(p.Pawn);

        SBasePawn pawn = w.SpawnPawn(true, default);
        p.Pawn = pawn;
        p.eventPlayerTick(Sim.DeltaTime);
        Assert.Same(p, pawn.Controller);
        Assert.True(pawn.bUpdateEyeheight);
        Assert.False(p.IsInState("WaitingForPawn"));
        Assert.Equal(0f, p.TimerRate);
        Assert.False(p.bBehindView);
    }

    [Fact]
    public void AskForPawn_hands_the_existing_pawn_back_or_restarts_the_player()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController p = w.Join("?Name=H?Team=0", local: true);
        w.Game.eventRestartPlayer(p);
        Pawn pawn = p.Pawn!;
        int starts = w.Ports.StartStates.Count;
        p.AskForPawn();
        Assert.Same(pawn, p.Pawn);
        Assert.Equal(starts + 1, w.Ports.StartStates.Count);

        SPlayerController r = w.Join("?Name=R?Team=1");
        w.Game.eventRestartPlayer(r);
        r.AskForPawn();
        Assert.Single(w.Host.To(r, "GivePawn"));
    }

    [Fact]
    public void UnPossess_destroys_the_pawn_and_clears_the_links()
    {
        GameWorld w = Ready(out _, out _);
        SPlayerController p = w.Join("?Name=S?Team=0");
        w.Game.eventRestartPlayer(p);
        Pawn pawn = p.Pawn!;
        p.UnPossess();
        Assert.True(pawn.bDeleteMe);
        Assert.Null(p.Pawn);
        Assert.Null(p.SBPawn);
        Assert.Null(pawn.Controller);
        Assert.Equal(pawn.Location, p.Location);
    }
}
