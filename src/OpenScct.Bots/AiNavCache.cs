using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Maps;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>
/// The navigation graphs of the server bots (<see cref="NavGraph"/>), built once per map content hash and side and
/// shared by every bot of a process, with a copy on disk so the next run loads it in a moment. The cache folder is
/// outside the checkout (<c>%LOCALAPPDATA%/SVM/ai-nav</c>, or <c>SVM_AI_NAV_CACHE</c>); nothing derived from a map
/// is written to tracked paths.
/// </summary>
public sealed class AiNavCache
{
    private static readonly FVector DefaultMargin = new(1200f, 1200f, 700f);
    private const int MaxPackedGraphBytes = 128 * 1024 * 1024;
    private readonly Dictionary<string, NavGraph> _spyGraphs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NavGraph> _mercGraphs = new(StringComparer.Ordinal);
    private int _built;
    private int _loaded;

    /// <summary>A cache over <paramref name="directory"/>; null for the default folder, empty for memory only.</summary>
    public AiNavCache(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory();
    }

    /// <summary>The folder of the cache files, or empty when the cache lives in memory only.</summary>
    public string Directory { get; }

    /// <summary>Widening of the sampled box around the map's starts, objectives and GEs (uu): X, Y, below and above.</summary>
    public FVector Margin { get; init; } = DefaultMargin;

    /// <summary>Graphs built (not loaded) by this cache (diagnostics).</summary>
    public int Built => Volatile.Read(ref _built);

    /// <summary>Graphs loaded from disk by this cache.</summary>
    public int Loaded => Volatile.Read(ref _loaded);

    public static string DefaultDirectory()
    {
        string? env = Environment.GetEnvironmentVariable("SVM_AI_NAV_CACHE");
        if (env is not null)
        {
            return env;
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local) ? string.Empty : Path.Combine(local, "SVM", "ai-nav");
    }

    /// <summary>The graph of <paramref name="map"/> for a spy (traversal links) or a merc.</summary>
    public NavGraph Get(MapInfo map, bool spy, byte activationMask) => GetGraph(map, spy, activationMask).Graph;

    private (NavGraph Graph, bool Built) GetGraph(MapInfo map, bool spy, byte activationMask, Func<MapInfo>? reload = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Physics is not { } physics)
        {
            throw new InvalidOperationException($"Map {map.Name} has no collision to plan on.");
        }

        NavProfile profile = spy ? NavProfile.Spy : NavProfile.Merc;
        string mapKey = MapKey(map, activationMask);
        string key = mapKey + "|" + profile.Key;
        Dictionary<string, NavGraph> graphs = spy ? _spyGraphs : _mercGraphs;
        lock (graphs)
        {
            NavGraph? graph = FindStoredLocked(graphs, mapKey, spy, map.SimHash, map.SourceFolder, out string? file);
            if (graph is not null)
            {
                return (graph, false);
            }

            if (reload is not null)
            {
                MapInfo reloaded = reload();
                if (reloaded.Name != map.Name || reloaded.SimHash != map.SimHash || reloaded.Physics is null)
                {
                    throw new InvalidOperationException($"Map {map.Name} changed while preparing bots.");
                }

                map = reloaded;
                physics = reloaded.Physics;
            }

            var ges = new List<GERecord>(physics.GEs.Count);
            for (int i = 0; i < physics.GEs.Count; i++)
            {
                ges.Add(physics.GEs[i]);
            }

            (FVector min, FVector max) = Bounds(map, ges);
            // Collision queries reuse scratch buffers. Public Get callers may share a map between sides.
            lock (physics.Collision)
            {
                graph = NavBuilder.Build(physics.Collision, min, max, profile, ges, activationMask);
            }
            Interlocked.Increment(ref _built);
            if (file is not null)
            {
                TryWrite(file, graph, mapKey);
            }

            graphs[key] = graph;
            return (graph, true);
        }
    }

    /// <summary>
    /// The graph already in memory, in the cache folder, in a downloaded pack or in the map's own pack, or null.
    /// A graph read from disk is kept in memory. The caller holds the lock of <paramref name="graphs"/>.
    /// <paramref name="file"/> is the cache file a built graph is written to (null for a memory-only cache).
    /// </summary>
    private NavGraph? FindStoredLocked(Dictionary<string, NavGraph> graphs, string mapKey, bool spy, string simHash,
        string? folder, out string? file)
    {
        NavProfile profile = spy ? NavProfile.Spy : NavProfile.Merc;
        string key = mapKey + "|" + profile.Key;
        file = Directory.Length == 0 ? null : Path.Combine(Directory, SafeName(mapKey + "-" + profile.Key) + ".svmnav");
        if (graphs.TryGetValue(key, out NavGraph? graph))
        {
            return graph;
        }

        if (file is not null && File.Exists(file))
        {
            try
            {
                using FileStream s = File.OpenRead(file);
                graph = NavGraph.Read(s, mapKey, profile);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                graph = null;
            }
        }

        if (graph is null && Margin == DefaultMargin)
        {
            string? downloaded = DownloadPath(simHash);
            if (downloaded is not null) graph = ReadPackedGraph(downloaded, spy, mapKey, profile);
            if (graph is null && folder is not null) graph = ReadPackedGraph(PackPath(folder), spy, mapKey, profile);
        }

        if (graph is not null)
        {
            Interlocked.Increment(ref _loaded);
            graphs[key] = graph;
        }

        return graph;
    }

    /// <summary>Looks up a stored graph without a loaded map (see <see cref="FindStoredLocked"/>).</summary>
    private NavGraph? FindStored(string mapKey, bool spy, string simHash, string? folder)
    {
        Dictionary<string, NavGraph> graphs = spy ? _spyGraphs : _mercGraphs;
        lock (graphs)
        {
            return FindStoredLocked(graphs, mapKey, spy, simHash, folder, out _);
        }
    }

    private static string MapKey(MapInfo map, byte activationMask) => MapKey(map.Name, map.SimHash, activationMask);

    private static string MapKey(string map, string simHash, byte activationMask) =>
        string.Create(CultureInfo.InvariantCulture, $"{map}-{simHash}-m{activationMask}");

    private static string PackPath(string folder) => Path.Combine(folder, "catalog", "bot-nav.zip");

    private static string EntryName(bool spy) => spy ? "spy.svmnav" : "merc.svmnav";

    private static string? DownloadPath(string simHash) =>
        ContentStore.BotNavigationPath(ContentPaths.PacksFolder, simHash, NavGraph.FormatVersion);

    /// <summary>Checks both graph headers without loading the graph data.</summary>
    public static bool PackMatches(string path, string map, string simHash, byte mask)
    {
        if (!File.Exists(path)) return false;

        string key = MapKey(map, simHash, mask);
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return Matches(true, NavProfile.Spy) && Matches(false, NavProfile.Merc);

            bool Matches(bool spy, NavProfile profile)
            {
                ZipArchiveEntry? entry = archive.GetEntry(EntryName(spy));
                if (entry is null || entry.Length is <= 0 or > MaxPackedGraphBytes) return false;
                using Stream stream = entry.Open();
                return NavGraph.MatchesHeader(stream, key, profile);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or FormatException)
        {
            return false;
        }
    }

    public static bool HasPreparedGraphs(string folder, string map, string simHash, byte mask)
    {
        string key = MapKey(map, simHash, mask);
        string directory = DefaultDirectory();
        return directory.Length > 0 && Cached(NavProfile.Spy) && Cached(NavProfile.Merc) ||
            DownloadPath(simHash) is { } download && PackMatches(download, map, simHash, mask) ||
            PackMatches(PackPath(folder), map, simHash, mask);

        bool Cached(NavProfile profile)
        {
            string path = Path.Combine(directory, SafeName(key + "-" + profile.Key) + ".svmnav");
            try
            {
                if (!File.Exists(path)) return false;
                using FileStream stream = File.OpenRead(path);
                return NavGraph.MatchesHeader(stream, key, profile);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
            {
                return false;
            }
        }
    }

    private static NavGraph? ReadPackedGraph(string path, bool spy, string mapKey, NavProfile profile)
    {
        if (!File.Exists(path)) return null;

        try
        {
            using var archive = ZipFile.OpenRead(path);
            ZipArchiveEntry? entry = archive.GetEntry(EntryName(spy));
            if (entry is null || entry.Length is <= 0 or > MaxPackedGraphBytes) return null;

            var bytes = new byte[(int)entry.Length];
            using Stream data = entry.Open();
            data.ReadExactly(bytes);
            if (data.ReadByte() != -1) return null;
            using var graph = new MemoryStream(bytes, writable: false);
            return NavGraph.Read(graph, mapKey, profile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private static void TryWrite(string file, NavGraph graph, string mapKey)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = file + ".tmp";
            using (FileStream s = File.Create(temp))
            {
                graph.Write(s, mapKey);
            }

            File.Move(temp, file, overwrite: true);
        }
        catch (IOException)
        {
            // A cache that cannot be written is rebuilt next time.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string SafeName(string name)
    {
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0 || chars[i] == '|')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Builds (or loads) both sides' graphs of <paramref name="mapName"/> now, so a real-time host does not stall on its
    /// first tick (SteeE takes tens of seconds to build in a Debug build). Stored graphs are looked up from the map's
    /// manifest first; only when one is missing is the map loaded into a scratch server world to build it.
    /// </summary>
    /// <param name="content">
    /// The map's sim files when the caller has them already (the map the catalog loads for <paramref name="mapName"/>),
    /// so the lookup does not read them again.
    /// </param>
    public void Prepare(MapCatalog maps, string mapName, Action<string>? progress = null, byte? activationMask = null,
        SvMap? content = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        progress?.Invoke($"nav prepare map={mapName}");
        if (TryPrepareStored(maps, mapName, activationMask, content, progress))
        {
            return;
        }

        MapInfo map = LoadMap(maps, mapName, out byte defaultMask);
        PrepareGraphs(maps, mapName, map, activationMask ?? defaultMask, progress);
    }

    /// <summary>
    /// Loads both stored graphs without a world: the key is the manifest's map id and sim hash, and the mask the caller
    /// gave or the LevelInfo's GEActivationMask from gameplay.json (what the load puts in the level). A wrong guess
    /// cannot load a wrong graph: graphs are only stored under the key of the map they were built from, so it misses
    /// and the scratch-world path runs as before.
    /// </summary>
    private bool TryPrepareStored(MapCatalog maps, string mapName, byte? activationMask, SvMap? content, Action<string>? progress)
    {
        // The lobby is always the built-in empty level, never a map folder of that name.
        if (string.Equals(mapName, MapCatalog.Lobby, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string name;
        string simHash;
        string folder;
        byte mask;
        if (content is not null)
        {
            name = content.Id;
            simHash = content.Manifest.SimHash;
            folder = content.Root;
            mask = activationMask ?? content.Gameplay.GEActivationMask;
        }
        else
        {
            if (maps.FindFolder(mapName) is not { } found)
            {
                return false;
            }

            try
            {
                folder = Path.GetFullPath(found);
                SvMapManifest manifest = SvMapManifest.Load(Path.Combine(folder, SvMapHash.ManifestFile));
                name = manifest.Map;
                simHash = manifest.SimHash;
                mask = activationMask ?? GameplayDoc.Load(Path.Combine(folder, GameplayDoc.RelativePath)).GEActivationMask;
            }
            catch (Exception error) when (error is SvMapFormatException or IOException or UnauthorizedAccessException or KeyNotFoundException)
            {
                // The scratch load reports a damaged map with its usual error.
                return false;
            }
        }

        string mapKey = MapKey(name, simHash, mask);
        NavGraph? spy = null;
        NavGraph? merc = null;
        if (Environment.ProcessorCount > 1)
        {
            try
            {
                Parallel.Invoke(
                    () => spy = FindStored(mapKey, spy: true, simHash, folder),
                    () => merc = FindStored(mapKey, spy: false, simHash, folder));
            }
            catch (AggregateException error)
            {
                ExceptionDispatchInfo.Throw(error.InnerExceptions[0]);
            }
        }
        else
        {
            spy = FindStored(mapKey, spy: true, simHash, folder);
            merc = FindStored(mapKey, spy: false, simHash, folder);
        }

        if (spy is null || merc is null)
        {
            // A graph found here stays in memory, so the scratch path below reports it as cached.
            return false;
        }

        progress?.Invoke("nav spy start");
        progress?.Invoke("nav merc start");
        ReportGraph(progress, "spy", spy, built: false);
        ReportGraph(progress, "merc", merc, built: false);
        progress?.Invoke("nav ready");
        return true;
    }

    private static void ReportGraph(Action<string>? progress, string side, NavGraph graph, bool built) =>
        progress?.Invoke(string.Create(CultureInfo.InvariantCulture,
            $"nav {side} {(built ? "built" : "cached")} nodes={graph.NodeCount} edges={graph.EdgeCount}"));

    /// <summary>Writes the default Spy and Merc graphs as optional map-pack data outside the simulation hash.</summary>
    public static string BakeMapPack(string folder, Action<string>? progress = null, string? cacheDirectory = null)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string mapName = Path.GetFileName(fullPath);
        var maps = new MapCatalog(Path.GetDirectoryName(fullPath) ?? fullPath);
        MapInfo map = LoadMap(maps, mapName, out byte mask);
        if (!string.Equals(map.SourceFolder, fullPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{fullPath} is not an SVMap folder.");
        }

        var cache = new AiNavCache(cacheDirectory);
        (NavGraph spy, NavGraph merc) = cache.PrepareGraphs(maps, mapName, map, mask, progress);
        string path = PackPath(fullPath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                string mapKey = MapKey(map, mask);
                WritePackedGraph(archive, spy, spy: true, mapKey);
                WritePackedGraph(archive, merc, spy: false, mapKey);
            }

            File.Move(temp, path, overwrite: true);
            return path;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static void WritePackedGraph(ZipArchive archive, NavGraph graph, bool spy, string mapKey)
    {
        ZipArchiveEntry entry = archive.CreateEntry(EntryName(spy), CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream stream = entry.Open();
        graph.Write(stream, mapKey);
    }

    private (NavGraph Spy, NavGraph Merc) PrepareGraphs(MapCatalog maps, string mapName, MapInfo map, byte mask,
        Action<string>? progress)
    {
        progress?.Invoke("nav spy start");
        progress?.Invoke("nav merc start");

        (NavGraph Graph, bool Built) spy = default;
        (NavGraph Graph, bool Built) merc = default;
        if (Environment.ProcessorCount > 1)
        {
            // The second collision world is only needed for a cold build. Cache hits reuse the first map's key.
            try
            {
                Parallel.Invoke(
                    () => spy = GetGraph(map, spy: true, mask),
                    () => merc = GetGraph(map, spy: false, mask, () => LoadMap(maps, mapName, out _)));
            }
            catch (AggregateException error)
            {
                // Keep the load error type that the callers handle, and wait for both builders before returning.
                ExceptionDispatchInfo.Throw(error.InnerExceptions[0]);
            }
        }
        else
        {
            spy = GetGraph(map, spy: true, mask);
            merc = GetGraph(map, spy: false, mask);
        }

        ReportGraph(progress, "spy", spy.Graph!, spy.Built);
        ReportGraph(progress, "merc", merc.Graph!, merc.Built);
        progress?.Invoke("nav ready");
        return (spy.Graph!, merc.Graph!);
    }

    private static MapInfo LoadMap(MapCatalog maps, string mapName, out byte activationMask)
    {
        var world = new World(0, ENetMode.NM_DedicatedServer);
        if (!maps.TryLoad(world, mapName, false, out MapInfo? map, out string? error) || map is null)
        {
            throw new InvalidOperationException($"Map {mapName} did not load: {error}");
        }

        activationMask = world.Level.Info.GEActivationMask;
        return map;
    }

    /// <summary>The box the graph samples: the map's starts, objective actors and GE points, widened by <see cref="Margin"/>.</summary>
    public (FVector Min, FVector Max) Bounds(MapInfo map, IReadOnlyList<GERecord> ges)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ges);
        var points = new List<FVector>();
        foreach (Actor? a in map.MapActors)
        {
            if (a is PlayerStart or SObjectiveTrigger or SFlagDropZone or SInterrupteur)
            {
                points.Add(a.Location);
            }
        }

        foreach (MapStart s in map.Starts)
        {
            points.Add(s.Location);
        }

        foreach (GERecord ge in ges)
        {
            points.Add(ge.P0);
            points.Add(ge.P1);
        }

        if (points.Count == 0)
        {
            points.Add(default);
        }

        return NavBuilder.BoundsOf(points, Margin);
    }
}
