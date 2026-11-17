using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
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
/// and every file is checked against its SHA-256 before the new version folder is renamed into place. A changed file
/// whose entry lists a binary patch from a version the player has is made from that file and the (much smaller) patch
/// instead; when the patch is missing or does not apply, the file's blob is downloaded after all.
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

    /// <summary>
    /// A file made by patching a local file: the patch, its asset, and the file's own blob (null when that is gone),
    /// which is downloaded instead if the patch does not apply.
    /// </summary>
    public sealed record PatchStep(BuildFile File, BuildPatch Patch, ReleaseAsset Asset, ReleaseAsset? Fallback);

    /// <summary>What an install will download, worked out before anything is fetched.</summary>
    public sealed record Plan(
        IReadOnlyDictionary<string, string> Local,
        IReadOnlyList<(BuildFile File, ReleaseAsset Asset)> Downloads,
        IReadOnlyList<PatchStep> Patches)
    {
        /// <summary>Bytes to download: each asset once (a pack holds many files), plus every patch.</summary>
        public long DownloadBytes => Downloads.DistinctBy(d => d.Asset.Id).Sum(d => d.Asset.Size) + Patches.Sum(p => p.Asset.Size);
    }

    /// <summary>
    /// Finds the files the player already has (by hashing the installed versions' files of the right sizes) and the
    /// release asset of each file still missing: the smallest usable patch from a local file when there is one, else
    /// the file's blob. Throws <see cref="BlobUnavailableException"/> when a file has neither.
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
        var patches = new List<PatchStep>();
        var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuildFile f in files.Files)
        {
            if (local.ContainsKey(f.Sha256) || !queued.Add(f.Sha256))
            {
                continue;
            }

            BlobRef blob = f.Blob!;
            ReleaseAsset? blobAsset = await FindAssetAsync(releases, blob.Tag, blob.Name, cancel).ConfigureAwait(false);

            // The smallest patch from a file the player has, when it is smaller than the blob (or the blob is gone).
            PatchStep? step = null;
            foreach (BuildPatch patch in (f.Patches ?? []).Where(p => local.ContainsKey(p.From)).OrderBy(p => p.Size))
            {
                if (blobAsset is not null && patch.Size >= blobAsset.Size)
                {
                    break;
                }

                // A patch missing from its release is skipped: the next patch or the blob stands in.
                if (await FindAssetAsync(releases, patch.Tag, patch.Name, cancel).ConfigureAwait(false) is { } patchAsset)
                {
                    step = new PatchStep(f, patch, patchAsset, blobAsset);
                    break;
                }
            }

            if (step is not null)
            {
                patches.Add(step);
            }
            else
            {
                downloads.Add((f, blobAsset ?? throw new BlobUnavailableException($"{f.Path} is not available: release {blob.Tag} has no {blob.Name}.")));
            }
        }

        return new Plan(local, downloads, patches);
    }

    /// <summary>Builds the version folder from the plan: copies what is local, downloads, patches and unpacks the rest.</summary>
    public async Task<string> InstallAsync(SemVersion version, BuildFiles files, Plan plan, IProgress<DownloadProgress>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(plan);
        cancel.ThrowIfCancellationRequested();
        var local = new Dictionary<string, string>(plan.Local, StringComparer.OrdinalIgnoreCase);
        string blobs = Path.Combine(_layout.Downloads, version.ToString(), "blobs");
        string staging = _store.NewStagingDir(version);
        try
        {
            EnsureFreeSpace(staging, files, plan);
            long total = plan.DownloadBytes;
            long done = 0;

            async Task<string> DownloadAsync(ReleaseAsset asset)
            {
                string path = Path.Combine(blobs, Path.GetFileName(asset.Name));
                long before = done;
                var inner = progress is null ? null : new SyncProgress<DownloadProgress>(p =>
                    progress.Report(new DownloadProgress(before + p.Received, Math.Max(total, before + p.Received), p.BytesPerSecond)));
                await _downloader.DownloadAsync(() => _github.CreateAssetRequest(asset), path,
                    new DownloadIdentity(asset.Id, asset.Size, asset.UpdatedAt), inner, cancel).ConfigureAwait(false);
                done += asset.Size;
                return path;
            }

            // Downloads first, each asset once (a pack holds many small files), every file unpacked straight into its
            // place and checked.
            foreach (IGrouping<long, (BuildFile File, ReleaseAsset Asset)> group in plan.Downloads.GroupBy(d => d.Asset.Id))
            {
                string blob = await DownloadAsync(group.First().Asset).ConfigureAwait(false);
                foreach ((BuildFile file, _) in group)
                {
                    local[file.Sha256] = await UnpackOrRejectAsync(blob, file, staging, cancel).ConfigureAwait(false);
                }
            }

            // Then the patched files, each from the local file its patch was made from. A patch that cannot be
            // downloaded or does not apply (damaged, or the local file is not the one it was made from) gives way to
            // the file's blob.
            foreach (PatchStep step in plan.Patches)
            {
                cancel.ThrowIfCancellationRequested();
                BuildFile file = step.File;
                string dest = Target(staging, file.Path);
                string? patchFile = null;
                try
                {
                    patchFile = await DownloadAsync(step.Asset).ConfigureAwait(false);
                    await ApplyPatchAsync(local[step.Patch.From], patchFile, dest, file, cancel).ConfigureAwait(false);
                    local[file.Sha256] = dest;
                    continue;
                }
                catch (Exception ex) when (!cancel.IsCancellationRequested
                    && ex is InvalidDataException or IOException or UnauthorizedAccessException or HttpRequestException or KeyNotFoundException)
                {
                    if (patchFile is not null)
                    {
                        TryDeleteFile(patchFile);
                    }

                    if (step.Fallback is null)
                    {
                        throw new UpdateRejectedException($"{file.Path} could not be patched ({ex.Message}) and its full download is not available.");
                    }
                }

                total += step.Fallback.Size;
                string fallback = await DownloadAsync(step.Fallback).ConfigureAwait(false);
                local[file.Sha256] = await UnpackOrRejectAsync(fallback, file, staging, cancel).ConfigureAwait(false);
            }

            // Then every file of the build, from wherever it now is.
            foreach (BuildFile file in files.Files)
            {
                cancel.ThrowIfCancellationRequested();
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

            cancel.ThrowIfCancellationRequested();
            string target = _store.CommitStaging(staging, version);
            TryDeleteDir(Path.Combine(_layout.Downloads, version.ToString()));
            return target;
        }
        finally
        {
            _store.DiscardStaging(staging);
        }
    }

    /// <summary>The asset <paramref name="name"/> of release <paramref name="tag"/>, or null when either is gone.</summary>
    private async Task<ReleaseAsset?> FindAssetAsync(Dictionary<string, ReleaseInfo?> releases, string tag, string name, CancellationToken cancel)
    {
        if (!releases.TryGetValue(tag, out ReleaseInfo? release))
        {
            // Older than the first page of the release list.
            release = await _github.GetReleaseByTagAsync(tag, cancel).ConfigureAwait(false);
            releases[tag] = release;
        }

        return release?.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// SHA-256 to path of every file in the installed versions whose size one of the build's files has, or one of the
    /// old files its patches apply to.
    /// </summary>
    private async Task<Dictionary<string, string>> IndexInstalledAsync(BuildFiles files, CancellationToken cancel)
    {
        List<BuildPatch> patches = files.Files.SelectMany(f => f.Patches ?? []).ToList();
        var sizes = new HashSet<long>(files.Files.Select(f => f.Size).Concat(patches.Select(p => p.FromSize)));
        var wanted = new HashSet<string>(files.Files.Select(f => f.Sha256).Concat(patches.Select(p => p.From)), StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// Unpacks <paramref name="file"/> from its downloaded blob into the staging folder and returns where; deletes the
    /// download and throws when it does not match.
    /// </summary>
    private static async Task<string> UnpackOrRejectAsync(string blob, BuildFile file, string staging, CancellationToken cancel)
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

        return dest;
    }

    /// <summary>
    /// Makes <paramref name="dest"/> from <paramref name="old"/> and the patch, off the calling thread (a big file takes
    /// a while). Throws <see cref="InvalidDataException"/> when the patch is not for this file or does not apply.
    /// </summary>
    private static Task ApplyPatchAsync(string old, string patch, string dest, BuildFile file, CancellationToken cancel) => Task.Run(() =>
    {
        BinaryPatch.Header header = BinaryPatch.ReadHeader(patch);
        if (header.NewSize != file.Size || !string.Equals(header.NewSha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("the patch makes a different file");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        BinaryPatch.Apply(old, patch, dest, cancel);
    }, cancel);

    /// <summary>
    /// Stops before anything is written when the disk cannot hold the new version folder and the downloads (both live
    /// in the launcher's folder). Skipped when the free space cannot be read.
    /// </summary>
    private static void EnsureFreeSpace(string staging, BuildFiles files, Plan plan) =>
        DiskSpace.Ensure(staging, files.Files.Sum(f => f.Size) + plan.DownloadBytes);

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
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? full
            : throw new UpdateRejectedException($"The build's file list names a path outside the build ({relative}).");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover download only costs disk space.
        }
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
