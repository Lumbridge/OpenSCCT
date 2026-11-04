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

internal enum LauncherAction { Install, Update, FinishInstall, Play, Cancel, Waiting }

/// <summary>The Linux UI uses exactly the same release, download, content and install services as Windows.</summary>
internal sealed class LauncherController : IDisposable
{
    private readonly HttpClient _http;
    private readonly IGameProcessProbe _games;
    private readonly UpdateService _updates;
    private readonly LauncherSwap? _swap;
    private CancellationTokenSource? _work;
    private Task _activeWork = Task.CompletedTask;
    private ContentIndex? _index;
    private bool _disposed;

    public LauncherController(LauncherLayout layout, HttpClient http, string? executable,
        IGameProcessProbe? games = null)
    {
        Layout = layout;
        _http = http;
        _games = games ?? new GameProcessProbe(layout.Platform);
        Store = new VersionStore(layout);
        Packs = new ContentStore(layout.Packs, http);
        Settings = LauncherSettings.Load(layout.Settings);
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
    public LauncherSettings Settings { get; private set; }
    public SemVersion? LauncherVersion { get; }
    public string? Executable { get; }
    public UpdateCheck? Check { get; private set; }
    public string Status { get; private set; } = "Install OpenSCCT and its game files in one step.";
    public bool IsError { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public double? Progress { get; private set; }
    public bool Busy => _work is not null;
    public bool GameRunning => _updates.GameRunning;
    public bool HasLauncherUpdate => _swap?.HasStaged == true || Check?.LauncherUpdateAvailable == true;
    public bool LauncherStaged => _swap?.HasStaged == true;
    public bool CanUpdateLauncher => _swap?.CanWriteFolder() == true;
    public bool ContentReady => PackedContent.Installed(Packs);
    public bool ContentUpdate => _index is not null &&
        (PackedContent.SharedToFetch(_index, Packs).Count > 0 || PackedContent.MapsToFetch(_index, Packs, false).Count > 0);
    public bool Playable => Store.Current() is not null && ContentReady;
    public string? PendingJoin { get; private set; }
    public LauncherAction Next => Busy ? LauncherAction.Cancel
        : Store.Pending() is not null ? LauncherAction.Waiting
        : Store.Current() is null ? LauncherAction.Install
        : Check?.GameUpdateAvailable == true || ContentUpdate ? LauncherAction.Update
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
        catch (OperationCanceledException) { Report("Stopped. Completed downloads are kept; you can continue later."); }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException
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
        try { _index = await Packs.FetchIndexAsync(timeout.Token); }
        catch (Exception ex) when (ex is IOException or HttpRequestException) { /* Installed content remains playable offline. */ }
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
        else if (Check.GameUpdateAvailable || ContentUpdate) Report("An update is available.");
        else Report(Store.Current() is null ? "Ready to install." : "Your installation is up to date.");
    }

    public async Task InstallAsync(CancellationToken cancel)
    {
        if (Check is null || Store.Current() is null && !Check.GameUpdateAvailable) await CheckAsync(cancel);
        if (Check is { GameUpdateAvailable: true, Latest: { } latest } && Store.Pending() is null)
        {
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
        if (GameRunning && (!ContentReady || ContentUpdate))
        {
            Report("Close OpenSCCT before updating shared game files, then choose Update.");
            return;
        }
        await InstallContentAsync(cancel);
    }

    public async Task InstallContentAsync(CancellationToken cancel)
    {
        if (GameRunning) throw new InvalidOperationException("Close OpenSCCT before changing its game files.");
        Operation = "Downloading game files";
        _index = await Packs.FetchIndexAsync(cancel);
        if (_index.Shared.Count == 0) throw new IOException(_index.Problem ?? "The game file catalogue contains no shared packs.");
        var shared = PackedContent.SharedToFetch(_index, Packs);
        var maps = PackedContent.MapsToFetch(_index, Packs, !ContentReady);
        int total = shared.Count + maps.Count, n = 0;
        // Shared packs are the completion marker; downloading them last keeps interrupted first installs resumable.
        foreach (ContentMap map in maps)
            await Packs.InstallMapAsync(map, ContentKind.Full, PackReporter($"{map.DisplayTitle} ({++n}/{total})"), cancel);
        foreach (ContentSharedPack pack in shared)
            await Packs.InstallSharedAsync(pack, PackReporter($"{pack.Title} ({++n}/{total})"), cancel);
        Report("All set. Press PLAY.");
    }

    public async Task PlayAsync(bool skipUpdateCheck, Func<UpdateCheck, Task<PlayUpdateChoice>> choose, CancellationToken cancel)
    {
        if (!Playable) throw new InvalidOperationException("Choose Finish install to download the required game files first.");
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
            join is null ? null : [WebJoinRequest.GameArgumentPrefix + join]));
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
        var settings = new LauncherSettings { CheckOnStart = checkOnStart, IncludePrereleases = previews };
        settings.Save(Layout.Settings);
        Settings = settings;
        Check = null;
        Notify();
    }

    public void Reset()
    {
        if (Busy || GameRunning) throw new InvalidOperationException("Close OpenSCCT and finish downloads before resetting the installation.");
        IReadOnlyList<string> failed = InstallReset.Run(Layout, Executable);
        Check = null;
        _index = null;
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
        _index = await Packs.FetchIndexAsync(cancel);
        if (_index.Problem is { } problem) Report(problem);
        return _index;
    }

    public async Task DownloadMapsAsync(IEnumerable<ContentMap> maps, CancellationToken cancel)
    {
        if (GameRunning) throw new InvalidOperationException("Close OpenSCCT before changing its downloaded maps.");
        _index ??= await Packs.FetchIndexAsync(cancel);
        foreach (ContentSharedPack pack in PackedContent.SharedToFetch(_index, Packs))
            await Packs.InstallSharedAsync(pack, PackReporter(pack.Title), cancel);
        foreach (ContentMap map in maps)
            await Packs.InstallMapAsync(map, ContentKind.Full, PackReporter(map.DisplayTitle), cancel);
        Report("Selected maps are ready to play.");
    }

    public void RemoveMaps(IEnumerable<string> codes)
    {
        if (Busy || GameRunning) throw new InvalidOperationException("Close OpenSCCT and finish downloads before removing maps.");
        foreach (string code in codes) Packs.RemoveMap(code);
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
