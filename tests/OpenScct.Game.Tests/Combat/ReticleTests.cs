using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Movement;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The merc's reticle sits where his shots go (spec/hud/merc-reticle.md rules 3a to 3d): SHud.SpotReticule is the
/// screen point, in 640 x 480 canvas units, of the eye plus 15000 uu along MyDesiredRotation seen through the view;
/// the rifle traces along the same line from the same eye (merc-rifle.md rule 10), which crouching lowers (the eye
/// eases to BaseCrouchEyeHeight: spec/movement/pawn.md "Eye position").
/// </summary>
public sealed class ReticleTests
{
    private const float Fov = 95f;
    private const float Aspect = 16f / 9f;

    [Fact]
    public void AnAimOnTheViewAxisLandsHalfAUnitPastTheCanvasCentreAsInTheOriginal()
    {
        var cam = new FVector(100f, 200f, 300f);
        var view = new FRotator(-1200, 9000, 0);
        view.GetAxes(out FVector x, out _, out _);

        (float X, float Y)? at = SHud.ProjectToCanvas(cam, view, Fov, Aspect, cam + (x * 15000f));

        // Measured in the sandbox: (320.5, 240.5) whenever the aim is the view, standing, crouched or strafing.
        Assert.NotNull(at);
        Assert.Equal(320.5f, at.Value.X, 3);
        Assert.Equal(240.5f, at.Value.Y, 3);
    }

    [Fact]
    public void TheCanvasSpansTheFourThreeFrameOfFovAngleAndWiderScreensAddWidth()
    {
        // FovAngle 90 over a 4:3 frame: tan 45 = 1 reaches the frame's side; the frame's top is at 3/4 of that.
        var view = new FRotator(0, 0, 0);
        (float X, float Y)? side = SHud.ProjectToCanvas(default, view, 90f, 4f / 3f, new FVector(1000f, 1000f, 0f));
        (float X, float Y)? top = SHud.ProjectToCanvas(default, view, 90f, 4f / 3f, new FVector(1000f, 0f, 750f));
        (float X, float Y)? wideSide = SHud.ProjectToCanvas(default, view, 90f, 16f / 9f, new FVector(1000f, 1000f, 0f));

        Assert.Equal(640.5f, side!.Value.X, 3);
        Assert.Equal(240.5f, side.Value.Y, 3);
        Assert.Equal(320.5f, top!.Value.X, 3);
        Assert.Equal(0.5f, top.Value.Y, 3);

        // 16:9 keeps the 4:3 frame's height: the same point is 3/4 of the way out from the centre.
        Assert.Equal(320.5f + (320f * 0.75f), wideSide!.Value.X, 3);
        Assert.Null(SHud.ProjectToCanvas(default, view, 90f, Aspect, new FVector(-10f, 0f, 0f)));
    }

    [Fact]
    public void TheAimPointIsTheEyeAsTheNativeReadsItPlusFifteenThousandAlongTheAim()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(false, new FVector(10f, 20f, 100f));
        pawn.EyeHeight = 55.5f;
        pawn.WalkBob = new FVector(1f, 2f, 3f);
        pc.MyDesiredRotation = new FRotator(0, 16384, 0);

        FVector aim = SHud.SpotReticuleAimPoint(pawn, pc);

        Assert.Equal(11f, aim.X, 1);
        Assert.Equal(22f + 15000f, aim.Y, 1);
        Assert.Equal(100f + 3f + 55.5f, aim.Z, 2);
    }

    [Fact]
    public void ARecoilKickOfTheAimMovesTheReticleOffTheViewCentre()
    {
        (SHud hud, SPlayerController pc, SBasePawn pawn) = MercHud();
        pc.vCamLocation = pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);
        pc.rCamRotation = new FRotator(0, 0, 0);
        pc.MyDesiredRotation = new FRotator(300, 0, 0);

        hud.UpdateSpotReticule(p => SHud.ProjectToCanvas(pc.vCamLocation, pc.rCamRotation, Fov, Aspect, p));

        // 300 units of pitch up: the aim point is above the centre, by tan(300 / 65536 x 2 pi) over the frame.
        Assert.Equal(320.5f, hud.SpotReticule.X, 2);
        Assert.True(hud.SpotReticule.Y < 240.5f - 5f, $"y {hud.SpotReticule.Y}");
        Assert.Equal(0f, hud.SpotReticule.Z);
    }

    [Theory]
    [InlineData("holstered")]
    [InlineData("reticuleOff")]
    [InlineData("noWeapon")]
    public void TheAimPointKeepsItsLastValueWhenHudDefSkipsIt(string why)
    {
        (SHud hud, SPlayerController pc, SBasePawn pawn) = MercHud();
        hud.SpotReticule = new FVector(1f, 2f, 0f);
        switch (why)
        {
            case "holstered":
                pawn.WeaponLoaded = 0;
                break;
            case "reticuleOff":
                hud.bReticuleOff = true;
                break;
            default:
                pawn.MyWeapon = null;
                break;
        }

        hud.UpdateSpotReticule(_ => (320.5f, 240.5f));

        Assert.Equal(new FVector(1f, 2f, 0f), hud.SpotReticule);
        Assert.NotNull(pc);
    }

    [Fact]
    public void TheReticleFlashAlphaFadesOutThenInOverEachPeriod()
    {
        (SHud hud, _, _) = MercHud();
        hud.bReticuleFlash = true;
        hud.ReticuleFlashOn = 0.2f;
        hud.ReticuleFlashOff = 0.3f;
        float now = hud.Level.TimeSeconds;

        hud.ReticuleFlashTime = now - 0.1f;
        hud.UpdateSpotReticule(_ => null);
        Assert.Equal(0.5f, hud.ReticuleFlashAlpha, 3);

        hud.ReticuleFlashTime = now - 0.35f;
        hud.UpdateSpotReticule(_ => null);
        Assert.Equal(0.5f, hud.ReticuleFlashAlpha, 3);

        // Past a whole period (0.5 s) the period start moves on by one period.
        hud.ReticuleFlashTime = now - 0.6f;
        hud.UpdateSpotReticule(_ => null);
        Assert.Equal(1f, hud.ReticuleFlashAlpha, 3);
        Assert.Equal(now - 0.1f, hud.ReticuleFlashTime, 3);

        hud.bReticuleFlash = false;
        hud.UpdateSpotReticule(_ => null);
        Assert.Equal(1f, hud.ReticuleFlashAlpha);
    }

    [Fact]
    public void ACrouchingMercsEyeEasesDownToItsCrouchedBaseLikeTheOriginal()
    {
        PawnRig rig = MovementTestKit.Rig(PawnTeam.Merc);
        Assert.True(rig.Pawn.bUpdateEyeheight);
        float standingEye = rig.Pawn.Location.Z + rig.Pawn.EyeHeight;
        float floor = rig.Pawn.Location.Z - rig.Pawn.CollisionHeight;

        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        rig.Hold(default, 90);

        // Original (sandbox capture): EyeHeight 55.5 standing, 20.0 crouched; the eye 144.0 and 89.0 over the floor.
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.Equal(20f, rig.Pawn.EyeHeight, 1);
        float crouchedEye = rig.Pawn.Location.Z + rig.Pawn.EyeHeight;
        Assert.Equal(55.5f + 87.5f, standingEye - floor, 0);
        Assert.Equal(20f + rig.Pawn.CollisionHeight, crouchedEye - floor, 0);
        Assert.True(standingEye - crouchedEye > 50f, $"the eye only went from {standingEye} to {crouchedEye}");
    }

    [Fact]
    public void TheCrouchingEyeStartsWhereItWasAndEasesAtTenPerSecond()
    {
        PawnRig rig = MovementTestKit.Rig(PawnTeam.Merc);
        float before = rig.Pawn.Location.Z + rig.Pawn.EyeHeight;
        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        float firstTick = rig.Pawn.Location.Z + rig.Pawn.EyeHeight;

        // The crouch lowers the pawn and raises EyeHeight by the same amount (StartCrouch); one tick of easing
        // (smooth = 10 / 60) then moves the eye a sixth of the way down.
        float target = rig.Pawn.Location.Z + rig.Pawn.BaseEyeHeight;
        Assert.True(firstTick < before && firstTick > target, $"{before} -> {firstTick} (target {target})");
        Assert.Equal(before - ((before - target) / 6f), firstTick, 0);
    }

    [Fact]
    public void TheMercsRifleFxBoneIsWhereTheOriginalThrowsFrom()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), merc);
        merc.EyeHeight = 55.5f;
        pc.MyDesiredRotation = new FRotator(-3584, 16128, 0);

        FCoords fx = OpenScct.Game.Combat.CombatNatives.MercThrowFxCoords(merc, pc.MyDesiredRotation);
        _ = rifle;

        // Sandbox capture: the frag spawned 25.3 forward, 11 right, 12.9 down of the eye (the FX bone plus 40 along the aim).
        pc.MyDesiredRotation.GetAxes(out FVector x, out FVector y, out FVector z);
        FVector rel = fx.Origin + (x * 40f) - (merc.Location + new FVector(0f, 0f, 55.5f));
        Assert.Equal(25.3f, rel | x, 1);
        Assert.Equal(11f, rel | y, 1);
        Assert.Equal(-12.9f, rel | z, 1);
    }

    [Fact]
    public void AFallingFragLosesSevenHundredFiftyUnitsASecondOfVerticalSpeedLikeTheOriginals()
    {
        var world = new World(1);
        var collision = OpenScct.Physics.Collision.CollisionWorld.Build(MovementTestKit.Floor().Build());
        collision.BindLevel(world.Level, _ => null);
        var driver = new OpenScct.Physics.Movement.PhysicsDriver(collision);
        world.Level.PhysicsDriver = driver;
        SProjFrag frag = world.Level.SpawnActor(new SProjFrag(), new FVector(0f, 0f, 500f), default)!;
        frag.Physics = EPhysics.PHYS_Falling;
        frag.Velocity = new FVector(700f, 0f, 0f);

        driver.performPhysics(frag, 1f / 60f);
        driver.performPhysics(frag, 1f / 60f);

        // Original: -12.48 uu/s per 1/60 s tick (decoded: half of (Acceleration + 1500 down) per second), and the move
        // is the new velocity times the step: -12.5 then -25 uu/s, so 0.208 + 0.417 uu down.
        Assert.Equal(-25f, frag.Velocity.Z, 2);
        Assert.Equal(500f - (12.5f / 60f) - (25f / 60f), frag.Location.Z, 2);
        Assert.Equal(700f * 2f / 60f, frag.Location.X, 2);
    }

    private static (SHud Hud, SPlayerController PC, SBasePawn Pawn) MercHud()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(false, new FVector(0f, 0f, 100f));
        SHud hud = scene.Level.SpawnActor(new SHud(), pawn.Location, default)!;
        hud.PlayerOwner = pc;
        hud.P = pawn;
        pawn.MyWeapon ??= scene.Give(new SWeaponDefense(), pawn);
        pawn.WeaponLoaded = 2;
        pawn.EyeHeight = 55.5f;
        return (hud, pc, pawn);
    }
}
