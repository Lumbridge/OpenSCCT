using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>
/// Collision primitives and trace rules (spec/native/collision-primitives.md, collision-traces.md) and MoveActor,
/// FarMoveActor and CheckEncroachment (collision-movement.md).
/// </summary>
public sealed class CollisionPrimitiveTests
{
    private static readonly FVector Pawn = new(35f, 35f, 87.5f);

    [Fact]
    public void TraceFlagValuesAreTheDecodedOnes()
    {
        Assert.Equal(0x2DFu, (uint)(ETraceFlags.TRACE_StopAtFirstHit | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Blocking |
                                     ETraceFlags.TRACE_Others | ETraceFlags.TRACE_Volumes | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers |
                                     ETraceFlags.TRACE_Pawns));
        Assert.Equal(0x1000u, (uint)ETraceFlags.TRACE_Material);
        Assert.Equal(0x10000u, (uint)ETraceFlags.TRACE_Camera);
        Assert.Equal(0x19u, (uint)K.Flags.MoveActors);
        Assert.Equal(0x86u, (uint)K.Flags.MoveWorld);
        Assert.Equal(0x243u, (uint)K.Flags.PawnsAndMovers);
    }

    [Fact]
    public void MeshTracesUseTheSimpleSetUnlessTheyAskForShadows()
    {
        // One static mesh: a per-poly box at x 100..110 and a simple-collision box at x 90..120, same transform.
        var b = new CollisionDataBuilder();
        int perPoly = b.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags, [(V(0f, -50f, -50f), V(10f, 50f, 50f))],
            V(100f, 0f, 0f));
        int simple = b.AddBoxMeshCollider(ColliderKind.MeshHull, null, CollisionDataBuilder.WorldFlags, [(V(-10f, -60f, -60f), V(20f, 60f, 60f))],
            V(100f, 0f, 0f));
        CollisionWorld w = CollisionWorld.Build(b.Build());
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(500f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_World));
        Assert.Equal(simple, hit.Collider);
        Assert.True(w.LineCheck(out hit, null, V(500f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_World | ETraceFlags.TRACE_ShadowCast));
        Assert.Equal(perPoly, hit.Collider);

        // Box traces too.
        Assert.True(w.LineCheck(out hit, null, V(500f, 0f, 0f), V(0f, 0f, 0f), V(5f, 5f, 5f), ETraceFlags.TRACE_World));
        Assert.Equal(simple, hit.Collider);
    }

    [Fact]
    public void CylinderHitsCapsSidesAndOnlyInwardFromInside()
    {
        CollisionWorld w = CollisionWorld.Build(new CollisionDataBuilder().Build());
        var world = new World(1);
        w.BindLevel(world.Level);
        TestPawn target = world.Level.SpawnActor(new TestPawn(), V(0f, 0f, 0f), default)!;
        w.NotifyCollisionChanged();

        // Down onto the top cap (87.5 + 10 for the box), normal up.
        Assert.True(w.LineCheck(out FCheckResult top, null, V(0f, 0f, -300f), V(0f, 0f, 300f), V(10f, 10f, 10f), ETraceFlags.TRACE_Pawns));
        Near(V(0f, 0f, 1f), top.Normal);
        Assert.Equal((300f - 97.5f) / 600f - 0.001f, top.Time, 5);

        // From the side: the box widens the radius (35 + 10).
        Assert.True(w.LineCheck(out FCheckResult side, null, V(300f, 0f, 0f), V(-300f, 0f, 0f), V(10f, 10f, 10f), ETraceFlags.TRACE_Pawns));
        Near(V(-1f, 0f, 0f), side.Normal);
        Assert.Equal((300f - 45f) / 600f - 0.001f, side.Time, 5);

        // From inside: only a move inward is blocked, at time 0.
        Assert.True(w.LineCheck(out FCheckResult inward, null, V(0f, 0f, 0f), V(30f, 0f, 0f), V(10f, 10f, 10f), ETraceFlags.TRACE_Pawns));
        Assert.Equal(0f, inward.Time);
        Assert.True(inward.bStartPenetrating);
        Assert.False(w.LineCheck(out _, null, V(60f, 0f, 0f), V(30f, 0f, 0f), V(10f, 10f, 10f), ETraceFlags.TRACE_Pawns));
        Assert.Same(target, inward.Actor);
    }

    [Fact]
    public void ShouldTraceGates()
    {
        var b = new CollisionDataBuilder();
        b.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags & ~ColliderFlags.BlockPlayers,
            [(V(100f, -50f, -50f), V(110f, 50f, 50f))]);
        CollisionWorld w = CollisionWorld.Build(b.Build());
        var world = new World(1);
        w.BindLevel(world.Level);
        TestPawn pawn = world.Level.SpawnActor(new TestPawn(), V(0f, 0f, -1000f), default)!;
        TestTrigger thing = world.Level.SpawnActor(new TestTrigger(), V(0f, 0f, -2000f), default)!;

        // A world static mesh that does not block players: a pawn source does not see it, anything else does.
        Assert.False(w.LineCheck(out _, pawn, V(500f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_World));
        Assert.True(w.LineCheck(out _, thing, V(500f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_World));

        // bOnlyAffectPawns on the source: only pawns are hit.
        thing.bOnlyAffectPawns = true;
        Assert.False(w.LineCheck(out _, thing, V(500f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_World));
    }

    [Fact]
    public void OnlyProjActorAndCameraGateActorCylinders()
    {
        CollisionWorld w = CollisionWorld.Build(new CollisionDataBuilder().Build());
        var world = new World(1);
        w.BindLevel(world.Level);
        var crate = new TestTrigger { bBlockZeroExtentTraces = true, bBlockActors = true };
        world.Level.SpawnActor(crate, V(300f, 0f, 0f), default);
        w.NotifyCollisionChanged();
        Assert.True(w.LineCheck(out _, null, V(600f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_Others));
        Assert.False(w.LineCheck(out _, null, V(600f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_Others | ETraceFlags.TRACE_OnlyProjActor));
        crate.bProjTarget = true;
        w.NotifyCollisionChanged();
        Assert.True(w.LineCheck(out _, null, V(600f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_Others | ETraceFlags.TRACE_OnlyProjActor));
        crate.bBlockCamera = false;
        w.NotifyCollisionChanged();
        Assert.False(w.LineCheck(out _, null, V(600f, 0f, 0f), V(0f, 0f, 0f), default, ETraceFlags.TRACE_Others | ETraceFlags.TRACE_Camera));
    }

    // ---- MoveActor, FarMoveActor, encroachment ------------------------------------------------------------

    private static Scene WallScene() => Scene.Create(Floor().Also(b => b.AddBox(V(200f, -500f, 0f), V(300f, 500f, 400f))).Build());

    [Fact]
    public void MoveActorStopsAtTheFirstBlockingHit()
    {
        Scene s = WallScene();
        TestPawn p = s.SpawnPawn(V(0f, 0f, 200f), EPhysics.PHYS_None, noController: true);
        Assert.True(s.Driver.MoveActor(p, V(400f, 0f, 0f), p.Rotation, out FCheckResult hit));
        Assert.True(hit.bHit);
        Assert.Equal(200f - 35f - 0.1f, p.Location.X, 3);

        // Touching the wall the move has Time 0: MoveActor returns false.
        p.Location = V(165f, 0f, 200f);
        Assert.False(s.Driver.MoveActor(p, V(50f, 0f, 0f), p.Rotation, out hit));
        Assert.Equal(165f, p.Location.X);

        // bNoFail moves the whole way; bTest still moves but raises nothing.
        TestPawn q = s.SpawnPawn(V(0f, 100f, 200f), EPhysics.PHYS_None, noController: true);
        s.Driver.MoveActor(q, V(400f, 0f, 0f), q.Rotation, out hit, bTest: false, bIgnorePawns: false, bIgnoreBases: false, bNoFail: true);
        Assert.Equal(400f, q.Location.X, 3);
    }

    [Fact]
    public void BumpsGoToTheOtherActorFirst()
    {
        Scene s = WallScene();
        TestPawn a = s.SpawnPawn(V(0f, 0f, 200f), EPhysics.PHYS_None, noController: true);
        TestPawn other = s.SpawnPawn(V(100f, 0f, 200f), EPhysics.PHYS_None, noController: true);
        var order = new List<Actor>();
        s.Driver.MoveActor(a, V(100f, 0f, 0f), a.Rotation, out FCheckResult hit);
        Assert.Same(other, hit.Actor);
        Assert.Equal(1, other.Bumps);
        Assert.Equal(1, a.Bumps);

        int bumps = a.Bumps;
        s.Driver.MoveActor(a, V(-10f, 0f, 0f), a.Rotation, out _);
        s.Driver.MoveActor(a, V(100f, 0f, 0f), a.Rotation, out _, bTest: true, bIgnorePawns: false, bIgnoreBases: false, bNoFail: false);
        Assert.Equal(bumps, a.Bumps);
        _ = order;
    }

    [Fact]
    public void AttachedActorsMoveAlongButBasedPawnsDoNot()
    {
        Scene s = WallScene();
        TestPawn carrier = s.SpawnPawn(V(0f, 0f, 200f), EPhysics.PHYS_None, noController: true);
        TestTrigger attachment = s.Level.SpawnActor(new TestTrigger { bCollideActors = false }, V(0f, 0f, 300f), default)!;
        attachment.SetBase(carrier);
        TestPawn rider = s.SpawnPawn(V(0f, 0f, 400f), EPhysics.PHYS_None, noController: true);
        rider.SetBase(carrier);
        s.Driver.MoveActor(carrier, V(50f, 0f, 0f), carrier.Rotation, out _);
        Assert.Equal(50f, attachment.Location.X, 3);
        Assert.Equal(0f, rider.Location.X, 3);
    }

    [Fact]
    public void FarMoveActorTeleportsLeavesTheBaseAndCanBeATest()
    {
        Scene s = WallScene();
        TestPawn p = s.SpawnPawn(V(0f, 0f, WalkZ()), EPhysics.PHYS_None, noController: true);
        s.Driver.FindBase(p);
        Assert.Same(s.Level.Info, p.Base);
        Assert.True(s.Driver.FarMoveActor(p, V(-500f, 0f, WalkZ())));
        Assert.Equal(-500f, p.Location.X);
        Assert.True(p.bJustTeleported);
        Assert.Null(p.Base);

        // A destination inside the floor is nudged clear by FindSpot.
        Assert.True(s.Driver.FarMoveActor(p, V(-500f, 0f, 80f)));
        Assert.True(p.Location.Z >= 87.5f);

        // bTest moves the actor (the caller puts it back) without the teleport flag.
        p.bJustTeleported = false;
        Assert.True(s.Driver.FarMoveActor(p, V(-600f, 0f, WalkZ()), bTest: true));
        Assert.False(p.bJustTeleported);
        Assert.Equal(-600f, p.Location.X);

        // bAttachedMove keeps the base.
        s.Driver.FindBase(p);
        Assert.True(s.Driver.FarMoveActor(p, V(-650f, 0f, WalkZ()), bAttachedMove: true));
        Assert.Same(s.Level.Info, p.Base);
    }

    [Fact]
    public void ARotatingMoverShovesWithOneAndAHalfTimesItsTurn()
    {
        // A door hinged at the origin, 200 long along +X; a pawn at x 150 just in front of it (y -50).
        CollisionDataBuilder b = Floor();
        b.AddBoxHullCollider(ColliderKind.Mover, "Door", CollisionDataBuilder.WorldFlags, V(0f, -5f, 0f), V(200f, 5f, 200f));
        TestMover? door = null;
        Scene s = Scene.Create(b.Build(), level =>
        {
            door = level.SpawnActor(new TestMover { Physics = EPhysics.PHYS_None }, default, default)!;
            return new Dictionary<string, Actor> { ["Door"] = door };
        });
        TestPawn p = s.SpawnPawn(V(150f, -41f, WalkZ()), EPhysics.PHYS_None, noController: true);
        FVector before = p.Location;
        var turn = new FRotator(0, -1000, 0);
        Assert.True(s.Driver.MoveActor(door!, default, turn, out _));
        // The pawn's spot turns with the door; the shove is 1.5 times that offset (plus the push margin).
        FVector rel = before;
        FCoords oldAxes = FCoords.FromRotator(default);
        FCoords newAxes = FCoords.FromRotator(turn);
        FVector turned = newAxes.XAxis * (rel | oldAxes.XAxis) + newAxes.YAxis * (rel | oldAxes.YAxis) + newAxes.ZAxis * (rel | oldAxes.ZAxis);
        FVector shove = (turned - rel) * 1.5f;
        Assert.InRange(FVector.Dist(before + shove, p.Location), 0f, 0.2f);
        Assert.Empty(door!.Encroached);
    }
}
