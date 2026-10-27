using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>performPhysics, startNewPhysics, setPhysics (physics-dispatch.md) and root motion, repositioning, Vandamme.</summary>
public sealed class DispatchAndRootMotionTests
{
    private sealed class CountingMode : IPhysicsModeHandler
    {
        public int Calls;

        public void performPhysics(PhysicsDriver Driver, Actor Actor, float DeltaSeconds) => Calls++;
    }

    private static Scene FloorScene(System.Action<CollisionDataBuilder>? more = null)
    {
        CollisionDataBuilder b = Floor();
        more?.Invoke(b);
        return Scene.Create(b.Build());
    }

    // ---- dispatch ------------------------------------------------------------------------------------------

    [Fact]
    public void StartNewPhysicsHasAMinimumStepAndAnIterationLimit()
    {
        Scene s = FloorScene();
        TestPawn p = s.SpawnPawn(V(0f, 0f, 500f), EPhysics.PHYS_Flying);
        var mode = new CountingMode();
        s.Driver.SetModeHandler(EPhysics.PHYS_Flying, mode);
        s.Driver.startNewPhysics(p, 0.00029f, 0);
        Assert.Equal(0, mode.Calls);
        s.Driver.startNewPhysics(p, 0.0003f, 0);
        Assert.Equal(1, mode.Calls);
        s.Driver.startNewPhysics(p, 0.1f, 8);
        Assert.Equal(1, mode.Calls);
        s.Driver.startNewPhysics(p, 0.1f, 7);
        Assert.Equal(2, mode.Calls);
        Assert.Equal(0.0003f, K.Dispatch.MinStep);
        Assert.Equal(7, K.Dispatch.MaxIterations);
    }

    [Fact]
    public void PerformPhysicsKeepsThePawnBookkeeping()
    {
        Scene s = FloorScene();
        var c = new TestController { MoveTimer = 1f };
        TestPawn p = s.SpawnPawn(V(0f, 0f, 500f), EPhysics.PHYS_Falling, c);
        p.Acceleration = V(10f, 0f, 0f);
        p.AvgPhysicsTime = 0.1f;
        s.Driver.performPhysics(p, 0.05f);
        Assert.Equal(500f, p.OldZ);
        Assert.True(p.bSimulateGravity);
        Assert.Equal(V(10f, 0f, 0f), p.OldAcceleration);
        Assert.Equal(0.2f * 0.05f + 0.8f * 0.1f, p.AvgPhysicsTime, 6);
        Assert.Equal(1f - 0.05f, c.MoveTimer, 6);

        s.Driver.setPhysics(p, EPhysics.PHYS_None);
        s.Driver.performPhysics(p, 0.05f);
        Assert.False(p.bSimulateGravity);
    }

    [Fact]
    public void SetPhysicsSideEffects()
    {
        Scene s = FloorScene();
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 400f), EPhysics.PHYS_Walking, c);
        p.Velocity = V(100f, 0f, 0f);
        p.bKeepLocalVandammePos = true;

        // Entering falling starts the apex at Location.Z, leaves the base, and sets bActionsSendByServerMove.
        s.Driver.setPhysics(p, EPhysics.PHYS_Falling);
        Assert.Equal(400f, c.fZMaxFalling);
        Assert.Null(p.Base);
        Assert.True(p.bActionsSendByServerMove);
        Assert.False(p.bKeepLocalVandammePos);

        // The same mode changes nothing; a RootMotion call in RootMotion still sets bActionsSendByServerMove.
        c.fZMaxFalling = 0f;
        s.Driver.setPhysics(p, EPhysics.PHYS_Falling);
        Assert.Equal(0f, c.fZMaxFalling);
        s.Driver.setPhysics(p, EPhysics.PHYS_RootMotion);
        p.bActionsSendByServerMove = false;
        s.Driver.setPhysics(p, EPhysics.PHYS_RootMotion);
        Assert.True(p.bActionsSendByServerMove);

        // None stops the actor and finds a base under it.
        p.Location = V(0f, 0f, WalkZ());
        p.Velocity = V(1f, 2f, 3f);
        p.Acceleration = V(4f, 5f, 6f);
        s.Driver.setPhysics(p, EPhysics.PHYS_None);
        Assert.Equal(default, p.Velocity);
        Assert.Equal(default, p.Acceleration);
        Assert.Same(s.Level.Info, p.Base);

        // Walking with a floor given takes it as the base.
        s.Driver.setPhysics(p, EPhysics.PHYS_Ledge);
        Assert.Null(p.Base);
        s.Driver.setPhysics(p, EPhysics.PHYS_Walking, s.Level.Info, V(0f, 0f, 1f));
        Assert.Same(s.Level.Info, p.Base);
    }

    [Fact]
    public void LeavingRepositioningOnTheAuthoritySnapsToTheTarget()
    {
        Scene s = FloorScene(b => b.AddBox(V(40f, -100f, 0f), V(60f, 100f, 300f)));
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Repositionnal);
        p.vVandammePos = V(100f, 0f, WalkZ());
        p.vWallJumpDir = V(0f, 1f, 0f);
        s.Driver.setPhysics(p, EPhysics.PHYS_Walking);
        // Through the wall (collision with the world off), facing vWallJumpDir, stopped.
        Assert.Equal(100f, p.Location.X, 3);
        Assert.Equal(16384, p.Rotation.Yaw);
        Assert.True(p.bCollideWorld);

        // bNoReposAtEnd skips the snap once.
        p.Physics = EPhysics.PHYS_Repositionnal;
        p.bNoReposAtEnd = true;
        p.vVandammePos = V(300f, 0f, WalkZ());
        s.Driver.setPhysics(p, EPhysics.PHYS_Walking);
        Assert.Equal(100f, p.Location.X, 3);
        Assert.False(p.bNoReposAtEnd);
    }

    [Fact]
    public void FixedTurnTakesTheShortWayAndSnaps()
    {
        Assert.Equal(3000, PhysicsDriver.FixedTurn(0, 10000, 3000));
        Assert.Equal(65536 - 3000, PhysicsDriver.FixedTurn(0, 50000, 3000));
        Assert.Equal(10000, PhysicsDriver.FixedTurn(9000, 10000, 3000));
        Assert.Equal(100, PhysicsDriver.FixedTurn(65000, 100, 3000));
    }

    // ---- root motion ---------------------------------------------------------------------------------------

    private static (Scene Scene, TestSBasePawn Pawn, TestRootMotionMesh Mesh) RootMotionScene(bool wall = true)
    {
        Scene s = FloorScene(b =>
        {
            if (wall)
            {
                b.AddBox(V(60f, -500f, 0f), V(80f, 500f, 400f));
            }
        });
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_RootMotion);
        var mesh = new TestRootMotionMesh { DeltaPerCall = V(10f, 0f, 0f) };
        p.RootMesh = mesh;
        p.iLockRootMotionType = 3;
        p.bJustTeleported = false; // Actor's default is true; walking or falling clears it
        return (s, p, mesh);
    }

    [Fact]
    public void RootMotionWithoutAdditionalPhysicsMovesThroughTheWorld()
    {
        (Scene s, TestSBasePawn p, TestRootMotionMesh mesh) = RootMotionScene();
        s.Step(10);
        Assert.Equal(3, mesh.RootLockType);
        Assert.Equal(100f, p.Location.X, 3);
        Assert.Equal(10f * 60f, p.Velocity.X, 0);
        Assert.True(p.bCollideWorld);
        Assert.True(p.bCollideActors);

        // One update per game time step.
        int calls = mesh.Calls;
        s.Driver.physRootMotion(p, Sim.DeltaTime);
        Assert.Equal(calls, mesh.Calls);

        // Leaving root motion unlocks the mesh.
        s.Driver.setPhysics(p, EPhysics.PHYS_Falling);
        Assert.Equal(0, mesh.RootLockType);
    }

    [Fact]
    public void RootMotionWithCollisionStopsAtWalls()
    {
        (Scene s, TestSBasePawn p, _) = RootMotionScene();
        p.bUseRootMotionAdditionnalPhysics = true;
        p.RootMotionAdditionnalPhysics = EPhysics.PHYS_None;
        s.Step(10);
        Assert.InRange(p.Location.X, 60f - 35f - 0.2f, 60f - 35f);
    }

    [Fact]
    public void RootMotionWithWalkingWalksTheRootDelta()
    {
        (Scene s, TestSBasePawn p, _) = RootMotionScene(wall: false);
        p.bUseRootMotionAdditionnalPhysics = true;
        p.RootMotionAdditionnalPhysics = EPhysics.PHYS_Walking;
        s.Step(10);
        Assert.Equal(100f, p.Location.X, 1);
        AssertWalkHeight(0f, p.Location.Z);
        Assert.Equal(EPhysics.PHYS_RootMotion, p.Physics);
    }

    [Fact]
    public void RootMotionWithoutAMeshStopsThePawn()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_RootMotion);
        p.Velocity = V(100f, 0f, 0f);
        p.Acceleration = V(100f, 0f, 0f);
        s.Step(1);
        Assert.Equal(default, p.Velocity);
        Assert.Equal(default, p.Acceleration);
    }

    [Fact]
    public void EndRootMotionPlacesThePawnAtTheEndOfTheMove()
    {
        (Scene s, TestSBasePawn p, _) = RootMotionScene(wall: false);
        p.vLocationBeforeRM = V(0f, 0f, WalkZ());
        p.rRotBeforeRM = 1000;
        p.vMoveAfterRM = V(57.8f, 0f, 210.8f);
        p.rRotAfterRM = 16384;
        p.vSpeedAfterRM = V(0f, 0f, 50f);
        s.Driver.EndRootMotion(p);
        Assert.Equal(V(0f, 0f, 50f), p.Velocity);
        Assert.Equal(default, p.vSpeedAfterRM);
        Assert.Equal(17384, p.Rotation.Yaw);
        Near(V(57.8f, 0f, WalkZ() + 210.8f), p.Location, 1e-3f);

        p.bReverseRM = true;
        s.Driver.EndRootMotion(p);
        Near(V(0f, 0f, WalkZ()), p.Location, 1e-3f);
        Assert.Equal(1000, p.Rotation.Yaw);
        Assert.False(p.bReverseRM);
    }

    [Fact]
    public void TheServerRefusesAPullUpWithoutRoomToStand()
    {
        Scene s = FloorScene(b => b.AddBox(V(-500f, -500f, 300f), V(500f, 500f, 400f)));
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Walking, c);
        p.RootMesh = new TestRootMotionMesh();
        Assert.False(s.Driver.bServerTestRootMotion(p, 113, V(57.8f, 0f, 210.8f), 0f));
        Assert.Equal(1, c.ClientStopRMs);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);

        Assert.True(s.Driver.bServerTestRootMotion(p, 114, V(75.7f, 0f, 206.1f), 16384.4f));
        Assert.Equal(EPhysics.PHYS_RootMotion, p.Physics);
        Assert.Equal(V(75.7f, 0f, 206.1f), p.vMoveAfterRM);
        Assert.Equal(16384, p.rRotAfterRM);
    }

    [Fact]
    public void RootMotionFallingLandsOrHitsWalls()
    {
        Scene s = FloorScene(b => b.AddBox(V(60f, -500f, 0f), V(80f, 500f, 400f)));
        var c = new TestController { bScriptModes = false };
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ() + 5f), EPhysics.PHYS_RootMotion, c);
        p.RootMesh = new TestRootMotionMesh { DeltaPerCall = V(0f, 0f, -10f) };
        p.bUseRootMotionAdditionnalPhysics = true;
        p.RootMotionAdditionnalPhysics = EPhysics.PHYS_Falling;
        s.Step(1);
        Assert.Equal(1, c.MyLandeds);

        p.RootMesh.DeltaPerCall = V(40f, 0f, 0f);
        s.Step(1);
        Assert.Equal(1, c.HitWalls);
    }

    // ---- repositioning and Vandamme ------------------------------------------------------------------------

    [Fact]
    public void RepositioningSlidesAt87AndTurnsAt3000ThenTopSynchro()
    {
        Scene s = FloorScene(b => b.AddBox(V(40f, -100f, 0f), V(60f, 100f, 300f)));
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Repositionnal, c);
        p.vVandammePos = V(100f, 0f, WalkZ() + 3f); // within the 5 uu dead band: Z is ignored
        p.vWallJumpDir = V(-1f, 0f, 0f);
        p.Acceleration = V(5f, 0f, 0f);
        s.Step(1);
        Assert.Equal(87f * Sim.DeltaTime, p.Location.X, 3);
        Assert.Equal(WalkZ(), p.Location.Z, 3);
        Assert.Equal(3000, p.Rotation.Yaw);
        Assert.Equal(default, p.Acceleration);
        Assert.Equal(87f, p.Velocity.X, 1);
        Assert.True(p.bCollideWorld);

        for (int i = 0; i < 120 && c.TopSynchros == 0; i++)
        {
            s.Step(1);
        }

        // Through the wall (repositioning without additional physics ignores the world), facing vWallJumpDir.
        Assert.True(c.TopSynchros > 0);
        Assert.Equal(100f, p.Location.X, 2);
        Assert.Equal(32768, p.Rotation.Yaw & 0xFFFF);
    }

    [Fact]
    public void RepositioningWaitsForItsStartTime()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Repositionnal);
        p.vVandammePos = V(100f, 0f, WalkZ());
        p.fLastRootMotionTimeCalled = 1000f;
        s.Step(5);
        Assert.Equal(0f, p.Location.X);
    }

    [Fact]
    public void RepositioningWithAdditionalPhysicsWalksWithCollision()
    {
        Scene s = FloorScene(b => b.AddBox(V(40f, -100f, 0f), V(60f, 100f, 300f)));
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Repositionnal);
        p.vVandammePos = V(100f, 0f, WalkZ());
        p.vWallJumpDir = V(1f, 0f, 0f);
        p.bUseAdditionnalPhysicsForRepos = true;
        s.Step(60);
        // The walking pre-probe slides the move off within one step (87 / 60) of the wall.
        Assert.InRange(p.Location.X, 40f - 35f - 1.5f, 40f - 35f);
    }

    [Fact]
    public void VandammeHoldsAt87WithATinyVelocity()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 300f), EPhysics.PHYS_Vandamme);
        p.vVandammePos = V(0f, 0f, 400f);
        p.vWallJumpDir = V(0f, 1f, 0f);
        p.Rotation = new FRotator(0, 4000, 0);
        s.Step(1);
        Assert.Equal(300f + 87f * Sim.DeltaTime, p.Location.Z, 3);
        // Velocity = displacement x dt, as the original stores it.
        Assert.Equal(87f * Sim.DeltaTime * Sim.DeltaTime, p.Velocity.Z, 4);
        // Facing the forward axis with its vWallJumpDir part removed (X), at 3000 per call.
        Assert.Equal(1000, p.Rotation.Yaw);
    }
}
