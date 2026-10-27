using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Maps;

/// <summary>A downloaded map as <c>installed.json</c> keeps it.</summary>
public sealed class InstalledMap
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("commit")]
    public string Commit { get; set; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("codes")]
    public List<string> Codes { get; set; } = [];

    [JsonPropertyName("files")]
    public List<MapFile> Files { get; set; } = [];

    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; set; }

    /// <summary>
    /// Whether the packages the map imports were looked for when it was downloaded (false for maps downloaded before
    /// that existed: the Maps window offers them as an update).
    /// </summary>
    [JsonPropertyName("dependenciesChecked")]
    public bool DependenciesChecked { get; set; }

    /// <summary>Packages the map imports that neither the player's install nor the map repository has.</summary>
    [JsonPropertyName("missingPackages")]
    public List<string> MissingPackages { get; set; } = [];
}

/// <summary>Progress of a map download: bytes received of the bytes to download, and the file being fetched.</summary>
public sealed record MapDownloadProgress(long Received, long Total, string File);

/// <summary>
/// What a map download brought: the packages fetched from other folders of the repository because the install
/// lacks them, and the packages the map needs that were found nowhere (it plays with missing textures, objects or
/// sounds).
/// </summary>
public sealed record MapInstallResult(IReadOnlyList<string> Added, IReadOnlyList<string> Missing)
{
    /// <summary>A sentence for the player about <see cref="Missing"/>, or null when nothing is missing.</summary>
    public string? Warning(string mapName) => MissingWarning(mapName, Missing);

    public static string? MissingWarning(string mapName, IReadOnlyCollection<string> missing) => missing.Count == 0
        ? null
        : $"{mapName} needs {missing.Count} package{(missing.Count == 1 ? string.Empty : "s")} that your game and the map "
            + $"repository do not have ({string.Join(", ", missing)}). It will play with missing textures, objects or "
            + "sounds; installing Enhanced SCCT Versus 4.0 usually provides them.";
}

/// <summary>
/// The maps a player downloaded from the SCCT-Maps repository, under <c>%LOCALAPPDATA%\OpenSCCT\maps</c>:
/// <code>
/// installed.json   the downloaded maps and the files each one brought
/// game\            their files, laid out like an install (Packages\Maps, Packages\Textures, ..., System\_PC_)
/// downloads\       files being downloaded
/// </code>
/// The player's own install is never written. The content builder reads <c>game\</c> next to the install
/// (<c>--addons</c>), and the install's files always win, so a map that ships its own copy of a stock package
/// cannot change the stock maps. Maps share files (community maps share <c>community/_shared</c>): a file is
/// downloaded once and removed with the last map that uses it.
/// </summary>
public sealed class MapLibrary
{
    public const string ManifestName = "installed.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public MapLibrary(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>Where the launcher keeps downloaded maps: %LOCALAPPDATA%\OpenSCCT\maps.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSCCT", "maps");

    public string Root { get; }

    /// <summary>The folder passed to the content builder as --addons.</summary>
    public string GameFolder => Path.Combine(Root, "game");

    public string DownloadsFolder => Path.Combine(Root, "downloads");

    public string ManifestPath => Path.Combine(Root, ManifestName);

    /// <summary>The downloaded maps, by name.</summary>
    public IReadOnlyList<InstalledMap> Installed()
    {
        try
        {
            if (File.Exists(ManifestPath))
            {
                List<InstalledMap> maps = JsonSerializer.Deserialize<List<InstalledMap>>(File.ReadAllText(ManifestPath), Json) ?? [];
                return maps.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Treated as none; the files stay until a map is downloaded or removed again.
        }

        return [];
    }

    public InstalledMap? Find(string id) => Installed().FirstOrDefault(m => m.Id == id);

    /// <summary>
    /// What the content build depends on from here: changes when a map is downloaded, updated or removed (empty
    /// when there are none), so the launcher knows the game files need preparing again.
    /// </summary>
    public string Fingerprint()
    {
        IReadOnlyList<InstalledMap> maps = Installed();
        return maps.Count == 0
            ? string.Empty
            : MapPaths.Fingerprint(maps.Select(m => m.Id + ":" + m.Fingerprint));
    }

    /// <summary>Whether there is anything for the content builder to read.</summary>
    public bool HasMaps => Installed().Count > 0 && Directory.Exists(Path.Combine(GameFolder, "Packages", "Maps"));

    /// <summary>
    /// The map codes of <paramref name="map"/> that the player's install already has (Packages\Maps\&lt;code&gt;.sdc):
    /// the install's copy is the one the game uses, so downloading those adds nothing.
    /// </summary>
    public static IReadOnlyList<string> CodesInInstall(MapPackage map, string? gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return [];
        }

        string folder = Path.Combine(gamePath, "Packages", "Maps");
        return map.Codes.Where(c => File.Exists(Path.Combine(folder, c + ".sdc"))).ToList();
    }

    /// <summary>
    /// Downloads <paramref name="map"/> (or brings an older download up to date): every file it lists that is not
    /// here with the same content is downloaded, checked and put in place, then the map is recorded. Files the map
    /// no longer lists are removed unless another map uses them.
    /// <para>
    /// With the catalogue (<paramref name="index"/>) and the player's install (<paramref name="gamePath"/>), the
    /// packages the map's files import are looked for too, and again for each package that brings: one the install
    /// has is used from there, one another downloaded map brought is shared, and any other is downloaded from the
    /// repository folder that carries it (<see cref="MapIndex.Packages"/>). The result names those, and the ones found
    /// nowhere.
    /// </para>
    /// </summary>
    public async Task<MapInstallResult> InstallAsync(MapPackage map, MapRepository repository, IProgress<MapDownloadProgress>? progress,
        CancellationToken cancel, MapIndex? index = null, string? gamePath = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(repository);
        Directory.CreateDirectory(GameFolder);
        Directory.CreateDirectory(DownloadsFolder);

        var files = new Dictionary<string, MapFile>(StringComparer.OrdinalIgnoreCase);
        foreach (MapFile f in map.Files)
        {
            files.TryAdd(f.Destination, f);
        }

        var transfer = new Transfer(progress);
        await DownloadAsync(map, files.Values.ToList(), repository, transfer, cancel).ConfigureAwait(false);

        var added = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        bool checkDependencies = index is not null && !string.IsNullOrWhiteSpace(gamePath) && Directory.Exists(gamePath);
        if (checkDependencies)
        {
            HashSet<string> inInstall = PackageNames(gamePath!);

            // Files other downloaded maps brought, by package name: shared, so they stay while this map uses them.
            var shared = new Dictionary<string, List<MapFile>>(StringComparer.OrdinalIgnoreCase);
            foreach (MapFile f in Installed().Where(m => m.Id != map.Id).SelectMany(m => m.Files))
            {
                string name = Path.GetFileNameWithoutExtension(f.Destination);
                if (!shared.TryGetValue(name, out List<MapFile>? list))
                {
                    shared[name] = list = [];
                }

                if (!list.Any(x => string.Equals(x.Destination, f.Destination, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(f);
                }
            }

            var have = new HashSet<string>(files.Keys.Select(k => Path.GetFileNameWithoutExtension(k)), StringComparer.OrdinalIgnoreCase);
            var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<MapFile> pending = files.Values.ToList();
            while (pending.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                var fetch = new List<MapFile>();
                foreach (MapFile f in pending.Where(f => scanned.Add(f.Destination)))
                {
                    foreach (string name in ImportsOf(MapPaths.Under(GameFolder, f.Destination)))
                    {
                        if (have.Contains(name) || inInstall.Contains(name) || missing.Contains(name))
                        {
                            continue;
                        }

                        have.Add(name);
                        if (shared.TryGetValue(name, out List<MapFile>? brought))
                        {
                            fetch.AddRange(brought);
                        }
                        else if (index!.Packages.TryGetValue(name, out IReadOnlyList<MapFile>? found))
                        {
                            fetch.AddRange(found);
                            added.Add(name);
                        }
                        else
                        {
                            missing.Add(name);
                        }
                    }
                }

                pending = fetch.Where(f => files.TryAdd(f.Destination, f)).ToList();
                await DownloadAsync(map, pending, repository, transfer, cancel).ConfigureAwait(false);
            }
        }

        // Record the map, then drop files its older version had that nothing uses now.
        List<InstalledMap> maps = Installed().ToList();
        InstalledMap? previous = maps.FirstOrDefault(m => m.Id == map.Id);
        maps.RemoveAll(m => m.Id == map.Id);
        maps.Add(new InstalledMap
        {
            Id = map.Id,
            Name = map.Name,
            Category = map.Category,
            Version = map.Version,
            Commit = map.Commit,
            Fingerprint = map.Fingerprint,
            Codes = map.Codes.ToList(),
            Files = files.Values.OrderBy(f => f.Destination, StringComparer.OrdinalIgnoreCase).ToList(),
            InstalledAt = DateTimeOffset.UtcNow,
            DependenciesChecked = checkDependencies,
            MissingPackages = missing.ToList(),
        });
        Save(maps);
        if (previous is not null)
        {
            DeleteUnused(previous.Files, maps);
        }

        transfer.Finish();
        return new MapInstallResult(added.ToList(), missing.ToList());
    }

    /// <summary>Downloads the files of <paramref name="files"/> that are not here with the same content.</summary>
    private async Task DownloadAsync(MapPackage map, IReadOnlyList<MapFile> files, MapRepository repository, Transfer transfer, CancellationToken cancel)
    {
        var known = KnownHashes();
        foreach ((string destination, string hash) in transfer.Fetched)
        {
            known[destination] = hash;
        }

        var needed = files.Where(f => !(known.TryGetValue(f.Destination, out string? hash)
                && string.Equals(hash, f.Hash, StringComparison.OrdinalIgnoreCase)
                && File.Exists(MapPaths.Under(GameFolder, f.Destination))))
            .ToList();
        transfer.Total += needed.Sum(f => f.Size);
        foreach (MapFile file in needed)
        {
            cancel.ThrowIfCancellationRequested();
            string target = MapPaths.Under(GameFolder, file.Destination);
            string temp = Path.Combine(DownloadsFolder, file.Hash.ToLowerInvariant() + ".part");
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            long before = transfer.Done;
            var bytes = new Counter(n =>
            {
                transfer.Done += n;
                transfer.Report(file.Destination);
            });
            transfer.Report(file.Destination);
            await repository.DownloadAsync(map, file, temp, bytes, cancel).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(temp, target, overwrite: true);
            transfer.Done = before + file.Size;
            transfer.Fetched[file.Destination] = file.Hash;
        }
    }

    /// <summary>
    /// The packages a file imports; none for files that are not packages (sound banks, text) or cannot be read (they
    /// are then used as they are).
    /// </summary>
    private static IReadOnlyList<string> ImportsOf(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".sdc" or ".utx" or ".utc" or ".usx" or ".uax" or ".ukx"))
        {
            return [];
        }

        try
        {
            return File.Exists(path) ? PackageImports.RootPackages(path) : [];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The names of the packages an install has: every file under Packages (any folder) and the script packages of
    /// System (<c>System\_PC_\*.u</c>), without their extensions.
    /// </summary>
    public static HashSet<string> PackageNames(string gamePath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string packages = Path.Combine(gamePath, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (string f in Directory.EnumerateFiles(packages, "*", SearchOption.AllDirectories))
            {
                names.Add(Path.GetFileNameWithoutExtension(f));
            }
        }

        string system = Path.Combine(gamePath, "System");
        if (Directory.Exists(system))
        {
            foreach (string f in Directory.EnumerateFiles(system, "*.u", SearchOption.AllDirectories))
            {
                names.Add(Path.GetFileNameWithoutExtension(f));
            }
        }

        return names;
    }

    /// <summary>Bytes received of the bytes to download over every batch of one map's download, and the files fetched.</summary>
    private sealed class Transfer(IProgress<MapDownloadProgress>? progress)
    {
        public long Total { get; set; }

        public long Done { get; set; }

        public Dictionary<string, string> Fetched { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Report(string file) => progress?.Report(new MapDownloadProgress(Done, Total, file));

        public void Finish() => progress?.Report(new MapDownloadProgress(Total, Total, string.Empty));
    }

    /// <summary>Removes a downloaded map and the files no other downloaded map uses.</summary>
    public void Remove(string id)
    {
        List<InstalledMap> maps = Installed().ToList();
        InstalledMap? map = maps.FirstOrDefault(m => m.Id == id);
        if (map is null)
        {
            return;
        }

        maps.Remove(map);
        Save(maps);
        DeleteUnused(map.Files, maps);
    }

    /// <summary>The hash each file in game\ has, as the maps that brought it recorded it (the newest record wins).</summary>
    private Dictionary<string, string> KnownHashes()
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (InstalledMap m in Installed().OrderBy(m => m.InstalledAt))
        {
            foreach (MapFile f in m.Files)
            {
                hashes[f.Destination] = f.Hash;
            }
        }

        return hashes;
    }

    private void DeleteUnused(IEnumerable<MapFile> files, IReadOnlyList<InstalledMap> remaining)
    {
        var used = remaining.SelectMany(m => m.Files).Select(f => f.Destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MapFile f in files.Where(f => !used.Contains(f.Destination)))
        {
            string path = MapPaths.Under(GameFolder, f.Destination);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Reports on the downloading thread, in order (Progress&lt;T&gt; would post each report elsewhere).</summary>
    private sealed class Counter(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    private void Save(List<InstalledMap> maps)
    {
        Directory.CreateDirectory(Root);
        MapPaths.WriteAllTextAtomic(ManifestPath, JsonSerializer.Serialize(maps.OrderBy(m => m.Id, StringComparer.Ordinal).ToList(), Json));
    }
}
