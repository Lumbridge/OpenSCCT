using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Maps;

/// <summary>Which archive of a map: everything the game needs, or what a server needs (manifest and sim).</summary>
public enum ContentKind
{
    Full = 0,
    Sim = 1,
}

/// <summary>One pack installed from the content index (<c>installed.json</c>).</summary>
public sealed record InstalledContent
{
    /// <summary>The map's id (<c>stock/AquaE</c>) or <c>shared/&lt;id&gt;</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The map's code, or the shared pack's id.</summary>
    public required string Code { get; init; }

    public string Version { get; init; } = string.Empty;

    /// <summary>SHA-256 of the archive it came from.</summary>
    public string Sha256 { get; init; } = string.Empty;

    public ContentKind Kind { get; init; }

    public string ContentHash { get; init; } = string.Empty;

    public DateTimeOffset InstalledAt { get; init; }

    public bool IsShared => Id.StartsWith("shared/", StringComparison.Ordinal);
}

/// <summary>How far an install is: bytes received of the archive, then the extracting step.</summary>
public readonly record struct PackProgress(long Received, long Total, bool Extracting);

/// <summary>
/// Packs downloaded from OpenSCCT-Content (docs/CONTENT-PACKS.md) into one folder laid out like converted content:
/// <c>svmap/&lt;code&gt;/</c>, <c>shared/</c>, <c>installed.json</c>, <c>downloads/</c> and <c>cache/</c>. Every archive
/// is checked against the index's size and SHA-256, every extracted file against the map's manifest, and a map
/// replaces its old folder only once it is complete. Thread-safe; one install per code at a time.
/// </summary>
public sealed class ContentStore
{
    public const string Owner = "Lumbridge";
    public const string Repo = "OpenSCCT-Content";
    public const string IndexUrl = $"https://raw.githubusercontent.com/{Owner}/{Repo}/main/index.json";
    public const string WebUrl = $"https://github.com/{Owner}/{Repo}";

    /// <summary>Largest index accepted.</summary>
    public const int MaxIndexBytes = 16 * 1024 * 1024;

    private const string InstalledFile = "installed.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, SemaphoreSlim> _busy = new(StringComparer.OrdinalIgnoreCase);

    public ContentStore(string root, HttpClient http)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        Root = Path.GetFullPath(root);
    }

    /// <summary>The game's packs folder: <c>%LOCALAPPDATA%\OpenSCCT\packs</c>.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSCCT", "packs");

    public string Root { get; }

    /// <summary>The installed maps (a map root for MapCatalog).</summary>
    public string SvMapRoot => Path.Combine(Root, "svmap");

    public string SharedRoot => Path.Combine(Root, "shared");

    /// <summary>The index URL (tests and servers may point elsewhere).</summary>
    public string Source { get; init; } = IndexUrl;

    /// <summary>The installed packs.</summary>
    public IReadOnlyList<InstalledContent> Installed()
    {
        lock (_gate)
        {
            return ReadInstalled();
        }
    }

    /// <summary>The installed entry of a map code, or null.</summary>
    public InstalledContent? InstalledMap(string code)
    {
        foreach (InstalledContent c in Installed())
        {
            if (!c.IsShared && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(SvMapRoot, c.Code, "manifest.json")))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>The map is installed as the index has it now (and at least as <paramref name="kind"/>).</summary>
    public bool Has(ContentMap map, ContentKind kind = ContentKind.Full)
    {
        ArgumentNullException.ThrowIfNull(map);
        return InstalledMap(map.Code) is { } c && (c.Kind == ContentKind.Full || kind == ContentKind.Sim) &&
            (string.Equals(c.Sha256, map.Full.Sha256, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(c.Sha256, map.Sim.Sha256, StringComparison.OrdinalIgnoreCase) ||
             (c.ContentHash.Length > 0 && string.Equals(c.ContentHash, map.ContentHash, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The index: fetched (sending the cached ETag), else the cached copy when offline. Throws
    /// <see cref="IOException"/> with a reason a player can read when there is neither.
    /// </summary>
    public async Task<ContentIndex> FetchIndexAsync(CancellationToken cancel)
    {
        string cacheDir = Path.Combine(Root, "cache");
        string cacheFile = Path.Combine(cacheDir, "index.json");
        string etagFile = Path.Combine(cacheDir, "index.etag");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Source);
            request.Headers.UserAgent.ParseAdd("OpenSCCT");
            if (File.Exists(cacheFile) && File.Exists(etagFile) && File.ReadAllText(etagFile).Trim() is { Length: > 0 } etag)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified && File.Exists(cacheFile))
            {
                return ContentIndex.Parse(await File.ReadAllTextAsync(cacheFile, cancel).ConfigureAwait(false));
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxIndexBytes)
            {
                throw new IOException("The content index is too large.");
            }

            string text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            ContentIndex index = ContentIndex.Parse(text);
            Directory.CreateDirectory(cacheDir);
            await File.WriteAllTextAsync(cacheFile, text, cancel).ConfigureAwait(false);
            if (response.Headers.ETag is { } tag)
            {
                await File.WriteAllTextAsync(etagFile, tag.ToString(), cancel).ConfigureAwait(false);
            }

            return index;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException && !cancel.IsCancellationRequested)
        {
            if (File.Exists(cacheFile))
            {
                return ContentIndex.Parse(await File.ReadAllTextAsync(cacheFile, cancel).ConfigureAwait(false));
            }

            throw e is OperationCanceledException
                ? new IOException("The map list did not load in time. Check your internet connection and try again.", e)
                : new IOException("The map list could not be loaded: " + e.Message, e);
        }
    }

    /// <summary>
    /// Downloads and installs a map (replacing an older copy of the same code once the new one is complete). A
    /// <see cref="ContentKind.Sim"/> install is for servers: manifest.json and sim/ only.
    /// </summary>
    public async Task<InstalledContent> InstallMapAsync(ContentMap map, ContentKind kind, IProgress<PackProgress>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!ContentIndex.IsSafeCode(map.Code))
        {
            throw new InvalidDataException($"The map code {map.Code} is not a valid folder name.");
        }

        SemaphoreSlim busy = Busy(map.Code);
        await busy.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            if (Has(map, kind) && InstalledMap(map.Code) is { } already)
            {
                return already;
            }

            ContentDownload download = kind == ContentKind.Sim ? map.Sim : map.Full;
            bool full = kind == ContentKind.Full || ReferenceEquals(map.Sim, map.Full);
            string archive = await DownloadAsync(download, progress, cancel).ConfigureAwait(false);
            string incoming = Staging("map-" + map.Code);
            try
            {
                progress?.Report(new PackProgress(download.Size, download.Size, true));
                Extract(archive, incoming);
                VerifyMap(incoming, requireAll: full);
                Replace(incoming, Path.Combine(SvMapRoot, map.Code), Staging("old-" + map.Code));
            }
            finally
            {
                TryDeleteDirectory(incoming);
                TryDelete(archive);
            }

            var entry = new InstalledContent
            {
                Id = map.Id, Code = map.Code, Version = map.Version, Sha256 = download.Sha256,
                Kind = full ? ContentKind.Full : ContentKind.Sim, ContentHash = map.ContentHash, InstalledAt = DateTimeOffset.UtcNow,
            };
            Record(entry);
            return entry;
        }
        finally
        {
            busy.Release();
        }
    }

    /// <summary>Downloads and installs a shared pack: each of its contents replaces the same name under shared/.</summary>
    public async Task<InstalledContent> InstallSharedAsync(ContentSharedPack pack, IProgress<PackProgress>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(pack);
        if (!ContentIndex.IsSafeCode(pack.Id))
        {
            throw new InvalidDataException($"The shared pack id {pack.Id} is not valid.");
        }

        SemaphoreSlim busy = Busy("shared/" + pack.Id);
        await busy.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            string archive = await DownloadAsync(pack.Download, progress, cancel).ConfigureAwait(false);
            string incoming = Staging("shared-" + pack.Id);
            try
            {
                progress?.Report(new PackProgress(pack.Download.Size, pack.Download.Size, true));
                Extract(archive, incoming);
                Directory.CreateDirectory(SharedRoot);
                foreach (string name in pack.Contents.Count > 0 ? pack.Contents : Directory.EnumerateFileSystemEntries(incoming).Select(Path.GetFileName).OfType<string>())
                {
                    MapPaths.ValidateRelative(name);
                    string from = Path.Combine(incoming, name);
                    string to = Path.Combine(SharedRoot, name);
                    if (Directory.Exists(from))
                    {
                        Replace(from, to, Staging("old-shared-" + name));
                    }
                    else if (File.Exists(from))
                    {
                        File.Move(from, to, overwrite: true);
                    }
                }
            }
            finally
            {
                TryDeleteDirectory(incoming);
                TryDelete(archive);
            }

            var entry = new InstalledContent
            {
                Id = "shared/" + pack.Id, Code = pack.Id, Version = pack.Version, Sha256 = pack.Download.Sha256,
                Kind = ContentKind.Full, InstalledAt = DateTimeOffset.UtcNow,
            };
            Record(entry);
            return entry;
        }
        finally
        {
            busy.Release();
        }
    }

    /// <summary>The shared pack is installed as the index has it now.</summary>
    public bool HasShared(ContentSharedPack pack) =>
        Installed().Any(c => c.IsShared && string.Equals(c.Code, pack.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Sha256, pack.Download.Sha256, StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes an installed map (its folder and its entry).</summary>
    public void RemoveMap(string code)
    {
        if (!ContentIndex.IsSafeCode(code))
        {
            return;
        }

        TryDeleteDirectory(Path.Combine(SvMapRoot, code));
        lock (_gate)
        {
            List<InstalledContent> list = ReadInstalled();
            list.RemoveAll(c => !c.IsShared && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
            WriteInstalled(list);
        }
    }

    private SemaphoreSlim Busy(string key)
    {
        lock (_gate)
        {
            if (!_busy.TryGetValue(key, out SemaphoreSlim? s))
            {
                s = new SemaphoreSlim(1, 1);
                _busy[key] = s;
            }

            return s;
        }
    }

    /// <summary>Downloads an archive to downloads/, checking its size as it comes and its SHA-256 at the end.</summary>
    private async Task<string> DownloadAsync(ContentDownload download, IProgress<PackProgress>? progress, CancellationToken cancel)
    {
        string dir = Path.Combine(Root, "downloads");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, download.Sha256 + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, download.Url);
            request.Headers.UserAgent.ParseAdd("OpenSCCT");
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
            {
                byte[] buffer = new byte[256 * 1024];
                long length = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) != 0)
                {
                    length += read;
                    if (length > download.Size)
                    {
                        throw new InvalidDataException("The download is larger than the map list says.");
                    }

                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    progress?.Report(new PackProgress(length, download.Size, false));
                }

                if (length != download.Size)
                {
                    throw new InvalidDataException("The download did not complete.");
                }
            }

            if (!string.Equals(Convert.ToHexStringLower(sha.GetHashAndReset()), download.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The download does not match the map list (damaged download).");
            }

            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>Extracts a pack, refusing any entry that would land outside <paramref name="dir"/>.</summary>
    internal static void Extract(string archive, string dir)
    {
        Directory.CreateDirectory(dir);
        string rootFull = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        using ZipArchive zip = ZipFile.OpenRead(archive);
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            string name = e.FullName.Replace('\\', '/');
            if (name.EndsWith('/'))
            {
                continue;
            }

            MapPaths.ValidateRelative(name);
            string target = Path.GetFullPath(Path.Combine(dir, name));
            if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The pack has a file outside its folder: {name}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: false);
        }
    }

    /// <summary>
    /// Checks the extracted map against its manifest: every file it lists that is present has its size and SHA-256;
    /// with <paramref name="requireAll"/> every listed file must be present.
    /// </summary>
    internal static void VerifyMap(string dir, bool requireAll)
    {
        string manifest = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifest))
        {
            throw new InvalidDataException("The map pack has no manifest.json.");
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
        if (!doc.RootElement.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The map's manifest lists no files.");
        }

        foreach (JsonProperty f in files.EnumerateObject())
        {
            MapPaths.ValidateRelative(f.Name);
            string path = Path.Combine(dir, f.Name);
            if (!File.Exists(path))
            {
                if (requireAll)
                {
                    throw new InvalidDataException($"The map pack is missing {f.Name}.");
                }

                continue;
            }

            long size = f.Value.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long n) ? n : -1;
            string want = f.Value.TryGetProperty("sha256", out JsonElement h) ? h.GetString() ?? string.Empty : string.Empty;
            if ((size >= 0 && new FileInfo(path).Length != size) || (want.Length > 0 && !string.Equals(Sha256Of(path), want, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"{f.Name} in the map pack does not match its manifest.");
            }
        }
    }

    private static string Sha256Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// A new folder name under <c>.staging/</c> (outside svmap/, so a half-made map is never listed as a map). The
    /// folder itself is not made.
    /// </summary>
    private string Staging(string what)
    {
        string dir = Path.Combine(Root, ".staging");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, what + "-" + Guid.NewGuid().ToString("N")[..8]);
    }

    /// <summary>Puts <paramref name="from"/> at <paramref name="to"/>, moving any old folder to <paramref name="aside"/> first and deleting it after.</summary>
    private static void Replace(string from, string to, string aside)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        string? old = null;
        if (Directory.Exists(to))
        {
            old = aside;
            Directory.Move(to, old);
        }

        try
        {
            Directory.Move(from, to);
        }
        catch
        {
            if (old is not null && !Directory.Exists(to))
            {
                Directory.Move(old, to);
                old = null;
            }

            throw;
        }

        if (old is not null)
        {
            TryDeleteDirectory(old);
        }
    }

    private void Record(InstalledContent entry)
    {
        lock (_gate)
        {
            List<InstalledContent> list = ReadInstalled();
            list.RemoveAll(c => string.Equals(c.Id, entry.Id, StringComparison.OrdinalIgnoreCase) ||
                (!c.IsShared && !entry.IsShared && string.Equals(c.Code, entry.Code, StringComparison.OrdinalIgnoreCase)));
            list.Add(entry);
            WriteInstalled(list);
        }
    }

    private List<InstalledContent> ReadInstalled()
    {
        string file = Path.Combine(Root, InstalledFile);
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<InstalledContent>>(File.ReadAllText(file), Json) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }

    private void WriteInstalled(List<InstalledContent> list)
    {
        Directory.CreateDirectory(Root);
        string file = Path.Combine(Root, InstalledFile);
        string temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(list.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToList(), Json));
        File.Move(temp, file, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the next cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the next cleanup.
        }
    }
}
