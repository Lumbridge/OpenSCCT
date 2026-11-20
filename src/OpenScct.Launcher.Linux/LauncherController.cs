using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using OpenScct.Maps;

namespace OpenScct.Launcher.Linux;

internal enum LauncherAction { ChooseSteam, Install, Update, FinishInstall, Play, Cancel, Waiting }

/// <summary>The Linux UI uses exactly the same release, download, content and install services as Windows.</summary>
internal sealed class LauncherController : IDisposable
{
    private readonly HttpClient _http;
    private readonly IGameProcessProbe _games;
    private readonly UpdateService _updates;
    private readonly LauncherSwap? _swap;
    private readonly GodotSteamContent _steamContent;
    private CancellationTokenSource? _work;
    private Task _activeWork = Task.CompletedTask;
    private bool _disposed;

    public LauncherController(LauncherLayout layout, HttpClient http, string? executable,
        IGameProcessProbe? games = null, bool discoverSteam = true)
    {
        Layout = layout;
        _http = http;
        _games = games ?? new GameProcessProbe(layout.Platform);
        Store = new VersionStore(layout);
        Packs = new ContentStore(layout.Packs, http);
        Settings = LauncherSettings.Load(layout.Settings);
        _steamContent = new GodotSteamContent(layout);
        SteamSource = discoverSteam ? GodotSteamContent.FindSource(Settings.GodotSteamFolder) : null;
        _updates = new UpdateService(layout, Store, new GitHubReleaseClient(http, ReleaseSource.Official, layout.Cache), http, _games);
        Executable = executable;
        if (executable is not null) _swap = new LauncherSwap(executable, layout.Platform);
        string? info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        LauncherVersion = SemVersion.TryParse(info, out SemVersion? version) ? version : null;
    }

    public event Action? Changed;
    public event Action? GameStarted;
    public event Action? Restarted;
    public LauncherLayout Layout { get; }
    public VersionStore Store { get; }
    public ContentStore Packs { get; }
    public string? SteamSource { get; private set; }
    public LauncherSettings Settings { get; private set; }
    public SemVersion? LauncherVersion { get; }
    public string? Executable { get; }
    public UpdateCheck? Check { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public bool IsError { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public double? Progress { get; private set; }
    public bool Busy => _work is not null;
    public bool GameRunning => _updates.GameRunning;
    public bool HasLauncherUpdate => _swap?.HasStaged == true || Check?.LauncherUpdateAvailable == true;
    public bool LauncherStaged => _swap?.HasStaged == true;
    public bool CanUpdateLauncher => _swap?.CanWriteFolder() == true;
    public bool ContentReady => _steamContent.ActiveRoot() is not null;
    public bool ClientUpdateRequired => Store.Current() is { } current && !GodotSteamContent.SupportsClient(current);
    public bool Playable => GodotSteamContent.SupportsClient(Store.Current()) && ContentReady;
    public string? PendingJoin { get; private set; }
    public LauncherAction Next => Busy ? LauncherAction.Cancel
        : Store.Pending() is not null ? LauncherAction.Waiting
        : ClientUpdateRequired ? LauncherAction.Update
        : !ContentReady && SteamSource is null ? LauncherAction.ChooseSteam
        : Store.Current() is null ? LauncherAction.Install
        : Check?.GameUpdateAvailable == true ? LauncherAction.Update
        : !ContentReady ? LauncherAction.FinishInstall : LauncherAction.Play;

    public void Report(string message, bool error = false)
    {
        Status = message;
        IsError = error;
        Notify();
    }

    public void Cancel() => _work?.Cancel();
    public Task WaitForWorkAsync() => _activeWork;

    public Task InitializeAsync(bool network)
    {
        _updates.TryFinishPending();
        Notify();
        return network && Settings.CheckOnStart ? RunAsync("Checking for updates", CheckAsync) : Task.CompletedTask;
    }

    public Task RunAsync(string operation, Func<CancellationToken, Task> work)
    {
        if (Busy || _disposed) return Task.CompletedTask;
        _activeWork = RunCoreAsync(operation, work);
        return _activeWork;
    }

    private async Task RunCoreAsync(string operation, Func<CancellationToken, Task> work)
    {
        using var cancel = new CancellationTokenSource();
        _work = cancel;
        Operation = operation;
        IsError = false;
        Progress = null;
        Notify();
        try { await work(cancel.Token); }
        catch (OperationCanceledException) { Report("Stopped. You can continue setup when you are ready."); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException
            or InvalidOperationException or UpdateRejectedException or Win32Exception or System.Text.Json.JsonException)
        { Report(ex.Message, true); }
        finally
        {
            _work = null;
            Operation = string.Empty;
            Progress = null;
            Notify();
        }
    }

    public async Task CheckAsync(CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        Check = await _updates.CheckAsync(Settings.IncludePrereleases, LauncherVersion, timeout.Token);
        Notify();
        if (Check.LauncherUpdateAvailable && _swap is { HasStaged: false } && _swap.CanWriteFolder())
        {
            try { await StageLauncherAsync(cancel); }
            catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or UpdateRejectedException)
            { Report("The launcher could not update itself: " + ex.Message, true); return; }
        }
        if (Check.Feed.Problem is { } problem) Report(problem);
        else if (Check.Latest is null && Store.Current() is null)
            Report("A native Linux game build has not been published on the selected release channel yet. Check again after the Linux release is available.");
        else if (Check.GameUpdateAvailable) Report("An update is available.");
        else Report(Store.Current() is null ? "Ready to install." : "Your installation is up to date.");
    }

    public async Task InstallAsync(CancellationToken cancel)
    {
        bool updatingOlderClient = ClientUpdateRequired;
        if (!updatingOlderClient && !ContentReady && SteamSource is null)
            throw new InvalidOperationException("Choose your Steam installation before installing OpenSCCT Godot.");
        if (Check is null || (Store.Current() is null || updatingOlderClient) && !Check.GameUpdateAvailable) await CheckAsync(cancel);
        if (Check is { GameUpdateAvailable: true, Latest: { } latest } && Store.Pending() is null)
        {
            if (!GodotSteamContent.SupportsClient(latest.Version))
                throw new InvalidOperationException("This release predates Steam content builds. Check for a newer Linux release.");
            Operation = "Downloading OpenSCCT " + latest.Version;
            Notify();
            InstallOutcome outcome = await _updates.InstallAsync(latest, DownloadReporter(), cancel);
            Check = Check with { Installed = Store.Current(), GameUpdateAvailable = false };
            if (outcome == InstallOutcome.WaitingForGameToClose)
            {
                Report("The new build is installed. Close the running game to finish the update.");
                return;
            }
        }
        if (Store.Current() is null)
            throw new InvalidOperationException("No native Linux game build is available on this release channel yet. No Windows build was downloaded.");
        if (ClientUpdateRequired) throw new InvalidOperationException("This client predates Steam content builds. Connect to the release channel and update Godot.");
        if (!ContentReady && SteamSource is null) Report("Choose your Steam installation to build the game content.");
        else if (!ContentReady) await InstallContentAsync(cancel);
        else Report("OpenSCCT Godot is ready to play.");
    }

    public void SelectSteam(string folder)
    {
        if (Busy || GameRunning) throw new InvalidOperationException("Finish the current operation and close Godot before changing its Steam source.");
        string source = GodotSteamContent.FindSource(folder) ?? throw new ArgumentException("Choose the stock Chaos Theory installation from Steam (app 13570).");
        SaveSettings(settings => settings.GodotSteamFolder = source);
        SteamSource = source;
        Report("Steam installation selected.");
    }

    public async Task InstallContentAsync(CancellationToken cancel)
    {
        if (GameRunning) throw new InvalidOperationException("Close Godot before building its game content.");
        string source = SteamSource ?? throw new InvalidOperationException("Choose the installed Steam copy of Chaos Theory first.");
        SemVersion current = Store.Current() ?? throw new InvalidOperationException("Install the Godot client before building Steam content.");
        if (!GodotSteamContent.SupportsClient(current)) throw new InvalidOperationException("Update Godot before building content from Steam.");
        Operation = "Building Steam content";
        Progress = null;
        Notify();
        var progress = new Progress<ContentProgress>(p =>
        {
            if (!Busy || cancel.IsCancellationRequested) return;
            Progress = p.Total > 0 ? (double)p.Done / p.Total : null;
            Report(p.Line);
        });
        await _steamContent.BuildAsync(Layout.VersionDir(current.ToString()), source, progress, cancel);
        Report("Steam content built. OpenSCCT Godot is ready to play.");
    }

    public async Task PlayAsync(bool skipUpdateCheck, Func<UpdateCheck, Task<PlayUpdateChoice>> choose, CancellationToken cancel)
    {
        string contentRoot = _steamContent.ActiveRoot() ?? throw new InvalidOperationException("Build game content from Steam first.");
        if (!GodotSteamContent.SupportsClient(Store.Current())) throw new InvalidOperationException("Install or update Godot before playing with Steam content.");
        if (!skipUpdateCheck)
        {
            UpdateCheck? check = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                check = await _updates.CheckAsync(Settings.IncludePrereleases, LauncherVersion, timeout.Token);
                Check = check;
                Notify();
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException) { }
            cancel.ThrowIfCancellationRequested();
            if (check is { GameUpdateAvailable: true, Feed.Source: ReleaseFeedSource.Network or ReleaseFeedSource.NotModified })
            {
                PlayUpdateChoice choice = await choose(check);
                cancel.ThrowIfCancellationRequested();
                if (choice == PlayUpdateChoice.Cancel) return;
                if (choice == PlayUpdateChoice.Update) { await InstallAsync(cancel); return; }
            }
        }
        string? join = PendingJoin;
        cancel.ThrowIfCancellationRequested();
        if (join is not null && await WebJoinPipe.SendAsync(WebJoinPipe.GameName, join))
        {
            PendingJoin = null;
            Report("Join request sent. Continue in OpenSCCT; an active match will not be interrupted.");
            GameStarted?.Invoke();
            return;
        }
        if (GameRunning) throw new InvalidOperationException("OpenSCCT is already running. Continue in the game, or close it before launching again.");
        SemVersion current = Store.Current()!;
        if (join is not null && current < WebJoinRequest.MinimumGameVersion)
            throw new InvalidOperationException("Update OpenSCCT before using website Join links.");
        cancel.ThrowIfCancellationRequested();
        using Process? process = Process.Start(GameStart.Create(Store.ExePath(current),
            join is null ? null : [WebJoinRequest.GameArgumentPrefix + join], contentRoot: contentRoot));
        if (process is null) throw new IOException("The game could not be started.");
        PendingJoin = null;
        Report("Started OpenSCCT " + current + ".");
        GameStarted?.Invoke();
    }

    public void QueueJoin(string serverId)
    {
        if (!WebJoinRequest.IsValidServerId(serverId)) return;
        PendingJoin = serverId;
        Report(Busy ? "Your Join request is waiting for the current operation to finish."
            : Playable ? "Your Join request is ready. Choose JOIN SESSION." : "Install OpenSCCT to continue with your Join request.");
    }

    public void Tick()
    {
        if (Busy) return;
        if (_updates.TryFinishPending() is InstallOutcome.Activated) Report("The new game build is ready.");
        else Notify();
    }

    public async Task StageLauncherAsync(CancellationToken cancel)
    {
        if (_swap is null || Check?.Latest is not { } latest) return;
        if (!_swap.CanWriteFolder()) throw new IOException("The launcher folder is read only. Move the launcher to a folder you own to enable self-updates.");
        Operation = "Updating launcher";
        await _updates.StageLauncherAsync(latest, _swap, DownloadReporter(), cancel);
        Report("A launcher update is ready. Choose Restart to finish.");
    }

    public void RestartLauncher()
    {
        if (_swap is not { HasStaged: true }) return;
        string target = _swap.Swap();
        var start = new ProcessStartInfo(target) { UseShellExecute = false };
        start.ArgumentList.Add(Program.AfterUpdateArg);
        if (PendingJoin is { } join) start.ArgumentList.Add(WebJoinRequest.UriPrefix + join);
        try
        {
            using Process? process = Process.Start(start);
            if (process is null) throw new IOException("The updated launcher did not start.");
            Restarted?.Invoke();
        }
        catch
        {
            // Keep a usable launcher at the original path when the replacement cannot execute.
            if (File.Exists(_swap.OldPath))
            {
                File.Move(target, _swap.NewPath, overwrite: true);
                File.Move(_swap.OldPath, _swap.ExePath, overwrite: true);
            }
            throw;
        }
    }

    public void SaveSettings(bool checkOnStart, bool previews)
    {
        SaveSettings(settings =>
        {
            settings.CheckOnStart = checkOnStart;
            settings.IncludePrereleases = previews;
        });
        Check = null;
        Notify();
    }

    public void SaveSettings(Action<LauncherSettings> edit)
    {
        LauncherSettings changed = Settings.Copy();
        edit(changed);
        changed.Save(Layout.Settings);
        Settings = changed;
    }

    public void Reset()
    {
        if (Busy || GameRunning) throw new InvalidOperationException("Close OpenSCCT and finish downloads before resetting the installation.");
        IReadOnlyList<string> failed = InstallReset.Run(Layout, Executable);
        Check = null;
        Report(failed.Count == 0 ? "Installation reset. Game settings and saves were kept."
            : "Some files could not be removed: " + string.Join(", ", failed), failed.Count != 0);
    }

    public void Rollback()
    {
        bool switched = _updates.Rollback();
        Check = null;
        Report(switched ? "Switched to the previous installed game build." : "No previous build is available.");
    }

    public async Task<ContentIndex> LoadMapsAsync(CancellationToken cancel)
    {
        ContentIndex index = await Packs.FetchIndexAsync(cancel);
        if (index.Problem is { } problem) Report(problem);
        return index;
    }

    public async Task DownloadMapsAsync(IEnumerable<ContentMap> maps, CancellationToken cancel)
    {
        if (GameRunning) throw new InvalidOperationException("Close OpenSCCT before changing its downloaded maps.");
        foreach (ContentMap map in maps)
        {
            if (SteamVersusMaps.Contains(map.Code)) throw new InvalidOperationException("Build original maps from your Steam installation.");
            await Packs.InstallMapAsync(map, ContentKind.Full, PackReporter(map.DisplayTitle), cancel);
        }
        Report("Selected maps installed.");
    }

    public void RemoveMaps(IEnumerable<string> codes)
    {
        if (Busy || GameRunning) throw new InvalidOperationException("Close OpenSCCT and finish downloads before removing maps.");
        foreach (string code in codes)
            if (!SteamVersusMaps.Contains(code)) Packs.RemoveMap(code);
        Report("Selected maps removed.");
    }

    private IProgress<DownloadProgress> DownloadReporter() => new Progress<DownloadProgress>(p =>
    {
        Progress = p.Total > 0 ? (double)p.Received / p.Total : null;
        Report($"{Size(p.Received)} of {Size(p.Total)}" + (p.BytesPerSecond > 0 ? $" · {Size((long)p.BytesPerSecond)}/s" : string.Empty));
    });

    private IProgress<PackProgress> PackReporter(string title) => new Progress<PackProgress>(p =>
    {
        Progress = p.Total > 0 ? (double)p.Received / p.Total : null;
        Report(p.Extracting ? title + ": installing…" : $"{title}: {Size(p.Received)} of {Size(p.Total)}");
    });

    public static string Size(long bytes) => $"{bytes / (1024d * 1024d):0.0} MiB";
    private void Notify() { if (!_disposed) Changed?.Invoke(); }
    public void Dispose() { _disposed = true; _work?.Cancel(); }
}
