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
}

/// <summary>Progress of a map download: bytes received of the bytes to download, and the file being fetched.</summary>
public sealed record MapDownloadProgress(long Received, long Total, string File);

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
    /// </summary>
    public async Task InstallAsync(MapPackage map, MapRepository repository, IProgress<MapDownloadProgress>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(repository);
        Directory.CreateDirectory(GameFolder);
        Directory.CreateDirectory(DownloadsFolder);

        var known = KnownHashes();
        var needed = map.Files.Where(f => !(known.TryGetValue(f.Destination, out string? hash)
                && string.Equals(hash, f.Hash, StringComparison.OrdinalIgnoreCase)
                && File.Exists(MapPaths.Under(GameFolder, f.Destination))))
            .ToList();
        long total = needed.Sum(f => f.Size);
        long done = 0;
        foreach (MapFile file in needed)
        {
            cancel.ThrowIfCancellationRequested();
            string target = MapPaths.Under(GameFolder, file.Destination);
            string temp = Path.Combine(DownloadsFolder, file.Hash.ToLowerInvariant() + ".part");
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            long before = done;
            var bytes = new Counter(n =>
            {
                done += n;
                progress?.Report(new MapDownloadProgress(done, total, file.Destination));
            });
            progress?.Report(new MapDownloadProgress(done, total, file.Destination));
            await repository.DownloadAsync(map, file, temp, bytes, cancel).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(temp, target, overwrite: true);
            done = before + file.Size;
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
            Files = map.Files.ToList(),
            InstalledAt = DateTimeOffset.UtcNow,
        });
        Save(maps);
        if (previous is not null)
        {
            DeleteUnused(previous.Files, maps);
        }

        progress?.Report(new MapDownloadProgress(total, total, string.Empty));
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
