using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The merc kit natives decoded for the wave: the grenade launch and bounce (grenade-throw.md rules 4 and 10), the
/// merc's throw origin (UseGren), the torch placement (merc-flashlight.md rule 7) and the laser's end point and mark
/// target (merc-laser.md rule 9).
/// </summary>
public sealed class MercKitTests
{
    private static (CombatScene Scene, SPlayerController PC, SBasePawn Merc, SWeaponDefense Rifle) Merc(FVector At = default)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, At == default ? new FVector(0f, 0f, 100f) : At);
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), merc);
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        scene.Run(2.5f);
        return (scene, pc, merc, rifle);
    }

    private static SProjFrag Frag(CombatScene scene, Actor owner, FRotator rotation, FVector at)
    {
        SProjFrag frag = scene.Level.Spawn<SProjFrag>(at, rotation, owner, owner as Pawn)!;
        return frag;
    }

    [Fact]
    public void AnAimedLaunchFliesAtTheForcePlus210WithNoSpeedCap()
    {
        (CombatScene scene, _, SBasePawn merc, _) = Merc();
        SProjFrag frag = Frag(scene, merc, new FRotator(0, 0, 0), new FVector(100f, 0f, 150f));
        frag.Launch(1800, false);
        Assert.Equal(2010f, frag.Velocity.Size(), 1);
        Assert.Equal(2010f, frag.Velocity.X, 1);
    }

    [Fact]
    public void TheThrowersSpeedAlongTheAimAddsToTheLaunch()
    {
        (CombatScene scene, _, SBasePawn merc, _) = Merc();
        merc.Velocity = new FVector(300f, 400f, 0f);
        SProjFrag frag = Frag(scene, merc, new FRotator(0, 0, 0), new FVector(100f, 0f, 150f));
        frag.Launch(450, false);

        // Only the part of the thrower's velocity along the aim counts: 300 of it here.
        Assert.Equal(450f + 210f + 300f, frag.Velocity.X, 1);
        Assert.Equal(0f, frag.Velocity.Y, 1);
    }

    [Fact]
    public void AHalfChargedMercThrowStartsFortyUnitsAlongTheAimFromTheRifle()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn merc, SWeaponDefense rifle) = Merc();
        pc.MyDesiredRotation = new FRotator(1000, 8000, 0);
        SGadFragGrenade gadget = GadgetKit.Gadget(scene, merc, new SGadFragGrenade());
        gadget.UseGren(1012);
        SProjFrag? thrown = null;
        foreach (SProjFrag p in scene.Level.DynamicActors<SProjFrag>())
        {
            thrown = p;
        }

        Assert.NotNull(thrown);
        // Thrown from the FX bone of the rifle held for the throw (grenade-throw.md rule 4a).
        FCoords fx = OpenScct.Game.Combat.CombatNatives.MercThrowFxCoords(merc, pc.MyDesiredRotation);
        _ = rifle;
        FVector want = fx.Origin + (pc.MyDesiredRotation.Vector() * 40f);
        Assert.True(FVector.Dist(want, thrown.Location) < 1.5f, $"thrown at {thrown.Location}, decoded origin {want}");
        Assert.Equal(1012f + 210f, thrown.Velocity.Size(), 0);
    }

    [Fact]
    public void ABounceMirrorsTheVelocityAndKeepsAFifthForAFrag()
    {
        (CombatScene scene, _, SBasePawn merc, _) = Merc();
        GadgetKit.Walls(scene, GadgetKit.WallAt(300f));
        SProjFrag frag = Frag(scene, merc, default, new FVector(296f, 0f, 150f));
        frag.SetPhysics(EPhysics.PHYS_Falling);
        frag.Velocity = new FVector(1000f, 0f, 0f);
        frag.eventHitWall(new FVector(-1f, 0f, 0f), scene.Level.Info);
        Assert.Equal(-200f, frag.Velocity.X, 1);
        Assert.True(frag.bAlreadyRebounded);
        Assert.Equal(EPhysics.PHYS_Falling, frag.Physics);
    }

    [Fact]
    public void AFastUpwardBounceIsHalvedTowardFourHundred()
    {
        (CombatScene scene, _, SBasePawn merc, _) = Merc();
        GadgetKit.Walls(scene, (new FVector(-1000f, -1000f, -40f), new FVector(1000f, 1000f, 0f), OpenScct.Content.Collision.CollisionMaterialFlags.None));
        SProjFrag frag = Frag(scene, merc, default, new FVector(0f, 0f, 3f));
        frag.SetPhysics(EPhysics.PHYS_Falling);
        frag.Velocity = new FVector(0f, 0f, -5000f);
        frag.eventHitWall(new FVector(0f, 0f, 1f), scene.Level.Info);

        // 5000 x 0.2 = 1000 up, then (1000 + 400) / 2.
        Assert.Equal(700f, frag.Velocity.Z, 1);
    }

    [Fact]
    public void AFragWithItsFuseKeepsBouncingWhenSlowInsteadOfExploding()
    {
        (CombatScene scene, _, SBasePawn merc, _) = Merc();
        GadgetKit.Walls(scene, (new FVector(-1000f, -1000f, -40f), new FVector(1000f, 1000f, 0f), OpenScct.Content.Collision.CollisionMaterialFlags.None));
        SProjFrag frag = Frag(scene, merc, default, new FVector(0f, 0f, 3f));
        frag.SetPhysics(EPhysics.PHYS_Falling);
        frag.Velocity = new FVector(0f, 0f, -50f);
        frag.eventHitWall(new FVector(0f, 0f, 1f), scene.Level.Info);
        Assert.False(frag.bDeleteMe);
        Assert.Equal(10f, frag.Velocity.Z, 1);
    }

    [Fact]
    public void ABounceWithNothingBehindTheHitExplodesTheGrenadeOnTheServer()
    {
        (CombatScene scene, _, SBasePawn merc, _) = Merc();
        GadgetKit.Walls(scene, GadgetKit.WallAt(5000f));
        SProjFrag frag = Frag(scene, merc, default, new FVector(100f, 0f, 150f));
        frag.SetPhysics(EPhysics.PHYS_Falling);
        frag.Velocity = new FVector(500f, 0f, 0f);
        frag.eventHitWall(new FVector(-1f, 0f, 0f), scene.Level.Info);
        Assert.True(frag.bDeleteMe);
        Assert.True(frag.bExploding);
    }

    [Fact]
    public void AFallingFragBouncesOffTheFloorInsteadOfLanding()
    {
        (CombatScene scene, _, SBasePawn merc, _) = Merc();
        OpenScct.Physics.Collision.CollisionWorld cw = GadgetKit.Walls(scene, (new FVector(-1000f, -1000f, -40f), new FVector(1000f, 1000f, 0f), OpenScct.Content.Collision.CollisionMaterialFlags.None));
        cw.BindLevel(scene.Level, _ => null);
        scene.Level.PhysicsDriver = new OpenScct.Physics.Movement.PhysicsDriver(cw);
        SProjFrag frag = Frag(scene, merc, default, new FVector(0f, 0f, 60f));
        frag.SetPhysics(EPhysics.PHYS_Falling);
        frag.Velocity = new FVector(200f, 0f, -600f);
        bool bounced = false;
        for (int i = 0; i < 40 && !bounced; i++)
        {
            scene.World.Step();
            bounced = frag.bAlreadyRebounded;
        }

        Assert.True(bounced, $"no bounce: {frag.Velocity} at {frag.Location}, physics {frag.Physics}, deleted {frag.bDeleteMe}");
        Assert.Equal(EPhysics.PHYS_Falling, frag.Physics);
        Assert.True(frag.Velocity.Z > 0f, $"after the bounce {frag.Velocity} at {frag.Location}");
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10000, 0, 3000)]
    [InlineData(0, 10000, 7000)]
    [InlineData(65000, 100, -90)]
    public void TheTorchPointsAlongTheDesiredAimLeaningThreeTenthsToTheBody(int bodyYaw, int desiredYaw, int expectedYaw)
    {
        (_, _, SBasePawn merc, _) = Merc();
        merc.IK_Type = IK_Type_.IKT_WEAPONLOADED;
        merc.Rotation = new FRotator(0, bodyYaw, 0);
        merc.iDesiredYawPitch = (1200 << 16) | (desiredYaw & 0xFFFF);
        FRotator? aim = SGadTorche.AimRotation(merc);
        Assert.NotNull(aim);
        Assert.Equal(1200, aim.Value.Pitch);
        Assert.Equal(expectedYaw, aim.Value.Yaw);
    }

    [Fact]
    public void TheTorchIsLitExactlyWhileInUse()
    {
        (CombatScene scene, _, SBasePawn merc, _) = Merc();
        var torch = GadgetKit.Gadget(scene, merc, new SGadTorche(), Select: false);
        torch.bInUse = true;
        torch.NativeTickTorche();
        Assert.Equal(ELightType.LT_Steady, torch.LightType);
        Assert.True(merc.bIgnoreOwnEchelonLights);

        // A malfunction's random switch is overwritten by the native in the same tick.
        torch.LightType = ELightType.LT_None;
        torch.NativeTickTorche();
        Assert.Equal(ELightType.LT_Steady, torch.LightType);
        torch.bInUse = false;
        torch.NativeTickTorche();
        Assert.Equal(ELightType.LT_None, torch.LightType);
    }

    [Fact]
    public void TheLaserMarksTheFirstLivingPawnOnItsBeam()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn merc, _) = Merc();
        pc.MyDesiredRotation = new FRotator(0, 0, 0);
        (_, SBasePawn spy) = scene.Player(true, new FVector(400f, 0f, 100f));
        var laser = GadgetKit.Gadget(scene, merc, new SGadTorcheLaser(), Select: false);
        laser.bInUse = true;
        laser.NativeTickTorcheLaser(Sim.DeltaTime);
        Assert.Same(spy, laser.TargetPawn);
        Assert.Equal(SvmMath.Trunc(scene.Time), laser.TimeTarget);
        Assert.True(laser.HitLocation.X < 420f);

        spy.Health = 0;
        laser.NativeTickTorcheLaser(Sim.DeltaTime);
        Assert.Null(laser.TargetPawn);
        Assert.Equal(0, laser.TimeTarget);
    }

    [Fact]
    public void TheLaserRunsToAPointFifteenThousandUnitsOutRaisedFromTheMerc()
    {
        (CombatScene scene, SPlayerController pc, SBasePawn merc, _) = Merc();
        pc.MyDesiredRotation = new FRotator(0, 0, 0);
        var laser = GadgetKit.Gadget(scene, merc, new SGadTorcheLaser(), Select: false);
        laser.bInUse = true;
        laser.NativeTickTorcheLaser(Sim.DeltaTime);
        // Along the aimed rifle's FX bone, which points a little left of and under the aim (merc-laser.md rule 9a).
        FVector dir = OpenScct.Game.Combat.CombatNatives.MercAimFxCoords(merc, pc.MyDesiredRotation).XAxis.SafeNormal();
        FVector want = merc.Location + (dir * 15000f) + new FVector(0f, 0f, 55.5f);
        Assert.True(FVector.Dist(want, laser.HitLocation) < 1f, $"end {laser.HitLocation}, want {want}");
        Assert.InRange(want.X - merc.Location.X, 14990f, 15000f);
    }
}
