using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Maps;
using OpenScct.Server;

namespace OpenScctGame.Online;

/// <summary>
/// Downloads optional map packs and tracks active jobs so the lobby, hub and content screen share progress.
/// </summary>
public static class ContentPacks
{
    /// <summary>Seconds an index is kept before it is fetched again.</summary>
    public const double IndexSeconds = 600;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DownloadJob> Jobs = new(StringComparer.OrdinalIgnoreCase);
    private static ContentStore? _store;
    private static Task<ContentIndex>? _index;
    private static DateTime _indexAt;
    private static int _generation;

    /// <summary>Install a host-specific store before any content requests (the browser's same-origin mirror).</summary>
    public static void ConfigureStore(ContentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        lock (Gate)
        {
            if (_index is not null || Jobs.Count > 0) throw new InvalidOperationException("Content downloads have already started.");
            _store = store;
        }
    }

    private static Task Run(Func<Task> action) => OperatingSystem.IsBrowser() ? action() : Task.Run(action);

    /// <summary>One map's download as a screen shows it.</summary>
    public sealed class DownloadJob
    {
        internal DownloadJob(ContentMap map)
        {
            Map = map;
            Title = map.DisplayTitle;
        }

        internal DownloadJob(string title) => Title = title;

        /// <summary>The map being downloaded, or null for standalone navigation.</summary>
        public ContentMap? Map { get; }

        /// <summary>The download title.</summary>
        public string Title { get; }

        /// <summary>What is being fetched: the map or its bot navigation.</summary>
        public string Step { get; internal set; } = "Starting";

        public long Received { get; internal set; }

        public long Total { get; internal set; }

        public bool Done { get; internal set; }

        /// <summary>Why it failed (or "stopped"), else null.</summary>
        public string? Error { get; internal set; }

        /// <summary>Still downloading or installing.</summary>
        public bool Running => !Done && Error is null;

        /// <summary>The share received of what is downloading now (0 to 1; 0 while installing).</summary>
        public double Fraction => Total > 0 ? Math.Clamp((double)Received / Total, 0, 1) : 0;

        internal CancellationTokenSource Stop { get; } = new();

        /// <summary>Stops the download (a half-made map is never installed).</summary>
        public void Cancel() => Stop.Cancel();

        /// <summary>"Downloading Aquarius: 12.3 / 31.0 MB", "Installing Aquarius", the error, or "Downloaded".</summary>
        public string Describe() =>
            Error is { } e ? $"{Title}: {e}" :
            Done ? $"{Title} downloaded" :
            Total > 0 ? string.Create(CultureInfo.InvariantCulture, $"{Step}: {Received / 1048576.0:0.0} / {Total / 1048576.0:0.0} MB") :
            Step;

        /// <summary>Describe() for the job's own row, which already shows the title: "Downloading 12.3 / 31.0 MB".
        /// </summary>
        public string DescribeBrief()
        {
            string step = Step == $"Downloading {Title}" ? "Downloading" : Step == $"Installing {Title}" ? "Installing" : Step;
            return Error is not null || Done ? Describe() :
                Total > 0 ? string.Create(CultureInfo.InvariantCulture, $"{step} {Received / 1048576.0:0.0} / {Total / 1048576.0:0.0} MB") :
                step;
        }
    }

    public static ContentStore Store
    {
        get
        {
            lock (Gate)
            {
                return _store ??= new ContentStore(ContentPaths.PacksFolder, Http);
            }
        }
    }

    /// <summary>Moves on each time a map finished installing (screens then refresh their catalogs).</summary>
    public static int Generation => Volatile.Read(ref _generation);

    /// <summary>The index once read, else null (still loading, or it failed: <see cref="IndexError"/>).</summary>
    public static ContentIndex? Index
    {
        get
        {
            RefreshIndex();
            lock (Gate)
            {
                return _index is { IsCompletedSuccessfully: true } t ? t.Result : null;
            }
        }
    }

    /// <summary>Why the index could not be read, else null.</summary>
    public static string? IndexError
    {
        get
        {
            lock (Gate)
            {
                return _index is { IsFaulted: true } t ? NetworkDiagnostics.Describe(t.Exception!) : null;
            }
        }
    }

    /// <summary>Starts reading the index when there is none, it failed a minute ago, or it is old.</summary>
    public static void RefreshIndex(bool force = false)
    {
        lock (Gate)
        {
            double age = (DateTime.UtcNow - _indexAt).TotalSeconds;
            bool stale = _index is null || force || (_index.IsFaulted && age > 60) || (_index.IsCompletedSuccessfully && age > IndexSeconds);
            if (!stale || _index is { IsCompleted: false })
            {
                return;
            }

            _indexAt = DateTime.UtcNow;
            ContentStore store = _store ??= new ContentStore(ContentPaths.PacksFolder, Http);
            _index = OperatingSystem.IsBrowser() ? FetchIndexLoggedAsync(store) : Task.Run(() => FetchIndexLoggedAsync(store));
        }
    }

    private static async Task<ContentIndex> FetchIndexLoggedAsync(ContentStore store)
    {
        try
        {
            return await store.FetchIndexAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            NetworkDiagnostics.Report("content index download", error);
            throw;
        }
    }

    /// <summary>The download of a map code, running or finished, else null.</summary>
    public static DownloadJob? Job(string code)
    {
        lock (Gate)
        {
            return Jobs.TryGetValue(code, out DownloadJob? j) ? j : null;
        }
    }

    public static bool CanDownloadMap(string code) => !SteamVersusMaps.Contains(code);

    public static bool NeedsNativeLauncher(ContentMap map) => map.SteamRecipe is not null &&
        (OperatingSystem.IsBrowser() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID")));

    public static string InstallAction(ContentMap map) => map.SteamRecipe is not null ? "Download and build from Steam" : "Download";

    public static string SteamMapSetup(string code) =>
        $"Build {code} from your installed Steam game in the OpenSCCT launcher.";

    public static ContentMap? Find(string code) => CanDownloadMap(code) ? Index?.FindMap(code) : null;

    /// <summary>
    /// Removes a downloaded map (its folder under packs\svmap and its entry) and forgets its finished download, so
    /// the lobby and the hub offer it again. A running download is left alone.
    /// </summary>
    public static void Remove(string code)
    {
        if (!CanDownloadMap(code)) return;
        lock (Gate)
        {
            if (Jobs.TryGetValue(code, out DownloadJob? job))
            {
                if (job.Running)
                {
                    return;
                }

                Jobs.Remove(code);
            }
        }

        Store.RemoveMap(code);
        Interlocked.Increment(ref _generation);
    }

    /// <summary>Downloads an optional map, sharing concurrent jobs for the same map.</summary>
    public static DownloadJob Download(ContentMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!CanDownloadMap(map.Code)) return new DownloadJob(map) { Error = SteamMapSetup(map.Code) };
        if (NeedsNativeLauncher(map)) return new DownloadJob(map) { Error = "Use the native OpenSCCT launcher to build this map from Steam." };
        DownloadJob job;
        lock (Gate)
        {
            // A finished job is not handed out again: the index or the host may have a newer version by now.
            if (Jobs.TryGetValue(map.Code, out DownloadJob? running) && running.Running)
            {
                return running;
            }

            job = new DownloadJob(map);
            Jobs[map.Code] = job;
        }

        _ = Run(() => RunAsync(job));
        return job;
    }

    public static DownloadJob DownloadBotNavigation(string map, ContentBotNavigation navigation)
    {
        var job = new DownloadJob("Bot navigation");
        _ = Run(async () =>
        {
            try
            {
                job.Step = "Downloading bot navigation";
                await Store.InstallBotNavigationAsync(navigation,
                    path => OpenScct.Bots.AiNavCache.PackMatches(path, map, navigation.SimHash, navigation.ActivationMask),
                    Progress(job), job.Stop.Token).ConfigureAwait(false);
                job.Done = true;
            }
            catch (OperationCanceledException) when (job.Stop.IsCancellationRequested)
            {
                job.Error = "stopped";
            }
            catch (Exception e) when (e is HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or InvalidOperationException or UnauthorizedAccessException or TaskCanceledException)
            {
                NetworkDiagnostics.Report("bot navigation download: " + map, e);
                job.Error = e is TaskCanceledException ? "the download timed out" : NetworkDiagnostics.Describe(e);
            }
        });
        return job;
    }

    private static async Task RunAsync(DownloadJob job)
    {
        ContentStore store = Store;
        try
        {
            job.Step = $"Downloading {job.Title}";
            await store.InstallMapAsync(job.Map!, ContentKind.Full, Progress(job), job.Stop.Token).ConfigureAwait(false);
            if (OperatingSystem.IsBrowser() && job.Map?.BotNavigation is { } nav && nav.FormatVersion == OpenScct.Game.AI.NavGraph.FormatVersion)
            {
                job.Step = "Downloading bot navigation";
                await store.InstallBotNavigationAsync(nav,
                    path => OpenScct.Bots.AiNavCache.PackMatches(path, job.Map.Code, nav.SimHash, nav.ActivationMask),
                    Progress(job), job.Stop.Token).ConfigureAwait(false);
            }
            job.Done = true;
            Interlocked.Increment(ref _generation);
        }
        catch (OperationCanceledException) when (job.Stop.IsCancellationRequested)
        {
            job.Error = "stopped";
        }
        catch (Exception e) when (e is HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or InvalidOperationException or UnauthorizedAccessException or TaskCanceledException)
        {
            NetworkDiagnostics.Report("content download: " + job.Title, e);
            job.Error = e is TaskCanceledException ? "the download timed out" : NetworkDiagnostics.Describe(e);
        }
    }

    private static Progress<PackProgress> Progress(DownloadJob job) => new(p =>
    {
        job.Received = p.Received;
        job.Total = p.Total;
        if (p.Extracting)
        {
            job.Step = $"Installing {job.Title}";
            job.Total = 0;
        }
    });
}
