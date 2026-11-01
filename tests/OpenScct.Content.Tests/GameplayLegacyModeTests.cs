using System;
using System.Text;
using OpenScct.Content.Json;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class GameplayLegacyModeTests
{
    private static byte[] Document(string settings) => Encoding.UTF8.GetBytes($$$"""
        {
          "formatVersion": "1.1.0",
          "map": "Legacy",
          "levelInfo": {"id": "LevelInfo0", "class": "Engine.LevelInfo", "props": {"GEActivationMask": 0}},
          "mapSettings": { {{{settings}}} },
          "actors": [], "tagIndex": {}, "eventEdges": [], "missionTree": []
        }
        """);

    [Fact]
    public void LegacyMercDeathmatchSettingsLoadByNameWithoutChangingStoredKeysOrCanonicalBytes()
    {
        byte[] input = Document("\"7\": {\"GameModeId\": 7, \"TimeLimit\": 10, \"ScoreLimit\": 20, \"RespawnTime\": 1}");
        GameplayDoc doc = GameplayDoc.Parse(input);
        var stored = Assert.Single(doc.MapSettings);
        Assert.Equal("7", stored.Key);
        Assert.Same(stored.Value, doc.GetModeSettings("MercDeathmatch"));
        Assert.Same(stored.Value, doc.GetModeSettings("mercdeathmatch"));
        Assert.Same(stored.Value, doc.GetModeSettings("7"));
        Assert.Equal(7, stored.Value.GetInt("GameModeId"));
        Assert.Equal(20, stored.Value.GetInt("ScoreLimit"));
        Assert.Equal(1, stored.Value.GetInt("RespawnTime"));
        Assert.Equal(CanonicalJson.Canonicalize(input), CanonicalJson.ToCanonicalBytes(doc.ToJson()));
    }

    [Theory]
    [InlineData("\"7\": {\"GameModeId\": 7}, \"MercDeathmatch\": {\"GameModeId\": 7, \"ScoreLimit\": 40}")]
    [InlineData("\"MercDeathmatch\": {\"GameModeId\": 7, \"ScoreLimit\": 40}, \"7\": {\"GameModeId\": 7}")]
    public void ExplicitCanonicalModeTakesPrecedenceOverTheLookupAlias(string settings)
    {
        GameplayDoc doc = GameplayDoc.Parse(Document(settings));
        Assert.Equal(2, doc.MapSettings.Count);
        Assert.Equal(40, doc.GetModeSettings("MercDeathmatch")!.GetInt("ScoreLimit"));
        Assert.Equal(0, doc.GetModeSettings("7")!.GetInt("ScoreLimit"));
    }

    [Theory]
    [InlineData("\"7\": {}")]
    [InlineData("\"7\": {\"GameModeId\": 6}")]
    [InlineData("\"7\": {\"GameModeId\": \"7\"}")]
    [InlineData("\"07\": {\"GameModeId\": 7}")]
    [InlineData("\"8\": {\"GameModeId\": 8}")]
    [InlineData("\"7x\": {\"GameModeId\": 7}")]
    public void CompatibilityDoesNotAcceptArbitraryNumericOrMismatchedModeKeys(string settings)
    {
        var error = Assert.Throws<SvMapFormatException>(() => GameplayDoc.Parse(Document(settings)));
        Assert.Contains("is not a game mode name", error.Message, StringComparison.Ordinal);
    }
}
