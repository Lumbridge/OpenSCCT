using System;
using System.Linq;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Physics.Collision;
using Xunit;
using CombatServices = OpenScct.Game.Combat.CombatServices;

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
    public void UsageCountsDeploymentOnceAndDoesNotCountEachGasDose()
    {
        (CombatScene scene, SPlayerController pc, SPawnAttaque spy, SGadStickyCam gad) = Spy();
        pc.PlayerReplicationInfo = scene.Level.SpawnActor(new PlayerReplicationInfo { Stats = new USPlayerStats() }, default, default);
        gad.Use(1f);
        SStickyCam cam = Assert.Single(Cams(scene));
        Assert.Equal(1, pc.PlayerReplicationInfo!.Stats!.Spy_StickyCamUsed);
        cam.Smoke_NbDoses = 2;
        cam.SpawnGaz();
        cam.NextUseTime = 0;
        cam.SpawnGaz();
        Assert.Equal(0, cam.Smoke_NbDoses);
        Assert.Equal(1, pc.PlayerReplicationInfo.Stats.Spy_StickyCamUsed);
    }

    [Fact]
    public void TheLauncherCarriesFiveCamsWithOneGasDoseEach()
    {
        (_, _, _, SGadStickyCam gad) = Spy();
        Assert.Equal(5, gad.Charge);
        Assert.Equal(1, new SStickyCam().Smoke_NbDoses);
        Assert.Equal(1f, gad.RefireRate);
    }

    [Fact]
    public void ACamSticksOutsideAPlainWallFacingBack()
    {
        (CombatScene scene, SPlayerController pc, SPawnAttaque spy, SGadStickyCam gad) = Spy();
        // The shot leaves the pistol in the right hand (CombatNatives.SpyPistolCoords), right of the view line.
        float lineY = OpenScct.Game.Combat.CombatServices.Ports(gad).BoneCoords(spy.MyWeapon!, SWeaponDefense.FxBone).Origin.Y;
        gad.Use(1f);
        SStickyCam cam = Assert.Single(Cams(scene));
        Assert.InRange(cam.Location.X, 295.5f, 297.5f);
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

    [Theory]
    [InlineData(false, false, 0, 0)]
    [InlineData(false, true, 0, 0)]
    [InlineData(true, false, 0, 0)]
    [InlineData(true, true, 0, 0)]
    [InlineData(false, false, 9000, 5000)]
    [InlineData(false, true, 9000, -5000)]
    [InlineData(true, false, 9000, -5000)]
    [InlineData(true, true, 9000, 5000)]
    public void ACornerShotClearsTheSupportingWallOnEitherSide(bool left, bool crouched, int aimYaw, int aimPitch)
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        GadgetKit.Shocker(scene, pawn);
        SGadStickyCam gad = GadgetKit.Gadget(scene, pawn, new SGadStickyCam());
        scene.Run(1.5f);
        pawn.Rotation = default;
        pawn.bIsCrouched = crouched;
        pawn.BTW = left
            ? crouched ? BTW_.BTWFireCrouchLeft : BTW_.BTWFireStandLeft
            : crouched ? BTW_.BTWFireCrouchRight : BTW_.BTWFireStandRight;
        // The spy faces away from the supporting wall; the pistol leans beyond its end while looking back.
        GadgetKit.Walls(scene, GadgetKit.WallAt(-500f),
            (new FVector(-90f, left ? -1000f : -25f, -1000f),
             new FVector(-30f, left ? 25f : 1000f, 1000f), CollisionMaterialFlags.None));
        pc.Rotation = new FRotator(aimPitch, 32768 + (left ? -aimYaw : aimYaw), 0);

        Assert.True(gad.UseStickyCam(true));
        Assert.True(gad.UseStickyCam(false));
        SStickyCam cam = Assert.Single(Cams(scene));
        Assert.InRange(cam.Location.X, -457.5f, -455.5f);
        Assert.True(left ? cam.Location.Y > 25f : cam.Location.Y < -25f,
            $"The corner shot mounted on the supporting wall: {cam.Location}");
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
    /// A side wall parallel to the shot must push the mount back into the room. Orienting its normal against the
    /// shot instead of the corner probe previously put the camera inside the wall.
    /// </summary>
    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void AParallelSideWallInsideTheProbeKeepsTheCamOutside(float side)
    {
        CombatScene scene = GadgetKit.Scene();
        // The back wall at x = 300 and a side wall 3 uu from the shot, inside the 5.5 uu probe.
        GadgetKit.Walls(scene, GadgetKit.WallAt(300f),
            (new FVector(-1000f, side > 0f ? 3f : -60f, -1000f),
             new FVector(1000f, side > 0f ? 60f : -3f, 1000f), CollisionMaterialFlags.None));
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
        Assert.InRange(3f - cam.Location.Y * side, 4.5f, 6.5f);
        PlayerCalcView.StickyCamView(pc, false, out _, out FVector eye, out _);
        Assert.True(eye.Y * side < 3f, $"The camera eye crossed the side wall: {eye}");
    }

    [Fact]
    public void ACornerTooNarrowForTheCameraDoesNotTakeAChargeOrRefireDelay()
    {
        (CombatScene scene, _, SPawnAttaque spy, SGadStickyCam gad) = Spy();
        float lineY = CombatServices.Ports(gad).BoneCoords(spy.MyWeapon!, SWeaponDefense.FxBone).Origin.Y;
        GadgetKit.Walls(scene, GadgetKit.WallAt(300f),
            (new FVector(250f, lineY + 1f, -1000f), new FVector(400f, lineY + 60f, 1000f), CollisionMaterialFlags.None),
            (new FVector(250f, lineY - 60f, -1000f), new FVector(400f, lineY - 1f, 1000f), CollisionMaterialFlags.None));
        float lastUsed = gad.LastUsed;
        int charge = gad.Charge;

        Assert.False(gad.UseStickyCam(true));
        Assert.False(gad.UseStickyCam(false));
        Assert.Empty(Cams(scene));
        Assert.Equal(lastUsed, gad.LastUsed);
        Assert.Equal(charge, gad.Charge);
        // A valid wall is usable immediately after the refused shot.
        GadgetKit.Walls(scene, GadgetKit.WallAt(300f));
        Assert.True(gad.UseStickyCam(false));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void APlayerInTheShotDoesNotOverrideTheWallPlacementRules(bool otherIsSpy, bool noStickyWall)
    {
        (CombatScene scene, _, SPawnAttaque spy, SGadStickyCam gad) = Spy();
        FCoords muzzle = CombatServices.Ports(gad).BoneCoords(spy.MyWeapon!, SWeaponDefense.FxBone);
        (_, SBasePawn other) = scene.Player(otherIsSpy, new FVector(150f, muzzle.Origin.Y, spy.Location.Z));
        CollisionWorld world = GadgetKit.Walls(scene,
            GadgetKit.WallAt(300f, noStickyWall ? CollisionMaterialFlags.NoStickyCam : CollisionMaterialFlags.None));
        world.BindLevel(scene.Level);
        FVector start = muzzle.Origin - muzzle.XAxis * K.StickyLaunchPullBack;
        FVector end = start + muzzle.XAxis * gad.MaxLaunchDist;
        Assert.True(world.LineCheck(out FCheckResult body, spy, end, start, default,
            SGadStickyCam.StickTraceFlags | ETraceFlags.TRACE_Pawns));
        Assert.Same(other, body.Actor);

        Assert.Equal(!noStickyWall, gad.UseStickyCam(true));
        Assert.Equal(!noStickyWall, gad.UseStickyCam(false));
        if (noStickyWall)
        {
            Assert.Empty(Cams(scene));
            Assert.Equal(5, gad.Charge);
            return;
        }

        SStickyCam cam = Assert.Single(Cams(scene));
        Assert.InRange(cam.Location.X, 295.5f, 297.5f);
        Assert.Equal(4, gad.Charge);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 32768)]
    [InlineData(0, 16384)]
    [InlineData(0, -16384)]
    [InlineData(8192, 8192)]
    [InlineData(-8192, -8192)]
    public void AGrazingShotKeepsTheQuantizedMountAndCameraViewOutsideTheSurface(int shotYaw, int surfaceRoll)
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f), shotYaw);
        GadgetKit.Shocker(scene, pawn);
        SGadStickyCam gad = GadgetKit.Gadget(scene, pawn, new SGadStickyCam());
        scene.Run(1.5f);
        pc.Rotation = pc.MyDesiredRotation = new FRotator(0, shotYaw, 0);
        FCoords muzzle = CombatServices.Ports(gad).BoneCoords(pawn.MyWeapon!, SWeaponDefense.FxBone);
        float roll = surfaceRoll * MathF.PI / 32768f;
        FVector tangentNormal = muzzle.YAxis * MathF.Cos(roll) + muzzle.ZAxis * MathF.Sin(roll);
        // The shot meets a real collision triangle at less than two degrees from parallel.
        FVector normal = (tangentNormal - muzzle.XAxis * 0.03f).SafeNormal();
        FVector surface = muzzle.Origin + muzzle.XAxis * 600f;
        normal.Rotation().GetAxes(out _, out FVector side, out FVector up);
        var builder = new CollisionDataBuilder();
        builder.AddQuad(surface - side * 4000f - up * 4000f,
            surface + side * 4000f - up * 4000f, surface + side * 4000f + up * 4000f,
            surface - side * 4000f + up * 4000f);
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));

        Assert.True(gad.UseStickyCam(false));
        SStickyCam cam = Assert.Single(Cams(scene));
        float clearance = (cam.Location - surface) | normal;
        Assert.True(clearance >= 2.5f, $"The quantized mount is only {clearance:F3} uu outside the surface: {cam.Location}");

        // At the widest sticky camera FOV on a 16:9 viewport, every near-plane corner must remain on the room side
        // of the wall even when looking to the mount's pitch/yaw limits. The render near plane is 2 uu.
        float halfHeight = 2f * MathF.Tan(55f * MathF.PI / 180f) * 0.75f;
        float halfWidth = halfHeight * 16f / 9f;
        FRotator mount = cam.Rotation;
        foreach (int pitch in new[] { -13000, 0, 13000 })
        foreach (int yaw in new[] { -14000, 0, 14000 })
        {
            cam.Rotation = new FRotator(mount.Pitch + pitch, mount.Yaw + yaw, mount.Roll);
            PlayerCalcView.StickyCamView(pc, false, out _, out FVector eye, out FRotator view);
            view.GetAxes(out FVector forward, out FVector right, out FVector vertical);
            foreach (float horizontalSign in new[] { -1f, 1f })
            foreach (float verticalSign in new[] { -1f, 1f })
            {
                FVector corner = eye + forward * 2f + right * (horizontalSign * halfWidth) + vertical * (verticalSign * halfHeight);
                float outside = (corner - surface) | normal;
                Assert.True(outside > 0f,
                    $"Near plane crossed the wall by {-outside:F3} uu at pitch {pitch}, yaw {yaw}; mount clearance {clearance:F3}");
            }
        }
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
