using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The merc's mine launcher and its mines: placing, the menu, the proximity, laser and poison mines, chaff, removal
/// and the limit (spec/equipment/mine-launcher.md).
/// </summary>
public sealed class MineTests
{
    private static readonly FVector Up = new(0f, 0f, 1f);

    private static T Mine<T>(CombatScene Scene, T Mine, FVector At, Pawn? Owner = null)
        where T : SMines =>
        Scene.Level.SpawnActor(Mine, At, default, Owner, Owner) ?? throw new System.InvalidOperationException("mine did not spawn");

    private static void Steps(CombatScene Scene, float Seconds, Pawn? Moving = null, FVector Velocity = default)
    {
        int frames = (int)(Seconds * Sim.Hz + 0.5f);
        for (int i = 0; i < frames; i++)
        {
            if (Moving is not null)
            {
                Moving.Velocity = Velocity;
            }

            Scene.World.Step();
        }
    }

    // ---- the proximity mine -------------------------------------------------------------------------------

    [Fact]
    public void AProximityMineArmsAtOnceAndChecksTwiceASecond()
    {
        var mine = new SMineProx();
        Assert.Equal(0, mine.TempsActivation);
        Assert.Equal(400, mine.MyRange);
        Assert.Equal(200, mine.VitesseMin);
        Assert.Equal(215, mine.MyDamage);
        Assert.Equal(0.5f, mine.TempsAvantExplosion);
        CombatScene scene = GadgetKit.Scene();
        SMineProx placed = Mine(scene, new SMineProx(), new FVector(0f, 0f, 100f));
        Assert.Equal(GadgetStates.s_Activated, placed.GetStateName());
        Assert.Equal(placed.LightOn, placed.CurrentSkin);
        Assert.Equal(new FVector(5f, 0f, 100f), placed.refloc);
    }

    [Fact]
    public void ARunningSpyInRangeSetsOffTheMineHalfASecondLaterAndIsHurt()
    {
        CombatScene scene = GadgetKit.Scene();
        SMineProx mine = Mine(scene, new SMineProx(), new FVector(0f, 0f, 100f));
        (_, SBasePawn spy) = scene.Player(true, new FVector(250f, 0f, 100f));
        int health = spy.Health;
        Steps(scene, 0.6f, spy, new FVector(0f, 300f, 0f));
        Assert.True(mine.Explode || mine.bDeleteMe);
        Steps(scene, 0.6f, spy, new FVector(0f, 300f, 0f));
        Assert.True(mine.bDeleteMe);
        Assert.True(spy.Health < health, $"health {spy.Health}");
    }

    [Fact]
    public void ASpyWalkingSlowlyOrFallingPastTheMineDoesNotSetItOff()
    {
        CombatScene scene = GadgetKit.Scene();
        SMineProx mine = Mine(scene, new SMineProx(), new FVector(0f, 0f, 100f));
        (_, SBasePawn spy) = scene.Player(true, new FVector(250f, 0f, 100f));
        Steps(scene, 1.2f, spy, new FVector(0f, 150f, 0f));
        Assert.False(mine.Explode);
        // Falling: only the horizontal speed counts when going down.
        Steps(scene, 1.2f, spy, new FVector(0f, 0f, -400f));
        Assert.False(mine.Explode);
        // Going up: the whole speed counts.
        Steps(scene, 1.2f, spy, new FVector(0f, 0f, 400f));
        Assert.True(mine.Explode || mine.bDeleteMe);
    }

    [Fact]
    public void MercsDoNotSetOffMines()
    {
        CombatScene scene = GadgetKit.Scene();
        SMineProx mine = Mine(scene, new SMineProx(), new FVector(0f, 0f, 100f));
        (_, SBasePawn merc) = scene.Player(false, new FVector(250f, 0f, 100f));
        Steps(scene, 1.2f, merc, new FVector(0f, 400f, 0f));
        Assert.False(mine.Explode);
    }

    [Fact]
    public void ASpyOutOfRangeOrBehindAWallDoesNotSetItOff()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(100f));
        SMineProx mine = Mine(scene, new SMineProx(), new FVector(0f, 0f, 100f));
        (_, SBasePawn behind) = scene.Player(true, new FVector(250f, 0f, 100f));
        (_, SBasePawn far) = scene.Player(true, new FVector(-450f, 0f, 100f));
        for (int i = 0; i < (int)(1.2f * Sim.Hz); i++)
        {
            behind.Velocity = new FVector(0f, 400f, 0f);
            far.Velocity = new FVector(0f, 400f, 0f);
            scene.World.Step();
        }

        Assert.False(mine.Explode);
    }

    [Fact]
    public void AnyDamageBlowsTheMine()
    {
        CombatScene scene = GadgetKit.Scene();
        SMineProx mine = Mine(scene, new SMineProx(), new FVector(0f, 0f, 100f));
        mine.eventTakeDamage(1, null, mine.Location, default, SDamageType.SDTBase);
        Assert.True(mine.bDeleteMe);
        Assert.Single(GadgetKit.All<SMineProxEmitter>(scene));
    }

    [Fact]
    public void TheBlastFallsOffWithDistanceAndStopsAtItsReach()
    {
        // Beyond fDistForBlast (250) the blast is plain damage, linear to 0 at MyDamageRange (500) from the mine.
        CombatScene scene = GadgetKit.Scene();
        SMineProx mine = Mine(scene, new SMineProx(), new FVector(0f, 0f, 100f));
        (_, SBasePawn mid) = scene.Player(true, new FVector(0f, 300f, 100f));
        (_, SBasePawn edge) = scene.Player(true, new FVector(0f, -380f, 100f));
        (_, SBasePawn out_) = scene.Player(true, new FVector(450f, 0f, 100f));
        scene.Run(1f);
        int full = mid.Health;
        mine.eventTakeDamage(1, null, mine.Location, default, SDamageType.SDTBase);
        Assert.InRange(full - mid.Health, 85, 86);
        Assert.InRange(full - edge.Health, 50, 51);
        Assert.Equal(full, out_.Health);
    }

    [Fact]
    public void AWallShieldsAPawnFromTheBlast()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(60f));
        SMineProx mine = Mine(scene, new SMineProx(), new FVector(0f, 0f, 100f));
        (_, SBasePawn shielded) = scene.Player(true, new FVector(300f, 0f, 100f));
        (_, SBasePawn open) = scene.Player(true, new FVector(-300f, 0f, 100f));
        scene.Run(1f);
        int full = shielded.Health;
        mine.eventTakeDamage(1, null, mine.Location, default, SDamageType.SDTBase);
        Assert.Equal(full, shielded.Health);
        Assert.True(open.Health < full);
    }

    // ---- the laser mine -----------------------------------------------------------------------------------

    [Fact]
    public void ALaserMineArmsAfterFiveSecondsWithABeamToTheOppositeWall()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(1000f));
        SMineLaser mine = Mine(scene, new SMineLaser(), new FVector(0f, 0f, 100f));
        Steps(scene, 4.8f);
        Assert.NotEqual(GadgetStates.s_Activated, mine.GetStateName());
        Steps(scene, 0.4f);
        Assert.Equal(GadgetStates.s_Activated, mine.GetStateName());
        Assert.Equal(1000f, mine.fTailleFaisceau, 0);
        SLaserBeam_MineLaser beam = Assert.Single(GadgetKit.All<SLaserBeam_MineLaser>(scene));
        Assert.Equal(mine.fTailleFaisceau, beam.fTailleFaisceau);
    }

    [Fact]
    public void ALaserMineWithoutAWallHasABeamOfItsFullLength()
    {
        CombatScene scene = GadgetKit.Scene();
        SMineLaser mine = Mine(scene, new SMineLaser(), new FVector(0f, 0f, 100f));
        Steps(scene, 5.2f);
        Assert.Equal(2000.1f, mine.fTailleFaisceau, 1);
        Assert.Equal(mine.Location, mine.vMurTouche);
    }

    [Fact]
    public void ASpyCrossingTheBeamSetsOffTheLaserMine()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(1000f));
        SMineLaser mine = Mine(scene, new SMineLaser(), new FVector(0f, 0f, 100f));
        Steps(scene, 5.2f);
        (_, SBasePawn spy) = scene.Player(true, new FVector(500f, 0f, 100f));
        Steps(scene, 0.1f);
        Assert.Equal(GadgetStates.s_wait4boom, mine.GetStateName());
        Steps(scene, 0.6f);
        Assert.True(mine.bDeleteMe);
        Assert.Empty(GadgetKit.All<SLaserBeam_MineLaser>(scene));
        _ = spy;
    }

    [Fact]
    public void APawnBesideTheBeamOrAMercOnItDoesNotSetItOff()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(1000f));
        SMineLaser mine = Mine(scene, new SMineLaser(), new FVector(0f, 0f, 100f));
        Steps(scene, 5.2f);
        scene.Player(true, new FVector(500f, 120f, 100f));
        scene.Player(false, new FVector(600f, 0f, 100f));
        Steps(scene, 0.5f);
        Assert.Equal(GadgetStates.s_Activated, mine.GetStateName());
    }

    [Fact]
    public void ChaffRemovesTheBeamAndTheMineArmsAgainAfter()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(1000f));
        SMineLaser mine = Mine(scene, new SMineLaser(), new FVector(0f, 0f, 100f));
        Steps(scene, 5.2f);
        mine.eventSetLockedByChaff(10f);
        Assert.Equal(GadgetStates.s_LockedByChaff, mine.GetStateName());
        Assert.Empty(GadgetKit.All<SLaserBeam_MineLaser>(scene));
        scene.Player(true, new FVector(500f, 0f, 100f));
        Steps(scene, 1f);
        Assert.Equal(GadgetStates.s_LockedByChaff, mine.GetStateName());
        Steps(scene, 9.2f);
        Assert.NotEqual(GadgetStates.s_LockedByChaff, mine.GetStateName());
        Assert.Single(GadgetKit.All<SLaserBeam_MineLaser>(scene));
    }

    // ---- the poison mine -------------------------------------------------------------------------------------

    [Fact]
    public void APoisonMineSitsThreeAndAHalfUnitsOffTheWallWithABeamToTheOppositeWall()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(1000f));
        SMinePoison mine = Mine(scene, new SMinePoison(), new FVector(0f, 0f, 100f));
        Assert.Equal(3.5f, mine.Location.X, 2);
        Assert.Equal(1000f, mine.LaserEndLocation.X, 0);
        Assert.False(mine.bLaserHidden);
        Assert.Equal(GadgetStates.s_Activated, mine.GetStateName());
        Assert.Equal(0, mine.iMineType);
    }

    [Fact]
    public void ASpyCrossingThePoisonBeamIsPoisonedByTheMineOwner()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(1000f));
        (_, SBasePawn merc) = scene.Player(false, new FVector(-300f, 0f, 100f));
        SMinePoison mine = Mine(scene, new SMinePoison(), new FVector(0f, 0f, 100f), merc);
        (_, SBasePawn spy) = scene.Player(true, new FVector(500f, 0f, 100f));
        Steps(scene, 0.2f);
        Assert.True(mine.bDeleteMe);
        Assert.True(spy.bPoisonned);
        Assert.Same(merc, spy.PoisonnedBy);
        Assert.Single(GadgetKit.All<SPoisonEmitter>(scene));
    }

    [Fact]
    public void ASpyBesideThePoisonBeamIsNotPoisoned()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(1000f));
        SMinePoison mine = Mine(scene, new SMinePoison(), new FVector(0f, 0f, 100f));
        (_, SBasePawn spy) = scene.Player(true, new FVector(500f, 150f, 100f));
        Steps(scene, 0.5f);
        Assert.False(mine.bDeleteMe);
        Assert.False(spy.bPoisonned);
    }

    [Fact]
    public void APoisonMineWithoutAnOppositeWallHasNoBeamAndCatchesNobody()
    {
        CombatScene scene = GadgetKit.Scene();
        SMinePoison mine = Mine(scene, new SMinePoison(), new FVector(0f, 0f, 100f));
        Assert.Equal(mine.Location, mine.LaserEndLocation);
        (_, SBasePawn spy) = scene.Player(true, new FVector(300f, 0f, 100f));
        Steps(scene, 0.5f);
        Assert.False(spy.bPoisonned);
    }

    [Fact]
    public void ShootingAPoisonMineReleasesItsGas()
    {
        CombatScene scene = GadgetKit.Scene();
        SMinePoison mine = Mine(scene, new SMinePoison(), new FVector(0f, 0f, 100f));
        mine.eventTakeDamage(5, null, mine.Location, default, SDamageType.SDTBase);
        Assert.True(mine.bDeleteMe);
        Assert.Single(GadgetKit.All<SMinePoisonEmitter>(scene));
    }

    // ---- the launcher ------------------------------------------------------------------------------------------

    private static (CombatScene Scene, SPlayerController PC, SBasePawn Merc, SGadMultiMinesLauncher Launcher) Merc(bool OppositeWall = false)
    {
        CombatScene scene = GadgetKit.Scene();
        if (OppositeWall)
        {
            GadgetKit.Walls(scene, GadgetKit.WallAt(300f), (new FVector(-1040f, -1000f, -1000f), new FVector(-1000f, 1000f, 1000f), CollisionMaterialFlags.None));
        }
        else
        {
            GadgetKit.Walls(scene, GadgetKit.WallAt(300f));
        }

        (SPlayerController pc, SBasePawn merc) = GadgetKit.Local(scene, false, new FVector(260f, 0f, 100f));
        merc.SetPhysics(EPhysics.PHYS_Walking);
        pc.Rotation = default;
        SGadMultiMinesLauncher launcher = GadgetKit.Gadget(scene, merc, new SGadMultiMinesLauncher());
        scene.Run(2.5f);
        return (scene, pc, merc, launcher);
    }

    [Fact]
    public void TheLauncherHoldsThreeMinesOfThreeTypesAndAllowsThreeOnTheMap()
    {
        var launcher = new SGadMultiMinesLauncher();
        Assert.Equal(3, launcher.Charge);
        Assert.Equal(5, launcher.NbIT);
        Assert.Equal(3, launcher.iNbMineMax);
        Assert.Equal("SBase.SMineProx", launcher.MineClass[0].Path);
        Assert.Equal("SBase.SMineLaser", launcher.MineClass[1].Path);
        Assert.Equal("SBase.SMinePoison", launcher.MineClass[2].Path);
    }

    [Fact]
    public void AWallWithinReachIsFoundAndTheReachToItBegins()
    {
        (_, SPlayerController pc, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.Use(1f);
        Assert.IsType<LevelInfo>(launcher.HitActor);
        Assert.Equal(new FVector(-1f, 0f, 0f), launcher.HitNormal);
        Assert.Equal(GadgetStates.s_PutMineOnWall_Grab, pc.GetStateName());
    }

    [Fact]
    public void NoWallWithinReachShowsTheCantPlaceMessage()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(400f));
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(260f, 0f, 100f));
        merc.SetPhysics(EPhysics.PHYS_Walking);
        pc.Rotation = default;
        SGadMultiMinesLauncher launcher = GadgetKit.Gadget(scene, merc, new SGadMultiMinesLauncher());
        Name before = pc.GetStateName();
        launcher.Use(1f);
        Assert.Null(launcher.HitActor);
        Assert.Equal(before, pc.GetStateName());
    }

    [Fact]
    public void LookingUpTooFarFindsNoWall()
    {
        (_, SPlayerController pc, _, SGadMultiMinesLauncher launcher) = Merc();
        pc.Rotation = new FRotator(8000, 0, 0);
        launcher.UseMine();
        Assert.Null(launcher.HitActor);
    }

    [Fact]
    public void DroppingAMinePutsItFiveUnitsOffTheWallAndCountsIt()
    {
        (CombatScene scene, SPlayerController pc, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.UseMine();
        launcher.DropMineOnWall();
        SMineProx mine = Assert.Single(GadgetKit.All<SMineProx>(scene));
        Assert.InRange(mine.Location.X, 294f, 296f);
        Assert.Equal(32768, mine.Rotation.Yaw & 0xFFFF);
        Assert.Equal(2, launcher.Charge);
        Assert.Equal(1, pc.NbBasicMineInMapIOwn);
        Assert.Same(pc, mine.PcOwner);
    }

    [Fact]
    public void TheFourthMineIsRefusedAndRemovingTheOldestMakesRoom()
    {
        (CombatScene scene, SPlayerController pc, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.Charge = 10;
        for (int i = 0; i < 3; i++)
        {
            pc.Pawn!.SetLocation(new FVector(260f, i * 40f, 100f));
            launcher.UseMine();
            launcher.DropMineOnWall();
            scene.Run(0.2f);
        }

        Assert.Equal(3, pc.NbBasicMineInMapIOwn);
        SMines oldest = GadgetKit.All<SMineProx>(scene)[0];
        pc.Pawn!.SetLocation(new FVector(260f, 200f, 100f));
        launcher.UseMine();
        launcher.DropMineOnWall();
        Assert.Equal(3, GadgetKit.All<SMineProx>(scene).Count);
        launcher.AskServerToRemoveOldestMine(pc, 0);
        Assert.True(oldest.bDeleteMe);
        Assert.Equal(2, pc.NbBasicMineInMapIOwn);
        Assert.Equal(1, launcher.CanUse[launcher.NbIT - 2]);
        launcher.DropMineOnWall();
        Assert.Equal(3, GadgetKit.All<SMineProx>(scene).Count);
    }

    [Fact]
    public void AskingWhetherAnOldMineExistsRemovesNothing()
    {
        (CombatScene scene, SPlayerController pc, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.AskServerToRemoveOldestMine(pc, 0, true);
        Assert.Equal(0, launcher.CanUse[launcher.NbIT - 2]);
        launcher.UseMine();
        launcher.DropMineOnWall();
        launcher.AskServerToRemoveOldestMine(pc, 0, true);
        Assert.Equal(1, launcher.CanUse[launcher.NbIT - 2]);
        Assert.Single(GadgetKit.All<SMineProx>(scene));
    }

    [Fact]
    public void TheLaserAndPoisonTypesNeedAnOppositeWall()
    {
        (_, _, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.UseMine();
        Assert.Equal(1, launcher.CanUse[0]);
        Assert.Equal(0, launcher.CanUse[1]);
        Assert.Equal(0, launcher.CanUse[2]);
        (_, _, _, SGadMultiMinesLauncher walled) = Merc(OppositeWall: true);
        walled.UseMine();
        Assert.Equal(1, walled.CanUse[1]);
        Assert.Equal(1, walled.CanUse[2]);
    }

    [Fact]
    public void ALaserMineIsNotDroppedWithoutAnOppositeWall()
    {
        (CombatScene scene, _, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.UseMine();
        launcher.iCurrentMineType = 1;
        launcher.DropMineOnWall();
        Assert.Empty(GadgetKit.All<SMineLaser>(scene));
        Assert.Equal(3, launcher.Charge);
    }

    [Fact]
    public void AMineIsNotPlacedOnTopOfAnother()
    {
        (CombatScene scene, _, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.UseMine();
        launcher.DropMineOnWall();
        SMineProx first = Assert.Single(GadgetKit.All<SMineProx>(scene));
        launcher.UseMine();
        Assert.Same(first, launcher.HitActor);
        Assert.False(first.AcceptStickableGPO());
    }

    [Fact]
    public void TheMenuStepsThroughFiveItemsWithWrap()
    {
        var launcher = new SGadMultiMinesLauncher();
        launcher.SelectPreviousMineType();
        Assert.Equal(4, launcher.iCurrentMineType);
        launcher.SelectNextMineType();
        Assert.Equal(0, launcher.iCurrentMineType);
        launcher.SelectNextMineType();
        Assert.Equal(1, launcher.iCurrentMineType);
    }

    [Fact]
    public void RemovingAMineGivesTheChargeBack()
    {
        (CombatScene scene, SPlayerController pc, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.UseMine();
        launcher.DropMineOnWall();
        SMineProx mine = Assert.Single(GadgetKit.All<SMineProx>(scene));
        Assert.Equal(2, launcher.Charge);
        SInteractObject interaction = Assert.IsAssignableFrom<SInteractObject>(mine.InteractionObj);
        interaction.eventInitInteract(pc, 0);
        Assert.True(mine.bDeleteMe);
        Assert.Equal(3, launcher.Charge);
        Assert.Equal(0, pc.NbBasicMineInMapIOwn);
    }

    [Fact]
    public void TheMineOnWallStatesRunGrabPlaceAndBack()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn merc, SGadMultiMinesLauncher launcher) = Merc();
        launcher.Use(1f);
        Assert.Equal(GadgetStates.s_PutMineOnWall_Grab, pc.GetStateName());
        pc.bAltFire = 0;
        pc.eventAnimEnd(SBasePawn.INTERACTCHANNEL);
        Assert.Equal(GadgetStates.s_PutMineOnWall, pc.GetStateName());
        pc.eventAnimNotificationInteractNow();
        Assert.Single(GadgetKit.All<SMineProx>(scene));
        pc.eventAnimEnd(SBasePawn.INTERACTCHANNEL);
        Assert.NotEqual(GadgetStates.s_PutMineOnWall, pc.GetStateName());
        _ = merc;
    }

    [Fact]
    public void HoldingAltFireAtTheWallOpensTheMenu()
    {
        (_, SPlayerController pc, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.Use(1f);
        pc.bAltFire = 1;
        pc.eventAnimEnd(SBasePawn.INTERACTCHANNEL);
        Assert.Equal(GadgetStates.s_PutMineOnWall_InteractionMenu, pc.GetStateName());
        pc.eventWheelDown();
        Assert.Equal(1, launcher.iCurrentMineType);
        // Exit (the last item), then release Alt-fire.
        launcher.iCurrentMineType = launcher.NbIT - 1;
        pc.bAltFire = 0;
        pc.eventPlayerMove(Sim.DeltaTime);
        Assert.Equal(GadgetStates.s_PutMineOnWall_UnGrab, pc.GetStateName());
    }

    [Fact]
    public void GadgetSelectionIsRefusedWhilePuttingAMineOnTheWall()
    {
        (_, SPlayerController pc, _, SGadMultiMinesLauncher launcher) = Merc();
        launcher.Use(1f);
        Assert.True(GadgetStates.IsPuttingMineOnWall(pc));
    }
}
