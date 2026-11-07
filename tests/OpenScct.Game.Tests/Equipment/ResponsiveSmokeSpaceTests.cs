using System;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ResponsiveSmokeSpaceTests
{
    private static (CombatScene Scene, ResponsiveSmokeField Field) Bloom(bool corridor = false, float ceiling = 0)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        scene.Level.SpawnActor(new GameReplicationInfo { ResponsiveSmokeEnabled = true }, default, default);
        var builder = new CollisionDataBuilder();
        builder.AddBox(new(-1000, -1000, -50), new(1000, 1000, 0));
        if (corridor)
        {
            builder.AddBox(new(-1000, -1000, 0), new(-75, 1000, 750));
            builder.AddBox(new(75, -1000, 0), new(1000, 1000, 750));
        }
        if (ceiling > 0) builder.AddBox(new(-1000, -1000, ceiling), new(1000, 1000, ceiling + 50));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        SSmokeEmitter cloud = scene.Level.SpawnActor(new SSmokeEmitter(), new(0, 0, 5), default)!;
        scene.Run(2);
        Assert.True(ResponsiveSmokeWorld.For(scene.Level).TryGet(cloud, out var field));
        return (scene, field);
    }

    [Fact]
    public void OpenAirFormsAWideLowBodyWithFilledShoulders()
    {
        var (scene, field) = Bloom();
        Assert.True(field.SampleDensity(field.Center + new FVector(210, 170, 0), scene.Time) > .55f);
        Assert.Equal(0f, field.SampleDensity(field.Center + new FVector(0, 0, 220), scene.Time));
        // The rounded rectangular shoulders extend outside the former sphere. Optical queries must include them.
        float depth = ResponsiveSmokeWorld.For(scene.Level).OpticalDepth(new(213, 213, -300), new(213, 213, 600));
        Assert.True(depth > 1.05f, $"shoulder optical depth={depth}");
    }

    [Fact]
    public void NarrowWallsRedirectTheCloudUpwardAndAlongTheCorridor()
    {
        var (openScene, open) = Bloom();
        var (scene, field) = Bloom(corridor: true);
        Assert.Equal(0f, open.SampleDensity(open.Center + new FVector(0, 0, 220), openScene.Time));
        Assert.True(field.SampleDensity(field.Center + new FVector(0, 0, 220), scene.Time) > .55f);
        Assert.True(field.SampleDensity(field.Center + new FVector(0, 245, 0), scene.Time) > .55f);
        Assert.Equal(0f, field.SampleDensity(field.Center + new FVector(100, 0, 0), scene.Time));
        Assert.Equal(0f, field.SampleDensity(field.Center + new FVector(-100, 0, 0), scene.Time));
        ResponsiveSmokeSnapshot copy = ResponsiveSmokeCodec.Decode(ResponsiveSmokeCodec.Encode(field.Capture(scene.Time)));
        Assert.Equal(field.ShapeExtents, ResponsiveSmokeField.GeometryShapeExtents(copy.Cells, copy.ActivationTicks, copy.Origin, copy.Center));
    }

    [Theory]
    [InlineData(110f)]
    [InlineData(260f)]
    public void LowCeilingBroadensTheFootprintWhileKeepingItsSolidSurfaceClear(float ceiling)
    {
        var (_, open) = Bloom();
        var (scene, field) = Bloom(ceiling: ceiling);
        float gain = 0;
        for (int x = 250; x <= 290; x += 5)
            gain += field.SampleDensity(new(x, 0, 60), scene.Time)
                - open.SampleDensity(new(x, 0, 60), scene.Time);
        Assert.True(gain > .5f, $"footprint density gain={gain}");
        Assert.Equal(0f, field.SampleDensity(new(0, 0, ceiling + 10), scene.Time));
        Assert.Equal(0f, field.SampleDensity(new(0, 0, ceiling + 80), scene.Time));
    }
}
