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
/// The game's downloads from OpenSCCT-Content (docs/CONTENT-PACKS.md): the content index, read in the background and
/// kept for ten minutes, and one download job per map, which first fetches the shared packs when the player has no
/// content of his own (no converted install). Godot-free and thread-safe; screens poll <see cref="Job"/> and
/// <see cref="Generation"/> each frame, and refresh their map catalogs when the generation moves on.
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

    /// <summary>One map's (or shared pack's) download as a screen shows it.</summary>
    public sealed class DownloadJob
    {
        internal DownloadJob(ContentMap map)
        {
            Map = map;
            Title = map.DisplayTitle;
        }

        internal DownloadJob(ContentSharedPack pack)
        {
            Shared = pack;
            Title = pack.Title.Length > 0 ? pack.Title : pack.Id;
        }

        /// <summary>The map being downloaded, or null for a shared pack.</summary>
        public ContentMap? Map { get; }

        /// <summary>The shared pack being downloaded, or null for a map.</summary>
        public ContentSharedPack? Shared { get; }

        /// <summary>The map's title, or the shared pack's.</summary>
        public string Title { get; }

        /// <summary>What is being fetched: the shared packs, then the map.</summary>
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
        /// A step that fetches a shared pack first keeps that pack's name.</summary>
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

    /// <summary>Moves on each time a map or shared pack finished installing (screens then refresh their catalogs).</summary>
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
                return _index is { IsFaulted: true } t ? t.Exception?.GetBaseException().Message : null;
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
            _index = Task.Run(() => store.FetchIndexAsync(CancellationToken.None));
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

    /// <summary>The map of the index for a code, else null.</summary>
    public static ContentMap? Find(string code) => Index?.FindMap(code);

    /// <summary>The download of a shared pack, running or finished, else null.</summary>
    public static DownloadJob? SharedJob(string id) => Job(SharedKey(id));

    private static string SharedKey(string id) => "shared:" + id;

    /// <summary>
    /// Downloads a shared pack (core, chars, audio) on its own, as the Content page's Download does; a running one
    /// is returned as it is.
    /// </summary>
    public static DownloadJob DownloadShared(ContentSharedPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        DownloadJob job;
        lock (Gate)
        {
            if (Jobs.TryGetValue(SharedKey(pack.Id), out DownloadJob? running) && running.Running)
            {
                return running;
            }

            job = new DownloadJob(pack);
            Jobs[SharedKey(pack.Id)] = job;
        }

        _ = Task.Run(() => RunSharedAsync(job, pack));
        return job;
    }

    /// <summary>
    /// Removes a downloaded map (its folder under packs\svmap and its entry) and forgets its finished download, so
    /// the lobby and the hub offer it again. A running download is left alone.
    /// </summary>
    public static void Remove(string code)
    {
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

    /// <summary>The player has converted shared content of their own (so the downloaded shared packs go unused).</summary>
    public static bool UsesOwnSharedContent => HasOwnSharedContent();

    /// <summary>
    /// Downloads a map (once; a failed one may be started again). The shared packs come first when the player has
    /// no content of his own, or when the index's are newer than the ones downloaded before.
    /// </summary>
    public static DownloadJob Download(ContentMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ContentIndex? index = Index;
        DownloadJob job;
        lock (Gate)
        {
            if (Jobs.TryGetValue(map.Code, out DownloadJob? running) && running.Error is null)
            {
                return running;
            }

            job = new DownloadJob(map);
            Jobs[map.Code] = job;
        }

        _ = Task.Run(() => RunAsync(job, index));
        return job;
    }

    private static async Task RunAsync(DownloadJob job, ContentIndex? index)
    {
        ContentStore store = Store;
        try
        {
            // Without a converted install the game has no shared content: the downloaded packs supply it.
            if (index is not null && !HasOwnSharedContent())
            {
                foreach (ContentSharedPack pack in index.Shared)
                {
                    if (!store.HasShared(pack))
                    {
                        job.Step = $"Downloading {(pack.Title.Length > 0 ? pack.Title : pack.Id)}";
                        await store.InstallSharedAsync(pack, Progress(job), job.Stop.Token).ConfigureAwait(false);
                        Interlocked.Increment(ref _generation);
                    }
                }
            }

            job.Step = $"Downloading {job.Title}";
            await store.InstallMapAsync(job.Map!, ContentKind.Full, Progress(job), job.Stop.Token).ConfigureAwait(false);
            job.Done = true;
            Interlocked.Increment(ref _generation);
        }
        catch (OperationCanceledException) when (job.Stop.IsCancellationRequested)
        {
            job.Error = "stopped";
        }
        catch (Exception e) when (e is HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            job.Error = e is TaskCanceledException ? "the download timed out" : e.Message;
        }
    }

    private static async Task RunSharedAsync(DownloadJob job, ContentSharedPack pack)
    {
        try
        {
            job.Step = $"Downloading {job.Title}";
            await Store.InstallSharedAsync(pack, Progress(job), job.Stop.Token).ConfigureAwait(false);
            job.Done = true;
            Interlocked.Increment(ref _generation);
        }
        catch (OperationCanceledException) when (job.Stop.IsCancellationRequested)
        {
            job.Error = "stopped";
        }
        catch (Exception e) when (e is HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            job.Error = e is TaskCanceledException ? "the download timed out" : e.Message;
        }
    }

    /// <summary>The player converted an install (the launcher's content, or a checkout's .content): its shared folder is used.</summary>
    private static bool HasOwnSharedContent()
    {
        if (ContentPaths.Root is { } root && System.IO.Directory.Exists(System.IO.Path.Combine(root, "shared")))
        {
            return true;
        }

        return MapCatalog.FindRepoRoot(AppContext.BaseDirectory) is { } repo &&
            System.IO.Directory.Exists(System.IO.Path.Combine(repo, ".content", "shared"));
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
