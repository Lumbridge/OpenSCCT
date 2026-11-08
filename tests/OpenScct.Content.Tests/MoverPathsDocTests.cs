using System;
using System.Linq;
using System.Text;
using OpenScct.Content.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class MoverPathsDocTests
{
    [Fact]
    public void NativePathRoundTripsPastBothLegacyLimits()
    {
        var path = new MoverPath(Enumerable.Range(0, 300).Select(i => new MoverPathKey(new FVector(i, 2 * i, -i), new FRotator(0, 64 * i, 0))).ToArray(), 280);
        var read = MoverPathsDoc.FromJson(MoverPathsDoc.ToJson([("Platform", path)]))["platform"];
        Assert.Equal(300, read.Keys.Count); Assert.Equal(280, read.Start); Assert.Equal(path.Keys[299], read.Keys[299]);
    }

    [Theory]
    [InlineData("{\"format\":\"bad\",\"movers\":[]}")]
    [InlineData("{\"format\":\"svm.mover-paths/1\",\"movers\":[{\"actor\":\"A\",\"keys\":[]}]}")]
    [InlineData("{\"format\":\"svm.mover-paths/1\",\"movers\":[{\"actor\":\"A\",\"start\":1,\"keys\":[{\"offset\":[0,0,0],\"turn\":[0,0,0]}]}]}")]
    [InlineData("{\"format\":\"svm.mover-paths/1\",\"movers\":[{\"actor\":\"A\",\"keys\":[{\"offset\":[0,0],\"turn\":[0,0,0]}]}]}")]
    [InlineData("{\"format\":\"svm.mover-paths/1\",\"movers\":[{\"actor\":\"A\",\"keys\":[{\"offset\":[0,0,0],\"turn\":[0,0.5,0]}]}]}")]
    public void MalformedPathsAreRejected(string json) =>
        Assert.Throws<SvMapFormatException>(() => MoverPathsDoc.FromJson(SvJson.Parse(Encoding.UTF8.GetBytes(json))));

    [Fact]
    public void DuplicateActorNamesIgnoreCase()
    {
        var path = new MoverPath([new()]);
        Assert.Throws<SvMapFormatException>(() => MoverPathsDoc.FromJson(MoverPathsDoc.ToJson([("Platform", path), ("platform", path)])));
    }
}
