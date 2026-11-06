using System;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScctGame.Render;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SmokeGrowthCacheTests
{
    private static SmokeRenderCloud Cloud(object identity, uint revision = 1, uint geometry = 1, ushort activation = 0, float created = 0)
    {
        var cells = new byte[ResponsiveSmokeField.CellCount]; cells[0] = 1;
        var ticks = new ushort[cells.Length]; Array.Fill(ticks,ResponsiveSmokeField.Unreached); ticks[0] = activation;
        return new(identity,new(revision,created,created+2,default,FVector.Zero,cells,ticks,[]),geometry);
    }

    [Fact]
    public void MatureGrowthSurvivesTimeAndCarveOnlyRevisions()
    {
        var cache = new SmokeGrowthCache(); var resource = new object(); var identity = new object();
        cache.Begin(1,resource,1); Assert.True(cache.Update(0,Cloud(identity),2));
        cache.Begin(1,resource,1); Assert.False(cache.Update(0,Cloud(identity,revision:25),3));
        cache.Begin(1,resource,1); Assert.False(cache.Update(0,Cloud(identity,revision:26),4));
    }

    [Fact]
    public void LateGeometryResumesTheActivationRampEvenWithUnchangedCarveRevision()
    {
        var cache = new SmokeGrowthCache(); var resource = new object(); var identity = new object();
        cache.Begin(1,resource,1); cache.Update(0,Cloud(identity,revision:90),2);
        SmokeRenderCloud opened = Cloud(identity,revision:90,geometry:2,activation:40);
        cache.Begin(1,resource,1); Assert.True(cache.Update(0,opened,2));
        cache.Begin(1,resource,1); Assert.True(cache.Update(0,opened,2.05f));
        cache.Begin(1,resource,1); Assert.True(cache.Update(0,opened,2.2f));
        cache.Begin(1,resource,1); Assert.False(cache.Update(0,opened,3));
    }

    [Fact]
    public void LongUptimeCannotRoundTheActivationRampEndDown()
    {
        var cache = new SmokeGrowthCache(); var resource = new object();
        SmokeRenderCloud cloud = Cloud(new object(),activation:1,created:60000);
        float before = 60000.15f;
        Assert.True(((before-cloud.CreatedAt)-.05f)/ResponsiveSmokeField.GrowthFade < 1);
        cache.Begin(1,resource,1); cache.Update(0,cloud,before);
        cache.Begin(1,resource,1); Assert.True(cache.Update(0,cloud,MathF.BitIncrement(before)));
        cache.Begin(1,resource,1); Assert.False(cache.Update(0,cloud,60001));
    }

    [Fact]
    public void AuthoritativeGrowthChangesWithoutAReceivedSnapshotStamp()
    {
        var cache = new SmokeGrowthCache(); var resource = new object(); var identity = new object();
        cache.Begin(1,resource,1); cache.Update(0,Cloud(identity,geometry:0),2);
        cache.Begin(1,resource,1);
        Assert.True(cache.Update(0,Cloud(identity,revision:2,geometry:0,activation:40),2));
    }

    [Fact]
    public void IndependentCloudClocksAndReplayRewindsInvalidateOnlyTheirOwnRamp()
    {
        var cache = new SmokeGrowthCache(); var resource = new object();
        SmokeRenderCloud mature = Cloud(new object()), growing = Cloud(new object(),activation:20);
        cache.Begin(2,resource,1); cache.Update(0,mature,2); cache.Update(1,growing,1);
        cache.Begin(2,resource,1); Assert.False(cache.Update(0,mature,3)); Assert.True(cache.Update(1,growing,1.05f));
        cache.Begin(2,resource,1); Assert.True(cache.Update(0,mature,1)); Assert.False(cache.Update(1,growing,1.05f));
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("quality")]
    [InlineData("layout")]
    [InlineData("idle")]
    public void ResourceAndLifecycleChangesRebuildTheAtlas(string change)
    {
        var cache = new SmokeGrowthCache(); var resource = new object(); SmokeRenderCloud cloud = Cloud(new object());
        cache.Begin(1,resource,1); cache.Update(0,cloud,2);
        if (change == "idle") cache.Clear();
        cache.Begin(change == "layout" ? 2 : 1,change == "resource" ? new object() : resource,change == "quality" ? 2 : 1);
        Assert.True(cache.Update(0,cloud,3));
    }

    [Fact]
    public void LayerReorderingCannotReuseAnotherCloudsGrowth()
    {
        var cache = new SmokeGrowthCache(); var resource = new object();
        SmokeRenderCloud a = Cloud(new object()), b = Cloud(new object());
        cache.Begin(2,resource,1); cache.Update(0,a,2); cache.Update(1,b,2);
        cache.Begin(2,resource,1); Assert.True(cache.Update(0,b,3)); Assert.True(cache.Update(1,a,3));
    }
}
