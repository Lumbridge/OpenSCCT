using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Update;

/// <summary>A file of the build list could not be found on GitHub (its release was deleted, or GitHub is unreachable).</summary>
public sealed class BlobUnavailableException(string message) : Exception(message);

/// <summary>
/// Installs a build from its file list (<see cref="BuildFiles"/>): every file the player already has in an installed
/// version (same SHA-256) is copied from there, only the others are downloaded, each as its gzipped release asset,
/// and every file is checked against its SHA-256 before the new version folder is renamed into place.
/// </summary>
public sealed class DeltaInstaller
{
    private readonly LauncherLayout _layout;
    private readonly VersionStore _store;
    private readonly GitHubReleaseClient _github;
    private readonly ResumableDownloader _downloader;

    public DeltaInstaller(LauncherLayout layout, VersionStore store, GitHubReleaseClient github, ResumableDownloader downloader)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(github);
        ArgumentNullException.ThrowIfNull(downloader);
        _layout = layout;
        _store = store;
        _github = github;
        _downloader = downloader;
    }

    /// <summary>What an install will download, worked out before anything is fetched.</summary>
    public sealed record Plan(IReadOnlyDictionary<string, string> Local, IReadOnlyList<(BuildFile File, ReleaseAsset Asset)> Downloads)
    {
        /// <summary>Bytes to download: each asset once (a pack holds many files).</summary>
        public long DownloadBytes => Downloads.DistinctBy(d => d.Asset.Id).Sum(d => d.Asset.Size);
    }

    /// <summary>
    /// Finds the files the player already has (by hashing the installed versions' files of the right sizes) and the
    /// release asset of each file still missing. Throws <see cref="BlobUnavailableException"/> when an asset is gone.
    /// </summary>
    public async Task<Plan> PlanAsync(BuildFiles files, IReadOnlyList<ReleaseInfo> feed, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(feed);
        Dictionary<string, string> local = await IndexInstalledAsync(files, cancel).ConfigureAwait(false);

        var releases = new Dictionary<string, ReleaseInfo?>(StringComparer.OrdinalIgnoreCase);
        foreach (ReleaseInfo r in feed)
        {
            releases.TryAdd(r.TagName, r);
        }

        var downloads = new List<(BuildFile, ReleaseAsset)>();
        var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuildFile f in files.Files)
        {
            if (local.ContainsKey(f.Sha256) || !queued.Add(f.Sha256))
            {
                continue;
            }

            BlobRef blob = f.Blob!;
            if (!releases.TryGetValue(blob.Tag, out ReleaseInfo? release))
            {
                // Older than the first page of the release list.
                release = await _github.GetReleaseByTagAsync(blob.Tag, cancel).ConfigureAwait(false);
                releases[blob.Tag] = release;
            }

            ReleaseAsset asset = release?.Assets.FirstOrDefault(a => string.Equals(a.Name, blob.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new BlobUnavailableException($"{f.Path} is not available: release {blob.Tag} has no {blob.Name}.");
            downloads.Add((f, asset));
        }

        return new Plan(local, downloads);
    }

    /// <summary>Builds the version folder from the plan: copies what is local, downloads and unpacks the rest.</summary>
    public async Task<string> InstallAsync(SemVersion version, BuildFiles files, Plan plan, IProgress<DownloadProgress>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(plan);
        var local = new Dictionary<string, string>(plan.Local, StringComparer.OrdinalIgnoreCase);
        string blobs = Path.Combine(_layout.Downloads, version.ToString(), "blobs");
        string staging = _store.NewStagingDir(version);
        try
        {
            // Downloads first, each asset once (a pack holds many small files), every file unpacked straight into its
            // place and checked.
            long total = plan.DownloadBytes;
            long done = 0;
            foreach (IGrouping<long, (BuildFile File, ReleaseAsset Asset)> group in plan.Downloads.GroupBy(d => d.Asset.Id))
            {
                ReleaseAsset asset = group.First().Asset;
                string blob = Path.Combine(blobs, Path.GetFileName(asset.Name));
                long before = done;
                var inner = progress is null ? null : new SyncProgress<DownloadProgress>(p =>
                    progress.Report(new DownloadProgress(before + p.Received, total, p.BytesPerSecond)));
                await _downloader.DownloadAsync(() => _github.CreateAssetRequest(asset), blob,
                    new DownloadIdentity(asset.Id, asset.Size, asset.UpdatedAt), inner, cancel).ConfigureAwait(false);
                done += asset.Size;

                foreach ((BuildFile file, _) in group)
                {
                    string dest = Target(staging, file.Path);
                    bool ok = file.Blob!.Member is { } member
                        ? await UnpackMemberAsync(blob, member, dest, file.Sha256, cancel).ConfigureAwait(false)
                        : await UnpackAsync(blob, dest, file.Sha256, cancel).ConfigureAwait(false);
                    if (!ok)
                    {
                        File.Delete(blob);
                        throw new UpdateRejectedException($"{file.Path} does not match its published SHA-256. The download was deleted; try again.");
                    }

                    local[file.Sha256] = dest;
                }
            }

            // Then every file of the build, from wherever it now is.
            foreach (BuildFile file in files.Files)
            {
                string dest = Target(staging, file.Path);
                if (File.Exists(dest))
                {
                    continue;
                }

                string from = local.TryGetValue(file.Sha256, out string? source)
                    ? source
                    : throw new InvalidOperationException($"No source for {file.Path}.");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(from, dest);
            }

            string target = _store.CommitStaging(staging, version);
            TryDeleteDir(Path.Combine(_layout.Downloads, version.ToString()));
            return target;
        }
        finally
        {
            _store.DiscardStaging(staging);
        }
    }

    /// <summary>SHA-256 to path of every file in the installed versions whose size one of the build's files has.</summary>
    private async Task<Dictionary<string, string>> IndexInstalledAsync(BuildFiles files, CancellationToken cancel)
    {
        var sizes = new HashSet<long>(files.Files.Select(f => f.Size));
        var wanted = new HashSet<string>(files.Files.Select(f => f.Sha256), StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var versions = new List<SemVersion>();
        if (_store.Current() is { } current)
        {
            versions.Add(current);
        }

        if (_store.Previous() is { } previous && !versions.Contains(previous))
        {
            versions.Add(previous);
        }

        foreach (SemVersion v in versions)
        {
            string dir = _layout.VersionDir(v.ToString());
            foreach (string path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                cancel.ThrowIfCancellationRequested();
                long length;
                try
                {
                    length = new FileInfo(path).Length;
                    if (!sizes.Contains(length))
                    {
                        continue;
                    }

                    string hash = await Checksums.HashFileAsync(path, cancel).ConfigureAwait(false);
                    if (wanted.Contains(hash))
                    {
                        found.TryAdd(hash, path);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file we cannot read is downloaded instead.
                }
            }
        }

        return found;
    }

    /// <summary>Gunzips <paramref name="gz"/> to <paramref name="dest"/>; false (and no file) when the result has another SHA-256.</summary>
    private static async Task<bool> UnpackAsync(string gz, string dest, string sha256, CancellationToken cancel)
    {
        await using FileStream input = new(gz, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var unzip = new GZipStream(input, CompressionMode.Decompress);
        return await WriteCheckedAsync(unzip, dest, sha256, cancel).ConfigureAwait(false);
    }

    /// <summary>Extracts entry <paramref name="member"/> of the zip pack to <paramref name="dest"/>; false when it is missing or has another SHA-256.</summary>
    private static async Task<bool> UnpackMemberAsync(string pack, string member, string dest, string sha256, CancellationToken cancel)
    {
        using ZipArchive zip = ZipFile.OpenRead(pack);
        if (zip.GetEntry(member) is not { } entry)
        {
            return false;
        }

        await using Stream input = entry.Open();
        return await WriteCheckedAsync(input, dest, sha256, cancel).ConfigureAwait(false);
    }

    /// <summary>Copies <paramref name="input"/> to <paramref name="dest"/> while hashing; keeps the file only when the SHA-256 matches.</summary>
    private static async Task<bool> WriteCheckedAsync(Stream input, string dest, string sha256, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        string temp = dest + ".tmp";
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using (FileStream output = new(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[1 << 16];
                int n;
                while ((n = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, n);
                    await output.WriteAsync(buffer.AsMemory(0, n), cancel).ConfigureAwait(false);
                }
            }

            if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                return false;
            }
        }

        File.Move(temp, dest, overwrite: true);
        return true;
    }

    private static string Target(string staging, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new UpdateRejectedException($"The build's file list names a path outside the build ({relative}).");
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftover downloads only cost disk space.
        }
    }

    /// <summary>An IProgress that reports on the calling thread (the outer Progress posts to the UI once).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
