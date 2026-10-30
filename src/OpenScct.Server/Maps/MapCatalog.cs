using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics;

namespace OpenScct.Server;

/// <summary>A map the catalog can load: its id (folder name), display title, content hash and folder (null when built in).</summary>
public sealed record MapListEntry(string Id, string Title, ulong ContentHash, string? Folder);

/// <summary>A place a player can start: location, rotation and team (0 spies, 1 mercs, as converted maps suggest).</summary>
public readonly record struct MapStart(FVector Location, FRotator Rotation, byte TeamNumber);

/// <summary>A loaded map as the host sees it.</summary>
public sealed class MapInfo
{
    /// <summary>
    /// A map: its name, the file a version mismatch names, its content hash and sim hash, its sim file digests, its
    /// player starts, the map actors by SVMap record index (bound to channels), and what the loader and physics made.
    /// Maps built in code pass their own actors and no loader result.
    /// </summary>
    public MapInfo(string name, string contentFile, ulong contentHash, string simHash, IReadOnlyList<SvMapFileDigest> files,
        MapStart[] playerStarts, IReadOnlyList<Actor?> mapActors, MapLoadResult? loaded = null, PhysicsWorld? physics = null)
    {
        Name = name;
        ContentFile = contentFile;
        ContentHash = contentHash;
        SimHash = simHash;
        Files = files;
        Starts = playerStarts;
        var locations = new FVector[playerStarts.Length];
        for (int i = 0; i < playerStarts.Length; i++)
        {
            locations[i] = playerStarts[i].Location;
        }

        PlayerStarts = locations;
        MapActors = mapActors;
        Loaded = loaded;
        Physics = physics;
    }

    public string Name { get; }

    /// <summary>File named in a version mismatch rejection when no finer answer is known.</summary>
    public string ContentFile { get; }

    /// <summary>The map's content hash: the first 64 bits of the manifest's sim hash (0 for the built-in stub).</summary>
    public ulong ContentHash { get; }

    /// <summary>The manifest's sim hash (hex), or the stub's name.</summary>
    public string SimHash { get; }

    /// <summary>Digest of every sim file, to name the file that differs between two installs.</summary>
    public IReadOnlyList<SvMapFileDigest> Files { get; }

    public IReadOnlyList<FVector> PlayerStarts { get; }

    public IReadOnlyList<MapStart> Starts { get; }

    /// <summary>What the loader created (null for the built-in stub).</summary>
    public MapLoadResult? Loaded { get; }

    public PhysicsWorld? Physics { get; }

    /// <summary>The map actors by SVMap record index, for <see cref="NetMapBinding"/> (empty for the stub).</summary>
    public IReadOnlyList<Actor?> MapActors { get; }

    /// <summary>
    /// The sim files listed as <c>path=sha256</c> pairs separated by <c>;</c>, sent to clients so a mismatch names
    /// the file.
    /// </summary>
    public string FileListing
    {
        get
        {
            var parts = new List<string>(Files.Count);
            foreach (SvMapFileDigest f in Files)
            {
                parts.Add(f.Path + "=" + f.Sha256);
            }

            return string.Join(";", parts);
        }
    }

    /// <summary>The first sim file whose digest differs from <paramref name="listing"/> (see <see cref="FileListing"/>), or null.</summary>
    public string? FirstDifferingFile(string? listing)
    {
        var theirs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in (listing ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                theirs[part[..eq]] = part[(eq + 1)..];
            }
        }

        foreach (SvMapFileDigest f in Files)
        {
            if (!theirs.TryGetValue(f.Path, out string? digest) || digest != f.Sha256)
            {
                return Name + "/" + f.Path;
            }

            theirs.Remove(f.Path);
        }

        foreach (string path in theirs.Keys)
        {
            return Name + "/" + path;
        }

        return null;
    }
}

/// <summary>
/// The maps a host can load: SVMap folders found under the search roots (the server reads only <c>sim/</c>), maps
/// built in code (tests), and the built-in TestBox stub when no TestBox folder is installed. A map loads through
/// OpenScct.Content's <see cref="MapLoader"/> with the ported classes registered and every other generated class
/// created from its defaults, gets its physics from <see cref="PhysicsWorld.Attach"/>, and is identified by its
/// manifest sim hash.
/// <para>
/// On a client the load follows UE2's client map setup: before the begin-play passes, every map actor that is
/// bStatic or bNoDelete has its Role and RemoteRole exchanged and every other one is destroyed; the server sends
/// those back if they matter (spec/net/model.md rule 14).
/// </para>
/// </summary>
public sealed partial class MapCatalog
{
    public const string TestBox = "TestBox";

    /// <summary>
    /// The lobby "map" of an online session (docs/ONLINE.md): an empty level that only carries the connections while
    /// players pick sides and the host sets up the match, as the original's Lobby map does.
    /// </summary>
    public const string Lobby = "Lobby";

    private readonly List<string> _roots = new();
    private readonly Dictionary<string, SvMap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<World, bool, MapInfo>> _builders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MapListEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private OpenScct.Game.Movement.IPawnAnimationSource? _animations;

    /// <summary>A catalog searching <paramref name="roots"/> for SVMap folders (map name as folder name, ASCII case ignored).</summary>
    public MapCatalog(params string[] roots)
    {
        foreach (string root in roots)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                _roots.Add(root);
            }
        }
    }

    /// <summary>Hash of the built-in TestBox stub (no files).</summary>
    public static ulong StubContentHash { get; } = NetHash.Fnv1a64("svm-content/m0-stub;TestBox");

    /// <summary>Hash of the built-in lobby level (no files).</summary>
    public static ulong LobbyContentHash { get; } = NetHash.Fnv1a64("svm-content/lobby;1");

    public IReadOnlyList<string> Roots => _roots;

    /// <summary>
    /// A catalog with the default roots: <c>maps</c> next to the executable (a release), then, in a checkout, the
    /// converted maps in <c>.content/svmap</c> and the synthetic ones in <c>fixtures</c>. Share one catalog between
    /// the server and clients of a process to parse each map once.
    /// </summary>
    public static MapCatalog CreateDefault()
    {
        // An installed build: the player's converted maps and the maps shipped with the build (ContentPaths).
        var roots = new List<string>(ContentPaths.MapRoots) { Path.Combine(AppContext.BaseDirectory, "maps") };
        if (FindRepoRoot(AppContext.BaseDirectory) is { } repo)
        {
            roots.Add(Path.Combine(repo, ".content", "svmap"));
            roots.Add(Path.Combine(repo, "fixtures"));
        }

        return new MapCatalog(roots.ToArray());
    }

    /// <summary>The checkout root (the folder holding OpenSCCT.slnx) above <paramref name="start"/>, or null.</summary>
    public static string? FindRepoRoot(string start)
    {
        for (DirectoryInfo? d = new(start); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "OpenSCCT.slnx")))
            {
                return d.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// The converted characters folder (<c>chars</c> next to the maps of a release, or <c>shared/chars</c> beside a
    /// checkout's <c>.content/svmap</c>), or null.
    /// </summary>
    public string? FindCharacterContent()
    {
        foreach (string root in _roots)
        {
            foreach (string candidate in new[] { Path.Combine(root, "..", "shared", "chars"), Path.Combine(root, "..", "chars") })
            {
                if (Directory.Exists(Path.Combine(candidate, "SPerso")))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The pawn animation sets every level of this catalog uses: the converted ones when <see cref="FindCharacterContent"/>
    /// finds them, else the built-in table. Read once per catalog, so a server and its clients share one.
    /// </summary>
    public OpenScct.Game.Movement.IPawnAnimationSource PawnAnimations
    {
        get
        {
            lock (_gate)
            {
                return _animations ??= FindCharacterContent() is { } chars
                    ? OpenScct.Game.Movement.PawnAnimationSource.FromContent(chars)
                    : OpenScct.Game.Movement.PawnAnimationSource.Default;
            }
        }
    }

    /// <summary>
    /// Makes the builder of a map found in a folder that has none registered (map id, folder), or returns null for the
    /// plain load (<see cref="LoadSvMap"/>). Lets a map downloaded after the catalog was made load the same way as the
    /// maps registered at start. The builder it returns is registered, so it is made once per map.
    /// </summary>
    public Func<string, string, Func<World, bool, MapInfo>?>? FolderBuilder { get; set; }

    /// <summary>Adds a map built in code; <c>build(world, isClient)</c> loads it into the world's level.</summary>
    public void Register(string name, Func<World, bool, MapInfo> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        lock (_gate)
        {
            _builders[name] = build;
        }
    }

    /// <summary>The SVMap folder of <paramref name="mapName"/>, or null.</summary>
    public string? FindFolder(string mapName)
    {
        foreach (string root in _roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string dir in Directory.GetDirectories(root))
            {
                if (string.Equals(Path.GetFileName(dir), mapName, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(dir, SvMapHash.ManifestFile)))
                {
                    return dir;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Loads <paramref name="mapName"/> into <paramref name="world"/> (which must not have ticked). A client load
    /// prepares the map actors for replication (roles exchanged, dynamic actors removed). <paramref name="beforeBeginPlay"/>
    /// runs after the map's actors are placed and before the begin-play passes (where the engine spawns the game
    /// info); for a map built in code it runs after that map's own load, still before the first tick.
    /// </summary>
    public bool TryLoad(World world, string mapName, bool isClient, out MapInfo? map, out string? error,
        Action<Level>? beforeBeginPlay = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        // The online lobby is always the built-in empty level, never the converted menu map of the same name.
        if (string.Equals(mapName, Lobby, StringComparison.OrdinalIgnoreCase))
        {
            beforeBeginPlay?.Invoke(world.Level);
            map = new MapInfo(Lobby, Lobby, LobbyContentHash, Lobby, [], [new MapStart(default, default, 0)], []);
            error = null;
            return true;
        }

        Func<World, bool, MapInfo>? builder;
        lock (_gate)
        {
            _builders.TryGetValue(mapName, out builder);
        }

        if (builder is null && FolderBuilder is { } makeBuilder && FindFolder(mapName) is { } found &&
            makeBuilder(mapName, found) is { } made)
        {
            lock (_gate)
            {
                builder = _builders.TryGetValue(mapName, out Func<World, bool, MapInfo>? existing) ? existing : _builders[mapName] = made;
            }
        }

        try
        {
            if (builder is not null)
            {
                map = builder(world, isClient);
                beforeBeginPlay?.Invoke(world.Level);
                error = null;
                return true;
            }

            if (FindFolder(mapName) is { } folder)
            {
                map = LoadSvMap(world, GetSvMap(folder), isClient, beforeBeginPlay: beforeBeginPlay);
                error = null;
                return true;
            }
        }
        catch (SvMapFormatException e)
        {
            map = null;
            error = $"Map '{mapName}' is damaged: {e.File}: {e.Message}";
            return false;
        }

        if (string.Equals(mapName, TestBox, StringComparison.OrdinalIgnoreCase))
        {
            beforeBeginPlay?.Invoke(world.Level);
            map = new MapInfo(TestBox, TestBox, StubContentHash, TestBox, [],
            [
                new MapStart(new FVector(512f, 0f, 0f), default, 0),
                new MapStart(new FVector(0f, 512f, 0f), default, 0),
                new MapStart(new FVector(-512f, 0f, 0f), default, 1),
                new MapStart(new FVector(0f, -512f, 0f), default, 1),
            ], []);
            error = null;
            return true;
        }

        map = null;
        error = $"Unknown map '{mapName}'.";
        return false;
    }

    /// <summary>
    /// Every playable map of the catalog (SVMap folders under the roots, first root first, and the built-in TestBox
    /// stub when no TestBox folder exists), by id, with its title and content hash. Reads only the manifests.
    /// </summary>
    public IReadOnlyList<MapListEntry> ListMaps()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<MapListEntry>();
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
                if (TryReadEntry(dir) is { } entry && !IsMenuMap(entry.Id) && seen.Add(entry.Id))
                {
                    list.Add(entry);
                }
            }
        }

        lock (_gate)
        {
            foreach (string name in _builders.Keys)
            {
                if (seen.Add(name) && !string.Equals(name, Lobby, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new MapListEntry(name, name, 0, null));
                }
            }
        }

        if (seen.Add(TestBox))
        {
            list.Add(new MapListEntry(TestBox, TestBox, StubContentHash, null));
        }

        return list;
    }

    /// <summary>The original's menu maps (the front end and the lobby), which are not played.</summary>
    public static bool IsMenuMap(string mapName) =>
        string.Equals(mapName, Lobby, StringComparison.OrdinalIgnoreCase) || string.Equals(mapName, "Entry", StringComparison.OrdinalIgnoreCase);

    /// <summary>The entry of one map (see <see cref="ListMaps"/>), or null when the catalog cannot load it.</summary>
    public MapListEntry? FindMap(string mapName)
    {
        if (string.Equals(mapName, Lobby, StringComparison.OrdinalIgnoreCase))
        {
            return new MapListEntry(Lobby, Lobby, LobbyContentHash, null);
        }

        if (FindFolder(mapName) is { } folder && TryReadEntry(folder) is { } entry)
        {
            return entry;
        }

        lock (_gate)
        {
            if (_builders.ContainsKey(mapName))
            {
                return new MapListEntry(mapName, mapName, 0, null);
            }
        }

        return string.Equals(mapName, TestBox, StringComparison.OrdinalIgnoreCase)
            ? new MapListEntry(TestBox, TestBox, StubContentHash, null)
            : null;
    }

    private MapListEntry? TryReadEntry(string dir)
    {
        string manifest = Path.Combine(dir, SvMapHash.ManifestFile);
        if (!File.Exists(manifest))
        {
            return null;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(dir, out MapListEntry? cached))
            {
                return cached;
            }
        }

        try
        {
            SvMapManifest m = SvMapManifest.Load(manifest);
            var entry = new MapListEntry(Path.GetFileName(dir), string.IsNullOrEmpty(m.Title) ? m.Map : m.Title, ContentHashOf(m.SimHash), dir);
            lock (_gate)
            {
                _entries[dir] = entry;
            }

            return entry;
        }
        catch (Exception e) when (e is SvMapFormatException or IOException or KeyNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The map's <c>SMapSettings</c> (display name and the per-mode entries the match creation menu starts from), or
    /// null for maps without an SVMap folder.
    /// </summary>
    public USMapSettings? GetMapSettings(string mapName)
    {
        if (FindFolder(mapName) is not { } folder)
        {
            return null;
        }

        SvMap map = GetSvMap(folder);
        var settings = new USMapSettings { MapName = map.Id };
        var modeKeys = new List<string>();
        foreach (KeyValuePair<string, PropBag> mode in map.Gameplay.MapSettings)
        {
            PropBag p = mode.Value;
            modeKeys.Add(mode.Key);
            settings.Modes.Add(new FGameModeInfo
            {
                TimeLimit = p.GetInt("TimeLimit"),
                ScoreLimit = p.GetInt("ScoreLimit"),
                SpiesLives = p.GetInt("SpiesLives"),
                RespawnTime = p.GetInt("RespawnTime"),
                SentriesLives = p.GetInt("SentriesLives"),
                Handicap = p.GetInt("Handicap"),
                NumDisksMax = p.GetInt("NumDisksMax"),
                GameModeId = (EGameMode)(byte)p.GetInt("GameModeId"),
            });
        }

        FillTexts(folder, settings, modeKeys);
        return settings;
    }

    private SvMap GetSvMap(string folder)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(folder, out SvMap? map))
            {
                map = SvMap.Load(folder);
                _cache[folder] = map;
            }

            return map;
        }
    }

    /// <summary>
    /// Loads an SVMap: its actors, client preparation, physics, and the identity the handshake compares. The level
    /// gets the map's mesh animations first, as a play session's loader gives them, so a dedicated server plays its
    /// animated props (AquaE's falling crate and breaking amphoras, SteeE's crane) and fires their notify events.
    /// </summary>
    public static MapInfo LoadSvMap(World world, SvMap map, bool isClient, ActorClassRegistry? classes = null,
        Action<Level>? beforeBeginPlay = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(map);
        Level level = world.Level;
        level.SetService<IMeshAnimations>(new ContentMeshAnimations(map.Gameplay));
        level.BeginLoad();
        MapLoadResult loaded = new MapLoader(classes ?? CreateMapClasses()).Load(world, map);
        if (isClient)
        {
            NetMapBinding.PrepareClientMap(level, loaded.Actors);
        }

        beforeBeginPlay?.Invoke(level);
        level.EndLoad();
        loaded.AttachStartupBases();
        PhysicsWorld physics = PhysicsWorld.Attach(map, loaded);
        var starts = new List<MapStart>();
        foreach (Actor? actor in loaded.Actors)
        {
            if (actor is PlayerStart start)
            {
                starts.Add(new MapStart(start.Location, start.Rotation, start.TeamNumber));
            }
        }

        if (starts.Count == 0)
        {
            starts.Add(new MapStart(default, default, 0));
        }

        return new MapInfo(map.Id, map.Id + "/" + SvMapHash.ManifestFile, ContentHashOf(map.Manifest.SimHash), map.Manifest.SimHash,
            map.SimFiles, starts.ToArray(), loaded.Actors, loaded, physics);
    }

    /// <summary>
    /// The class registry maps load with: every concrete generated actor class of Core, Engine and SBase (so map
    /// actors are typed and replicate), with the ported device classes registered over them.
    /// </summary>
    public static ActorClassRegistry CreateMapClasses()
    {
        ActorClassRegistry registry = ActorClassRegistry.Create(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);
        DeviceClasses.Register(registry);
        return registry;
    }

    /// <summary>The content hash of a sim hash: its first 16 hex digits.</summary>
    public static ulong ContentHashOf(string simHash) =>
        simHash.Length >= 16 && ulong.TryParse(simHash.AsSpan(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong v)
            ? v
            : NetHash.Fnv1a64(simHash);
}
