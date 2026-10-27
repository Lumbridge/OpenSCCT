using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Content.Collision;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>
/// Rules confirmed against the native code for the real-game goldens (docs/spikes/R4-sim-vs-game.md, findings F1 to
/// F11; spec/native/ANSWERS-fidelity.md).
/// </summary>
public sealed class FidelityTests
{
    private static Scene FloorScene() => Scene.Create(Floor().Build());

    // ---- F2: MyDoJump by physics mode (editor 0x111f9cf0, game 0x10adff00) ------------------------------------

    [Fact]
    public void MyDoJumpOnALedgeLeavesThePawnAlone()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 500f), EPhysics.PHYS_Ledge, rotation: new FRotator(0, 16384, 0));
        p.Velocity = default;
        s.Driver.MyDoJump(p);
        Assert.Equal(EPhysics.PHYS_Ledge, p.Physics);
        Assert.Equal(default, p.Velocity);
        Assert.Equal(0, p.nJumpNumber);

        // SA_LEDGEFALL plays the fall itself: from rest, so the first frame of gravity gives -25 at 60 Hz.
        s.Driver.setPhysics(p, EPhysics.PHYS_Falling);
        s.Step(1);
        Assert.Equal(-25f, p.Velocity.Z, 2);
    }

    [Theory]
    [InlineData(EPhysics.PHYS_OnPipe)]
    [InlineData(EPhysics.PHYS_OnLadder)]
    [InlineData(EPhysics.PHYS_OnVerticalPlane)]
    public void MyDoJumpFromAPipeLadderOrFencePushesOffBackward(EPhysics mode)
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 500f), mode, rotation: new FRotator(0, 16384, 0));
        p.Velocity = V(0f, 0f, 145f);
        s.Driver.MyDoJump(p);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.Equal(0f, p.Velocity.X, 3);
        Assert.Equal(-200f, p.Velocity.Y, 3);
        Assert.Equal(50f, p.Velocity.Z, 3);
        Assert.Equal(0, p.nJumpNumber);
    }

    [Fact]
    public void MyDoJumpDuringLedgeFallPushesOffInAnyMode()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 500f), EPhysics.PHYS_Ledge, rotation: new FRotator(0, 0, 0));
        p.sAction = 119;
        s.Driver.MyDoJump(p);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Near(V(-200f, 0f, 50f), p.Velocity);
    }

    [Fact]
    public void MyDoJumpFromAZipLineFallsWithItsVelocity()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 500f), EPhysics.PHYS_Linear);
        p.Velocity = V(89.1f, -318f, -20f);
        s.Driver.MyDoJump(p);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.Equal(V(89.1f, -318f, -20f), p.Velocity);
        Assert.Equal(0, p.nJumpNumber);
    }

    [Fact]
    public void MyDoJumpInTheAirIsTheSecondJumpOnlyOnce()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 500f), EPhysics.PHYS_Falling);
        p.Velocity = V(10f, 20f, -300f);
        p.vWallJumpDir = V(0f, 1f, 0f);
        s.Driver.MyDoJump(p);
        Assert.Equal(V(10f, 20f, -300f), p.Velocity);

        p.nJumpNumber = 1;
        s.Driver.MyDoJump(p);
        Assert.Equal(V(0f, 250f, 700f), p.Velocity);
        Assert.Equal(2, p.nJumpNumber);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);

        s.Driver.MyDoJump(p);
        Assert.Equal(V(0f, 250f, 700f), p.Velocity);
    }

    [Fact]
    public void MyDoJumpFromVandammeDropsWithNoVerticalSpeed()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, 500f), EPhysics.PHYS_Vandamme);
        p.Velocity = V(30f, 0f, 87f);
        p.nJumpNumber = 1;
        s.Driver.MyDoJump(p);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.Equal(V(30f, 0f, 0f), p.Velocity);
        Assert.Equal(0, p.nJumpNumber);
    }

    [Fact]
    public void MyDoJumpFromWalkingAddsAMovingBasesRise()
    {
        Scene s = FloorScene();
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, StandZ() + 1f));
        s.Settle(p);
        Actor lift = s.Level.SpawnActor(new TestTrigger(), V(0f, 0f, -10f), default)!;
        lift.Velocity = V(0f, 0f, 100f);
        p.SetBase(lift);
        s.Driver.MyDoJump(p);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.Equal(p.JumpZ + 100f, p.Velocity.Z);
        Assert.Equal(1, p.nJumpNumber);

        // World geometry adds nothing.
        TestSBasePawn q = s.SpawnSpy(V(300f, 0f, StandZ() + 1f));
        s.Settle(q);
        lift.bWorldGeometry = true;
        q.SetBase(lift);
        s.Driver.MyDoJump(q);
        Assert.Equal(q.JumpZ, q.Velocity.Z);
    }

    [Fact]
    public void MyDoJumpRunsOnlyOnTheControllingSide()
    {
        Scene s = FloorScene();
        var remote = new TestController { RemoteRole = ENetRole.ROLE_AutonomousProxy };
        TestSBasePawn p = s.SpawnSpy(V(0f, 0f, WalkZ()), EPhysics.PHYS_Walking, remote);
        s.Driver.MyDoJump(p);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
        Assert.Equal(0f, p.Velocity.Z);

        TestSBasePawn q = s.Spawn(new TestSBasePawn(), V(300f, 0f, StandZ() + 1f), EPhysics.PHYS_Walking, noController: true);
        s.Driver.MyDoJump(q);
        Assert.Equal(EPhysics.PHYS_Walking, q.Physics);
    }

    // ---- F1: static mesh box sweeps and point checks (0x111b5d80, 0x111b6520) ----------------------------------

    [Fact]
    public void AMeshBoxSweepMovesAVerticalCylinderPastACorner()
    {
        // A box falling past the corner of a block: its corner overlaps the block's corner column, its cylinder (radius
        // 35 from the centre, 42.4 from the corner) does not. Level geometry still takes the box.
        var b = new CollisionDataBuilder();
        int mesh = b.AddBoxMeshCollider(ColliderKind.MeshHull, null, CollisionDataBuilder.WorldFlags, [(V(-50f, -50f, -100f), V(50f, 50f, 0f))]);
        CollisionWorld w = CollisionWorld.Build(b.Build());
        var pawn = new FVector(35f, 35f, 87.5f);
        Assert.False(w.LineCheck(out _, null, V(80f, 80f, 50f), V(80f, 80f, 200f), pawn, ETraceFlags.TRACE_World));
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(60f, 60f, 50f), V(60f, 60f, 200f), pawn, ETraceFlags.TRACE_World));
        Assert.Equal(mesh, hit.Collider);
        Assert.Equal(1f, hit.Normal.Z, 3);

        var bsp = new CollisionDataBuilder();
        bsp.AddBox(V(-50f, -50f, -100f), V(50f, 50f, 0f));
        CollisionWorld level = CollisionWorld.Build(bsp.Build());
        Assert.True(level.LineCheck(out _, null, V(80f, 80f, 50f), V(80f, 80f, 200f), pawn, ETraceFlags.TRACE_World));
    }

    [Fact]
    public void AMeshPointCheckDropsAPointOutsideTheTrianglesBoxGrownByTheExtent()
    {
        // A sliver of a yawed mesh's wall whose top is a corner (no horizontal edge, so no edge axis is vertical). A
        // standing box 38.7 above that corner reaches over the wall's plane (30.8 < 35): the walk's box test drops it.
        var t = new TriPrim();
        Assert.True(t.Set(V(0f, 0f, 0f), V(0f, 8.5f, -224f), V(0f, 0f, -227.7f)));
        var c = new ColliderState { Kind = ColliderKind.MeshHull, Location = V(-80f, 168f, -220f), Rotation = new FRotator(0, -16384, 0), Scale = V(1f, 1f, 1f) };
        var pawn = new FVector(35f, 35f, 87.5f);
        Assert.False(StaticMeshSweep.OverlapTriangle(in t, c, V(-30.8f, 14f, 126.2f), pawn, out _));

        // Lowered to the wall's height it overlaps, with the normal facing the box.
        Assert.True(StaticMeshSweep.OverlapTriangle(in t, c, V(-30.8f, 5f, -100f), pawn, out FVector n));
        Assert.Equal(-1f, n.X, 3);
    }

    private static void Near(FVector expected, FVector actual, float tol = 1e-3f)
    {
        Assert.Equal(expected.X, actual.X, tol);
        Assert.Equal(expected.Y, actual.Y, tol);
        Assert.Equal(expected.Z, actual.Z, tol);
    }
}
