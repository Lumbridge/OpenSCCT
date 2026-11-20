using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Retail;
using OpenScct.Launcher.Versioning;
using OpenScct.Maps;

namespace OpenScct.Launcher.Content;

/// <summary>Builds Godot's base game content in a fresh generation from Steam app 13570.</summary>
public sealed class GodotSteamContent(LauncherLayout layout)
{
    public static SemVersion MinimumGameVersion { get; } = SemVersion.Parse("0.8.166");
    public static bool SupportsClient(SemVersion? version) => version >= MinimumGameVersion;

    private readonly object _cache = new();
    private long _validatedAt;
    private DateTime _pointerTime;
    private string? _root;
    private bool _validated;

    public static string? FindSource(string? chosenFolder = null)
    {
        var install = string.IsNullOrWhiteSpace(chosenFolder) ? RetailGame.FindSteam() : RetailGame.Inspect(chosenFolder);
        if (install is not { HasVersus: true }) return null;
        var versus = new FileInfo(install.Executable).Directory?.Parent;
        return versus?.Parent is { } game && SteamLibraryManifest.IsManifestInstall(game) ? versus.FullName : null;
    }

    public string? ActiveRoot()
    {
        lock (_cache)
        {
            // Both desktop clients poll readiness while drawing their libraries. Limit filesystem probes to once a second.
            DateTime pointerTime = File.GetLastWriteTimeUtc(Path.Combine(layout.Content, SteamContentInstall.PointerFile));
            if (!_validated || pointerTime != _pointerTime || Environment.TickCount64 - _validatedAt >= 1000)
            {
                _root = SteamContentInstall.ActiveRoot(layout.Content);
                _validatedAt = Environment.TickCount64;
                _pointerTime = pointerTime;
                _validated = true;
            }
            return _root;
        }
    }

    public async Task<string> BuildAsync(string versionDirectory, string steamVersusFolder, IProgress<ContentProgress>? progress,
        CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamVersusFolder);
        string source = FindSource(steamVersusFolder)
            ?? throw new InvalidOperationException("Choose the installed Steam copy of Splinter Cell Chaos Theory (app 13570).");
        foreach (string code in SteamVersusMaps.All)
        {
            if (!File.Exists(Path.Combine(source, "Packages", "Maps", code + ".sdc")))
                throw new InvalidOperationException($"The Steam installation is missing {code}.sdc. Verify the game's files in Steam, then rebuild.");
        }
        var gameRoot = Directory.GetParent(source)!.FullName;
        if (ContainsPath(gameRoot, layout.Content) || ContainsPath(layout.Content, gameRoot))
            throw new InvalidOperationException("Choose a launcher data folder separate from your Steam installation.");
        string tool = await SteamBuildTools.ResolveAsync(versionDirectory, layout, cancel).ConfigureAwait(false);
        string python = layout.Platform.IsLinux ? Path.Combine(tool, "python", "bin", "python3") : Path.Combine(tool, "python", "python.exe");
        string version = new DirectoryInfo(versionDirectory).Name;
        return await PublishAsync(async (output, token) =>
        {
            string logPath = Path.Combine(layout.Content, "steam-build.log");
            using var log = new StreamWriter(logPath) { AutoFlush = true };
            var state = await ContentBuilder.RunAsync(python, version, source, output, ContentBuildPlan.DefaultJobs, progress,
                log.WriteLine, token, steamBase: true, toolDirectory: Path.Combine(tool, "repo", "content")).ConfigureAwait(false);
            if (!state.Ok)
                throw new InvalidOperationException($"Building content from Steam failed. Details are in {logPath}.");
        }, cancel).ConfigureAwait(false);
    }

    internal async Task<string> PublishAsync(Func<string, CancellationToken, Task> convert, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        string builds = Path.Combine(layout.Content, SteamContentInstall.BuildsDirectory);
        Directory.CreateDirectory(builds);
        using var buildLock = AcquireBuildLock();
        string? previous = SteamContentInstall.ActiveRoot(layout.Content);
        // Retain the active generation throughout conversion. Reclaim earlier or interrupted builds before writing gigabytes.
        if (!ContentInUse())
        {
            foreach (string dir in Directory.EnumerateDirectories(builds))
                if (dir != previous && Guid.TryParseExact(Path.GetFileName(dir), "N", out _)) TryDelete(dir, builds);
        }
        string id = Guid.NewGuid().ToString("N");
        string output = Path.Combine(builds, id);
        Directory.CreateDirectory(output);
        bool published = false;
        try
        {
            await convert(output, cancel).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            if (SteamContentInstall.MissingFile(output) is { } missing)
                throw new InvalidDataException("Steam conversion did not produce " + missing + ". Check the build log and verify the original installation.");
            AtomicFile.WriteAllText(Path.Combine(layout.Content, SteamContentInstall.PointerFile),
                JsonSerializer.Serialize(new { format = SteamContentInstall.Format, build = id }));
            published = true;
            lock (_cache) _validated = false;
            return output;
        }
        finally
        {
            if (!published) TryDelete(output, builds);
        }
    }

    private FileStream AcquireBuildLock()
    {
        try { return new FileStream(Path.Combine(layout.Content, "steam-build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Could not start the Godot content build. Another launcher may already be building it.", ex); }
    }

    private bool ContentInUse()
    {
        if (new GameProcessProbe(layout.Platform).Running().Count > 0) return true;
        foreach (string name in new[] { "OpenSCCT Editor", "ose", "OpenSCCTUnreal", "OpenSCCTUnreal-Win64-Shipping" })
        {
            var processes = Process.GetProcessesByName(name);
            bool found = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (found) return true;
        }
        return false;
    }

    private static bool ContainsPath(string parent, string path)
    {
        string relative = Path.GetRelativePath(parent, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static void TryDelete(string dir, string builds)
    {
        try { SafeDelete.Tree(dir, builds); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A running reader can still hold the retired generation. */ }
    }
}
