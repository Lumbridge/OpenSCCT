using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Update;

/// <summary>The answer of an update check.</summary>
public sealed record UpdateCheck(
    ReleaseFeed Feed,
    VersionedRelease? Latest,
    SemVersion? Installed,
    bool GameUpdateAvailable,
    bool LauncherUpdateAvailable)
{
    /// <summary>The version of the launcher the latest release carries (its own, not the game's), or null.</summary>
    public SemVersion? LatestLauncher { get; init; }
}

public enum InstallOutcome
{
    /// <summary>Installed and made current.</summary>
    Activated,

    /// <summary>Installed; becomes current when the running game exits.</summary>
    WaitingForGameToClose,

    AlreadyCurrent,
}

/// <summary>Raised for an update that must not be installed: no checksum, a wrong checksum, no game build.</summary>
public sealed class UpdateRejectedException(string message) : Exception(message);

/// <summary>Check, download, verify, install and switch, with no UI.</summary>
public sealed class UpdateService
{
    private const long MaxChecksumFileBytes = 1 << 20;

    private readonly LauncherLayout _layout;
    private readonly VersionStore _store;
    private readonly GitHubReleaseClient _github;
    private readonly HttpClient _http;
    private readonly ResumableDownloader _downloader;
    private readonly IGameProcessProbe _games;

    public UpdateService(LauncherLayout layout, VersionStore store, GitHubReleaseClient github, HttpClient http, IGameProcessProbe games)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(github);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(games);
        _layout = layout;
        _store = store;
        _github = github;
        _http = http;
        _downloader = new ResumableDownloader(http);
        _games = games;
    }

    public async Task<UpdateCheck> CheckAsync(bool includePrereleases, SemVersion? launcherVersion, CancellationToken cancel)
    {
        ReleaseFeed feed = await _github.GetReleasesAsync(cancel).ConfigureAwait(false);
        VersionedRelease? latest = ReleasePicker.Latest(feed.Releases, includePrereleases);
        if (latest is not null)
        {
            // The game files release: in the list, or asked for by tag once it is older than the list's first page.
            ReleaseInfo? files = feed.Releases.FirstOrDefault(r => string.Equals(r.TagName, ReleasePicker.StoreTag, StringComparison.Ordinal) && !r.Draft)
                ?? (ReleasePicker.FilesAsset(latest) is null && ReleasePicker.GameAsset(latest) is null
                    ? await _github.GetReleaseByTagAsync(ReleasePicker.StoreTag, cancel).ConfigureAwait(false)
                    : null);
            latest = latest with { Store = files };
        }

        SemVersion? installed = _store.Current();
        bool game = latest is not null && (ReleasePicker.FilesAsset(latest) is not null || ReleasePicker.GameAsset(latest) is not null)
            && (installed is null || latest.Version > installed);
        // The launcher updates itself only to a newer launcher: a game release usually carries the same one again.
        SemVersion? latestLauncher = latest is null ? null : ReleasePicker.LauncherVersion(latest);
        bool launcher = latestLauncher is not null && launcherVersion is not null && latestLauncher > launcherVersion;
        return new UpdateCheck(feed, latest, installed, game, launcher) { LatestLauncher = latestLauncher };
    }

    /// <summary>Folders a running game uses. A game whose exe path cannot be read counts as using every version.</summary>
    public IReadOnlyCollection<string> VersionDirsInUse()
    {
        IReadOnlyList<RunningGame> running = _games.Running();
        if (running.Count == 0)
        {
            return [];
        }

        if (running.Any(g => g.ExePath is null))
        {
            return _store.Installed().Select(v => _layout.VersionDir(v.ToString())).ToList();
        }

        return running.Select(g => _store.VersionDirOf(g.ExePath!)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool GameRunning => _games.Running().Count > 0;

    /// <summary>
    /// Downloads, verifies and installs the game build of <paramref name="release"/>, then switches to it. With the
    /// release's file list only the files the player does not already have are downloaded; the whole zip is the
    /// fallback for a release without one, or when a file's asset can no longer be found.
    /// </summary>
    public async Task<InstallOutcome> InstallAsync(VersionedRelease release, IProgress<DownloadProgress>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (_store.Current() is { } current && current == release.Version)
        {
            return InstallOutcome.AlreadyCurrent;
        }

        if (!_store.IsInstalled(release.Version))
        {
            if (!await TryInstallChangedFilesAsync(release, progress, cancel).ConfigureAwait(false))
            {
                ReleaseAsset asset = ReleasePicker.GameAsset(release)
                    ?? throw new UpdateRejectedException($"Release {release.Version} has no game build ({ReleasePicker.GameAssetName(release.Version)}).");
                string zip = await DownloadVerifiedAsync(release, asset, progress, cancel).ConfigureAwait(false);
                _store.InstallFromZip(zip, release.Version);
                TryDeleteFile(zip);
            }
        }

        return FinishSwitch(release.Version);
    }

    /// <summary>Bytes the last install downloaded through the file list, and the build's full size (for the status line).</summary>
    public (long Downloaded, long BuildSize)? LastDelta { get; private set; }

    /// <summary>
    /// The update through the file list. False when the release has none, or a file's asset is gone and the zip
    /// can stand in; a file list that is there but wrong is an error.
    /// </summary>
    private async Task<bool> TryInstallChangedFilesAsync(VersionedRelease release, IProgress<DownloadProgress>? progress, CancellationToken cancel)
    {
        LastDelta = null;
        if (ReleasePicker.FilesAsset(release) is not { } listAsset)
        {
            return false;
        }

        string listFile = await DownloadVerifiedAsync(release, listAsset, null, cancel).ConfigureAwait(false);
        BuildFiles files;
        try
        {
            files = BuildFiles.Parse(await File.ReadAllTextAsync(listFile, cancel).ConfigureAwait(false));
        }
        finally
        {
            TryDeleteFile(listFile);
        }

        var delta = new DeltaInstaller(_layout, _store, _github, _downloader);
        ReleaseFeed feed = await _github.GetReleasesAsync(cancel).ConfigureAwait(false);
        DeltaInstaller.Plan plan;
        try
        {
            plan = await delta.PlanAsync(files, feed.Releases, cancel).ConfigureAwait(false);
        }
        catch (BlobUnavailableException) when (ReleasePicker.GameAsset(release) is not null)
        {
            return false;
        }

        await delta.InstallAsync(release.Version, files, plan, progress, cancel).ConfigureAwait(false);
        LastDelta = (plan.DownloadBytes, files.Files.Sum(f => f.Size));
        return true;
    }

    /// <summary>Makes a pending version current once no game is running. Call on start and on a timer.</summary>
    public InstallOutcome? TryFinishPending()
    {
        SemVersion? pending = _store.Pending();
        return pending is null ? null : FinishSwitch(pending);
    }

    /// <summary>Downloads and verifies the launcher exe of <paramref name="release"/> and stages it for the swap on restart.</summary>
    public async Task StageLauncherAsync(VersionedRelease release, LauncherSwap swap, IProgress<DownloadProgress>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(swap);
        ReleaseAsset asset = ReleasePicker.LauncherAsset(release)
            ?? throw new UpdateRejectedException($"Release {release.Version} has no launcher ({ReleasePicker.LauncherPrefix}*-{ReleasePicker.Platform}.exe).");
        string file = await DownloadVerifiedAsync(release, asset, progress, cancel).ConfigureAwait(false);
        swap.Stage(file);
        TryDeleteFile(file);
    }

    public bool Rollback()
    {
        if (GameRunning)
        {
            throw new InvalidOperationException("Close OpenSCCT before switching versions.");
        }

        return _store.Rollback();
    }

    private InstallOutcome FinishSwitch(SemVersion version)
    {
        if (GameRunning)
        {
            // The new folder is complete; the running game keeps its own. Switch when it exits.
            _store.MarkPending(version);
            return InstallOutcome.WaitingForGameToClose;
        }

        _store.Activate(version);
        _store.Prune(VersionDirsInUse());
        return InstallOutcome.Activated;
    }

    private async Task<string> DownloadVerifiedAsync(VersionedRelease release, ReleaseAsset asset, IProgress<DownloadProgress>? progress, CancellationToken cancel)
    {
        string expected = await ExpectedHashAsync(release, asset, cancel).ConfigureAwait(false);
        string folder = Path.Combine(_layout.Downloads, release.Version.ToString());
        string target = Path.Combine(folder, Path.GetFileName(asset.Name));
        await _downloader.DownloadAsync(
            () => _github.CreateAssetRequest(asset),
            target,
            new DownloadIdentity(asset.Id, asset.Size, asset.UpdatedAt),
            progress,
            cancel).ConfigureAwait(false);

        if (!await Checksums.VerifyAsync(target, expected, cancel).ConfigureAwait(false))
        {
            TryDeleteFile(target);
            throw new UpdateRejectedException($"{asset.Name} does not match its published SHA-256. The download was deleted; try again.");
        }

        return target;
    }

    private async Task<string> ExpectedHashAsync(VersionedRelease release, ReleaseAsset asset, CancellationToken cancel)
    {
        ReleaseAsset sums = ReleasePicker.ChecksumAsset(release, asset)
            ?? throw new UpdateRejectedException($"Release {release.Version} publishes no checksum for {asset.Name}; it will not be installed.");
        if (sums.Size > MaxChecksumFileBytes)
        {
            throw new UpdateRejectedException($"{sums.Name} is too large to be a checksum file.");
        }

        using HttpRequestMessage request = _github.CreateAssetRequest(sums);
        using HttpResponseMessage response = await _http.SendAsync(request, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        return Checksums.FindExpected(text, asset.Name)
            ?? throw new UpdateRejectedException($"{sums.Name} has no SHA-256 for {asset.Name}; it will not be installed.");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover zip only costs disk space.
        }
    }
}
