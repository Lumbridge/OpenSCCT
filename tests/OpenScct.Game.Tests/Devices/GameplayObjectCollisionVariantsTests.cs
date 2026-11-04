using System;
using System.Buffers.Binary;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

public sealed class GameplayObjectCollisionVariantsTests
{
    [Theory]
    [InlineData(ENetMode.NM_Standalone)]
    [InlineData(ENetMode.NM_DedicatedServer)]
    [InlineData(ENetMode.NM_Client)]
    public void LoadedVariantMasksSwitchCollisionAndRestoreItWithoutAnExternalProvider(ENetMode mode)
    {
        var world = new World(1, mode);
        world.Level.BeginLoad();
        var wall = world.Level.SpawnActor(new SGamePlayObject
        {
            StaticMesh = new AssetRef("Test.intact"), TriggeredMesh = new AssetRef("Test.breached"),
        }, default, default)!;
        var builder = new CollisionDataBuilder();
        int intact = builder.AddBoxHullCollider(ColliderKind.Gpo, "wall", CollisionDataBuilder.WorldFlags,
            new FVector(-5, -100, -100), new FVector(5, 100, 100));
        int breached = builder.AddBoxHullCollider(ColliderKind.Gpo, "wall", CollisionDataBuilder.WorldFlags | ColliderFlags.StartsDisabled,
            new FVector(-5, 60, -100), new FVector(5, 100, 100));
        int shared = builder.AddBoxHullCollider(ColliderKind.Gpo, "wall", CollisionDataBuilder.WorldFlags,
            new FVector(-5, 100, -100), new FVector(5, 150, 100));
        CollisionData source = builder.Build();
        var rows = new byte[source.Colliders.Length * 16];
        BinaryPrimitives.WriteUInt16LittleEndian(rows.AsSpan(intact * 16 + 10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(rows.AsSpan(breached * 16 + 10), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(rows.AsSpan(shared * 16 + 10), 3);
        var data = new CollisionData
        {
            Strings = source.Strings, Vertices = source.Vertices, Triangles = source.Triangles,
            Materials = source.Materials, Planes = source.Planes, Hulls = source.Hulls,
            Colliders = source.Colliders, Zones = source.Zones,
            UnknownChunks = [new CollisionUnknownChunk("CSRC", 1, (uint)source.Colliders.Length, rows)],
        };
        CollisionWorld collision = CollisionWorld.Build(data);
        collision.BindLevel(world.Level, id => id == "wall" ? wall : null);
        DeviceNatives natives = DeviceNatives.Install(world.Level, new PhysicsDriver(collision));
        world.Level.EndLoad();
        Assert.Null(natives.Variants);
        Assert.True(collision.GetCollider(intact).Enabled);
        Assert.False(collision.GetCollider(breached).Enabled);
        wall.SetStaticMesh(wall.TriggeredMesh);
        Assert.False(collision.GetCollider(intact).Enabled);
        Assert.True(collision.GetCollider(breached).Enabled);
        Assert.True(collision.GetCollider(shared).Enabled);
        Assert.False(collision.LineCheck(out _, null, new FVector(100, 0, 0), new FVector(-100, 0, 0),
            new FVector(35, 35, 50), ETraceFlags.TRACE_AllBlocking));
        wall.SetStaticMesh(wall.InitialStaticMesh);
        Assert.True(collision.GetCollider(intact).Enabled);
        Assert.False(collision.GetCollider(breached).Enabled);
        Assert.True(collision.GetCollider(shared).Enabled);
        Assert.True(collision.LineCheck(out _, null, new FVector(100, 0, 0), new FVector(-100, 0, 0),
            new FVector(35, 35, 50), ETraceFlags.TRACE_AllBlocking));
    }
}
