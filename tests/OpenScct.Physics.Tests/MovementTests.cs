using System;
using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>Walking (spec/native/physics-walking.md) and falling (physics-falling.md) with the decoded constants.</summary>
public sealed class MovementTests
{
    private static Scene FloorScene(Action<CollisionDataBuilder>? more = null)
    {
        CollisionDataBuilder b = Floor();
        more?.Invoke(b);
        return Scene.Create(b.Build());
    }

    private static T Standing<T>(Scene s, T pawn, FVector at, TestController? controller = null)
        where T : Pawn
    {
        T p = s.Spawn(pawn, V(at.X, at.Y, at.Z + 1f), EPhysics.PHYS_Falling, controller);
        s.Settle(p);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
        return p;
    }

    private static TestPawn StandingPawn(Scene s, FVector at, TestController? controller = null) => Standing(s, new TestPawn(), at, controller);

    // ---- MycalcVelocity --------------------------------------------------------------------------------------

    [Fact]
    public void GroundSpeedRampsByAccelerationTimesDtUpToGroundSpeed()
    {
        Scene s = FloorScene();
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()));
        p.Acceleration = V(3000f, 0f, 0f);
        float expected = 0f;
        for (int i = 0; i < 12; i++)
        {
            s.Step(1);
            expected = SvmMath.Min(expected + 3000f * Sim.DeltaTime, 423.5f);
            Assert.Equal(expected, p.Velocity.X, 2);
        }

        Assert.Equal(0f, p.Velocity.Z);
        AssertWalkHeight(0f, p.Location.Z);
    }

    [Fact]
    public void WalkingStopsDeadWhenTheInputStops()
    {
        Scene s = FloorScene();
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()));
        p.Acceleration = V(3000f, 0f, 0f);
        s.Step(30);
        Assert.Equal(423.5f, p.Velocity.X, 1);
        p.Acceleration = default;
        s.Step(1);
        Assert.Equal(0f, p.GroundSpeed);
        Assert.Equal(0f, p.Velocity.Size(), 4);
    }

    [Fact]
    public void MycalcVelocityTurnsBrakesAndCaps()
    {
        var world = new World(1);
        TestPawn p = world.Level.SpawnActor(new TestPawn(), default, default)!;

        // Turning: the velocity swings toward the input keeping its speed (friction 8), then Acceleration dt is added.
        p.Velocity = V(300f, 0f, 0f);
        p.Acceleration = V(0f, 3000f, 0f);
        PhysicsDriver.MycalcVelocity(p, V(0f, 1f, 0f), 0.05f, 1000f, 8f, bFluid: false, bBrake: true);
        Near(V(300f - 300f * 0.4f, 300f * 0.4f + 150f, 0f), p.Velocity, 1e-3f);

        // Braking: V -= 2 V f h in 0.03 s steps then the rest; the result is the time average of the sub-step
        // velocities (game 0x10ace4d0); it stops below 10 uu/s.
        p.Acceleration = default;
        p.Velocity = V(400f, 0f, 0f);
        PhysicsDriver.MycalcVelocity(p, default, 0.05f, 1000f, 8f, bFluid: false, bBrake: true);
        float v1 = 400f * (1f - 2f * 8f * 0.03f);
        float v2 = v1 * (1f - 2f * 8f * 0.02f);
        Assert.Equal(v1 * 0.03f / 0.05f + v2 * 0.02f / 0.05f, p.Velocity.X, 3);
        p.Velocity = V(9f, 0f, 0f);
        PhysicsDriver.MycalcVelocity(p, default, 0.001f, 1000f, 8f, bFluid: false, bBrake: true);
        Assert.Equal(0f, p.Velocity.X);

        // A pawn that is not human controlled is capped at maxSpeed x DesiredSpeed.
        p.DesiredSpeed = 0.5f;
        p.Velocity = default;
        p.Acceleration = V(100000f, 0f, 0f);
        PhysicsDriver.MycalcVelocity(p, V(1f, 0f, 0f), 0.1f, 400f, 8f, bFluid: false, bBrake: true);
        Assert.Equal(200f, p.Velocity.X, 3);

        // bFluid scales the velocity by 1 - dt f before the acceleration.
        p.DesiredSpeed = 1f;
        p.Velocity = V(100f, 0f, 0f);
        p.Acceleration = V(1f, 0f, 0f);
        PhysicsDriver.MycalcVelocity(p, V(1f, 0f, 0f), 0.05f, 1000f, 8f, bFluid: true, bBrake: false);
        Assert.Equal(100f * (1f - 0.4f) + 0.05f, p.Velocity.X, 3);
    }

    // ---- floor band, steps, slopes -----------------------------------------------------------------------

    [Fact]
    public void WalkingKeepsTheFloorBandAboveBspAndMeshFloors()
    {
        Scene s = FloorScene();
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()));
        AssertWalkHeight(0f, p.Location.Z);
        Assert.Same(s.Level.Info, p.Base);
        Near(V(0f, 0f, 1f), p.Floor);

        var b = new CollisionDataBuilder();
        b.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags, [(V(-500f, -500f, -50f), V(500f, 500f, 0f))]);
        Scene m = Scene.Create(b.Build());
        TestPawn q = StandingPawn(m, V(0f, 0f, StandZ()));
        AssertWalkHeight(0f, q.Location.Z, pullback: 1f);
    }

    private static CollisionDataBuilder Stairs(float rise, int steps = 8, float run = 48f)
    {
        CollisionDataBuilder b = Floor();
        for (int k = 1; k <= steps; k++)
        {
            float y0 = 200f + run * (k - 1);
            b.AddBox(V(-100f, y0, 0f), V(100f, y0 + run, rise * k));
        }

        float top = 200f + run * steps;
        b.AddBox(V(-100f, top, 0f), V(100f, top + 2000f, rise * steps));
        return b;
    }

    [Theory]
    [InlineData(32f)]
    [InlineData(34f)]
    [InlineData(35f)]
    public void ClimbsStepsUpToTheStepHeightPlusTheFloorGap(float rise)
    {
        // stepUp lifts MAXSTEPHEIGHT (34) from 1.505 above the floor: a 35 step is climbed, 36 is not (measured 32 / 36).
        Assert.Equal(34f, K.MAXSTEPHEIGHT);
        Scene s = Scene.Create(Stairs(rise).Build());
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()));
        p.WalkSpeed = 300f;
        p.Acceleration = V(0f, 3000f, 0f);
        s.Step(4 * 60);
        Assert.True(p.Location.Y > 200f + 48f * 8f, $"stopped at {p.Location}");
        AssertWalkHeight(rise * 8f, p.Location.Z);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
    }

    [Fact]
    public void AStepAboveTheStepHeightBlocks()
    {
        Scene s = Scene.Create(Stairs(36f).Build());
        var c = new TestController();
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()), c);
        p.WalkSpeed = 300f;
        p.Acceleration = V(0f, 3000f, 0f);
        s.Step(2 * 60);
        AssertWalkHeight(0f, p.Location.Z);
        Assert.InRange(p.Location.Y, 200f - 35f - 6f, 200f - 35f);
        Assert.True(c.HitWalls > 0);
    }

    [Theory]
    [InlineData(30f, true)]
    [InlineData(44f, true)]
    [InlineData(50f, false)]
    public void SlopesWalkableToNormalZ07(float degrees, bool walkable)
    {
        float run = 300f;
        float rise = run * SvmMath.Tan(degrees * SvmMath.PI / 180f);
        Scene s = FloorScene(b =>
        {
            b.AddRamp(100f, 100f + run, -300f, 300f, 0f, rise);
            b.AddBox(V(100f + run, -300f, 0f), V(100f + run + 2000f, 300f, rise));
        });
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()));
        p.Acceleration = V(3000f, 0f, 0f);
        s.Step(3 * 60);
        if (walkable)
        {
            Assert.True(p.Location.X > 100f + run, $"stopped at {p.Location}");
            AssertWalkHeight(rise, p.Location.Z);
        }
        else
        {
            Assert.True(p.Location.Z < StandZ() + 60f, $"climbed to {p.Location}");
            Assert.True(p.Location.X < 100f + run * 0.5f);
        }
    }

    // ---- walls ---------------------------------------------------------------------------------------------

    [Fact]
    public void ThePreProbeSlidesAlongAWall()
    {
        var c = new TestController();
        Scene s = FloorScene(b => b.AddBox(V(-2000f, 100f, 0f), V(2000f, 200f, 300f)));
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()), c);
        p.Acceleration = V(3000f, 3000f, 0f);
        s.Step(2 * 60);
        // The pre-probe slides the move before contact: the pawn keeps within one move of the wall.
        Assert.InRange(p.Location.Y, 100f - 35f - 6f, 100f - 35f);
        Assert.True(p.Location.X > 300f, $"stuck at {p.Location}");
        AssertWalkHeight(0f, p.Location.Z);

        // A glancing wall is not "hit": NotifyHitWall needs velocity . normal at most MinHitWall (-1 for Controller).
        Assert.Equal(0, c.HitWalls);
    }

    [Fact]
    public void StopsInAClosedCorner()
    {
        Scene s = FloorScene(b =>
        {
            b.AddBox(V(-2000f, 100f, 0f), V(2000f, 200f, 300f));
            b.AddBox(V(100f, -2000f, 0f), V(200f, 2000f, 300f));
        });
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()));
        p.Acceleration = V(3000f, 3000f, 0f);
        s.Step(2 * 60);
        Assert.InRange(p.Location.X, 100f - 35f - 6f, 100f - 35f);
        Assert.InRange(p.Location.Y, 100f - 35f - 6f, 100f - 35f);
    }

    [Fact]
    public void AHeadOnHitRaisesNotifyHitWallThenHitWall()
    {
        var c = new TestController { MinHitWall = -0.98f };
        Scene s = FloorScene(b => b.AddBox(V(-2000f, 100f, 0f), V(2000f, 200f, 300f)));
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()), c);
        p.Acceleration = V(0f, 3000f, 0f);
        s.Step(60);
        Assert.True(c.HitWalls > 0);
        Assert.True(p.HitWalls > 0);
        Near(V(0f, -1f, 0f), p.LastHitWallNormal);

        // NotifyHitWall returning true takes the hit: no HitWall.
        int before = p.HitWalls;
        c.NotifyHitWallResult = true;
        s.Step(30);
        Assert.Equal(before, p.HitWalls);
    }

    // ---- losing the floor ----------------------------------------------------------------------------------

    private static Scene LedgeScene() => Scene.Create(Floor().Also(b => b.AddBox(V(-1000f, -500f, 0f), V(0f, 500f, 200f))).Build());

    [Fact]
    public void WalkingOffALedgeRaisesMyFallingWithLeftTimeZero()
    {
        Scene s = LedgeScene();
        var c = new TestController();
        TestPawn p = StandingPawn(s, V(-200f, 0f, StandZ(200f)), c);
        int landingsBefore = c.MyLandeds;
        p.Acceleration = V(3000f, 0f, 0f);
        for (int i = 0; i < 120 && c.MyFallings == 0; i++)
        {
            s.Step(1);
        }

        Assert.Equal(1, c.MyFallings);
        Assert.Equal(0f, c.LastLeftTime);
        Assert.True(c.MayFalls > 0);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.True(p.Location.X > 0f, $"fell at {p.Location}");
        Assert.Equal(p.Location.Z, c.fZMaxFalling);
        s.Settle(p);
        AssertWalkHeight(0f, p.Location.Z);
        Assert.Equal(landingsBefore + 1, c.MyLandeds);
    }

    [Fact]
    public void TheNativeDoesNotChangeTheModeWhenTheFloorIsLost()
    {
        Scene s = LedgeScene();
        var c = new TestController();
        TestPawn p = StandingPawn(s, V(-200f, 0f, StandZ(200f)), c);
        c.bScriptModes = false;
        p.Acceleration = V(3000f, 0f, 0f);
        s.Step(60);
        Assert.True(c.MyFallings > 0);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
    }

    [Fact]
    public void APawnThatCannotJumpStaysOnTheLedge()
    {
        Scene s = LedgeScene();
        var c = new TestController();
        TestPawn p = StandingPawn(s, V(-200f, 0f, StandZ(200f)), c);
        p.bCanJump = false;
        p.Acceleration = V(3000f, 0f, 0f);
        s.Step(90);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
        Assert.InRange(p.Location.X, -35f, 35f);
        Assert.Equal(0, c.MyFallings);
        // Stopped at the edge: Velocity and Acceleration cleared, the move timer set to -1 (then ticked down).
        Assert.True(c.MoveTimer < -1f + 1e-4f);
        Assert.Equal(default, p.Acceleration);
    }

    [Fact]
    public void TheNarrowProbeKeepsAPawnHalfOverAnEdgeWalking()
    {
        Scene s = LedgeScene();
        var c = new TestController();
        // The centre is 10 uu short of the edge: the floor probe's box still holds, and so does the narrow box.
        TestPawn p = StandingPawn(s, V(-10f, 0f, StandZ(200f)), c);
        s.Step(10);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
        Assert.Equal(0, c.MyFallings);
        AssertWalkHeight(200f, p.Location.Z);
    }

    [Fact]
    public void WithoutAControllerThereIsNoWalking()
    {
        Scene s = FloorScene();
        TestPawn p = s.SpawnPawn(V(0f, 0f, WalkZ()), EPhysics.PHYS_Walking, noController: true);
        p.Acceleration = V(3000f, 0f, 0f);
        p.Velocity = V(300f, 0f, 0f);
        s.Step(30);
        Assert.Equal(0f, p.Location.X);
    }

    [Fact]
    public void AStockControllerFallsAndLandsTheUe2Way()
    {
        Scene s = LedgeScene();
        TestPawn p = s.SpawnPawn(V(-200f, 0f, StandZ(200f) + 1f), EPhysics.PHYS_Falling, noController: true);
        var stock = new StockController();
        s.Level.SpawnActor(stock, p.Location, default);
        stock.Pawn = p;
        p.Controller = stock;
        s.Settle(p);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
        Assert.Equal(1, p.Landeds);
        p.Acceleration = V(3000f, 0f, 0f);
        for (int i = 0; i < 120 && p.Physics == EPhysics.PHYS_Walking; i++)
        {
            s.Step(1);
        }

        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.Equal(1, p.Fallings);
    }

    private sealed class StockController : Controller
    {
    }

    // ---- falling -------------------------------------------------------------------------------------------

    [Fact]
    public void FallsUnderAFixedGravityAndLandsThroughBCanLand()
    {
        Scene s = FloorScene();
        s.Level.Info.DefaultGravity = -500f; // zone gravity is not read by pawn physics
        var c = new TestController();
        TestPawn p = s.SpawnPawn(V(0f, 0f, 600f), EPhysics.PHYS_None, c);
        s.Driver.setPhysics(p, EPhysics.PHYS_Falling);
        Assert.Equal(600f, c.fZMaxFalling);
        Assert.Equal(600f, s.Driver.ZMaxFalling(p));
        int ticks = 0;
        while (p.Physics == EPhysics.PHYS_Falling && ticks < 300)
        {
            s.Step(1);
            ticks++;
        }

        // 512.4 uu at 1500 uu/s^2 takes sqrt(2 x 512.4 / 1500) = 0.83 s.
        Assert.InRange(ticks, 49, 51);
        Assert.Equal(1, c.MyLandeds);
        Near(V(0f, 0f, 1f), c.LastLandedNormal);
        Assert.InRange(c.LastLandedTimeLeft, 0f, Sim.DeltaTime);
        Assert.Equal(600f, c.ZMaxAtLanding);
        Assert.Equal(0, p.Landeds);
        s.Step(3);
        AssertWalkHeight(0f, p.Location.Z);
    }

    [Fact]
    public void JumpPeaksAtJumpZSquaredOverTwiceGravity()
    {
        Scene s = FloorScene();
        var c = new TestController();
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()), c);
        float start = p.Location.Z;
        s.Driver.MyDoJump(p);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.Equal(start, c.fZMaxFalling);
        float apex = start;
        for (int i = 0; i < 120 && p.Physics == EPhysics.PHYS_Falling; i++)
        {
            s.Step(1);
            apex = SvmMath.Max(apex, p.Location.Z);
        }

        // The half-step integrator is exact: 610^2 / 3000 = 124.03, sampled at 60 Hz 124.0 (measured 124).
        Assert.Equal(124f, apex - start, 1);
        Assert.Equal(apex, c.ZMaxAtLanding, 3);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);

        // Merc JumpZ 495 peaks at 81.7 (measured 82).
        s.Step(3);
        p.JumpZ = 495f;
        start = p.Location.Z;
        s.Driver.MyDoJump(p);
        apex = start;
        for (int i = 0; i < 120 && p.Physics == EPhysics.PHYS_Falling; i++)
        {
            s.Step(1);
            apex = SvmMath.Max(apex, p.Location.Z);
        }

        Assert.Equal(81.7f, apex - start, 0);
    }

    [Fact]
    public void HorizontalAirSpeedIsCappedAt417()
    {
        Scene s = FloorScene();
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()));
        p.Acceleration = V(3000f, 0f, 0f);
        s.Step(30);
        Assert.Equal(423.5f, p.Velocity.X, 1);
        s.Driver.MyDoJump(p);
        s.Step(1);
        Assert.Equal(417f, p.Velocity.Size2D(), 2);
        Assert.Equal(V(3000f, 0f, 0f), p.Acceleration);
    }

    [Fact]
    public void AirAccelerationIsClampedToAirControlTimesAccelRate()
    {
        Scene s = FloorScene();
        TestPawn p = s.SpawnPawn(V(0f, 0f, 3000f), EPhysics.PHYS_Falling);
        p.Velocity = V(100f, 0f, 0f);
        p.Acceleration = V(0f, 50000f, 0f);
        s.Step(1);
        // Half a step of the clamped 4096 uu/s^2 goes into the move (the rest is the displacement rewrite).
        Assert.Equal(0.5f * 4096f * Sim.DeltaTime, p.Velocity.Y, 2);
        Assert.Equal(V(0f, 50000f, 0f), p.Acceleration);
    }

    [Fact]
    public void FallSpeedIsCappedAt2500()
    {
        Scene s = FloorScene(b => b.AddBox(V(-100f, -100f, -40000f), V(100f, 100f, -39000f)));
        TestPawn p = s.SpawnPawn(V(5000f, 0f, 3000f), EPhysics.PHYS_Falling);
        s.Collision.KillZ = -100000f;
        s.Step(200);
        Assert.Equal(-2500f, p.Velocity.Z, 1);
    }

    [Fact]
    public void TheServerCopyOfARemotePlayerStaysFallingOnTheFloor()
    {
        Scene s = FloorScene();
        var c = new TestController { RemoteRole = ENetRole.ROLE_AutonomousProxy };
        TestPawn p = s.SpawnPawn(V(0f, 0f, 300f), EPhysics.PHYS_Falling, c);
        s.Step(60);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.Equal(0, c.MyLandeds);
        Assert.InRange(p.Location.Z, StandZ(), StandZ() + 1f);
    }

    [Fact]
    public void FellOutOfWorldBelowTheDeathHeight()
    {
        Scene s = FloorScene();
        s.Level.Info.fDeathPawnHeight = -500f;
        var c = new TestController();
        TestPawn p = s.SpawnPawn(V(10000f, 0f, 0f), EPhysics.PHYS_Falling, c);
        s.Step(60);
        Assert.True(c.FellOut > 0);
        Assert.True(p.Location.Z < -500f);
    }

    [Fact]
    public void KillZRaisesFellOutOfWorldOnceWithoutADeathHeight()
    {
        Scene s = FloorScene();
        var c = new TestController();
        TestPawn p = s.SpawnPawn(V(10000f, 0f, 0f), EPhysics.PHYS_Falling, c);
        s.Collision.KillZ = -500f;
        s.Step(60);
        Assert.Equal(1, c.FellOut);
        Assert.True(p.Location.Z < -500f);
    }

    // ---- drop attacks ----------------------------------------------------------------------------------------

    [Fact]
    public void StandingOnADropAttackablePawnDropAttacksIt()
    {
        Scene s = FloorScene();
        var targetController = new TestController { CanBeDropAttacked = true };
        TestSBasePawn merc = Standing(s, new TestSBasePawn { Team = PawnType.SPAWNDEFENSE }, V(0f, 0f, StandZ()), targetController);
        var c = new TestController();
        TestSBasePawn spy = s.SpawnSpy(V(0f, 0f, merc.Location.Z + 180f), EPhysics.PHYS_Falling, c);
        s.Step(60);
        Assert.Same(merc, spy.Base);
        Assert.True(c.DropAttacks > 0);
        Assert.Same(merc, c.LastDropAttack);

        // A sticky-skinned base gets StickyBlast instead.
        merc.StickySkinned = true;
        int drops = c.DropAttacks;
        s.Step(1);
        Assert.True(c.StickyBlasts > 0);
        Assert.Equal(drops, c.DropAttacks);
    }

    [Fact]
    public void ACrouchedPawnUncrouchesBeforeTheDropAttack()
    {
        Scene s = FloorScene();
        var targetController = new TestController { CanBeDropAttacked = true };
        TestSBasePawn merc = Standing(s, new TestSBasePawn { Team = PawnType.SPAWNDEFENSE }, V(0f, 0f, StandZ()), targetController);
        var c = new TestController();
        TestSBasePawn spy = s.SpawnSpy(V(0f, 0f, merc.Location.Z + 180f), EPhysics.PHYS_Falling, c);
        s.Step(30);
        spy.bIsCrouched = true;
        spy.UnCrouchSucceeds = false;
        int drops = c.DropAttacks;
        s.Step(1);
        Assert.True(spy.UnCrouchCalls > 0);
        Assert.Equal(drops, c.DropAttacks);
        spy.UnCrouchSucceeds = true;
        s.Step(1);
        Assert.True(c.DropAttacks > drops);
    }

    [Fact]
    public void FallingTowardAnEnemyHomesAndTriggersTheDropAttack()
    {
        Scene s = FloorScene();
        var targetController = new TestController { CanBeDropAttacked = true };
        TestSBasePawn merc = Standing(s, new TestSBasePawn { Team = PawnType.SPAWNDEFENSE }, V(300f, 0f, StandZ()), targetController);
        var c = new TestController();
        TestSBasePawn spy = s.SpawnSpy(V(0f, 0f, 600f), EPhysics.PHYS_Falling, c);
        s.Driver.setPhysics(spy, EPhysics.PHYS_None);
        s.Driver.setPhysics(spy, EPhysics.PHYS_Falling);
        spy.Velocity = V(300f, 0f, -200f);
        s.Step(1);
        Assert.Same(merc, spy.AttackTarget);

        // A friend is not a target; nor is an enemy whose controller refuses.
        targetController.CanBeDropAttacked = false;
        s.Step(1);
        Assert.Null(spy.AttackTarget);
        targetController.CanBeDropAttacked = true;
        for (int i = 0; i < 120 && c.DropAttacks == 0; i++)
        {
            s.Step(1);
        }

        Assert.True(c.DropAttacks > 0, $"spy at {spy.Location}, merc at {merc.Location}");
        Assert.Same(merc, c.LastDropAttack);
    }

    [Theory]
    [InlineData(450f, true)]
    [InlineData(2500f, false)]
    public void ThePassingTriggerDropAttacksUnlessTheFallWouldKill(float height, bool attacks)
    {
        // The spy falls past the merc, 100 uu in front of it: close (2.1 H), in front (0.7), level (0.5 H), overlapping
        // in height (-100) and fast (below -800). A fall that would kill (damage above Health) does not attack.
        Scene s = FloorScene();
        var targetController = new TestController { CanBeDropAttacked = true };
        TestSBasePawn merc = Standing(s, new TestSBasePawn { Team = PawnType.SPAWNDEFENSE }, V(100f, 0f, StandZ()), targetController);
        var c = new TestController();
        TestSBasePawn spy = s.SpawnSpy(V(0f, 0f, StandZ() + height), EPhysics.PHYS_None, c);
        s.Driver.setPhysics(spy, EPhysics.PHYS_Falling);
        for (int i = 0; i < 180 && spy.Physics == EPhysics.PHYS_Falling; i++)
        {
            s.Step(1);
        }

        Assert.Equal(attacks, c.DropAttacks > 0);
        if (attacks)
        {
            Assert.Same(merc, c.LastDropAttack);
        }
    }

    // ---- movers and touches --------------------------------------------------------------------------------

    private static Scene MoverScene(FVector location, FVector localMin, FVector localMax, out TestMover mover,
        Action<CollisionDataBuilder>? more = null)
    {
        CollisionDataBuilder b = Floor();
        more?.Invoke(b);
        b.AddBoxHullCollider(ColliderKind.Mover, "Mover0", CollisionDataBuilder.WorldFlags, localMin, localMax, location);
        TestMover? made = null;
        Scene s = Scene.Create(b.Build(), level =>
        {
            made = level.SpawnActor(new TestMover(), location, default)!;
            made.BasePos = location;
            return new Dictionary<string, Actor> { ["Mover0"] = made };
        });
        mover = made!;
        return s;
    }

    [Fact]
    public void ALiftShovesItsRiderUpAndLeavesItToItsOwnPhysicsAcross()
    {
        Scene s = MoverScene(V(0f, 0f, 100f), V(-150f, -150f, -20f), V(150f, 150f, 0f), out TestMover lift);
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ(100f)));
        Assert.Same(lift, p.Base);

        lift.KeyPos[1] = V(0f, 0f, 200f);
        lift.InterpolateTo(1, 1f);
        s.Step(70);
        Assert.False(lift.bInterpolating);
        Assert.Equal(1, lift.KeysReached);
        Assert.Equal(300f, lift.Location.Z, 3);
        Assert.InRange(p.Location.Z, StandZ(300f), StandZ(300f) + 3f);
        Assert.Same(lift, p.Base);
        Assert.Empty(lift.Encroached);

        // Based pawns are not carried sideways: the lift slides out from under the pawn.
        lift.KeyPos[2] = V(400f, 0f, 200f);
        lift.InterpolateTo(2, 1f);
        s.Step(70);
        Assert.Equal(400f, lift.Location.X, 3);
        Assert.Equal(0f, p.Location.X, 1);
    }

    [Fact]
    public void AMoverShovesAPawnInItsWay()
    {
        Scene s = MoverScene(V(0f, 0f, 0f), V(-10f, -100f, 0f), V(10f, 100f, 200f), out TestMover door);
        TestPawn p = StandingPawn(s, V(100f, 0f, StandZ()));
        door.KeyPos[1] = V(300f, 0f, 0f);
        door.InterpolateTo(1, 1f);
        s.Step(70);
        Assert.Equal(300f, door.Location.X, 3);
        Assert.True(p.Location.X >= 300f + 10f + 35f - 0.5f, $"pawn at {p.Location}");
        Assert.Empty(door.Encroached);
    }

    [Theory]
    [InlineData(EMoverEncroachType.ME_StopWhenEncroach)]
    [InlineData(EMoverEncroachType.ME_CrushWhenEncroach)]
    public void ABlockedMoverAsksEncroachingOn(EMoverEncroachType rule)
    {
        // A wall at x 250 pins the pawn against the door.
        Scene s = MoverScene(V(0f, 0f, 0f), V(-10f, -100f, 0f), V(10f, 100f, 200f), out TestMover door,
            b => b.AddBox(V(250f, -500f, 0f), V(300f, 500f, 300f)));
        TestPawn p = StandingPawn(s, V(100f, 0f, StandZ()));
        door.MoverEncroachType = rule;
        door.KeyPos[1] = V(300f, 0f, 0f);
        door.InterpolateTo(1, 1f);
        s.Step(70);
        // The pawn is shoved while the shove clears the door; a blocked shove is undone, so it ends within a step of
        // the wall and never in it.
        Assert.InRange(p.Location.X, 209f, 215f);
        Assert.Contains(p, door.Encroached);
        if (rule == EMoverEncroachType.ME_StopWhenEncroach)
        {
            Assert.InRange(door.Location.X, 160f, 170f);
            Assert.True(door.bInterpolating);
            Assert.Empty(door.Crushed);
            Assert.Equal(0, p.Encroached);
        }
        else
        {
            Assert.Equal(300f, door.Location.X, 3);
            Assert.Contains(p, door.Crushed);
            Assert.True(p.Encroached > 0);
        }
    }

    [Fact]
    public void TouchesTriggersItWalksThrough()
    {
        Scene s = FloorScene();
        TestTrigger trigger = s.Level.SpawnActor(new TestTrigger(), V(300f, 0f, 64f), default)!;
        s.Collision.NotifyCollisionChanged();
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()));
        p.Acceleration = V(3000f, 0f, 0f);
        s.Step(90);
        Assert.True(p.Location.X > 500f);
        Assert.Equal(1, trigger.Touches);
        Assert.Equal(1, trigger.UnTouches);
        Assert.Equal(1, p.Touches);
        Assert.Equal(1, p.UnTouches);
        Assert.Empty(p.Touching);
    }

    [Fact]
    public void BumpsIntoAnotherPawnAndStops()
    {
        Scene s = FloorScene();
        var c = new TestController();
        TestPawn other = StandingPawn(s, V(300f, 0f, StandZ()));
        TestPawn p = StandingPawn(s, V(0f, 0f, StandZ()), c);
        p.Acceleration = V(3000f, 0f, 0f);
        s.Step(60);
        // Pawns sweep as boxes against cylinders widened by the box: 35 + 35, minus the 0.001 time pull-back.
        Assert.InRange(p.Location.X, 300f - 70f - 0.1f, 300f - 70f);
        Assert.True(c.Bumps > 0);
        Assert.True(other.Bumps > 0);
        Assert.Same(p, other.LastBump);
    }

    [Fact]
    public void LandsOnAnotherPawnsHeadAndStandsThere()
    {
        Scene s = FloorScene();
        var c = new TestController();
        TestPawn below = StandingPawn(s, V(0f, 0f, StandZ()));
        TestPawn p = s.SpawnPawn(V(0f, 0f, 500f), EPhysics.PHYS_Falling, c);
        s.Settle(p);
        Assert.Same(below, p.Base);
        Assert.Equal(1, c.MyLandeds);
        Assert.Equal(0, c.DropAttacks);
        Assert.InRange(p.Location.Z, below.Location.Z + 175f, below.Location.Z + 175f + 2f);
    }
}

internal static class BuilderExtensions
{
    public static CollisionDataBuilder Also(this CollisionDataBuilder b, Action<CollisionDataBuilder> more)
    {
        more(b);
        return b;
    }
}
