using System;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed partial class ResponsiveSmokeNetworkTests
{
    [Fact]
    public void GroundedSurfaceBoundariesReplicateThroughDoorDeltaAndLateJoin()
    {
        using var rig = Create("100");
        Level level = rig.Server.World.Level;
        level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        Step(rig, 180);
        var builder = new CollisionDataBuilder();
        builder.AddBox(new FVector(-1000, -1000, -50), new FVector(1000, 1000, 0));
        int door = builder.AddBoxHullCollider(ColliderKind.Mover, null, CollisionDataBuilder.WorldFlags,
            new FVector(49, -1000, -1000), new FVector(51, 1000, 1000));
        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        level.SetService<ICollisionWorld>(collision);
        SSmokeEmitter cloud = level.Spawn<SSmokeEmitter>(new FVector(-50, 0, 5))!;
        cloud.bAlwaysRelevant = true;
        Step(rig, 120);
        ResponsiveSmokeWorld authority = ResponsiveSmokeWorld.For(level);
        Assert.True(authority.TryGet(cloud, out ResponsiveSmokeField source));
        Assert.NotEmpty(source.Boundaries);
        FVector floorPoint = new(-50, 0, .1f), nearWall = new(48.9f, 0, 50), farWall = new(51.1f, 0, 50);
        void AssertReplica(ClientSession client, bool open)
        {
            var remote = Assert.IsType<SSmokeEmitter>(client.Driver.FindActorByNetGuid((uint)cloud.ActorId));
            ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(client.World.Level);
            Assert.True(world.TryGet(remote, out ResponsiveSmokeField field));
            Assert.NotEmpty(field.Boundaries);
            Assert.Equal(source.Center, field.Center);
            foreach (FVector point in new[] { floorPoint, nearWall, farWall })
                Assert.InRange(MathF.Abs(field.SampleDensity(point, world.CurrentTime) - source.SampleDensity(point, world.CurrentTime)), 0, .001f);
            Assert.True(field.SampleDensity(floorPoint, world.CurrentTime) > .99f);
            Assert.True(field.SampleDensity(nearWall, world.CurrentTime) > .99f);
            if (open) Assert.True(field.SampleDensity(farWall, world.CurrentTime) > .99f);
            else Assert.Equal(0f, field.SampleDensity(farWall, world.CurrentTime));
            Assert.Equal(0, ResponsiveSmokeReplication.For(client.Driver).InvalidPayloads);
        }
        foreach (ClientSession client in rig.Clients) AssertReplica(client, false);
        collision.SetColliderEnabled(door, false);
        Step(rig, 90);
        foreach (ClientSession client in rig.Clients) AssertReplica(client, true);
        ClientSession late = rig.AddClient(); Step(rig, 120);
        AssertReplica(late, true);
    }
}
