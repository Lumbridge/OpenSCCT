using System;
using System.Text;
using OpenScct.Content.Json;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class TraversalDocTests
{
    private static TraversalDoc TestBox() => TraversalDoc.Parse(TestPaths.TestBoxFile("sim/traversal.json"));

    [Fact]
    public void TestBoxGes()
    {
        TraversalDoc doc = TestBox();
        Assert.Equal("TestBox", doc.Map);
        Assert.Equal(3, doc.Ges.Count);

        GERecord ledge = doc.Ges[0];
        Assert.Equal("GE0", ledge.Id);
        Assert.Equal(0, ledge.Index);
        Assert.Equal(GEKind.LG, ledge.Kind);
        Assert.Equal(2, ledge.Points.Length);
        Assert.Equal(new FVector(128f, 832f, 224f), ledge.P0);
        Assert.Equal(new FVector(384f, 832f, 224f), ledge.P1);
        Assert.Equal(GEPlatform.Common, ledge.Platform);
        Assert.Equal("synthetic", ledge.Source.Kind);
        Assert.Equal("Room A ledge block", ledge.Source.Note);

        GERecord zip = doc.Find("ge2")!;
        Assert.Equal(GEKind.ZL, zip.Kind);
        Assert.Equal(new FVector(2400f, 128f, 150f), zip.P1);
    }

    [Fact]
    public void MaskGatesGesByTheActivationMask()
    {
        TraversalDoc doc = TestBox();
        GameplayDoc gameplay = GameplayDoc.Parse(TestPaths.TestBoxFile("sim/gameplay.json"));
        GERecord gated = doc.Find("GE1")!;
        Assert.Equal(4, gated.Mask);
        Assert.True(gated.IsActive(gameplay.GEActivationMask));
        Assert.False(gated.IsActive(0));
        Assert.False(gated.IsActive(0b1011));
        Assert.True(doc.Find("GE0")!.IsActive(0));
        Assert.Equal(3, doc.CountActive(gameplay.GEActivationMask));
        Assert.Equal(2, doc.CountActive(0));
        Assert.Equal(1, doc.CountActive(0, GEKind.LG));
    }

    [Fact]
    public void ToJsonRoundTrips()
    {
        byte[] file = TestPaths.TestBoxFile("sim/traversal.json");
        TraversalDoc doc = TraversalDoc.Parse(file);
        Assert.Equal(CanonicalJson.Canonicalize(file), CanonicalJson.ToCanonicalBytes(doc.ToJson()));
        Assert.Equal(file, CanonicalJson.ToFileBytes(doc.ToJson()));
    }

    [Fact]
    public void PointCountsFollowTheKind()
    {
        Assert.Equal(3, GERecord.PointCount(GEKind.LADDER));
        Assert.Equal(3, GERecord.PointCount(GEKind.PIPE));
        Assert.Equal(2, GERecord.PointCount(GEKind.HOH));
        Assert.Throws<ArgumentException>(() =>
            new GERecord(0, "GE0", GEKind.PIPE, [default, default], 0, GEPlatform.Common, GESource.Synthetic()));
    }

    [Theory]
    [InlineData("\"kind\": \"LG\"", "\"kind\": \"lg\"", "unknown GE kind")]
    [InlineData("\"kind\": \"LG\"", "\"kind\": \"PIPE\"", "points")]
    [InlineData("\"mask\": 0", "\"mask\": 256", "outside 0..255")]
    [InlineData("\"platform\": 0", "\"platform\": 3", "outside 0..2")]
    [InlineData("\"id\": \"GE1\"", "\"id\": \"ge0\"", "duplicate GE id")]
    [InlineData("\"kind\": \"synthetic\"", "\"kind\": \"made up\"", "unknown GE source kind")]
    [InlineData("[128.0, 832.0, 224.0]", "[128.0, 832.0]", "3 coordinates")]
    public void MalformedDocumentsAreRejected(string from, string to, string reason)
    {
        string text = Encoding.UTF8.GetString(TestPaths.TestBoxFile("sim/traversal.json"));
        int at = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(at >= 0, from);
        byte[] broken = Encoding.UTF8.GetBytes(text[..at] + to + text[(at + from.Length)..]);
        var e = Assert.Throws<SvMapFormatException>(() => TraversalDoc.Parse(broken));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
    }
}
