using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenScct.Core;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests.Maps;

/// <summary>
/// The map catalog the menus show (docs/MAPS.md "The map catalog"): names, kinds, modes, starts, pictures, playlists
/// and order, on synthetic map folders and, when converted, on the real ones.
/// </summary>
public sealed class MapCatalogEntryTests : IDisposable
{
    private static readonly Encoding NoBom = new UTF8Encoding(false);

    private static readonly string Zeros = new('0', 64);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "svm-mapcatalog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_catalog_lists_host_maps_in_Reloaded_order_with_names_modes_starts_and_pictures()
    {
        MapCatalog catalog = Synthetic();
        IReadOnlyList<MapCatalogEntry> host = catalog.GetHostMaps();

        // Reloaded sorts by the name without its variant word, ties by code: Aquarius (AquaD, AquaE), Club House 3v3,
        // then Shadow House (ClubS). Training, menu and our own maps are not in the host list.
        Assert.Equal(["AquaD", "AquaE", "ClubT", "ClubS"], host.Select(e => e.Id));
        Assert.Equal([0, 1, 2, 3], host.Select(e => e.HostOrder));

        MapCatalogEntry aqua = catalog.GetCatalogEntry("aquae")!;
        Assert.Equal("ENHANCED AQUARIUS", aqua.DisplayName);
        Assert.Equal("Enhanced Aquarius", aqua.Name);
        Assert.Equal("Aquarius", aqua.SortName);
        Assert.Equal("Enhanced", aqua.VariantWord);
        Assert.Equal(MapKind.Versus, aqua.Kind);
        Assert.Equal(1, aqua.OriginalOrder);
        Assert.Equal((2, 1, 2), (aqua.SpyStarts, aqua.MercStarts, aqua.MercDeathmatchStarts));
        Assert.Equal(["Standard", "Merc Deathmatch"], aqua.Playlists);
        Assert.Equal(4, aqua.RecommendedPlayers);
        Assert.NotNull(aqua.PreviewImage);
        Assert.True(File.Exists(aqua.PreviewImage));
        Assert.Null(aqua.BriefingImage);
        Assert.Equal("Line one|Line two", aqua.Legend);

        // The four modes of the original's mode box, in its order.
        Assert.Equal(MapCatalog.MenuModes, aqua.Modes.Select(m => m.Mode));
        MapModeInfo story = aqua.Mode(EGameMode.GM_Multi)!;
        Assert.True(story.Playable);
        Assert.Equal((2, 1), (story.Objectives, story.ObjectivesNeeded));
        Assert.Equal(10, story.Defaults!.Value.TimeLimit);
        MapModeInfo disks = aqua.Mode(EGameMode.GM_DiskHunt)!;
        Assert.True(disks.Playable);
        Assert.Equal((3, 2), (disks.Objectives, disks.ObjectivesNeeded));
        Assert.True(aqua.Mode(EGameMode.GM_DeathMatch)!.Playable);
        MapModeInfo mdm = aqua.Mode(EGameMode.GM_MercDeathMatch)!;
        Assert.True(mdm.Playable);
        Assert.True(mdm.HasSettings); // reads the Deathmatch entry
        Assert.Equal(EGameMode.GM_Multi, aqua.DefaultMode);

        // A map without disks offers Disk Hunt in the menu, but it does not play there.
        MapCatalogEntry shadow = catalog.GetCatalogEntry("ClubS")!;
        Assert.False(shadow.Mode(EGameMode.GM_DiskHunt)!.Playable);
        Assert.Null(shadow.VariantWord);

        // A 3v3 playlist map is for six players; a map missing from Reloaded's names gets its menu name capitalised.
        MapCatalogEntry three = catalog.GetCatalogEntry("ClubT")!;
        Assert.Equal(6, three.RecommendedPlayers);
        Assert.Equal("Enhanced Club House 3v3", three.Name);
        Assert.Equal("Club House 3v3", three.SortName);
    }

    [Fact]
    public void Training_menu_and_own_maps_are_listed_but_not_hosted()
    {
        MapCatalog catalog = Synthetic();
        Assert.Equal(MapKind.Training, catalog.GetCatalogEntry("TSP20")!.Kind);
        Assert.Equal(MapKind.Menu, catalog.GetCatalogEntry("Entry")!.Kind);
        Assert.Equal(MapKind.Test, catalog.GetCatalogEntry("OwnMap")!.Kind);
        Assert.Equal(MapKind.Test, catalog.GetCatalogEntry(MapCatalog.TestBox)!.Kind);

        MapCatalogEntry tutorial = catalog.GetCatalogEntry("TSP20")!;
        Assert.Equal([EGameMode.GM_Multi], tutorial.PlayableModes);
        Assert.Equal("Movements", tutorial.Name);
        Assert.Empty(catalog.GetCatalogEntry("Entry")!.PlayableModes);

        // The full catalog: host maps first, then training, menu and test maps.
        Assert.Equal(["AquaD", "AquaE", "ClubT", "ClubS", "TSP20", "Entry"], catalog.GetCatalog().Take(6).Select(e => e.Id));
    }

    [Fact]
    public void Playlists_keep_installed_maps_in_host_order_and_name_the_missing_ones()
    {
        MapCatalog catalog = Synthetic();
        IReadOnlyList<MapPlaylist> lists = catalog.GetPlaylists();
        Assert.Equal(["Standard", "3v3", "Merc Deathmatch", MapCatalog.AllMapsPlaylist], lists.Select(p => p.Name));
        MapPlaylist standard = lists[0];
        Assert.Equal(EGameMode.GM_Multi, standard.Mode);
        Assert.Equal("Best maps.", standard.Description);
        Assert.Equal(["AquaE", "ClubS"], standard.Maps);
        Assert.Equal(["StatP"], standard.Missing);
        Assert.Equal(EGameMode.GM_MercDeathMatch, lists[2].Mode);
        Assert.Equal(catalog.GetHostMaps().Select(e => e.Id), lists[3].Maps);
    }

    [Fact]
    public void Without_the_catalog_data_every_converted_map_is_hosted_under_its_title()
    {
        string svmap = Path.Combine(_root, "bare", "svmap");
        WriteMap(svmap, "AquaE", "Enhanced Aquarius", "ENHANCED AQUARIUS", converted: true);
        WriteMap(svmap, "OwnMap", "Own", null, converted: false);
        var catalog = new MapCatalog(svmap);
        MapCatalogEntry aqua = catalog.GetCatalogEntry("AquaE")!;
        Assert.Equal(MapKind.Versus, aqua.Kind);
        Assert.Equal("Enhanced Aquarius", aqua.Name);
        Assert.Null(aqua.PreviewImage);
        Assert.Empty(aqua.Playlists);
        Assert.Equal(MapKind.Test, catalog.GetCatalogEntry("OwnMap")!.Kind);
        Assert.Equal([MapCatalog.AllMapsPlaylist], catalog.GetPlaylists().Select(p => p.Name));
    }

    /// <summary>The converted maps: every one is in the catalog, every host map has a picture and plays Story or Deathmatch.</summary>
    [Fact]
    public void Every_converted_map_is_in_the_catalog()
    {
        MapCatalog catalog = MapCatalog.CreateDefault();
        Assert.SkipWhen(catalog.FindCatalogFolder() is null, "the map catalog has not been built (python -m svmbuild maps-catalog).");
        string svmap = catalog.Roots.First(r => r.EndsWith("svmap", StringComparison.OrdinalIgnoreCase));
        var folders = Directory.GetDirectories(svmap).Select(Path.GetFileName).Where(n => File.Exists(Path.Combine(svmap, n!, "manifest.json"))).ToList();
        IReadOnlyList<MapCatalogEntry> all = catalog.GetCatalog();
        foreach (string? f in folders)
        {
            Assert.Contains(all, e => string.Equals(e.Id, f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (MapCatalogEntry e in catalog.GetHostMaps())
        {
            Assert.True(e.PreviewImage is not null, e.Id + " has no picture");
            Assert.True(e.Mode(EGameMode.GM_Multi)!.Playable || e.Mode(EGameMode.GM_DeathMatch)!.Playable, e.Id + " plays no mode");
            Assert.NotEqual(e.Id, e.DisplayName);
        }

        Assert.All(catalog.GetPlaylists().Where(p => p.Name != MapCatalog.AllMapsPlaylist), p => Assert.NotEmpty(p.Maps));
    }

    private MapCatalog Synthetic()
    {
        string svmap = Path.Combine(_root, "content", "svmap");
        WriteMap(svmap, "AquaD", "Aquarius", "AQUARIUS", converted: true);
        WriteMap(svmap, "AquaE", "Enhanced Aquarius", "ENHANCED AQUARIUS", converted: true, legend: "Line one|Line two");
        WriteMap(svmap, "ClubS", "Shadow House", "SHADOW HOUSE", converted: true, disks: 0);
        WriteMap(svmap, "ClubT", "Enhanced Club House", "ENHANCED CLUB HOUSE 3V3", converted: true);
        WriteMap(svmap, "TSP20", "Movements", null, converted: true);
        WriteMap(svmap, "Entry", "Entry", null, converted: true);
        WriteMap(svmap, "OwnMap", "Own", "OWN", converted: false);
        string shared = Path.Combine(_root, "content", "shared", "maps");
        Directory.CreateDirectory(Path.Combine(shared, "images", "AquaE"));
        File.WriteAllBytes(Path.Combine(shared, "images", "AquaE", "menu.png"), [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(shared, "catalog.json"), """
            {"format": "svm-map-catalog", "formatVersion": 1,
             "originalOrder": ["AquaD", "AquaE", "ClubS", "ClubT"],
             "training": {"spy": ["TSP20"], "merc": []}, "menuMaps": ["Entry", "Lobby"],
             "playlists": [
               {"name": "Standard", "description": "Best maps.", "gameMode": "Story", "maps": ["ClubS", "AquaE", "StatP"]},
               {"name": "3v3", "description": "More players.", "gameMode": "Story", "maps": ["ClubT"]},
               {"name": "Merc Deathmatch", "description": "Merc spawns.", "gameMode": "MercDeathMatch", "maps": ["AquaE"]}],
             "names": {"AquaD": "Aquarius", "AquaE": "Enhanced Aquarius", "ClubS": "Shadow House"},
             "variantWords": ["Enhanced", "Enh", "Phoenix's"],
             "images": {"AquaE": {"menu": "images/AquaE/menu.png", "briefing": "images/AquaE/briefing.png"}}}
            """, NoBom);
        return new MapCatalog(svmap);
    }

    private static void WriteMap(string svmap, string id, string title, string? mapName, bool converted, int disks = 3, string? legend = null)
    {
        string dir = Path.Combine(svmap, id);
        Directory.CreateDirectory(Path.Combine(dir, "sim"));
        Directory.CreateDirectory(Path.Combine(dir, "ui"));
        string source = converted
            ? $$"""{"generator": "test", "generatorVersion": 1, "kind": "converted", "original": {"file": "Packages/Maps/{{id}}.sdc", "sha256": "{{Zeros}}"} }"""
            : """{"generator": "test", "generatorVersion": 1, "kind": "synthetic"}""";
        File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""
            {"files": {}, "format": "svmap", "formatVersion": "1.1.0",
             "hashes": {"sim": "{{Zeros}}"},
             "map": "{{id}}", "requires": [],
             "source": {{source}}, "title": "{{title}}"}
            """, NoBom);
        if (mapName is not null)
        {
            File.WriteAllText(Path.Combine(dir, "ui", "mapinfo.json"),
                $$$"""{"MapName": "{{{mapName}}}", "LegendMap": "{{{legend ?? string.Empty}}}", "modes": {}}""", NoBom);
        }

        string diskList = string.Join(", ", Enumerable.Range(0, disks).Select(i => $"\"SDisk{i}\""));
        File.WriteAllText(Path.Combine(dir, "sim", "gameplay.json"), $$$"""
            {"actors": [
               {"class": "Engine.PlayerStart", "id": "S1", "props": {}},
               {"class": "Engine.PlayerStart", "id": "S2", "props": {"TeamNumber": 0}},
               {"class": "Engine.PlayerStart", "id": "M1", "props": {"TeamNumber": 1}},
               {"class": "SBase.STriggerPlayerStart", "id": "D1", "props": {"TeamNumber": 1, "bMercDeathMatchStart": true}},
               {"class": "Engine.PlayerStart", "id": "D2", "props": {"bMercDeathMatchStart": true}}],
             "missionTree": [
               {"gameMode": 5, "minimumObjectives": 1, "mission": "M5", "objectives": [{"objective": "O1", "triggers": ["T1"]}, {"objective": "O2", "triggers": ["T2"]}]},
               {"gameMode": 4, "minimumObjectives": 2, "mission": "M4", "objectives": [], "triggers": [{{{diskList}}}]},
               {"gameMode": 6, "mission": "M6", "objectives": []}],
             "mapSettings": {
               "Story": {"GameModeId": 5, "TimeLimit": 10, "SpiesLives": 4, "SentriesLives": 3, "RespawnTime": 10, "ScoreLimit": 0, "Handicap": 0, "NumDisksMax": 0},
               "DiskHunt": {"GameModeId": 4, "TimeLimit": 10, "SpiesLives": 4, "SentriesLives": 3, "RespawnTime": 10, "ScoreLimit": 2, "Handicap": 3, "NumDisksMax": 3},
               "Deathmatch": {"GameModeId": 6, "TimeLimit": 10, "SpiesLives": 8, "SentriesLives": 4, "RespawnTime": 5, "ScoreLimit": 0, "Handicap": 0, "NumDisksMax": 0} } }
            """, NoBom);
    }
}
