using System;
using System.IO;
using System.Text.Json;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class MapBrowserViewSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "svm-mapview-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ProfilesWithoutAViewKeepThePictureGrid()
    {
        Assert.Equal(MapBrowserView.PictureGrid, new GameSettings().MapView);
        Assert.Equal(MapBrowserView.PictureGrid, GameSettings.FromJson("{}").MapView);
    }

    [Theory]
    [InlineData(true, MapBrowserView.List)]
    [InlineData(false, MapBrowserView.PictureGrid)]
    public void LegacyListPreferenceMigratesWhenTheNewViewIsMissing(bool legacy, MapBrowserView expected)
    {
        GameSettings loaded = GameSettings.FromJson($$"""{"map_list_view":{{legacy.ToString().ToLowerInvariant()}}}""");
        Assert.Equal(expected, loaded.MapView);
        Assert.Equal(expected, loaded.Clone().MapView);
        using JsonDocument saved = JsonDocument.Parse(loaded.ToJson());
        Assert.Equal((int)expected, saved.RootElement.GetProperty("map_view").GetInt32());
        Assert.False(saved.RootElement.TryGetProperty("map_list_view", out _));
    }

    [Theory]
    [InlineData("""{"map_view":0,"map_list_view":true}""", MapBrowserView.PictureGrid)]
    [InlineData("""{"map_list_view":true,"map_view":0}""", MapBrowserView.PictureGrid)]
    [InlineData("""{"map_view":2,"map_list_view":true}""", MapBrowserView.ThumbnailList)]
    [InlineData("""{"map_list_view":false,"map_view":2}""", MapBrowserView.ThumbnailList)]
    public void ExplicitViewWinsOverLegacyPreferenceInEitherOrder(string json, MapBrowserView expected)
    {
        Assert.Equal(expected, GameSettings.FromJson(json).MapView);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(100)]
    public void UnknownViewValuesFallBackToPictureGrid(int value)
    {
        var direct = new GameSettings { MapView = (MapBrowserView)value };
        direct.Clamp();
        Assert.Equal(MapBrowserView.PictureGrid, direct.MapView);
        GameSettings loaded = GameSettings.FromJson($$"""{"map_view":{{value}},"map_list_view":true}""");
        Assert.Equal(MapBrowserView.PictureGrid, loaded.MapView);
    }

    [Fact]
    public void ThumbnailListSurvivesCloneSaveAndLoad()
    {
        var original = new GameSettings { MapView = MapBrowserView.ThumbnailList };
        Assert.False(original.MapListView);
        Assert.Equal(MapBrowserView.ThumbnailList, original.Clone().MapView);
        string path = Path.Combine(_directory, "settings.json");
        original.Save(path);
        Assert.Equal(MapBrowserView.ThumbnailList, GameSettings.Load(path).MapView);
    }

    [Fact]
    public void LegacyAliasStillSelectsTheOriginalTwoViews()
    {
        var settings = new GameSettings { MapView = MapBrowserView.ThumbnailList };
        settings.MapListView = true;
        Assert.Equal(MapBrowserView.List, settings.MapView);
        Assert.True(settings.MapListView);
        settings.MapListView = false;
        Assert.Equal(MapBrowserView.PictureGrid, settings.MapView);
        Assert.False(settings.MapListView);
    }
}
