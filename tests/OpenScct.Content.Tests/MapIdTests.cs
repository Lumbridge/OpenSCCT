using System;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class MapIdTests
{
    [Theory]
    [InlineData("new-island")]
    [InlineData("Map-2_3")]
    [InlineData("_island")]
    public void MapFileStemsAreAcceptedConsistently(string id)
    {
        Assert.Equal(id, SvMapManifest.Parse(Rename("manifest.json", id)).Map);
        Assert.Equal(id, GameplayDoc.Parse(Rename("sim/gameplay.json", id)).Map);
        Assert.Equal(id, TraversalDoc.Parse(Rename("sim/traversal.json", id)).Map);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../new-island")]
    [InlineData("new/island")]
    [InlineData("new\\island")]
    [InlineData("new island")]
    [InlineData("new.island")]
    [InlineData("-island")]
    [InlineData("9map")]
    [InlineData("éclair")]
    public void InvalidMapFileStemsAreRejectedConsistently(string id)
    {
        Assert.Throws<SvMapFormatException>(() => SvMapManifest.Parse(Rename("manifest.json", id)));
        Assert.Throws<SvMapFormatException>(() => GameplayDoc.Parse(Rename("sim/gameplay.json", id)));
        Assert.Throws<SvMapFormatException>(() => TraversalDoc.Parse(Rename("sim/traversal.json", id)));
    }

    [Fact]
    public void MapNameLengthLimitAndActorNameRulesRemainEnforced()
    {
        MapFileStemsAreAcceptedConsistently(new string('a', 63));
        InvalidMapFileStemsAreRejectedConsistently(new string('a', 64));
        string gameplay = Encoding.UTF8.GetString(Rename("sim/gameplay.json", "new-island"));
        Assert.Contains("\"id\": \"Mover0\"", gameplay, StringComparison.Ordinal);
        Assert.Throws<SvMapFormatException>(() => GameplayDoc.Parse(Encoding.UTF8.GetBytes(
            gameplay.Replace("\"id\": \"Mover0\"", "\"id\": \"Mover-0\"", StringComparison.Ordinal))));
        string traversal = Encoding.UTF8.GetString(Rename("sim/traversal.json", "new-island"));
        Assert.Contains("\"id\": \"GE0\"", traversal, StringComparison.Ordinal);
        Assert.Throws<SvMapFormatException>(() => TraversalDoc.Parse(Encoding.UTF8.GetBytes(
            traversal.Replace("\"id\": \"GE0\"", "\"id\": \"GE-0\"", StringComparison.Ordinal))));
    }

    private static byte[] Rename(string file, string id) => Encoding.UTF8.GetBytes(
        Encoding.UTF8.GetString(TestPaths.TestBoxFile(file)).Replace(
            "\"map\": \"TestBox\"", "\"map\": " + JsonSerializer.Serialize(id), StringComparison.Ordinal));
}
