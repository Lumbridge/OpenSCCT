using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The spy binoculars (spec/equipment/spy-binoculars.md), the merc's motion tracker (spec/equipment/motion-tracking.md)
/// and the EMF view (spec/equipment/vision-goggles.md rule 12).
/// </summary>
public sealed class BinocularsAndMotionTests
{
    // ---- binoculars ---------------------------------------------------------------------------------------------

    private static (CombatScene Scene, SPlayerController PC, SBasePawn Spy) Spy(bool Drawn = false, params FVector[] Objectives)
    {
        CombatScene scene = GadgetKit.Scene();
        // Objective triggers are map actors: placed during the map load.
        scene.Level.BeginLoad();
        foreach (FVector at in Objectives)
        {
            Assert.NotNull(scene.Level.SpawnActor(new SObjectiveTrigger(), at, default));
        }

        scene.Level.EndLoad();
        (SPlayerController pc, SBasePawn spy) = GadgetKit.Local(scene, true, new FVector(0f, 0f, 100f));
        pc.myHUD = scene.Level.SpawnActor(new SHud(), default, default);
        SGoggle goggle = scene.Level.SpawnActor(new SGoggle(), pc.Location, default)!;
        goggle.SetOwner(pc);
        goggle.SPC = pc;
        pc.Goggle = goggle;
        GadgetKit.Shocker(scene, spy, Drawn);
        scene.Run(1.5f);
        pc.Rotation = default;
        return (scene, pc, spy);
    }

    /// <summary>The goggle clip the goggle keys play, run to its end: its notify, then the end of channel 18.</summary>
    private static void GoggleClip(SBasePawn spy)
    {
        Assert.True(spy.PawnAnim.IsAnimating(SBasePawn.GOGGLECHANNEL));
        if (spy.bGogglesUp)
        {
            PawnVision.AnimEvent_GoggleSwitchDown(spy);
        }
        else
        {
            PawnVision.AnimEvent_GoggleSwitchUp(spy);
        }

        spy.PawnAnim.Stop(SBasePawn.GOGGLECHANNEL);
        spy.eventAnimEnd(SBasePawn.GOGGLECHANNEL);
    }

    [Fact]
    public void TheKeyTakesAHolsteredSpyIntoTheBinocularsAtThirtyDegrees()
    {
        (_, SPlayerController pc, SBasePawn spy) = Spy();
        pc.eventSwitchBinocularVision();
        Assert.Equal(GadgetStates.s_Binoculars_Att, pc.GetStateName());
        Assert.True(pc.bBinocularVision);
        Assert.True(spy.bBinocularsOn);
        Assert.Equal(30f, pc.fBinocularVisionFOV);
        Assert.Equal(30f, pc.FovAngle);
        Assert.Equal(0.33f, pc.AudioZoomFactor);
        Assert.False(pc.bBehindView);
    }

    [Fact]
    public void ASpyWithTheShockerDrawnIsOnlyAskedToHolster()
    {
        (_, SPlayerController pc, _) = Spy(Drawn: true);
        Name before = pc.GetStateName();
        pc.eventSwitchBinocularVision();
        Assert.True(pc.bForceExitFireMode);
        Assert.Equal(before, pc.GetStateName());
    }

    [Fact]
    public void TheZoomStepsThirtyTwentyTenAndBack()
    {
        (_, SPlayerController pc, _) = Spy();
        pc.eventSwitchBinocularVision();
        pc.eventZoomIn();
        Assert.Equal(20f, pc.fBinocularVisionFOV);
        Assert.Equal(0.66f, pc.AudioZoomFactor);
        Assert.Equal(15, pc.BinocularNoiseDuration);
        pc.eventWheelUp();
        Assert.Equal(10f, pc.fBinocularVisionFOV);
        Assert.Equal(1f, pc.AudioZoomFactor);
        pc.eventZoomIn();
        Assert.Equal(10f, pc.fBinocularVisionFOV);
        pc.eventWheelDown();
        Assert.Equal(20f, pc.fBinocularVisionFOV);
        pc.eventZoomOut();
        Assert.Equal(30f, pc.fBinocularVisionFOV);
        Assert.Equal(0.33f, pc.AudioZoomFactor);
    }

    [Fact]
    public void TheViewAngleSlidesToTheNewZoomInAFifthOfASecond()
    {
        (CombatScene scene, SPlayerController pc, _) = Spy();
        pc.eventSwitchBinocularVision();
        pc.eventZoomIn();
        pc.eventPlayerMove(Sim.DeltaTime);
        Assert.InRange(pc.FovAngle, 20.1f, 30f);
        for (int i = 0; i < (int)(0.3f * Sim.Hz); i++)
        {
            pc.eventPlayerMove(Sim.DeltaTime);
            scene.World.Step();
        }

        Assert.Equal(20f, pc.FovAngle);
    }

    [Fact]
    public void TheViewPitchIsClampedInTheBinoculars()
    {
        (_, SPlayerController pc, _) = Spy();
        pc.eventSwitchBinocularVision();
        pc.fSensibiliteSouris = 1f;
        pc.DES_fJumelleSpeed_Att_Zoom1 = 5f;
        // 0.1 s x 1 x 5 x 5 = 2.5 rotation units per input unit.
        pc.fLookUpValue = 8000f;
        pc.SpyBinocularsCamRotation(0.1f);
        Assert.Equal(13000, pc.Rotation.Pitch);
        pc.Rotation = default;
        pc.fLookUpValue = -8000f;
        pc.SpyBinocularsCamRotation(0.1f);
        Assert.Equal(-12000, pc.Rotation.Pitch);
        pc.Rotation = default;
        pc.fLookUpValue = 400f;
        pc.SpyBinocularsCamRotation(0.1f);
        Assert.Equal(1000, pc.Rotation.Pitch);
    }

    [Fact]
    public void ThePressAgainLeavesToTheUnarmedWalkAndRestoresTheView()
    {
        (_, SPlayerController pc, SBasePawn spy) = Spy();
        pc.eventSwitchBinocularVision();
        // The key is refused while the goggle clip lowers the goggles; this rig does not run the clips.
        GoggleClip(spy);
        pc.eventSwitchBinocularVision();
        Assert.False(pc.bBinocularVision);
        pc.eventPlayerTick(Sim.DeltaTime);
        Assert.NotEqual(GadgetStates.s_Binoculars_Att, pc.GetStateName());
        Assert.False(spy.bBinocularsOn);
        Assert.Equal(pc.DefaultFOV, pc.FovAngle);
        Assert.True(pc.bBehindView);
        Assert.Equal(0f, pc.AudioZoomFactor);
    }

    [Fact]
    public void UseMarksTheObjectiveUnderTheReticleAndAgainClearsIt()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn spy) = Spy(false, new FVector(1500f, 0f, 150f), new FVector(0f, 1500f, 100f));
        SObjectiveTrigger ahead = GadgetKit.All<SObjectiveTrigger>(scene)[0];
        pc.Rotation = (ahead.Location - (spy.Location + new FVector(0f, 0f, spy.EyeHeight))).Rotation();
        var hud = (SHud)pc.myHUD!;
        Assert.Same(ahead, SpyBinoculars.ObjectiveUnderReticle(pc));
        SpyBinoculars.LockObjectiveBinocularsAtt(hud, pc);
        Assert.Same(ahead, hud.LockedObjective);
        SpyBinoculars.LockObjectiveBinocularsAtt(hud, pc);
        Assert.Null(hud.LockedObjective);
    }

    [Fact]
    public void AnObjectiveOffTheReticleIsNotMarked()
    {
        (_, SPlayerController pc, _) = Spy(false, new FVector(1500f, 300f, 100f));
        Assert.Null(SpyBinoculars.ObjectiveUnderReticle(pc));
    }

    [Fact]
    public void TheUseKeyMarksAtMostOncePerSecond()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn spy) = Spy(false, new FVector(1500f, 0f, 150f));
        SObjectiveTrigger ahead = GadgetKit.All<SObjectiveTrigger>(scene)[0];
        pc.eventSwitchBinocularVision();
        pc.fTurnValue = 0f;
        pc.fLookUpValue = 0f;
        pc.Rotation = (ahead.Location - (spy.Location + new FVector(0f, 0f, spy.EyeHeight))).Rotation();
        var hud = (SHud)pc.myHUD!;
        pc.bUsing = 1;
        pc.TimeUsing = 0f;
        pc.eventPlayerMove(Sim.DeltaTime);
        Assert.Same(ahead, hud.LockedObjective);
        pc.eventPlayerMove(Sim.DeltaTime);
        Assert.Same(ahead, hud.LockedObjective);
    }

    // ---- motion tracking --------------------------------------------------------------------------------------

    private static (CombatScene Scene, SPlayerController PC, SMotionTrackingVision Tracker) Tracker()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, _) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        SMotionTrackingVision tracker = scene.Level.SpawnActor(new SMotionTrackingVision(), default, default)!;
        tracker.PlayerOwner = pc;
        tracker.GotoState(GadgetStates.s_Active);
        return (scene, pc, tracker);
    }

    private static void Steps(CombatScene Scene, float Seconds, Pawn? Moving, FVector Velocity)
    {
        for (int i = 0; i < (int)(Seconds * Sim.Hz + 0.5f); i++)
        {
            if (Moving is not null)
            {
                Moving.Velocity = Velocity;
            }

            Scene.World.Step();
        }
    }

    [Fact]
    public void TheTrackerScansTwiceASecondWithin2600UnitsAbove150UnitsPerSecond()
    {
        var t = new SMotionTrackingVision();
        Assert.Equal(2600, t.MaxDetectedDistance);
        Assert.Equal(150f, t.MinDetectedVelocity);
        Assert.Equal(1.5f, t.KeepLockTime);
        Assert.Equal(0.25f, t.OutTime);
        (_, _, SMotionTrackingVision tracker) = Tracker();
        Assert.True(tracker.IsActive());
    }

    [Fact]
    public void ARunningSpyInRangeIsLocked()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn spy) = scene.Player(true, new FVector(1500f, 0f, 100f));
        Steps(scene, 0.6f, spy, new FVector(0f, 300f, 0f));
        FLockedObject l = Assert.Single(tracker.LockedObjects);
        Assert.Same(spy, l.Actor);
        Assert.Equal(0f, l.EndTime);
    }

    [Fact]
    public void ASlowFarOrCloakedSpyIsNotLocked()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn slow) = scene.Player(true, new FVector(1000f, 0f, 100f));
        (_, SBasePawn far) = scene.Player(true, new FVector(2800f, 0f, 100f));
        (_, SBasePawn cloaked) = scene.Player(true, new FVector(0f, 1000f, 100f));
        cloaked.DesiredGlassEffect = 255;
        for (int i = 0; i < (int)(1.2f * Sim.Hz); i++)
        {
            slow.Velocity = new FVector(100f, 0f, 0f);
            far.Velocity = new FVector(0f, 400f, 0f);
            cloaked.Velocity = new FVector(400f, 0f, 0f);
            scene.World.Step();
        }

        Assert.Empty(tracker.LockedObjects);
    }

    [Fact]
    public void TheMercsOwnPawnIsNeverLocked()
    {
        (CombatScene scene, SPlayerController pc, SMotionTrackingVision tracker) = Tracker();
        Steps(scene, 1.2f, pc.Pawn, new FVector(400f, 0f, 0f));
        Assert.Empty(tracker.LockedObjects);
    }

    [Fact]
    public void AFastProjectileIsLocked()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        SProjChaffe grenade = scene.Level.SpawnActor(new SProjChaffe(), new FVector(800f, 0f, 100f), default)!;
        grenade.SetPhysics(EPhysics.PHYS_None);
        Steps(scene, 0.6f, null, default);
        grenade.Velocity = new FVector(0f, 500f, 0f);
        Steps(scene, 0.6f, null, default);
        Assert.Contains(tracker.LockedObjects, l => ReferenceEquals(l.Actor, grenade));
    }

    [Fact]
    public void AWallHidesAMover()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        GadgetKit.Walls(scene, GadgetKit.WallAt(500f));
        (_, SBasePawn spy) = scene.Player(true, new FVector(1500f, 0f, 100f));
        Steps(scene, 1.2f, spy, new FVector(0f, 300f, 0f));
        Assert.Empty(tracker.LockedObjects);
    }

    [Fact]
    public void AStoppedTargetIsHeldForOneAndAHalfSecondsThenFades()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn spy) = scene.Player(true, new FVector(1500f, 0f, 100f));
        Steps(scene, 0.6f, spy, new FVector(0f, 300f, 0f));
        Assert.Single(tracker.LockedObjects);
        Steps(scene, 0.6f, spy, default);
        FLockedObject held = Assert.Single(tracker.LockedObjects);
        Assert.True(held.EndTime > scene.Time);
        Steps(scene, 1.2f, spy, default);
        Assert.Single(tracker.LockedObjects);
        Steps(scene, 1.2f, spy, default);
        Assert.Empty(tracker.LockedObjects);
    }

    [Fact]
    public void LeavingTheModeClearsTheLocks()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn spy) = scene.Player(true, new FVector(1500f, 0f, 100f));
        Steps(scene, 0.6f, spy, new FVector(0f, 300f, 0f));
        Assert.Single(tracker.LockedObjects);
        tracker.GotoState(GadgetStates.s_Inactive);
        Assert.Empty(tracker.LockedObjects);
        Assert.False(tracker.IsActive());
    }

    [Fact]
    public void TheMercGogglesStartTheTrackerInMotionMode()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, _) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        SGoggle goggle = scene.Level.SpawnActor(new SGoggle(), pc.Location, default)!;
        goggle.SetOwner(pc);
        goggle.SPC = pc;
        pc.Goggle = goggle;
        goggle.SwitchMode(1);
        goggle.eventDown();
        Assert.NotNull(goggle.MotionTrackingVision);
        Assert.True(((SMotionTrackingVision)goggle.MotionTrackingVision!).IsActive());
        goggle.SwitchMode(2);
        Assert.False(((SMotionTrackingVision)goggle.MotionTrackingVision!).IsActive());
    }

    // ---- EMF ------------------------------------------------------------------------------------------------------

    [Fact]
    public void ElectronicsGlowInTheEmfView()
    {
        CombatScene scene = GadgetKit.Scene();
        SMineProx mine = scene.Level.SpawnActor(new SMineProx(), new FVector(100f, 0f, 0f), default)!;
        SStickyCam cam = scene.Level.SpawnActor(new SStickyCam(), new FVector(200f, 0f, 0f), default)!;
        Assert.Equal(1f, EmfVision.Glow(mine));
        Assert.Equal(1f, EmfVision.Glow(cam));
        Assert.Contains(mine, EmfVision.Electronics(scene.Level));
        mine.bEMFIsOn = false;
        Assert.Equal(0f, EmfVision.Glow(mine));
        Assert.DoesNotContain(mine, EmfVision.Electronics(scene.Level));
    }

    [Fact]
    public void TheBinocularsMakeASpyEmfVisible()
    {
        (_, SPlayerController pc, SBasePawn spy) = Spy();
        Assert.False(spy.bEMFIsOn);
        pc.eventSwitchBinocularVision();
        Assert.True(spy.bEMFIsOn);
        GoggleClip(spy);
        pc.eventSwitchBinocularVision();
        pc.eventPlayerTick(Sim.DeltaTime);
        Assert.False(spy.bEMFIsOn);
    }
}
