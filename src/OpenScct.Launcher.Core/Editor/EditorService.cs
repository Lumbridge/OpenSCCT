using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using OpenScct.Maps;

namespace OpenScct.Launcher.Editor;

/// <summary>Installs an optional editor once, then delegates updates to its own supported CLI protocol.</summary>
public sealed partial class EditorService
{
    public const string ExeName = "OpenSCCT Editor.exe";
    public const string ManagedMarker = ".launcher-managed";
    public const string ReleasesPage = "https://github.com/Lumbridge/OpenSCCT-Editor/releases";
    public static readonly SemVersion MinimumGameVersion = SemVersion.Parse("0.8.93");
    private readonly LauncherLayout _layout;
    private readonly HttpClient _http;
    private readonly GitHubReleaseClient _github;
    private readonly ResumableDownloader _downloader;
    internal Func<string, SemVersion, CancellationToken, Task>? ProbeOverride { get; set; }
    internal Func<string, bool>? RunningOverride { get; set; }

    public EditorService(LauncherLayout layout, HttpClient http)
    {
        _layout = layout;
        _http = http;
        _github = new GitHubReleaseClient(http, new ReleaseSource("Lumbridge", "OpenSCCT-Editor", null), layout.EditorCache);
        _downloader = new ResumableDownloader(http);
    }

    public EditorInstallation? Installed()
    {
        try
        {
            if (!File.Exists(Path.Combine(_layout.Editor, ExeName)) || !File.Exists(Path.Combine(_layout.Editor, "ose", "ose.exe"))) return null;
            return EditorManifest.Read(_layout.Editor).Installation;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    public async Task<EditorCheck> CheckAsync(bool includePrereleases, CancellationToken cancel)
    {
        ReleaseFeed feed = await _github.GetReleasesAsync(cancel).ConfigureAwait(false);
        EditorInstallation? installed = Installed();
        bool previews = includePrereleases || installed?.Version.IsPrerelease == true;
        EditorOffer? latest = feed.Releases.Select(Offer).OfType<EditorOffer>()
            .Where(o => previews || (!o.Release.Release.Prerelease && !o.Version.IsPrerelease))
            .OrderByDescending(o => o.Version).FirstOrDefault();
        return new EditorCheck(feed, latest, installed, latest is not null && (installed is null || latest.Version > installed.Version));
    }

    public static EditorOffer? Offer(ReleaseInfo release)
    {
        if (release.Draft || !SemVersion.TryParse(release.TagName, out SemVersion? version)) return null;
        string name = $"OpenSCCT-Editor-{version}-win-x64.zip";
        ReleaseAsset? archive = release.Assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        ReleaseAsset? metadata = release.Assets.FirstOrDefault(a => a.Name.Equals(name[..^4] + ".launcher.json", StringComparison.OrdinalIgnoreCase));
        var vr = new VersionedRelease(version, release);
        ReleaseAsset? checksum = archive is null ? null : ReleasePicker.ChecksumAsset(vr, archive);
        return archive is null || checksum is null || metadata is null ? null : new(vr, archive, checksum, metadata);
    }

    /// <summary>Missing prerequisites only; optional updates do not prevent offline use.</summary>
    public string? PrerequisiteProblem()
    {
        SemVersion minimum = Installed()?.MinimumGameVersion ?? MinimumGameVersion;
        SemVersion? game = new VersionStore(_layout).Current();
        if (game is null || game < minimum) return $"Editor needs OpenSCCT {minimum} or later. Set up Editor to install the game and its shared files.";
        if (!PackedContent.Installed(new ContentStore(_layout.Packs, _http)) || !File.Exists(Path.Combine(_layout.Packs, "shared", "classes.json")))
            return "Editor needs shared game files. Set up Editor to download them.";
        string maps = Path.Combine(_layout.Packs, "svmap");
        if (!Directory.Exists(maps) || !Directory.EnumerateDirectories(maps).Any(d => File.Exists(Path.Combine(d, "manifest.json"))))
            return "Editor needs downloaded map assets. Set up Editor to download the stock maps.";
        return null;
    }

    public bool IsRunning => (RunningOverride?.Invoke(_layout.Editor) ?? RunningIn(_layout.Editor)) || UpdateInProgress;

    public bool NeedsRecovery => File.Exists(Path.Combine(_layout.Editor, ".update", "journal.json"));

    public bool UpdateInProgress
    {
        get
        {
            string path = Path.Combine(_layout.Editor, ".update", "swap.lock");
            if (!File.Exists(path)) return false;
            try { using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
            catch (FileNotFoundException) { return false; }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
    }

    internal static bool RunningIn(string folder)
    {
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    string? exe = process.MainModule?.FileName;
                    if (exe is not null && exe.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // A known editor whose path cannot be inspected is conservatively considered in use.
                    try { if (process.ProcessName is "OpenSCCT Editor" or "ose") return true; }
                    catch (InvalidOperationException) { }
                }
            }
        }
        return false;
    }

    public ProcessStartInfo StartInfo()
    {
        if (Installed() is null) throw new InvalidOperationException("Install the editor first.");
        if (PrerequisiteProblem() is { } problem) throw new InvalidOperationException(problem);
        if (UpdateInProgress) throw new InvalidOperationException("The editor updater is finishing. Wait for it to complete before opening the editor.");
        if (NeedsRecovery)
            throw new InvalidOperationException("An editor update needs to finish recovery before opening. Check for updates and try again.");
        return CreateStartInfo(Path.Combine(_layout.Editor, ExeName), _layout.Editor);
    }

    private ProcessStartInfo CreateStartInfo(string exe, string directory)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true };
        foreach (string key in new[] { "OSE_GAME_EXE", "OPENSCCT_EXE", "OSE_CONTENT", "OSE_OPENSCCT", "OSE_OPENSCCT_GIT", "SVM_CONTENT", "SVM_GAME_ROOT" }) info.Environment.Remove(key);
        info.Environment["OSE_LAUNCHER_ROOT"] = _layout.Root;
        return info;
    }

    public async Task InstallOrUpdateAsync(EditorOffer offer, IProgress<EditorProgress>? progress, CancellationToken cancel)
    {
        if (!_layout.Platform.Equals(LauncherPlatform.WindowsX64)) throw new InvalidOperationException("Editor installation is currently supported on Windows only.");
        if (NeedsRecovery) await RecoverAsync(progress, cancel).ConfigureAwait(false);
        EditorInstallation? installed = Installed();
        if (installed is not null)
        {
            if (offer.Version <= installed.Version) return;
            if (!File.Exists(Path.Combine(_layout.Editor, ManagedMarker)))
                throw new InvalidOperationException("This editor folder is not managed by the launcher. Keep it separate from the launcher installation.");
            if (IsRunning) throw new InvalidOperationException("Save your work and close the editor and its command-line tools before updating here. The editor can also update itself.");
            await UpdateAsync(offer, progress, cancel).ConfigureAwait(false);
            return;
        }

        if (Directory.Exists(_layout.Editor))
            throw new InvalidOperationException("The editor folder exists but is incomplete. Restore its interrupted update or move that folder aside before reinstalling; your files have been kept.");

        progress?.Report(new("Downloading editor"));
        string metadata = await SmallAssetAsync(offer.Metadata, cancel).ConfigureAwait(false);
        ValidateMetadata(metadata);
        string expected = Checksums.FindExpected(await SmallAssetAsync(offer.Checksum, cancel).ConfigureAwait(false), offer.Archive.Name)
            ?? throw new InvalidDataException("The editor archive has no valid published checksum.");
        string zip = Path.Combine(_layout.EditorDownloads, offer.Archive.Name);
        await _downloader.DownloadAsync(() => _github.CreateAssetRequest(offer.Archive), zip,
            new(offer.Archive.Id, offer.Archive.Size, offer.Archive.UpdatedAt),
            new InlineProgress<DownloadProgress>(p => progress?.Report(new("Downloading editor", p.Received, p.Total, p.BytesPerSecond))), cancel).ConfigureAwait(false);
        if (!await Checksums.VerifyAsync(zip, expected, cancel).ConfigureAwait(false))
        {
            File.Delete(zip);
            throw new InvalidDataException("The editor download did not match its published checksum. Try again.");
        }
        Directory.CreateDirectory(_layout.Root);
        string staging = Path.Combine(_layout.Root, ".editor-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            progress?.Report(new("Verifying editor"));
            await ExtractAndValidateAsync(zip, staging, offer.Version, cancel).ConfigureAwait(false);
            EditorManifest package = EditorManifest.Read(staging);
            using (JsonDocument meta = JsonDocument.Parse(metadata))
            {
                if (package.MinimumGameVersion != meta.RootElement.GetProperty("minimumGameVersion").GetString())
                    throw new InvalidDataException("Editor package requirements do not match its release metadata.");
            }
            File.WriteAllText(Path.Combine(staging, ManagedMarker), "1");
            progress?.Report(new("Checking editor startup"));
            await (ProbeOverride is { } probe ? probe(staging, offer.Version, cancel) : ProbeAsync(staging, offer.Version, cancel)).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            Directory.Move(staging, _layout.Editor);
            progress?.Report(new("Editor installed"));
        }
        finally
        {
            if (Directory.Exists(staging)) SafeDelete.Tree(staging, _layout.Root);
        }
    }

    private static void ValidateMetadata(string text)
    {
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(text); }
        catch (JsonException ex) { throw new InvalidDataException("Editor release metadata could not be read.", ex); }
        using JsonDocument doc = parsed;
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("launcherProtocol", out JsonElement protocol) || protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out int value) || value != 1
            || !doc.RootElement.TryGetProperty("minimumGameVersion", out JsonElement minimum) || minimum.ValueKind != JsonValueKind.String
            || !SemVersion.TryParse(minimum.GetString(), out _))
            throw new InvalidDataException("This editor release does not support this launcher's integration protocol.");
    }

    private async Task<string> SmallAssetAsync(ReleaseAsset asset, CancellationToken cancel)
    {
        using HttpRequestMessage request = _github.CreateAssetRequest(asset);
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("Editor release metadata is too large.");
        await using Stream input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + count > 65536) throw new InvalidDataException("Editor release metadata is too large.");
            memory.Write(buffer, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    internal static async Task ExtractAndValidateAsync(string zip, string folder, SemVersion expected, CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancel.ThrowIfCancellationRequested();
                string name = entry.FullName;
                const string prefix = "OpenSCCT Editor/";
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Editor archive must contain one OpenSCCT Editor folder.");
                name = name[prefix.Length..];
                if (name.Length == 0) continue;
                bool directory = name.EndsWith('/');
                string relative = directory ? name.TrimEnd('/') : name;
                if (!EditorManifest.SafeRelative(relative) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new InvalidDataException("The editor archive contains an unsafe path.");
                if (!directory && !extracted.Add(relative)) throw new InvalidDataException("The editor archive contains duplicate files.");
                string target = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
                if (directory) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using Stream source = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(output, cancel).ConfigureAwait(false);
            }
        }
        EditorManifest manifest = EditorManifest.Read(folder);
        if (manifest.Installation.Version != expected) throw new InvalidDataException("The editor package version does not match its release.");
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "version.json" };
        foreach (EditorFile file in manifest.Files)
        {
            if (file is null || !EditorManifest.SafeRelative(file.Path) || file.Size < 0 || string.IsNullOrEmpty(file.Sha256) || !Checksums.IsSha256Hex(file.Sha256) || !listed.Add(file.Path)
                || !manifest.Entries.Contains(file.Path.Split('/')[0], StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("The editor file manifest is invalid.");
            string path = Path.Combine(folder, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size || !await Checksums.VerifyAsync(path, file.Sha256, cancel).ConfigureAwait(false))
                throw new InvalidDataException("An editor package file is missing or damaged: " + file.Path);
        }
        if (!listed.SetEquals(extracted) || !new HashSet<string>(manifest.Entries, StringComparer.OrdinalIgnoreCase).SetEquals(extracted.Select(p => p.Split('/')[0]))
            || !listed.Contains(ExeName) || !listed.Contains("ose/ose.exe")
            || !listed.Contains("data_OpenScctEditor_windows_x86_64/OpenScctEditor.dll"))
            throw new InvalidDataException("The editor package is incomplete or contains unlisted files.");
    }

    private async Task ProbeAsync(string folder, SemVersion version, CancellationToken cancel)
    {
        string answer = Path.Combine(folder, ".update", "bootstrap-probe.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(answer)!);
        ProcessStartInfo start = CreateStartInfo(Path.Combine(folder, ExeName), folder);
        foreach (string argument in new[] { "--headless", "--", "--update-probe=" + answer }) start.ArgumentList.Add(argument);
        using Process process = StartProcess(start, "The editor startup check did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Only this private headless probe is terminated, never a user's editor session.
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            cancel.ThrowIfCancellationRequested();
            throw new IOException("The editor startup check timed out. The editor was not installed.");
        }
        if (process.ExitCode != 0 || !File.Exists(answer) || !SemVersion.TryParse(File.ReadAllText(answer).Trim(), out SemVersion? actual) || actual != version)
            throw new IOException("The downloaded editor did not pass its startup check. The editor was not installed.");
        File.Delete(answer);
    }

    private async Task UpdateAsync(EditorOffer offer, IProgress<EditorProgress>? progress, CancellationToken cancel)
    {
        ValidateMetadata(await SmallAssetAsync(offer.Metadata, cancel).ConfigureAwait(false));
        Directory.CreateDirectory(_layout.EditorDownloads);
        string cancellation = Path.Combine(_layout.EditorDownloads, "cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            progress?.Report(new("Downloading editor update"));
            using (cancel.Register(() => { try { File.WriteAllText(cancellation, "cancel"); } catch (IOException) { } catch (UnauthorizedAccessException) { } }))
            {
                await RunCliAsync(["stage", "--version", offer.Version.ToString(), "--cancel-file", cancellation], progress, canCancel: true).ConfigureAwait(false);
            }
            cancel.ThrowIfCancellationRequested();
            if (IsRunning) throw new InvalidOperationException("The editor update is ready. Save your work and close the editor and its command-line tools, then update again.");
            progress?.Report(new("Installing editor update", CanCancel: false));
            string helper = Path.Combine(_layout.EditorDownloads, "cli-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyCli(Path.Combine(_layout.Editor, "ose"), helper);
                await RunCliAsync(["install-staged", "--version", offer.Version.ToString()], progress, canCancel: false,
                    Path.Combine(helper, "ose.exe")).ConfigureAwait(false);
            }
            finally { if (Directory.Exists(helper)) SafeDelete.Tree(helper, _layout.EditorDownloads); }
            if (Installed() is not { } finished || finished.Version < offer.Version)
                throw new IOException("The editor updater did not finish on the requested version. Check the editor update log.");
        }
        finally { if (File.Exists(cancellation)) File.Delete(cancellation); }
    }

    private static void CopyCli(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("The editor helper contains a linked file.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("The editor helper contains a linked folder.");
            CopyCli(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    /// <summary>Recover an interrupted swap without requiring a newer release or a working installed executable.</summary>
    public async Task RecoverAsync(IProgress<EditorProgress>? progress, CancellationToken cancel)
    {
        if (!NeedsRecovery) return;
        cancel.ThrowIfCancellationRequested();
        if (IsRunning) throw new InvalidOperationException("Wait for the editor updater to finish, or close the editor before recovering its installation.");
        string source = RecoveryCli(_layout.Editor)
            ?? throw new IOException("The interrupted editor update has no usable recovery helper. Keep the editor folder and restore its release package before retrying.");
        string helper = Path.Combine(_layout.EditorDownloads, "recovery-" + Guid.NewGuid().ToString("N"));
        bool started = false, recovered = false;
        try
        {
            CopyCli(source, helper);
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new("Recovering editor update", CanCancel: false));
            ProcessStartInfo start = CreateStartInfo(Path.Combine(helper, "ose.exe"), helper);
            foreach (string argument in new[] { "update", "recover", "--install", _layout.Editor, "--no-restart" }) start.ArgumentList.Add(argument);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using Process process = StartProcess(start, "The editor recovery helper did not start.");
            started = true;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await output.ConfigureAwait(false);
            string detail = await error.ConfigureAwait(false);
            if (process.ExitCode == 2) throw new InvalidOperationException("Another editor updater is recovering this installation. Wait for it to finish.");
            if (process.ExitCode != 0 || NeedsRecovery || Installed() is null)
                throw new IOException(detail.Length > 0 ? detail.Trim() : "The editor could not recover the interrupted update. Its files have been kept.");
            recovered = true;
            progress?.Report(new("Editor recovered"));
        }
        // A failed recovery may register this executable to retry at sign-in. Keep it until recovery succeeds.
        finally { if ((!started || recovered) && Directory.Exists(helper)) SafeDelete.Tree(helper, _layout.EditorDownloads); }
    }

    internal static string? RecoveryCli(string install)
    {
        // Do not trust journal paths: only consider known package locations under this installation.
        var candidates = new List<string> { Path.Combine(install, "ose") };
        string update = Path.Combine(install, ".update");
        if (Directory.Exists(update))
        {
            candidates.AddRange(Directory.EnumerateDirectories(update)
                .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(d => Path.Combine(d, "ose")));
        }
        return candidates.FirstOrDefault(c => Directory.Exists(c) && (File.GetAttributes(c) & FileAttributes.ReparsePoint) == 0
            && File.Exists(Path.Combine(c, "ose.exe")) && (File.GetAttributes(Path.Combine(c, "ose.exe")) & FileAttributes.ReparsePoint) == 0);
    }

    private async Task RunCliAsync(string[] arguments, IProgress<EditorProgress>? progress, bool canCancel, string? helperExe = null)
    {
        string executable = helperExe ?? Path.Combine(_layout.Editor, "ose", "ose.exe");
        ProcessStartInfo start = CreateStartInfo(executable, Path.GetDirectoryName(executable)!);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.ArgumentList.Add("update");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--install"); start.ArgumentList.Add(_layout.Editor); start.ArgumentList.Add("--json");
        using Process process = StartProcess(start, "The editor updater did not start.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string? status = null, problem = null;
        while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(line);
                JsonElement root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String) continue;
                if (type.GetString() == "progress")
                {
                    long received = root.TryGetProperty("received", out JsonElement r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out long rv) ? rv : 0;
                    long total = root.TryGetProperty("total", out JsonElement t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out long tv) ? tv : 0;
                    double rate = root.TryGetProperty("bytesPerSecond", out JsonElement b) && b.ValueKind == JsonValueKind.Number && b.TryGetDouble(out double bv) ? bv : 0;
                    progress?.Report(new(canCancel ? "Downloading editor update" : "Installing editor update", received, total, rate, canCancel));
                }
                else if (type.GetString() == "result")
                {
                    status = root.TryGetProperty("status", out JsonElement state) && state.ValueKind == JsonValueKind.String ? state.GetString() : null;
                    problem = root.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null;
                    problem ??= root.TryGetProperty("problem", out JsonElement issue) && issue.ValueKind == JsonValueKind.String ? issue.GetString() : null;
                }
            }
            catch (JsonException) { /* Older helper logs may accompany the structured terminal result. */ }
        }
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        string stderr = await error.ConfigureAwait(false);
        if (status == "cancelled") throw new OperationCanceledException("Editor update cancelled.");
        if (process.ExitCode == 2 || status == "busy") throw new InvalidOperationException(problem ?? "Another editor update is running. Let it finish, then try again.");
        if (process.ExitCode != 0 || status is not ("staged" or "current" or "installed"))
            throw new IOException(problem ?? (stderr.Length > 0 ? stderr.Trim() : "The editor update did not complete. Your existing files were kept."));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static Process StartProcess(ProcessStartInfo start, string problem)
    {
        try { return Process.Start(start) ?? throw new IOException(problem); }
        catch (Win32Exception ex) { throw new IOException(problem + " " + ex.Message, ex); }
    }
}
