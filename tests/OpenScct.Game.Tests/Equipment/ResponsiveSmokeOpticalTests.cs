using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ResponsiveSmokeOpticalTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(75f, .015f, .09f)]
    [InlineData(100f, .003f, .04f)]
    [InlineData(150f, .0002f, .008f)]
    public void ShortWallBoundedCoreAndShellPathsHideBackgroundGeometry(float depth, float coreLimit, float shellLimit)
    {
        var failures = new List<string>();
        // Both grid-aligned and off-grid walls must fill to their actual surfaces, not stop at whole voxels.
        foreach (FVector source in new[] { default(FVector), new FVector(7, 11, 13) })
        {
            var (scene, _, field, world) = Slab(depth, source);
            foreach (float age in new[] { 1f, 3f, 6f })
            {
                scene.Run(age - scene.Time);
                foreach (FVector offset in new[] { default(FVector), new FVector(0, 205, 0), new FVector(0, 0, 205) })
                {
                    var (start, end) = Ray(source + offset, depth);
                    float optical = world.OpticalDepth(start, end);
                    float reference = ReferenceDepth(field, start, end, scene.Time, out float minimumDensity);
                    bool core = offset == default;
                    Assert.True(minimumDensity >= (core ? .99f : .549f), $"Unfilled {depth}uu slab at {offset}: density={minimumDensity}");
                    Assert.InRange(MathF.Abs(optical - reference), 0f, .03f);
                    float transmission = MathF.Exp(-optical), limit = core ? coreLimit : shellLimit;
                    string sample = $"depth={depth:F0}, source={source}, age={age:F0}, offset={offset}: T={transmission:F6}, optical={optical:F4}, min density={minimumDensity:F3}, limit={limit:F4}";
                    output.WriteLine(sample);
                    if (transmission > limit) failures.Add(sample);
                    if (!world.SmokeBetween(start, end)) failures.Add($"AI visibility remains open: {sample}");
                }
                Assert.Equal(0f, field.SampleDensity(source + new FVector(depth * .5f + 5, 0, 0), scene.Time));
                Assert.Equal(0f, field.SampleDensity(source + new FVector(depth * .5f + 30, 0, 0), scene.Time));
                Assert.Equal(0f, field.SampleDensity(source - new FVector(depth * .5f + 30, 0, 0), scene.Time));
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData(75f, .09f)]
    [InlineData(100f, .04f)]
    [InlineData(150f, .008f)]
    public void ShortCoreAndShellBulletPathsKeepTheirClearHoldAndRefill(float depth, float recoveredLimit)
    {
        var (scene, cloud, field, world) = Slab(depth, new FVector(7, 11, 13));
        scene.Run(2);
        foreach (FVector offset in new[] { default(FVector), new FVector(0, 205, 0) })
        {
            FVector center = field.Center + offset;
            var (start, end) = Ray(center, depth);
            var (neighborStart, neighborEnd) = Ray(center + new FVector(0, 0, 40), depth);
            float initial = world.OpticalDepth(start, end);
            ResponsiveSmokeWorld.Bullet(cloud, start, end);
            ResponsiveSmokeCarve carve = Assert.Single(field.Carves);
            Assert.Equal(.10f, carve.Hold); Assert.Equal(.40f, carve.Refill);
            Assert.Equal(0f, world.OpticalDepth(start, end));
            Assert.False(world.SmokeBetween(start, end));
            scene.Run(.05f);
            Assert.Equal(0f, world.OpticalDepth(start, end));
            Assert.True(world.OpticalDepth(neighborStart, neighborEnd) > initial * .5f);
            scene.Run(.20f);
            Assert.InRange(world.OpticalDepth(start, end), .001f, initial * .9f);
            scene.Run(.30f);
            Assert.Empty(field.Carves);
            Assert.True(MathF.Exp(-world.OpticalDepth(start, end)) <= recoveredLimit);
            Assert.True(world.SmokeBetween(start, end));
        }
    }

    [Theory]
    [InlineData(75f, .015f)]
    [InlineData(100f, .003f)]
    [InlineData(150f, .0002f)]
    public void ShortBlastPathsKeepTheirClearHoldAndRefillWithoutClearingTheirNeighbor(float depth, float recoveredLimit)
    {
        var (scene, cloud, field, world) = Slab(depth, new FVector(7, 11, 13));
        scene.Run(2);
        var (start, end) = Ray(field.Center, depth);
        var (neighborStart, neighborEnd) = Ray(field.Center + new FVector(0, 150, 0), depth);
        float initial = world.OpticalDepth(start, end), neighbor = world.OpticalDepth(neighborStart, neighborEnd);
        ResponsiveSmokeWorld.Blast(cloud, field.Center, depth * .5f + 1);
        ResponsiveSmokeCarve carve = Assert.Single(field.Carves);
        Assert.Equal(.60f, carve.Hold); Assert.Equal(1.40f, carve.Refill);
        Assert.Equal(0f, world.OpticalDepth(start, end));
        Assert.Equal(neighbor, world.OpticalDepth(neighborStart, neighborEnd));
        scene.Run(.30f);
        Assert.Equal(0f, world.OpticalDepth(start, end));
        Assert.True(world.OpticalDepth(neighborStart, neighborEnd) > neighbor * .8f);
        scene.Run(.70f);
        Assert.InRange(world.OpticalDepth(start, end), .001f, initial * .9f);
        scene.Run(1.10f);
        Assert.Empty(field.Carves);
        Assert.True(MathF.Exp(-world.OpticalDepth(start, end)) <= recoveredLimit);
        Assert.True(world.SmokeBetween(start, end));
    }

    private static (CombatScene Scene, SSmokeEmitter Cloud, ResponsiveSmokeField Field, ResponsiveSmokeWorld World) Slab(float depth, FVector source)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        scene.Level.SpawnActor(new GameReplicationInfo { ResponsiveSmokeEnabled = true }, default, default);
        var builder = new CollisionDataBuilder();
        float near = source.X - depth * .5f, far = source.X + depth * .5f;
        builder.AddBox(new FVector(near - 25, -1000, -1000), new FVector(near, 1000, 1000));
        builder.AddBox(new FVector(far, -1000, -1000), new FVector(far + 25, 1000, 1000));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        SSmokeEmitter cloud = scene.Level.SpawnActor(new SSmokeEmitter(), source, default)!;
        var world = ResponsiveSmokeWorld.For(scene.Level);
        Assert.True(world.TryGet(cloud, out var field));
        Assert.Equal(source, field.Center);
        return (scene, cloud, field, world);
    }

    private static (FVector Start, FVector End) Ray(FVector center, float depth)
    {
        // Stay 0.1uu inside the true collision surfaces while retaining almost the entire constrained path.
        FVector half = new(depth * .5f - .1f, 0, 0);
        return (center - half, center + half);
    }

    private static float ReferenceDepth(ResponsiveSmokeField field, FVector start, FVector end, float now, out float minimumDensity)
    {
        float length = FVector.Dist(start, end);
        int count = (int)MathF.Ceiling(length); float sum = 0;
        minimumDensity = 1;
        for (int i = 0; i < count; i++)
        {
            float density = field.SampleDensity(start + (end - start) * ((i + .5f) / count), now);
            sum += density; minimumDensity = Math.Min(minimumDensity, density);
        }
        return sum * (length / count) * ResponsiveSmokeField.Extinction;
    }
}
