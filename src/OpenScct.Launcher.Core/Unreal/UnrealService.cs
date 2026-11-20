using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Unreal;

/// <summary>
/// Installs, updates and starts the Unreal Engine build of OpenSCCT ("OpenSCCT Unreal"), published as GitHub releases
/// tagged v&lt;version&gt;. It has its own install root (<see cref="LauncherLayout.Unreal"/>), laid out like the game's:
/// install.json, versions\&lt;v&gt;\, downloads\ and cache\. Updates go through the game's <see cref="UpdateService"/>
/// and <see cref="DeltaInstaller"/> (changed files and binary patches from the shared <see cref="StoreTag"/> release,
/// the whole ZIP as the fallback, a pending switch while the game runs). The game uses the active local Steam conversion for base assets and can load optional maps.
/// Windows only.
/// </summary>
public sealed class UnrealService
{
    public const string DefaultRepository = "OpenSCCT/OpenSCCT-Unreal";

    /// <summary>Tag of the prerelease that holds every version's blobs, packs and patches (not a version, so never offered).</summary>
    public const string StoreTag = "unreal-files-1";

    /// <summary>The starter exe at the top of each version folder; it starts the Shipping exe.</summary>
    public const string StarterExe = "OpenSCCTUnreal.exe";

    /// <summary>The product name the release's launcher.json must carry.</summary>
    public const string Product = "OpenSCCT Unreal";

    /// <summary>Shown when the repository has no compatible release (or does not exist yet).</summary>
    public const string NoReleaseMessage = "No OpenSCCT Unreal release is available yet.";

    /// <summary>Raised (as <see cref="UpdateRejectedException"/>) for a release this launcher cannot install.</summary>
    public const string NeedsNewerLauncherMessage = "This OpenSCCT Unreal version needs a newer launcher.";

    private const int LauncherProtocol = 1;
    private const int MaxMetadataBytes = 64 * 1024;
    private const string VcRuntimeKey = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64";
    private const string VcRuntimeKeyWow = @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64";

    public static readonly SemVersion MinimumSteamVersion = SemVersion.Parse("0.1.10");
    public static bool SupportsSteamContent(SemVersion? version) => version >= MinimumSteamVersion;

    private readonly GodotSteamContent _content;
    private readonly LauncherLayout _layout;
    private readonly LauncherLayout _install;
    private readonly HttpClient _http;
    private readonly VersionStore _store;
    private readonly GitHubReleaseClient _github;
    private readonly UpdateService _updates;

    /// <param name="layout">The main launcher layout; the build goes in its <see cref="LauncherLayout.Unreal"/> folder.</param>
    /// <param name="http">The shared client; no GitHub token is ever used.</param>
    /// <param name="repository">"owner/name" of the releases; null or invalid means <see cref="DefaultRepository"/>.</param>
    public UnrealService(LauncherLayout layout, HttpClient http, string? repository = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(http);
        _layout = layout;
        _content = new GodotSteamContent(layout);
        _http = http;
        Repository = IsValidRepository(repository) ? repository!.Trim() : DefaultRepository;
        string[] parts = Repository.Split('/');
        _install = new LauncherLayout(layout.Unreal, LauncherPlatform.UnrealWindowsX64);
        _store = new VersionStore(_install);
        _github = new GitHubReleaseClient(http, new ReleaseSource(parts[0], parts[1], null), _install.Cache);
        _updates = new UpdateService(_install, _store, _github, http, new Probe(this), ReleaseNames.Unreal);
    }

    /// <summary>The Unreal game's process names (exact; the Godot game's are OpenSCCT and OpenSCCT.console).</summary>
    public static IReadOnlyList<string> ProcessNames { get; } = ["OpenSCCTUnreal", "OpenSCCTUnreal-Win64-Shipping"];

    /// <summary>Test hook: whether a game is running, in place of the process list.</summary>
    internal Func<bool>? RunningOverride { get; set; }

    /// <summary>Test hook: whether the Visual C++ runtime is missing, in place of the registry.</summary>
    internal Func<bool>? VcRuntimeOverride { get; set; }

    /// <summary>Test hook: the running Unreal game processes, in place of the operating system's.</summary>
    internal Func<IReadOnlyList<UnrealProcess>>? ProcessesOverride { get; set; }

    /// <summary>Test hook: the free bytes of a path's drive (null when unknown), in place of the drive's, for the ZIP install.</summary>
    internal Func<string, long?>? FreeSpaceOverride
    {
        get => _updates.FreeSpaceProbe;
        set => _updates.FreeSpaceProbe = value;
    }

    /// <summary>The "owner/name" repository the releases come from.</summary>
    public string Repository { get; }

    public string ReleasesPage => $"https://github.com/{Repository}/releases";

    public string InstallRoot => _layout.Unreal;

    /// <summary>
    /// True for "owner/name" as GitHub allows it: an owner of up to 39 letters, digits and hyphens (not starting with a
    /// hyphen) and a name of up to 100 letters, digits, '.', '-' and '_' (not "." or "..").
    /// </summary>
    public static bool IsValidRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Trim().Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        string owner = parts[0];
        string name = parts[1];
        return owner.Length is >= 1 and <= 39 && owner[0] != '-' && owner.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && name.Length is >= 1 and <= 100 && name is not ("." or "..") && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    }

    /// <summary>The current version, or null when none is installed or its folder is broken.</summary>
    public UnrealInstallation? Installed()
    {
        SemVersion? version = _store.Current();
        if (version is null)
        {
            return null;
        }

        string folder = _install.VersionDir(version.ToString());
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "version.json")));
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("product", out JsonElement product) && product.GetString() != Product))
            {
                return null;
            }

            return new UnrealInstallation(version, folder, Text(root, "openscctVersion"), Text(root, "openscctCommit"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }

        static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>An installed version that becomes current when the game closes, or null.</summary>
    public SemVersion? Pending => _store.Pending();

    /// <summary>
    /// Whether an Unreal game runs: a process with one of <see cref="ProcessNames"/> started from under
    /// <see cref="InstallRoot"/>, or whose exe cannot be read.
    /// </summary>
    public bool IsRunning => RunningGames().Count > 0;

    /// <summary>Null when the OpenSCCT content the game needs is there, else a sentence for the player.</summary>
    public string? PrerequisiteProblem() => _content.ActiveRoot() is null
        ? "Build the original maps and assets from your Steam installation."
        : null;

    /// <summary>
    /// The newest compatible release: a version tag with a launcher.json and the build's ZIP or file list, without
    /// prereleases unless asked for. A missing repository gives no release and no problem.
    /// </summary>
    public async Task<UnrealCheck> CheckAsync(bool includePrereleases, CancellationToken cancel)
    {
        ReleaseFeed feed = await _github.GetReleasesAsync(cancel).ConfigureAwait(false);
        if (feed.RepositoryNotFound)
        {
            // Not published yet (or not public): nothing to offer, which is not an error.
            feed = new ReleaseFeed([], ReleaseFeedSource.Network, null, null) { RepositoryNotFound = true };
        }

        ReleaseInfo? store = feed.Releases.FirstOrDefault(r => string.Equals(r.TagName, StoreTag, StringComparison.Ordinal) && !r.Draft);
        VersionedRelease? latest = feed.Releases
            .Select(r => Offer(r, store, _install.Platform))
            .OfType<VersionedRelease>()
            .Where(o => includePrereleases || (!o.Release.Prerelease && !o.Version.IsPrerelease))
            .OrderByDescending(o => o.Version)
            .FirstOrDefault();
        UnrealInstallation? installed = Installed();
        return new UnrealCheck(feed, latest, installed, latest is not null && (installed is null || latest.Version > installed.Version));
    }

    /// <summary>The release as an offer, or null when it is not an installable Unreal build.</summary>
    internal static VersionedRelease? Offer(ReleaseInfo release, ReleaseInfo? store, LauncherPlatform platform)
    {
        if (release.Draft || !SemVersion.TryParse(release.TagName, out SemVersion? version))
        {
            return null;
        }

        var offer = new VersionedRelease(version, release) { Store = store };
        bool handshake = release.Assets.Any(a => string.Equals(a.Name, MetadataName(version), StringComparison.OrdinalIgnoreCase));
        bool build = ReleasePicker.FilesAsset(offer, platform, ReleaseNames.Unreal) is not null
            || ReleasePicker.GameAsset(offer, platform, ReleaseNames.Unreal) is not null;
        return handshake && build ? offer : null;
    }

    /// <summary>The release's handshake file: {"launcherProtocol":1,"product":"OpenSCCT Unreal",...}.</summary>
    internal static string MetadataName(SemVersion version) => $"{ReleasePicker.UnrealPrefix}{version}-win-x64.launcher.json";

    /// <summary>
    /// Checks the release's launcher.json, then installs its build: the changed files (and patches) from its file list
    /// when it has one, else the whole ZIP. It becomes current at once, or when the running game closes.
    /// </summary>
    public async Task<UnrealInstallResult> InstallAsync(VersionedRelease release, IProgress<UnrealProgress>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!_layout.Platform.Equals(LauncherPlatform.WindowsX64))
        {
            throw new InvalidOperationException("OpenSCCT Unreal runs on Windows only.");
        }

        cancel.ThrowIfCancellationRequested();
        bool repair = false;
        if (_store.Current() is { } current && current == release.Version)
        {
            if (Installed() is not null)
            {
                return new UnrealInstallResult(UnrealInstallOutcome.AlreadyCurrent, release.Version, null, null);
            }

            // The folder has its starter but no readable version.json: install the version again over it.
            repair = true;
        }

        progress?.Report(new UnrealProgress("Checking OpenSCCT Unreal " + release.Version));
        await CheckHandshakeAsync(release, cancel).ConfigureAwait(false);
        if (repair)
        {
            SetAsideBrokenVersion(release.Version);
        }

        bool present = _store.IsInstalled(release.Version);
        _updates.PhaseChanged = phase => progress?.Report(phase switch
        {
            UpdatePhase.CheckingFiles => new UnrealProgress("Checking installed files"),
            UpdatePhase.Downloading => new UnrealProgress("Downloading OpenSCCT Unreal"),
            UpdatePhase.Installing => new UnrealProgress("Installing OpenSCCT Unreal"),
            _ => new UnrealProgress("Installing OpenSCCT Unreal", CanCancel: false),
        });
        var bytes = progress is null ? null : new SyncProgress<DownloadProgress>(d => progress.Report(d.Total > 0 && d.Received >= d.Total
            ? new UnrealProgress("Installing OpenSCCT Unreal", d.Received, d.Total)
            : new UnrealProgress("Downloading OpenSCCT Unreal", d.Received, d.Total, d.BytesPerSecond)));
        InstallOutcome outcome;
        try
        {
            outcome = await _updates.InstallAsync(release, bytes, cancel).ConfigureAwait(false);
        }
        finally
        {
            _updates.PhaseChanged = null;
        }

        UnrealInstallOutcome result = outcome switch
        {
            InstallOutcome.Activated => UnrealInstallOutcome.Installed,
            InstallOutcome.WaitingForGameToClose => UnrealInstallOutcome.WaitingForGameToClose,
            _ => UnrealInstallOutcome.AlreadyCurrent,
        };
        progress?.Report(new UnrealProgress(result == UnrealInstallOutcome.WaitingForGameToClose
            ? "OpenSCCT Unreal installed; it switches when the game closes"
            : "OpenSCCT Unreal installed", CanCancel: false));
        long? downloaded = present ? null : _updates.LastDelta?.Downloaded ?? _updates.LastZip;
        long? build = present ? null : _updates.LastDelta?.BuildSize;
        return new UnrealInstallResult(result, release.Version, downloaded, build);
    }

    /// <summary>
    /// Moves the current version's damaged folder aside (it is pruned later), so the install builds it again. Refuses
    /// while the game runs, since it may be running from that folder.
    /// </summary>
    private void SetAsideBrokenVersion(SemVersion version)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException($"OpenSCCT Unreal {version} is damaged. Close OpenSCCT Unreal so it can be repaired.");
        }

        try
        {
            _store.SetAside(version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"OpenSCCT Unreal {version} is damaged and its folder could not be moved for repair: {ex.Message}", ex);
        }
    }

    /// <summary>Requires the release's launcher.json to name this product and launcher protocol 1.</summary>
    private async Task CheckHandshakeAsync(VersionedRelease release, CancellationToken cancel)
    {
        string name = MetadataName(release.Version);
        ReleaseAsset asset = release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new UpdateRejectedException(NeedsNewerLauncherMessage);
        if (!IsCompatible(await SmallAssetAsync(asset, cancel).ConfigureAwait(false)))
        {
            throw new UpdateRejectedException(NeedsNewerLauncherMessage);
        }
    }

    internal static bool IsCompatible(string metadata)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(metadata);
            JsonElement root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("launcherProtocol", out JsonElement protocol) && protocol.ValueKind == JsonValueKind.Number
                && protocol.TryGetInt32(out int value) && value == LauncherProtocol
                && root.TryGetProperty("product", out JsonElement product) && product.ValueKind == JsonValueKind.String
                && product.GetString() == Product;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string> SmallAssetAsync(ReleaseAsset asset, CancellationToken cancel)
    {
        const string tooLarge = "The OpenSCCT Unreal release information is too large to be read.";
        using HttpRequestMessage request = _github.CreateAssetRequest(asset);
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxMetadataBytes)
        {
            throw new UpdateRejectedException(tooLarge);
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + count > MaxMetadataBytes)
            {
                throw new UpdateRejectedException(tooLarge);
            }

            memory.Write(buffer, 0, count);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    /// <summary>Makes a pending version current when no Unreal game runs. True when it switched.</summary>
    public bool FinishPendingSwitch() => _updates.TryFinishPending() == InstallOutcome.Activated;

    /// <summary>
    /// How to start the current version: its starter exe, given the OpenSCCT content folder as
    /// <c>-ScctContent="&lt;Steam build&gt;"</c> (quoted after the '=', which Unreal's parser needs for a path with spaces).
    /// A pending version is made current first. Throws <see cref="InvalidOperationException"/> with a sentence for the
    /// player when it cannot start.
    /// </summary>
    public ProcessStartInfo StartInfo()
    {
        UnrealInstallation installed = Installed() ?? throw new InvalidOperationException("Install OpenSCCT Unreal first.");
        if (Pending is not null)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("An OpenSCCT Unreal update is waiting for the game to close. Close OpenSCCT Unreal, then start it again.");
            }

            if (FinishPendingSwitch())
            {
                installed = Installed() ?? throw new InvalidOperationException("Install OpenSCCT Unreal first.");
            }
        }

        if (!SupportsSteamContent(installed.Version))
            throw new InvalidOperationException($"Update OpenSCCT Unreal to {MinimumSteamVersion} or later to use your Steam game files.");
        string content = _content.ActiveRoot() ?? throw new InvalidOperationException("Build the original maps and assets from your Steam installation.");
        string arguments = ContentArgument(content);
        LauncherSettings settings = LauncherSettings.Load(_layout.Settings);
        if (settings.UnrealMapModsEnabled)
        {
            if (!UnrealMapMods.TryGetMapRoot(settings.UnrealMapModRoot, out string maps, out string error, Path.Combine(content, "svmap")))
                throw new InvalidOperationException(error);
            // A single quoted root keeps spaces and literal semicolons intact, including through UE's starter exe.
            arguments += $" --svm-maps=\"{maps.Replace('\\', '/')}\"";
        }
        string exe = Path.Combine(installed.Folder, StarterExe);
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = installed.Folder,
            UseShellExecute = false,
            Arguments = arguments,
        };
        info.Environment.Remove("SVM_CONTENT");
        info.Environment.Remove("SVM_GAME_ROOT");
        return info;
    }

    /// <summary>
    /// <c>-ScctContent="&lt;folder&gt;"</c>, written by hand: Unreal's parser reads a value up to the first space unless it
    /// is quoted after the '=' (ProcessStartInfo.ArgumentList would quote the whole argument instead).
    /// </summary>
    internal static string ContentArgument(string folder)
    {
        if (folder.Contains('"', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OpenSCCT Unreal cannot read a game files folder whose path has a double quote (\") in it: " + folder);
        }

        if (folder.EndsWith('\\'))
        {
            // A backslash before the closing quote would escape it.
            folder = folder.TrimEnd('\\');
        }

        return $"-ScctContent=\"{folder}\"";
    }

    /// <summary>
    /// True when the Visual C++ x64 runtime the build needs is not installed, or is older than the one it carries
    /// (HKLM ...\VC\Runtimes\X64, Installed = 1 and Major.Minor.Bld at least the bundled vc_redist.x64.exe's version,
    /// or <see cref="MinimumVcRuntime"/> when that cannot be read; in either registry view). An old 2015-2019 runtime
    /// writes the same key, but lacks what an Unreal Engine 5 build needs. Always false outside Windows.
    /// </summary>
    public bool VcRuntimeMissing()
    {
        if (VcRuntimeOverride is { } fake)
        {
            return fake();
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        Version required = BundledVcRuntimeVersion() ?? MinimumVcRuntime;
        return !RuntimeSatisfies(InstalledRuntime(VcRuntimeKey), required) && !RuntimeSatisfies(InstalledRuntime(VcRuntimeKeyWow), required);
    }

    /// <summary>The oldest runtime accepted when the bundled installer's version cannot be read (VCRUNTIME140_1.dll came with 14.20).</summary>
    internal static readonly Version MinimumVcRuntime = new(14, 20, 0);

    /// <summary>True when an installed runtime (null when the key says none is installed) is at least <paramref name="required"/>.</summary>
    internal static bool RuntimeSatisfies(Version? installed, Version required) =>
        installed is not null && new Version(installed.Major, installed.Minor, Math.Max(installed.Build, 0))
            >= new Version(required.Major, required.Minor, Math.Max(required.Build, 0));

    /// <summary>Major.Minor.Build of the current version's Prerequisites\vc_redist.x64.exe, or null.</summary>
    private Version? BundledVcRuntimeVersion()
    {
        if (VcRuntimeInstaller() is not { } installer)
        {
            return null;
        }

        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(installer.FileName);
            return info.FileMajorPart > 0 ? new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The runtime version a registry key records (Major/Minor/Bld, else its "v14.x.y.z" Version), or null when not installed.</summary>
    [SupportedOSPlatform("windows")]
    private static Version? InstalledRuntime(string keyPath)
    {
        try
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = machine.OpenSubKey(keyPath);
            return key is null ? null : ParseRuntime(key.GetValue("Installed"), key.GetValue("Major"), key.GetValue("Minor"), key.GetValue("Bld"), key.GetValue("Version"));
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The runtime version from a VC\Runtimes\X64 key's values, or null when it is not installed. An unknown version reads as 0.0.0.</summary>
    internal static Version? ParseRuntime(object? installed, object? major, object? minor, object? build, object? version)
    {
        if (installed is not int flag || flag != 1)
        {
            return null;
        }

        if (major is int ma && minor is int mi && build is int bld && ma >= 0 && mi >= 0 && bld >= 0)
        {
            return new Version(ma, mi, bld);
        }

        if (version is string text && Version.TryParse(text.TrimStart('v', 'V'), out Version? parsed))
        {
            return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        }

        return new Version(0, 0, 0);
    }

    /// <summary>
    /// The Visual C++ runtime installer the current version carries (Prerequisites\vc_redist.x64.exe), run through the
    /// shell so it can ask for elevation; null when there is none.
    /// </summary>
    public ProcessStartInfo? VcRuntimeInstaller()
    {
        if (Installed() is not { } installed)
        {
            return null;
        }

        string exe = Path.Combine(installed.Folder, "Prerequisites", "vc_redist.x64.exe");
        return File.Exists(exe)
            ? new ProcessStartInfo(exe) { Arguments = "/install /passive /norestart", UseShellExecute = true, WorkingDirectory = installed.Folder }
            : null;
    }

    /// <summary>
    /// Deletes the Unreal build (<see cref="LauncherLayout.Unreal"/>) and, when asked, its map packs
    /// (<see cref="LauncherLayout.UnrealPacks"/>). Refuses while the game runs. Never touches the Godot game, the
    /// OpenSCCT content, the editor or the game's own saved settings (outside the launcher's folder).
    /// </summary>
    public async Task<UnrealUninstallResult> UninstallAsync(bool removeMapPacks, IProgress<UnrealProgress>? progress, CancellationToken cancel)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Close OpenSCCT Unreal before uninstalling it.");
        }

        cancel.ThrowIfCancellationRequested();
        var failures = new List<string>();
        progress?.Report(new UnrealProgress("Uninstalling OpenSCCT Unreal", CanCancel: false));
        await Task.Run(() => Delete(_layout.Unreal, failures), CancellationToken.None).ConfigureAwait(false);
        if (removeMapPacks)
        {
            progress?.Report(new UnrealProgress("Removing Unreal map packs", CanCancel: false));
            await Task.Run(() => Delete(_layout.UnrealPacks, failures), CancellationToken.None).ConfigureAwait(false);
        }

        progress?.Report(new UnrealProgress("OpenSCCT Unreal uninstalled", CanCancel: false));
        return new UnrealUninstallResult(failures);
    }

    private void Delete(string folder, List<string> failures)
    {
        try
        {
            SafeDelete.Tree(folder, _layout.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(folder);
        }
    }

    /// <summary>Ends every Unreal game process started from under <see cref="InstallRoot"/> and waits briefly for each.</summary>
    public async Task ForceCloseAsync(CancellationToken cancel)
    {
        var owned = new List<Process>();
        try
        {
            IReadOnlyList<UnrealProcess> processes = ProcessesOverride?.Invoke() ?? Snapshot(owned);
            await Task.WhenAll(processes.Where(p => p.ExePath is not null && IsUnderInstallRoot(p.ExePath)).Select(p => p.KillAsync(cancel)))
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (Process process in owned)
            {
                process.Dispose();
            }
        }
    }

    private IReadOnlyList<RunningGame> RunningGames()
    {
        if (RunningOverride is { } fake)
        {
            return fake() ? [new RunningGame(0, null)] : [];
        }

        var owned = new List<Process>();
        try
        {
            IReadOnlyList<UnrealProcess> processes = ProcessesOverride?.Invoke() ?? Snapshot(owned);
            return processes.Where(p => p.ExePath is null || IsUnderInstallRoot(p.ExePath)).Select(p => new RunningGame(p.Id, p.ExePath)).ToList();
        }
        finally
        {
            foreach (Process process in owned)
            {
                process.Dispose();
            }
        }
    }

    private bool IsUnderInstallRoot(string exe)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallRoot)) + Path.DirectorySeparatorChar;
        try
        {
            return Path.GetFullPath(exe).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>The operating system's Unreal game processes; each is kept open (in <paramref name="owned"/>) so a kill cannot hit a reused id.</summary>
    private static List<UnrealProcess> Snapshot(List<Process> owned)
    {
        var found = new List<UnrealProcess>();
        foreach (string name in ProcessNames)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                owned.Add(process);
                string? exe = null;
                try
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    _ = process.SafeHandle;
                    exe = process.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Unknown path: counted as running, never force closed.
                }

                found.Add(new UnrealProcess(process.Id, exe, cancel => KillAsync(process, cancel)));
            }
        }

        return found;
    }

    private static async Task KillAsync(Process process, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            process.Kill();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // Still closing after the wait; the caller checks IsRunning again.
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Exited already, or not ours to end.
        }
    }

    /// <summary>The running games as the update service sees them, for the pending switch and pruning.</summary>
    private sealed class Probe(UnrealService owner) : IGameProcessProbe
    {
        public IReadOnlyList<RunningGame> Running() => owner.RunningGames();
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
