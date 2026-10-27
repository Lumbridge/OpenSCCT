using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenScct.Content;
using OpenScct.Content.Maps;
using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScct.Server;

/// <summary>What a map is for: hosted matches, the front end, the tutorials, or our own test levels.</summary>
public enum MapKind
{
    /// <summary>A map the host screen lists (the original's GetMapList).</summary>
    Versus,

    /// <summary>The front end levels (Entry, Lobby): loaded behind the menus, never played.</summary>
    Menu,

    /// <summary>The tutorial and exam maps (TSP spy, TME merc), played from the tutorials menu in Story mode.</summary>
    Training,

    /// <summary>Our own levels (TestRange, fixtures, maps built in code): not in the original.</summary>
    Test,
}

/// <summary>
/// One mode of a map as the host menu offers it (docs/MAPS.md "Modes"): whether it plays on this map, the map's
/// settings entry for it, and its objectives.
/// </summary>
/// <param name="Mode">Story is <see cref="EGameMode.GM_Multi"/>.</param>
/// <param name="Name">Story, Disk Hunt, Deathmatch or Merc Deathmatch.</param>
/// <param name="Playable">The map has what the mode plays with (missions, disks, starts).</param>
/// <param name="HasSettings">The map has an <c>SMapSettings</c> entry for the mode (Merc Deathmatch reads Deathmatch's).</param>
/// <param name="Objectives">Story: objectives of the mission; Disk Hunt: disks placed; 0 otherwise.</param>
/// <param name="ObjectivesNeeded">Story: the minimum to take; Disk Hunt: disks to take by default; 0 otherwise.</param>
/// <param name="Defaults">The settings entry (time limit, lives, respawn, disks), or null.</param>
public sealed record MapModeInfo(EGameMode Mode, string Name, bool Playable, bool HasSettings, int Objectives, int ObjectivesNeeded,
    FGameModeInfo? Defaults);

/// <summary>A map as the menus show it (docs/MAPS.md "The map catalog").</summary>
public sealed record MapCatalogEntry
{
    /// <summary>The map code and file name (AquaE).</summary>
    public required string Id { get; init; }

    /// <summary>The name the original's menus show: <c>SMapSettings.MapName</c> (ENHANCED AQUARIUS), else the code.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The name Reloaded's lobby shows (Enhanced Aquarius).</summary>
    public required string Name { get; init; }

    /// <summary><see cref="Name"/> without its variant word (Aquarius), as Reloaded sorts.</summary>
    public required string SortName { get; init; }

    /// <summary>The variant word of <see cref="Name"/> (Enhanced, Phoenix's...), or null.</summary>
    public string? VariantWord { get; init; }

    public required MapKind Kind { get; init; }

    /// <summary>Index in <see cref="MapCatalog.GetHostMaps"/> (Reloaded order), -1 when the host list does not show it.</summary>
    public int HostOrder { get; init; } = -1;

    /// <summary>Index in the original's host list, -1 when it is not there.</summary>
    public int OriginalOrder { get; init; } = -1;

    /// <summary>The four modes of the original's mode box, in its order (Story, Disk Hunt, Deathmatch, Merc Deathmatch).</summary>
    public required IReadOnlyList<MapModeInfo> Modes { get; init; }

    /// <summary>The mode to preselect: Story when it plays, else the first that plays.</summary>
    public EGameMode DefaultMode { get; init; } = EGameMode.GM_Multi;

    public int SpyStarts { get; init; }

    public int MercStarts { get; init; }

    public int MercDeathmatchStarts { get; init; }

    /// <summary>6 for maps of Reloaded's 3v3 playlist, else 4 (the original's Max Players default).</summary>
    public int RecommendedPlayers { get; init; } = 4;

    /// <summary>Reloaded playlists the map is in.</summary>
    public IReadOnlyList<string> Playlists { get; init; } = [];

    /// <summary>The map list picture (PNG path), or null.</summary>
    public string? PreviewImage { get; init; }

    public string? LoadingSpyImage { get; init; }

    public string? LoadingMercImage { get; init; }

    public string? BriefingImage { get; init; }

    /// <summary><c>SMapSettings.LegendMap</c> (lines split by <c>|</c>), or null.</summary>
    public string? Legend { get; init; }

    /// <summary>The SVMap folder, or null for maps built in code.</summary>
    public string? Folder { get; init; }

    public ulong ContentHash { get; init; }

    /// <summary>The map has the render content the play scene draws.</summary>
    public bool CanRender { get; init; }

    /// <summary>The mode entry of <paramref name="mode"/>, or null.</summary>
    public MapModeInfo? Mode(EGameMode mode) => Modes.FirstOrDefault(m => m.Mode == mode);

    /// <summary>The modes that play on this map.</summary>
    public IEnumerable<EGameMode> PlayableModes => Modes.Where(m => m.Playable).Select(m => m.Mode);
}

/// <summary>A Reloaded map playlist (or "All").</summary>
/// <param name="Maps">Installed maps of the playlist, in host order.</param>
/// <param name="Missing">Codes the playlist names that are not installed.</param>
public sealed record MapPlaylist(string Name, string Description, EGameMode Mode, IReadOnlyList<string> Maps, IReadOnlyList<string> Missing);

public sealed partial class MapCatalog
{
    /// <summary>The name of the playlist of every installed map (Reloaded's "All").</summary>
    public const string AllMapsPlaylist = "All";

    /// <summary>The original's mode box, in its order (moListEditBox_GameMode: FillList then RefreshList).</summary>
    public static IReadOnlyList<EGameMode> MenuModes { get; } =
        [EGameMode.GM_Multi, EGameMode.GM_DiskHunt, EGameMode.GM_DeathMatch, EGameMode.GM_MercDeathMatch];

    private List<MapCatalogEntry>? _catalog;
    private List<MapPlaylist>? _playlists;
    private MapCatalogData? _catalogData;

    /// <summary>
    /// Forgets what was read from the content folders, so maps converted while the game runs (a map fetched for a
    /// lobby) are listed and a map converted again is read again. Loaded maps already in use are not touched.
    /// </summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _catalog = null;
            _playlists = null;
            _catalogData = null;
            _entries.Clear();
            _cache.Clear();
        }
    }

    /// <summary>The menu name of a mode (Story, Disk Hunt, Deathmatch, Merc Deathmatch).</summary>
    public static string ModeName(EGameMode mode) => mode switch
    {
        EGameMode.GM_Multi => "Story",
        EGameMode.GM_DiskHunt => "Disk Hunt",
        EGameMode.GM_DeathMatch => "Deathmatch",
        EGameMode.GM_MercDeathMatch => "Merc Deathmatch",
        EGameMode.GM_Extraction => "Extraction",
        EGameMode.GM_Sabotage => "Sabotage",
        EGameMode.GM_Neutralization => "Neutralization",
        _ => "Undefined",
    };

    /// <summary>The <c>PlaySettings.Mode</c> name of a mode (Story, DiskHunt, Deathmatch, MercDeathMatch).</summary>
    public static string ModeKey(EGameMode mode) => mode switch
    {
        EGameMode.GM_DiskHunt => "DiskHunt",
        EGameMode.GM_DeathMatch => "Deathmatch",
        EGameMode.GM_MercDeathMatch => "MercDeathMatch",
        _ => "Story",
    };

    /// <summary>The map catalog data folder (<c>shared/maps</c> beside an SVMap root), or null.</summary>
    public string? FindCatalogFolder()
    {
        foreach (string root in _roots)
        {
            string candidate = Path.Combine(root, "..", "shared", "maps");
            if (File.Exists(Path.Combine(candidate, MapCatalogData.FileName)))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>The catalog data (playlists, names, pictures), read once; empty without the catalog folder.</summary>
    public MapCatalogData CatalogData
    {
        get
        {
            lock (_gate)
            {
                return _catalogData ??= MapCatalogData.Load(FindCatalogFolder());
            }
        }
    }

    /// <summary>
    /// Every map of the catalog (SVMap folders, maps built in code, the TestBox stub), menu and training maps too,
    /// in host order (the host list's maps first, then training, menu and test maps). Built once per catalog.
    /// </summary>
    public IReadOnlyList<MapCatalogEntry> GetCatalog()
    {
        List<MapCatalogEntry> built;
        lock (_gate)
        {
            built = _catalog ??= BuildCatalog();
        }

        // Maps built in code after the first call (tests) are added at the end.
        var extra = new List<MapCatalogEntry>();
        lock (_gate)
        {
            foreach (string name in _builders.Keys)
            {
                if (!built.Any(e => string.Equals(e.Id, name, StringComparison.OrdinalIgnoreCase)) &&
                    !string.Equals(name, Lobby, StringComparison.OrdinalIgnoreCase))
                {
                    extra.Add(CodeEntry(name, 0));
                }
            }
        }

        return extra.Count == 0 ? built : built.Concat(extra).ToList();
    }

    /// <summary>The maps the host screen lists (the original's host list: no menu, training or test maps), in Reloaded's order.</summary>
    public IReadOnlyList<MapCatalogEntry> GetHostMaps() => GetCatalog().Where(e => e.Kind == MapKind.Versus).ToList();

    /// <summary>
    /// The maps a session moves through without the host picking one (<c>!next</c>, <c>!prev</c>, Random Map): the
    /// host list in its order, or every listed map (<see cref="ListMaps"/>) when no converted map is installed.
    /// </summary>
    public IReadOnlyList<string> RotationMaps()
    {
        IReadOnlyList<MapCatalogEntry> host = GetHostMaps();
        return host.Count > 0 ? host.Select(e => e.Id).ToList() : ListMaps().Select(e => e.Id).ToList();
    }

    /// <summary>The catalog entry of a map (ASCII case ignored), or null.</summary>
    public MapCatalogEntry? GetCatalogEntry(string mapName) =>
        GetCatalog().FirstOrDefault(e => string.Equals(e.Id, mapName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reloaded's playlists (Standard, Extended, Classic, 3v3, Merc Deathmatch), then "All" (every host map).</summary>
    public IReadOnlyList<MapPlaylist> GetPlaylists()
    {
        IReadOnlyList<MapCatalogEntry> host = GetHostMaps();
        lock (_gate)
        {
            if (_playlists is not null)
            {
                return _playlists;
            }
        }

        var lists = new List<MapPlaylist>();
        var order = host.Select((e, i) => (e.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.OrdinalIgnoreCase);
        foreach (MapPlaylistData p in CatalogData.Playlists)
        {
            var maps = p.Maps.Where(order.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => order[m])
                .Select(m => host[order[m]].Id).ToList();
            var missing = p.Maps.Where(m => !order.ContainsKey(m)).ToList();
            EGameMode mode = string.Equals(p.GameMode, "MercDeathMatch", StringComparison.OrdinalIgnoreCase)
                ? EGameMode.GM_MercDeathMatch
                : EGameMode.GM_Multi;
            lists.Add(new MapPlaylist(p.Name, p.Description, mode, maps, missing));
        }

        lists.Add(new MapPlaylist(AllMapsPlaylist, "Every installed map.", EGameMode.GM_Multi, host.Select(e => e.Id).ToList(), []));
        lock (_gate)
        {
            _playlists = lists;
        }

        return lists;
    }

    private List<MapCatalogEntry> BuildCatalog()
    {
        MapCatalogData data = MapCatalogData.Load(FindCatalogFolder());
        _catalogData ??= data;
        var originalIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < data.OriginalOrder.Count; i++)
        {
            originalIndex.TryAdd(data.OriginalOrder[i], i);
        }

        var playlistsOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (MapPlaylistData p in data.Playlists)
        {
            foreach (string m in p.Maps)
            {
                if (!playlistsOf.TryGetValue(m, out List<string>? l))
                {
                    playlistsOf[m] = l = new List<string>();
                }

                if (!l.Contains(p.Name))
                {
                    l.Add(p.Name);
                }
            }
        }

        var entries = new List<MapCatalogEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in _roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            string[] dirs = Directory.GetDirectories(root);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                if (TryReadEntry(dir) is not { } listed || !seen.Add(listed.Id))
                {
                    continue;
                }

                SvMapManifest? manifest = null;
                try
                {
                    manifest = SvMapManifest.Load(Path.Combine(dir, SvMapHash.ManifestFile));
                }
                catch (Exception e) when (e is SvMapFormatException or IOException or KeyNotFoundException)
                {
                }

                MapFacts facts;
                try
                {
                    facts = MapFacts.Load(dir);
                }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
                {
                    facts = MapFacts.Parse(null, null);
                }

                entries.Add(FolderEntry(listed, manifest, facts, data, originalIndex, playlistsOf));
            }
        }

        lock (_gate)
        {
            foreach (string name in _builders.Keys)
            {
                if (seen.Add(name) && !string.Equals(name, Lobby, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(CodeEntry(name, 0));
                }
            }
        }

        if (seen.Add(TestBox))
        {
            entries.Add(CodeEntry(TestBox, StubContentHash));
        }

        // Host order: the host list's maps by name without the variant word (Reloaded), then training (tutorials
        // menu order), menu and test maps.
        var training = data.TrainingSpy.Concat(data.TrainingMerc).ToList();
        int KindRank(MapCatalogEntry e) => e.Kind switch
        {
            MapKind.Versus => 0,
            MapKind.Training => 1,
            MapKind.Menu => 2,
            _ => 3,
        };
        int TrainingRank(MapCatalogEntry e)
        {
            int i = training.FindIndex(t => string.Equals(t, e.Id, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        }

        List<MapCatalogEntry> ordered = entries
            .OrderBy(KindRank)
            .ThenBy(e => e.Kind == MapKind.Training ? TrainingRank(e) : 0)
            .ThenBy(e => e.Kind == MapKind.Versus ? e.SortName : e.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int host = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Kind == MapKind.Versus)
            {
                ordered[i] = ordered[i] with { HostOrder = host++ };
            }
        }

        return ordered;
    }

    private MapCatalogEntry FolderEntry(MapListEntry listed, SvMapManifest? manifest, MapFacts facts, MapCatalogData data,
        Dictionary<string, int> originalIndex, Dictionary<string, List<string>> playlistsOf)
    {
        string id = listed.Id;
        MapKind kind = KindOf(id, manifest, data, originalIndex);
        string title = string.IsNullOrEmpty(manifest?.Title) ? id : manifest!.Title;
        string? mapName = string.IsNullOrWhiteSpace(facts.MapName) || facts.MapName == "None" ? null : facts.MapName;
        string name = data.Names.TryGetValue(id, out string? reloaded) && !string.IsNullOrEmpty(reloaded)
            ? reloaded
            : mapName is not null && kind == MapKind.Versus ? TitleCase(mapName) : title;
        (string sortName, string? variant) = SplitVariant(name, data.VariantWords);
        IReadOnlyList<MapModeInfo> modes = ModesOf(kind, facts);
        playlistsOf.TryGetValue(id, out List<string>? playlists);
        bool threeVsThree = playlists?.Any(p => p == "3v3") == true;
        return new MapCatalogEntry
        {
            Id = id,
            DisplayName = mapName ?? id,
            Name = name,
            SortName = sortName,
            VariantWord = variant,
            Kind = kind,
            OriginalOrder = originalIndex.TryGetValue(id, out int o) ? o : -1,
            Modes = modes,
            DefaultMode = DefaultModeOf(modes),
            SpyStarts = facts.SpyStarts,
            MercStarts = facts.MercStarts,
            MercDeathmatchStarts = facts.MercDeathmatchStarts,
            RecommendedPlayers = threeVsThree ? 6 : 4,
            Playlists = playlists ?? [],
            PreviewImage = data.Image(id, "menu"),
            LoadingSpyImage = data.Image(id, "loading-spy"),
            LoadingMercImage = data.Image(id, "loading-merc"),
            BriefingImage = data.Image(id, "briefing"),
            Legend = string.IsNullOrWhiteSpace(facts.Legend) ? null : facts.Legend,
            Folder = listed.Folder,
            ContentHash = listed.ContentHash,
            CanRender = listed.Folder is { } f && Directory.Exists(Path.Combine(f, "render")),
        };
    }

    private static MapCatalogEntry CodeEntry(string name, ulong hash)
    {
        MapModeInfo[] modes = MenuModes.Select(m => new MapModeInfo(m, ModeName(m), m != EGameMode.GM_DiskHunt, false, 0, 0, null)).ToArray();
        return new MapCatalogEntry
        {
            Id = name,
            DisplayName = name,
            Name = name,
            SortName = name,
            Kind = MapKind.Test,
            Modes = modes,
            DefaultMode = EGameMode.GM_Multi,
            ContentHash = hash,
        };
    }

    private static MapKind KindOf(string id, SvMapManifest? manifest, MapCatalogData data, Dictionary<string, int> originalIndex)
    {
        if (IsMenuMap(id) || data.MenuMaps.Any(m => string.Equals(m, id, StringComparison.OrdinalIgnoreCase)))
        {
            return MapKind.Menu;
        }

        if (IsTrainingMap(id))
        {
            return MapKind.Training;
        }

        if (originalIndex.Count > 0)
        {
            return originalIndex.ContainsKey(id) ? MapKind.Versus : MapKind.Test;
        }

        // Without the catalog data: converted maps are hostable, our own are test maps.
        return manifest?.Source.IsConverted == true ? MapKind.Versus : MapKind.Test;
    }

    /// <summary>The original list builder's tutorial patterns (TME**, TSP**: same length, any character for '*').</summary>
    public static bool IsTrainingMap(string mapName) =>
        mapName.Length == 5 && (mapName.StartsWith("TME", StringComparison.OrdinalIgnoreCase) || mapName.StartsWith("TSP", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<MapModeInfo> ModesOf(MapKind kind, MapFacts facts)
    {
        var list = new List<MapModeInfo>(MenuModes.Count);
        foreach (EGameMode mode in MenuModes)
        {
            EGameMode rulesMode = mode == EGameMode.GM_MercDeathMatch ? EGameMode.GM_DeathMatch : mode;
            MapModeSettingsFacts? s = facts.SettingsFor(rulesMode);
            FGameModeInfo? defaults = s is null
                ? null
                : new FGameModeInfo
                {
                    TimeLimit = s.TimeLimit,
                    ScoreLimit = s.ScoreLimit,
                    SpiesLives = s.SpiesLives,
                    RespawnTime = s.RespawnTime,
                    SentriesLives = s.SentriesLives,
                    Handicap = s.Handicap,
                    NumDisksMax = s.NumDisksMax,
                    GameModeId = s.Mode,
                };
            MapMissionFacts? mission = facts.Mission(mode);
            int objectives = 0, needed = 0;
            bool playable;
            switch (mode)
            {
                case EGameMode.GM_Multi:
                    objectives = mission?.Objectives ?? 0;
                    needed = mission?.MinimumObjectives ?? 0;
                    // The tutorials play in Story mode whatever their missions (Tuto_MapsList.LoadMap).
                    playable = kind == MapKind.Training || objectives > 0;
                    break;
                case EGameMode.GM_DiskHunt:
                    objectives = mission?.Disks ?? 0;
                    needed = DisksToTake(s);
                    playable = kind != MapKind.Training && objectives > 0;
                    break;
                case EGameMode.GM_DeathMatch:
                    playable = kind != MapKind.Training && facts.SpyStarts > 0 && facts.MercStarts > 0;
                    break;
                default:
                    playable = kind != MapKind.Training && facts.SpyStarts + facts.MercStarts + facts.MercDeathmatchStarts > 0;
                    break;
            }

            if (kind == MapKind.Menu)
            {
                playable = false;
            }

            list.Add(new MapModeInfo(mode, ModeName(mode), playable, s is not null, objectives, needed, defaults));
        }

        return list;
    }

    /// <summary>Disks to take as the host menu starts them (Create_Match.LoadModeRules: active disks then disks to take, clamped).</summary>
    private static int DisksToTake(MapModeSettingsFacts? s)
    {
        if (s is null)
        {
            return 1;
        }

        int handicap = Math.Max(1, s.Handicap);
        if (handicap > s.NumDisksMax)
        {
            handicap = s.NumDisksMax;
        }

        int score = Math.Max(1, s.ScoreLimit);
        return score > handicap ? handicap : score;
    }

    private static EGameMode DefaultModeOf(IReadOnlyList<MapModeInfo> modes)
    {
        foreach (MapModeInfo m in modes)
        {
            if (m.Playable)
            {
                return m.Mode;
            }
        }

        return EGameMode.GM_Multi;
    }

    /// <summary>The name without its variant word (Reloaded's map_variant_words: the first whole word that is one), and that word.</summary>
    public static (string SortName, string? VariantWord) SplitVariant(string name, IReadOnlyList<string> variantWords)
    {
        string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            foreach (string v in variantWords)
            {
                if (string.Equals(words[i], v, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = string.Join(' ', words.Where((_, k) => k != i));
                    return (rest.Length == 0 ? name : rest, words[i]);
                }
            }
        }

        return (name, null);
    }

    /// <summary>A menu name in Reloaded's case for maps missing from its table: each word capitalised (ENHANCED ORPHANAGE 3V3 to Enhanced Orphanage 3v3).</summary>
    public static string TitleCase(string upper)
    {
        string[] words = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            words[i] = w.Length == 0 ? w : char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..].ToLower(CultureInfo.InvariantCulture);
        }

        return string.Join(' ', words);
    }
}
