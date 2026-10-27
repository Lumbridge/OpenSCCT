using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Modes;

/// <summary>spec/objectives/bomb-target.md: planting, the fuse, defusing, the blast and the decoded natives.</summary>
public class BombTargetTests
{
    private static readonly Name s_PlaceBomb = new("s_PlaceBomb");
    private static readonly Name s_PlaceBomb_End = new("s_PlaceBomb_End");
    private static readonly Name s_TemporaryInactive = new("s_TemporaryInactive");
    private static readonly Name s_Initial = new("s_Initial");

    private sealed class Scene
    {
        public GameWorld W = null!;
        public SBombTargetObjectiveTrigger A = null!;
        public SBombTargetObjectiveTrigger B = null!;
        public SObjective Objective = null!;
        public SMission Mission = null!;
        public SPlayerController Spy = null!;
        public SPlayerController Merc = null!;
        public SGadBomb Modem = null!;

        public SBombTargetInteraction EntryA => (SBombTargetInteraction)A.InteractionObj!;
    }

    /// <summary>A Story match on a level with one bomb objective of two spots; the host spy carries a modem.</summary>
    private static Scene Story(string options = "?GameMode=5?TimeLimit=10?LivesATT=4?LivesDEF=3")
    {
        var s = new Scene();
        s.W = new GameWorld().Load(options, x =>
        {
            MatchLifecycleTests.TwoSides(x);
            s.A = x.Level.SpawnActor(new SBombTargetObjectiveTrigger { CaptureTime = 30 }, new FVector(100f, 1000f, 0f), default)!;
            s.B = x.Level.SpawnActor(new SBombTargetObjectiveTrigger { CaptureTime = 30 }, new FVector(-100f, 1000f, 0f), default)!;
            s.Objective = x.Level.SpawnActor(new SObjective(), default, default)!;
            s.Objective.Triggers.Add(s.A);
            s.Objective.Triggers.Add(s.B);
            s.Mission = x.Level.SpawnActor(new SMission { GameMode = EGameMode.GM_Multi, bChained = false }, default, default)!;
            s.Mission.Objectives.Add(s.Objective);
        });
        s.Spy = s.W.Join("?Name=S?Team=0", local: true);
        s.Merc = s.W.Join("?Name=M?Team=1");
        s.W.StartNetworkMatch(s.Spy);
        s.W.Gri.bMatchHasBegun = true;
        var pawn = (SBasePawn)s.Spy.Pawn!;
        pawn.Physics = EPhysics.PHYS_Walking;
        s.Modem = s.W.Level.SpawnActor(new SGadBomb(), pawn.Location, default)!;
        s.Modem.GiveTo(pawn);
        s.W.Run(5f);
        return s;
    }

    /// <summary>The spy plants at <paramref name="spot"/>: the menu use, then the end of the plant animation.</summary>
    private static void Plant(Scene s, SBombTargetObjectiveTrigger spot)
    {
        spot.InteractionObj!.eventInitInteract(s.Spy, 0);
        Assert.True(s.Spy.IsInState(s_PlaceBomb));
        s.Spy.eventAnimEnd(0);
    }

    [Fact]
    public void A_mission_reset_shows_the_spots_unarmed_and_usable_by_spies()
    {
        Scene s = Story();
        foreach (SBombTargetObjectiveTrigger t in new[] { s.A, s.B })
        {
            Assert.True(t.bActive);
            Assert.False(t.bHidden);
            Assert.False(t.bSkinState);
            Assert.True(t.InteractionObj!.bUsable);
            Assert.Equal(Usable.U_SPAWNOFFENSE, t.InteractionObj!.UsableBy);
        }
    }

    [Fact]
    public void Planting_arms_the_spot_hides_the_others_and_warns_everyone()
    {
        Scene s = Story();
        Plant(s, s.A);
        Assert.True(s.Spy.IsInState(s_PlaceBomb_End));
        Assert.Same(s.A, s.Modem.BombTrigger);
        Assert.Equal(4999, s.Modem.Charge);
        Assert.True(s.A.bTriggering);
        Assert.True(s.A.bSkinState);
        Assert.Same(s.Spy, s.A.ControllerWhoTakesIt);
        Assert.Equal(Usable.U_SPAWNDEFENSE, s.A.InteractionObj!.UsableBy);
        Assert.True(s.EntryA.bRemove);
        Assert.Equal(K.BombArmedSound, s.A.AmbientSound);
        Assert.True(s.B.bHidden);
        Assert.False(s.B.InteractionObj!.bUsable);
        Assert.Equal(2, s.W.Host.Messages(K.MsgBombUnderAttack).Count);

        // The other spot cannot be armed while this one counts down.
        s.B.eventTriggering(true);
        Assert.False(s.B.bTriggering);
    }

    [Fact]
    public void A_merc_removes_the_charge_and_is_credited()
    {
        Scene s = Story();
        Plant(s, s.A);
        s.A.InteractionObj!.eventInitInteract(s.Merc, 0);
        Assert.False(s.A.bTriggering);
        Assert.False(s.A.bSkinState);
        Assert.Equal(Usable.U_SPAWNOFFENSE, s.A.InteractionObj!.UsableBy);
        Assert.False(s.EntryA.bRemove);
        Assert.False(s.B.bHidden);
        Assert.True(s.B.InteractionObj!.bUsable);
        Assert.Equal(1, s.Merc.PlayerReplicationInfo!.Stats!.Merc_SecuredBombs);
        Assert.Equal(2, s.W.Host.Messages(K.MsgBombSecured).Count);

        // The fuse no longer runs.
        s.W.Run(31f);
        Assert.False(s.Objective.ObjectiveTaken);
    }

    [Fact]
    public void Any_damage_defuses_the_charge()
    {
        Scene s = Story();
        Plant(s, s.A);
        s.A.eventTakeDamage(1, s.Merc.Pawn, s.A.Location, FVector.Zero, SDamageType.SDTBase);
        Assert.False(s.A.bTriggering);
        Assert.Equal(1, s.Merc.PlayerReplicationInfo!.Stats!.Merc_SecuredBombs);
        Assert.Equal(2, s.W.Host.Messages(K.MsgBombSecured).Count);

        // Unarmed, damage sends nothing.
        s.A.eventTakeDamage(1, s.Merc.Pawn, s.A.Location, FVector.Zero, SDamageType.SDTBase);
        Assert.Equal(2, s.W.Host.Messages(K.MsgBombSecured).Count);
    }

    [Fact]
    public void After_CaptureTime_the_charge_blows_and_takes_the_objective()
    {
        Scene s = Story();
        Plant(s, s.A);
        s.W.Run(29.5f);
        Assert.False(s.Objective.ObjectiveTaken);
        s.W.Run(1f);
        Assert.True(s.Objective.ObjectiveTaken);
        Assert.True(s.A.bHidden);
        Assert.False(s.A.bTriggering);
        Assert.Equal(1, s.A.HitPoints);
        Assert.Equal(2, s.W.Host.Messages(K.MsgBombBombed).Count);
        Assert.Empty(s.W.Host.Messages(K.MsgBombSecured));
        Assert.True(s.Mission.ObjectiveTaken);
        Assert.Equal(0, s.W.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_STORY, s.W.Gri.WinningReason);
    }

    [Fact]
    public void The_fuse_runs_the_native_blast_of_the_spot()
    {
        Scene s = Story();
        Plant(s, s.A);
        s.W.Run(31f);
        Assert.True(s.A.bAlreadyDestroyed);
        Assert.Equal(SDamageType.SDamage_Explosion, s.A.DamageActorType);
        Assert.Equal(200, s.A.DamageActorRange);
        Assert.Equal(15, s.A.DamageActorPoints);
    }

    [Fact]
    public void The_blast_hits_pawns_within_the_damage_range_only()
    {
        Scene s = Story();
        Pawn merc = s.Merc.Pawn!;
        Pawn spy = s.Spy.Pawn!;
        Plant(s, s.A);
        merc.SetLocation(s.A.Location + new FVector(150f, 0f, 0f));
        spy.SetLocation(s.A.Location + new FVector(0f, -400f, 0f));
        int spyHealth = spy.Health;
        s.W.Run(31f);
        Assert.True(s.Objective.ObjectiveTaken);

        // The native blast is SDamage_Explosion, which a player takes as the explosion damage (lethal) and the blast
        // reaction; the planter outside DamageActorRange is untouched.
        Assert.True(merc.Health <= 0);
        Assert.Equal(spyHealth, spy.Health);
    }

    [Fact]
    public void Desactivate_hides_the_spot_without_changing_its_state()
    {
        Scene s = Story();
        Plant(s, s.A);
        s.A.Desactivate();
        Assert.False(s.A.bActive);
        Assert.False(s.A.bTriggering);
        Assert.True(s.A.bHidden);
        Assert.False(s.A.bEMFIsOn);
        Assert.False(s.A.InteractionObj!.bUsable);
        Assert.True(s.A.IsInState(s_Initial));
    }

    [Fact]
    public void The_look_follows_the_replicated_flag_on_every_machine()
    {
        Scene s = Story();
        AssetRef absent = s.A.AbsentSkin;
        Plant(s, s.A);
        s.W.Run(0.1f);
        Assert.True(s.A.bLocalSkinState);
        Assert.All(s.A.Skins.GetRange(0, 3), skin => Assert.True(skin.IsNone));
        Assert.True(s.A.bAcceptsProjectors);

        s.A.eventTriggering(false);
        s.W.Run(0.1f);
        Assert.All(s.A.Skins.GetRange(0, 3), skin => Assert.Equal(absent, skin));
        Assert.False(s.A.bAcceptsProjectors);
    }

    [Fact]
    public void Outside_Story_the_spots_start_hidden()
    {
        Scene s = Story("?GameMode=4?TimeLimit=10?LivesATT=4?LivesDEF=3");
        Assert.True(s.A.bHidden);
        Assert.False(s.A.InteractionObj!.bUsable);
    }

    [Fact]
    public void The_modem_refuses_a_plant_while_rolling_or_not_walking_and_before_the_refire()
    {
        Scene s = Story();
        var pawn = (SBasePawn)s.Spy.Pawn!;
        Assert.True(s.Modem.CanUseGadget());

        pawn.sAction = SBasePawn.SA_ROLLINGUP;
        Assert.False(s.Modem.CanUseGadget());
        Assert.Equal(K.WhyCantUseWrongPosition, s.Modem.WhyCantUse);
        pawn.sAction = SBasePawn.SA_WALK_ST_0H;
        pawn.Physics = EPhysics.PHYS_Falling;
        Assert.False(s.Modem.CanUseGadget());
        pawn.Physics = EPhysics.PHYS_Walking;

        s.Modem.LastUsed = s.W.Level.TimeSeconds - 1f;
        Assert.False(s.Modem.CanUseGadget());
        Assert.Equal(K.WhyCantUseNoModemLeft, s.Modem.WhyCantUse);
        s.Modem.LastUsed = 0f;
        s.Modem.Charge = 0;
        Assert.False(s.Modem.CanUseGadget());
    }

    [Fact]
    public void Without_a_usable_modem_the_plant_stops_on_the_spy_machine()
    {
        Scene s = Story();
        s.Modem.Charge = 0;
        s.A.InteractionObj!.eventInitInteract(s.Spy, 0);
        Assert.False(s.Spy.IsInState(s_PlaceBomb));
        Assert.False(s.A.bTriggering);
    }

    [Fact]
    public void An_alarm_locks_an_unarmed_spot_until_it_unlocks()
    {
        Scene s = Story();
        s.A.eventLock();
        Assert.True(s.A.IsInState(s_TemporaryInactive));
        Assert.True(s.A.bLocked);
        Assert.False(s.A.InteractionObj!.bUsable);
        Assert.Equal(2, s.W.Host.Messages(K.MsgBombSecured).Count);

        s.A.eventUnLock();
        Assert.True(s.A.IsInState(s_Initial));
        Assert.False(s.A.bLocked);
        Assert.True(s.A.InteractionObj!.bUsable);
    }
}
