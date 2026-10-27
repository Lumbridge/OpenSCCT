using System.Linq;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The sticky cam: firing, sticking, the cam view, the gas and the pick-up (spec/equipment/sticky-cam.md).</summary>
public sealed class StickyCamTests
{
    private static (CombatScene Scene, SPlayerController PC, SPawnAttaque Spy, SGadStickyCam Gad) Spy(CollisionMaterialFlags Wall = CollisionMaterialFlags.None)
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(300f, Wall));
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        var spy = (SPawnAttaque)pawn;
        GadgetKit.Shocker(scene, spy);
        SGadStickyCam gad = GadgetKit.Gadget(scene, spy, new SGadStickyCam());
        pc.MyDesiredRotation = default;
        scene.Run(1.5f);
        return (scene, pc, spy, gad);
    }

    private static SStickyCam[] Cams(CombatScene Scene) => GadgetKit.All<SStickyCam>(Scene).ToArray();

    [Fact]
    public void TheLauncherCarriesFiveCamsWithOneGasDoseEach()
    {
        (_, _, _, SGadStickyCam gad) = Spy();
        Assert.Equal(5, gad.Charge);
        Assert.Equal(1, new SStickyCam().Smoke_NbDoses);
        Assert.Equal(1f, gad.RefireRate);
    }

    [Fact]
    public void ACamSticksTwoAndAHalfUnitsOffAPlainWallFacingBack()
    {
        (CombatScene scene, SPlayerController pc, SPawnAttaque spy, SGadStickyCam gad) = Spy();
        // The shot leaves the pistol in the right hand (CombatNatives.SpyPistolCoords), right of the view line.
        float lineY = OpenScct.Game.Combat.CombatServices.Ports(gad).BoneCoords(spy.MyWeapon!, SWeaponDefense.FxBone).Origin.Y;
        gad.Use(1f);
        SStickyCam cam = Assert.Single(Cams(scene));
        Assert.InRange(cam.Location.X, 296.5f, 298.5f);
        Assert.Equal(lineY, cam.Location.Y, 0);
        Assert.Equal(32768, cam.Rotation.Yaw & 0xFFFF);
        Assert.Equal(4, gad.Charge);
        Assert.Equal(scene.Time, gad.LastUsed, 3);
        Assert.Same(cam, spy.UsedObject);
        Assert.True(GadgetStates.InStickyCamViewState(pc) || pc.GetStateName() == GadgetStates.s_UseObject_StickyCam_SmoothToPosition,
            pc.GetStateName().ToString());
    }

    [Fact]
    public void AWallMarkedNoStickyCamTakesNoCam()
    {
        (CombatScene scene, _, _, SGadStickyCam gad) = Spy(CollisionMaterialFlags.NoStickyCam);
        Assert.False(gad.UseStickyCam(true));
        gad.Use(1f);
        Assert.Empty(Cams(scene));
        Assert.Equal(5, gad.Charge);
    }

    [Fact]
    public void NothingWithinRangeTakesNoCam()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(20000f));
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        GadgetKit.Shocker(scene, pawn);
        SGadStickyCam gad = GadgetKit.Gadget(scene, pawn, new SGadStickyCam());
        pc.MyDesiredRotation = default;
        scene.Run(1.5f);
        Assert.False(gad.UseStickyCam(true));
    }

    [Fact]
    public void AHolsteredShockerFiresNoCam()
    {
        (CombatScene scene, _, SPawnAttaque spy, SGadStickyCam gad) = Spy();
        spy.WeaponLoaded = 0;
        gad.Use(1f);
        Assert.Empty(Cams(scene));
    }

    [Fact]
    public void ASecondCamWithinTheRefireSecondIsRefused()
    {
        (CombatScene scene, _, _, SGadStickyCam gad) = Spy();
        Assert.True(gad.UseStickyCam(false));
        Assert.False(gad.UseStickyCam(false));
        scene.Run(1.1f);
        Assert.True(gad.UseStickyCam(true));
    }

    [Fact]
    public void ANewCamReplacesTheUsedOneOnTheServer()
    {
        (CombatScene scene, _, SPawnAttaque spy, SGadStickyCam gad) = Spy();
        gad.Use(1f);
        SStickyCam first = Assert.Single(Cams(scene));
        scene.Run(1.1f);
        gad.Use(1f);
        scene.World.Step();
        Assert.True(first.bDeleteMe);
        Assert.NotSame(first, spy.UsedObject);
        Assert.Equal(3, gad.Charge);
    }

    [Fact]
    public void AnEmptyLauncherOnlyClicks()
    {
        (CombatScene scene, _, _, SGadStickyCam gad) = Spy();
        gad.Charge = 0;
        Assert.False(gad.UseStickyCam(false));
        Assert.Empty(Cams(scene));
    }

    [Theory]
    [InlineData(1.5f, 2f)]
    [InlineData(2.4f, 2f)]
    [InlineData(-1.5f, -1f)]
    [InlineData(-2.6f, -3f)]
    [InlineData(297.5f, 298f)]
    public void CamPositionsAreRoundedAsTheNetworkSendsThem(float v, float expected) =>
        Assert.Equal(expected, SGadStickyCam.NetRoundVectorAxis(v));

    [Fact]
    public void TheStickTestTakesWorldGeometryWithoutTheNoStickyFlag()
    {
        var world = new FCheckResult { Time = 0.5f, Actor = null };
        Assert.True(SGadStickyCam.CanStickOn(world));
        var flagged = new FCheckResult { Time = 0.5f, MaterialFlags = CollisionMaterialFlags.NoStickyCam };
        Assert.False(SGadStickyCam.CanStickOn(flagged));
        var volume = new FCheckResult { Time = 0.5f, Actor = new BlockingVolume() };
        Assert.False(SGadStickyCam.CanStickOn(volume));
    }

    [Fact]
    public void TheCamNormalAlwaysFacesBackAlongTheShot()
    {
        var d = new FVector(1f, 0f, 0f);
        Assert.Equal(new FVector(-1f, 0f, 0f), SGadStickyCam.FacingBack(new FVector(-1f, 0f, 0f), d));
        Assert.Equal(new FVector(-1f, 0f, 0f), SGadStickyCam.FacingBack(new FVector(1f, 0f, 0f), d));
    }

    /// <summary>
    /// A side wall exactly parallel to the shot: its normal is at 90 degrees to the shot, the "face back" test (n . D
    /// at least 0) flips it, and the probe moves the cam 5 uu past that wall's face, as the original does.
    /// </summary>
    [Fact]
    public void AParallelSideWallInsideTheProbeMovesTheCamFiveUnitsPastItsFace()
    {
        CombatScene scene = GadgetKit.Scene();
        // The back wall at x = 300 and a side wall whose face is at y = 3, inside the 5.5 uu probe.
        GadgetKit.Walls(scene, GadgetKit.WallAt(300f), (new FVector(-1000f, 3f, -1000f), new FVector(1000f, 60f, 1000f), CollisionMaterialFlags.None));
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, -60f, 100f));
        GadgetKit.Shocker(scene, pawn);
        SGadStickyCam gad = GadgetKit.Gadget(scene, pawn, new SGadStickyCam());
        pc.MyDesiredRotation = default;
        scene.Run(1.5f);
        // The pistol's line (right of the view, CombatNatives.SpyPistolCoords) put at y = 0.
        float right = OpenScct.Game.Combat.CombatServices.Ports(gad).BoneCoords(pawn.MyWeapon!, SWeaponDefense.FxBone).Origin.Y - pawn.Location.Y;
        pawn.SetLocation(new FVector(0f, -right, 100f));
        Assert.True(gad.UseStickyCam(false));
        SStickyCam cam = Assert.Single(Cams(scene));
        Assert.Equal(8f, cam.Location.Y, 0);
    }

    [Fact]
    public void TheGasUsesTheDoseAndWaitsFiveSecondsBeforeAnother()
    {
        (CombatScene scene, SPlayerController pc, _, SGadStickyCam gad) = Spy();
        gad.Use(1f);
        SStickyCam cam = Assert.Single(Cams(scene));
        cam.Smoke_NbDoses = 2;
        pc.UseStickyCamGas();
        Assert.Equal(1, cam.Smoke_NbDoses);
        Assert.Single(GadgetKit.All<SStickyCamEmitter>(scene));
        pc.UseStickyCamGas();
        Assert.Equal(1, cam.Smoke_NbDoses);
        scene.Run(5.1f);
        pc.UseStickyCamGas();
        Assert.Equal(0, cam.Smoke_NbDoses);
    }

    [Fact]
    public void TheGasPutsAMercInFrontOfTheCamInSmokeButNotOneWearingTheMask()
    {
        (CombatScene scene, SPlayerController pc, _, SGadStickyCam gad) = Spy();
        gad.Use(1f);
        SStickyCam cam = Assert.Single(Cams(scene));
        FVector gasPoint = cam.Location + cam.Rotation.Vector() * 192f;
        pc.Pawn!.SetLocation(new FVector(-400f, 0f, 100f));
        (SPlayerController mercPc, SBasePawn merc) = scene.Player(false, gasPoint + new FVector(0f, 40f, 0f));
        (SPlayerController maskedPc, SBasePawn masked) = scene.Player(false, gasPoint - new FVector(0f, 40f, 0f));
        masked.bGasMask = true;
        scene.Run(3f);
        mercPc.fLastTimeWakeUp = -10f;
        maskedPc.fLastTimeWakeUp = -10f;
        pc.UseStickyCamGas();
        scene.World.Step();
        Assert.True(mercPc.bInSmoke);
        Assert.False(maskedPc.bInSmoke);
        SStickyCamEmitter gas = Assert.Single(GadgetKit.All<SStickyCamEmitter>(scene));
        Assert.Equal(1, gas.iNbPawnsGazes);
        Assert.Same(merc, gas.PawnsGazes[0]);
        Assert.NotSame(masked, gas.PawnsGazes[0]);
    }

    [Fact]
    public void AMercOutsideTheGasRadiusIsNotGassed()
    {
        (CombatScene scene, SPlayerController pc, _, SGadStickyCam gad) = Spy();
        gad.Use(1f);
        SStickyCam cam = Assert.Single(Cams(scene));
        FVector gasPoint = cam.Location + cam.Rotation.Vector() * 192f;
        (SPlayerController mercPc, _) = scene.Player(false, gasPoint + new FVector(0f, 140f, 0f));
        scene.Run(3f);
        mercPc.fLastTimeWakeUp = -10f;
        pc.UseStickyCamGas();
        scene.World.Step();
        Assert.False(mercPc.bInSmoke);
    }

    [Fact]
    public void ASpyPickingUpAnotherSpysCamGetsItBackAsACharge()
    {
        (CombatScene scene, SPlayerController pc, _, SGadStickyCam gad) = Spy();
        gad.Use(1f);
        SStickyCam cam = Assert.Single(Cams(scene));
        (SPlayerController otherPc, SBasePawn other) = scene.Player(true, new FVector(250f, 0f, 100f));
        SGadStickyCam otherGad = GadgetKit.Gadget(scene, other, new SGadStickyCam());
        otherGad.Charge = 3;
        other.MyGadget = otherGad;
        cam.eventTakeDamage(1, other, K.StickyPickupMarker, new FVector(0f, 0f, 1f), SDamageType.SDTBase);
        Assert.True(cam.bDeleteMe);
        Assert.Equal(4, otherGad.Charge);
        Assert.Equal(cam.Smoke_NbDoses, otherGad.iNbSmokeChargeForCurrentSticky);
        Assert.Null(pc.SBPawn!.UsedObject is { bDeleteMe: false } ? pc.SBPawn.UsedObject : null);
        _ = otherPc;
    }

    [Fact]
    public void ShootingACamDestroysIt()
    {
        (CombatScene scene, _, _, SGadStickyCam gad) = Spy();
        gad.Use(1f);
        SStickyCam cam = Assert.Single(Cams(scene));
        (_, SBasePawn merc) = scene.Player(false, new FVector(200f, 200f, 100f));
        cam.eventTakeDamage(10, merc, cam.Location, default, SDamageType.SDTBase);
        Assert.True(cam.bDeleteMe);
    }
}
