using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Physics.Tests;

public sealed class CollisionStartupOverrideTests
{
    private sealed class Target : Actor { }

    [Fact]
    public void ExplicitCollisionBeforeBindingWinsOverConvertedCylinderFlags()
    {
        var world = new World(1);
        var actor = world.Level.SpawnActor(new Target
        {
            CollisionRadius = 1000f, CollisionHeight = 150f,
            bCollideActors = true, bBlockActors = true, bBlockPlayers = true,
        }, default, default)!;
        var builder = new CollisionDataBuilder();
        builder.AddCylinder("target", CollisionDataBuilder.WorldFlags, default, 1000f, 150f);
        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        actor.SetCollision(false, false, false);
        collision.BindLevel(world.Level, _ => actor);

        Assert.True(actor.CollisionFlagsOverridden);
        collision.GetCylinder(actor, out float radius, out float height, out bool collide, out bool blockActors, out bool blockPlayers);
        Assert.Equal(1000f, radius);
        Assert.Equal(150f, height);
        Assert.False(collide);
        Assert.False(blockActors);
        Assert.False(blockPlayers);
        Assert.Equal(ColliderFlags.None, collision.GetCylinderFlags(actor) &
            (ColliderFlags.CollideActors | ColliderFlags.BlockActors | ColliderFlags.BlockPlayers));
        Assert.False(collision.LineCheck(out _, null, new FVector(2000f, 0f, 0f), new FVector(-2000f, 0f, 0f), default, ETraceFlags.TRACE_AllColliding));
    }

    [Fact]
    public void UntouchedConvertedCylinderStillUsesItsAuthoredDimensionsAndFlags()
    {
        var world = new World(1);
        Actor actor = world.Level.SpawnActor(new Target(), default, default)!;
        var builder = new CollisionDataBuilder();
        builder.AddCylinder("target", CollisionDataBuilder.WorldFlags, default, 1000f, 150f);
        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        collision.BindLevel(world.Level, _ => actor);
        collision.GetCylinder(actor, out float radius, out float height, out bool collide, out bool blockActors, out bool blockPlayers);
        Assert.False(actor.CollisionFlagsOverridden);
        Assert.Equal(1000f, radius);
        Assert.Equal(150f, height);
        Assert.True(collide);
        Assert.True(blockActors);
        Assert.True(blockPlayers);
    }

    [Fact]
    public void ExplicitFlagsKeepUndecodedAuthoredDimensionsUntilSizeChanges()
    {
        var world = new World(1);
        Actor actor = world.Level.SpawnActor(new Target(), default, default)!;
        var builder = new CollisionDataBuilder();
        builder.AddCylinder("target", CollisionDataBuilder.WorldFlags, default, 1000f, 150f);
        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        actor.SetCollision(false, false, false);
        collision.BindLevel(world.Level, _ => actor);
        collision.GetCylinder(actor, out float radius, out float height, out bool collide, out bool blockActors, out bool blockPlayers);
        Assert.Equal(1000f, radius);
        Assert.Equal(150f, height);
        Assert.False(collide);
        Assert.False(blockActors);
        Assert.False(blockPlayers);

        actor.SetCollision(true, true, true);
        collision.GetCylinder(actor, out radius, out height, out collide, out blockActors, out blockPlayers);
        Assert.Equal(1000f, radius);
        Assert.Equal(150f, height);
        Assert.True(collide && blockActors && blockPlayers);

        actor.CollisionRadius = 17f;
        actor.CollisionHeight = 31f;
        collision.GetCylinder(actor, out radius, out height, out _, out _, out _);
        Assert.Equal(17f, radius);
        Assert.Equal(31f, height);
    }

    [Fact]
    public void ExplicitCollisionBeforeBindingDisablesMeshAndCanBeEnabledAgain()
    {
        var world = new World(1);
        Actor actor = world.Level.SpawnActor(new Target { bCollideActors = true, bBlockActors = true, bBlockPlayers = true }, default, default)!;
        var builder = new CollisionDataBuilder();
        builder.AddBoxMeshCollider(ColliderKind.Mover, "target", CollisionDataBuilder.WorldFlags,
            [(new FVector(-20f, -50f, -50f), new FVector(20f, 50f, 50f))]);
        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        actor.SetCollision(false, false, false);
        collision.BindLevel(world.Level, _ => actor);
        Assert.False(collision.LineCheck(out _, null, new FVector(200f, 0f, 0f), new FVector(-200f, 0f, 0f), default, ETraceFlags.TRACE_AllColliding));
        actor.SetCollision(true, true, true);
        Assert.True(collision.LineCheck(out _, null, new FVector(200f, 0f, 0f), new FVector(-200f, 0f, 0f), default, ETraceFlags.TRACE_AllColliding));
    }
}
