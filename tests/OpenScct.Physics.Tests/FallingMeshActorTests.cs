using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

public sealed class FallingMeshActorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DroppedMeshPropHitsFloorAndKeepsItsMeshCollision(bool bounce)
    {
        FVector start = V(0, 0, 200);
        CollisionDataBuilder geometry = Floor();
        geometry.AddBoxHullCollider(ColliderKind.Gpo, "Prop", CollisionDataBuilder.WorldFlags,
            V(-10, -10, -10), V(10, 10, 10), start);
        FallingProp? prop = null;
        Scene scene = Scene.Create(geometry.Build(), level =>
        {
            prop = level.SpawnActor(new FallingProp { bBounce = bounce }, start, default)!;
            return new Dictionary<string, Actor> { ["Prop"] = prop };
        });
        Assert.True(scene.Collision.HasColliders(prop!));

        scene.Step(120);

        Assert.Equal(EPhysics.PHYS_None, prop!.Physics);
        Assert.InRange(prop.Location.Z, 10, 10.2f);
        Assert.Equal(1, prop.FloorHits);
        Assert.True(scene.Collision.HasColliders(prop));
        Assert.True(scene.Collision.LineCheck(out FCheckResult hit, null,
            prop.Location + V(100, 0, 0), prop.Location - V(100, 0, 0), default, ETraceFlags.TRACE_Others));
        Assert.Same(prop, hit.Actor);
        Assert.False(scene.Collision.LineCheck(out _, null,
            start + V(100, 0, 0), start - V(100, 0, 0), default, ETraceFlags.TRACE_Others));
    }

    [Fact]
    public void MeshMoverKeepsEncroachmentMotionInsteadOfSweepingTheMapFloor()
    {
        FVector start = V(0, 0, 100);
        CollisionDataBuilder geometry = Floor();
        geometry.AddBoxHullCollider(ColliderKind.Mover, "Mover", CollisionDataBuilder.WorldFlags,
            V(-10, -10, -10), V(10, 10, 10), start);
        TestMover? mover = null;
        Scene scene = Scene.Create(geometry.Build(), level =>
        {
            mover = level.SpawnActor(new TestMover { Physics = EPhysics.PHYS_None, bCollideWorld = true }, start, default)!;
            return new Dictionary<string, Actor> { ["Mover"] = mover };
        });

        Assert.True(scene.Driver.MoveActor(mover!, V(0, 0, -200), default, out FCheckResult hit));
        Assert.False(hit.bHit);
        Assert.Equal(-100, mover!.Location.Z);
    }

    private sealed class FallingProp : Actor
    {
        public FallingProp()
        {
            bCollideWorld = bCollideActors = bBlockActors = bBlockPlayers = true;
            CollisionRadius = CollisionHeight = 10;
            Physics = EPhysics.PHYS_Falling;
        }

        public int FloorHits;

        protected override void Landed(FVector HitNormal) => FloorHits++;

        protected override void HitWall(FVector HitNormal, Actor? Wall)
        {
            FloorHits++;
            Velocity = default;
            SetPhysics(EPhysics.PHYS_None);
        }
    }
}
