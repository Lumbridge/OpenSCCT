using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using OpenScct.Maps;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Controls;

namespace OpenScctGame.Online;

/// <summary>
/// Maps from the SCCT-Maps repository in the game: where the launcher keeps them (<c>--svm-map-downloads=</c>), where a
/// hosted map came from (told to players with the setup), and where the content builder is, so a player can fetch
/// the map a lobby picks (<see cref="MapFetch"/>; docs/LAUNCHER.md "Downloaded maps", docs/ONLINE.md).
/// </summary>
public static class MapDownloads
{
    /// <summary>The downloaded maps folder (the launcher's, or <c>--svm-map-downloads=</c>).</summary>
    public static string LibraryRoot { get; private set; } = MapLibrary.DefaultRoot;

    /// <summary>The player's original game folder (Packages, System), which the builder converts from.</summary>
    public static string GameRoot { get; private set; } = InputBindings.GameRoot(null);

    public static MapLibrary Library => new(LibraryRoot);

    private static string _exeDir = ".";
    private static string? _repoRoot;

    /// <summary>Set once at start from the command line (main thread: it asks Godot where the game runs from).</summary>
    public static void Configure(LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        LibraryRoot = string.IsNullOrWhiteSpace(options.MapsRoot) ? MapLibrary.DefaultRoot : Path.GetFullPath(options.MapsRoot);
        GameRoot = InputBindings.GameRoot(options.GameRoot);
        _exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";

        // A checkout: res:// is its game folder, the builder is in ../content.
        string game = ProjectSettings.GlobalizePath("res://");
        string? parent = string.IsNullOrEmpty(game) ? null : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(game));
        _repoRoot = parent is not null && Directory.Exists(Path.Combine(parent, "content", "svmbuild")) ? parent : null;
    }

    /// <summary>
    /// Where the host's <paramref name="map"/> came from: the downloaded map that brought it, unless the install has
    /// the map itself (the install's copy is the one converted then). Null for every other map.
    /// </summary>
    public static MapSourceInfo? SourceOf(string map)
    {
        if (string.IsNullOrEmpty(map) || File.Exists(Path.Combine(GameRoot, "Packages", "Maps", map + ".sdc")))
        {
            return null;
        }

        InstalledMap? installed = Library.Installed()
            .FirstOrDefault(m => m.Codes.Any(c => string.Equals(c, map, StringComparison.OrdinalIgnoreCase)));
        return installed is { Commit.Length: 40 } ? new MapSourceInfo(installed.Id, installed.Commit, installed.Fingerprint) : null;
    }

    /// <summary>The content folder maps are converted into: the launcher's, else a checkout's .content.</summary>
    public static string? ContentRoot =>
        ContentPaths.Root ?? (_repoRoot is { } repo && Directory.Exists(Path.Combine(repo, ".content")) ? Path.Combine(repo, ".content") : null);

    /// <summary>The builder shipped beside the exe, else a checkout's.</summary>
    public static BuilderTool? Builder =>
        BuilderTool.InBuild(_exeDir) is { } shipped && shipped.SupportsAddons(_exeDir)
            ? shipped
            : _repoRoot is { } repo ? BuilderTool.InCheckout(repo) : null;

    /// <summary>Why this game cannot fetch maps (no builder, no game install, no content), or null when it can.</summary>
    public static string? Problem()
    {
        if (Builder is null)
        {
            return "This version of OpenSCCT cannot prepare downloaded maps. Update it from the launcher.";
        }

        if (!Directory.Exists(Path.Combine(GameRoot, "Packages")))
        {
            return "Your Splinter Cell Chaos Theory folder was not found. Start the game from the launcher.";
        }

        return ContentRoot is null ? "The game files have not been prepared. Start the game from the launcher." : null;
    }

}

/// <summary>
/// Fetches the map a lobby picked that this player lacks: the host's version from SCCT-Maps (the commit and
/// fingerprint the host told), into the downloaded maps folder, then converts it with the content builder. Runs in
/// the background; the lobby screen reads <see cref="Snapshot"/> every frame. When it is
/// <see cref="MapFetchStage.Done"/>, the caller refreshes the map catalog and checks the map again (main thread).
/// </summary>
public sealed class MapFetch
{
    private static readonly System.Net.Http.HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
    })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancel = new();
    private MapFetchStage _stage = MapFetchStage.Listing;
    private string _text;
    private double _fraction;

    private MapFetch(string map, MapSourceInfo source)
    {
        Map = map;
        Source = source;
        _text = "Looking up " + map + "...";
    }

    /// <summary>The map code the lobby picked.</summary>
    public string Map { get; }

    public MapSourceInfo Source { get; }

    /// <summary>Starts fetching <paramref name="map"/> from <paramref name="source"/> in the background.</summary>
    public static MapFetch Start(string map, MapSourceInfo source)
    {
        var fetch = new MapFetch(map, source);
        _ = Task.Run(fetch.RunAsync);
        return fetch;
    }

    public (MapFetchStage Stage, string Text, double Fraction) Snapshot()
    {
        lock (_gate)
        {
            return (_stage, _text, _fraction);
        }
    }

    public bool Finished => Snapshot().Stage is MapFetchStage.Done or MapFetchStage.Failed or MapFetchStage.Cancelled;

    public void Cancel() => _cancel.Cancel();

    private void Set(MapFetchStage stage, string text, double fraction)
    {
        lock (_gate)
        {
            _stage = stage;
            _text = text;
            _fraction = fraction;
        }
    }

    private async Task RunAsync()
    {
        CancellationToken cancel = _cancel.Token;
        try
        {
            string? problem = MapDownloads.Problem();
            if (problem is not null || MapDownloads.Builder is not { } builder || MapDownloads.ContentRoot is not { } content)
            {
                Set(MapFetchStage.Failed, problem ?? "Downloaded maps cannot be prepared here.", 0);
                return;
            }

            // The host's exact version: the map list at the commit it downloaded from.
            var repository = new MapRepository(Http);
            MapIndex index = await repository.FetchCatalogAsync(cancel, Source.Commit).ConfigureAwait(false);
            MapPackage? map = index.Maps.FirstOrDefault(m => m.Id == Source.Id);
            if (map is null || map.Fingerprint != Source.Fingerprint)
            {
                Set(MapFetchStage.Failed, $"The host's version of {Map} was not found in the community maps.", 0);
                return;
            }

            if (MapLibrary.CodesInInstall(map, MapDownloads.GameRoot).Count > 0)
            {
                Set(MapFetchStage.Failed, $"Your game has its own {Map}, which is used instead of a download. The host has a different one.", 0);
                return;
            }

            MapLibrary library = MapDownloads.Library;
            string before = library.Fingerprint();
            var progress = new Counter<MapDownloadProgress>(p => Set(MapFetchStage.Downloading,
                $"Downloading {map.Name}: {Mb(p.Received)} of {Mb(p.Total)}", p.Total > 0 ? (double)p.Received / p.Total : 1));
            Set(MapFetchStage.Downloading, $"Downloading {map.Name}...", 0);
            await library.InstallAsync(map, repository, progress, cancel).ConfigureAwait(false);

            // Convert only this map (the builder's cache keeps the rest); its output goes to a log beside the content.
            Set(MapFetchStage.Converting, $"Preparing {map.Name}. This can take a minute...", 1);
            string log = Path.Combine(content, "map-fetch.log");
            using var writer = new StreamWriter(log, append: false) { AutoFlush = true };
            object logGate = new();
            int exit = await builder.RunAsync(
                MapBuilder.Arguments(MapDownloads.GameRoot, content, library.GameFolder, map.Codes, ContentBuildFile.Language(content)),
                content,
                line =>
                {
                    lock (logGate)
                    {
                        writer.WriteLine(line);
                    }
                },
                cancel).ConfigureAwait(false);
            if (exit != 0)
            {
                Set(MapFetchStage.Failed, $"{map.Name} was downloaded but could not be prepared. Details: {log}", 1);
                return;
            }

            ContentBuildFile.AdvanceDownloadedMaps(content, before, library.Fingerprint());
            Set(MapFetchStage.Done, $"{map.Name} is ready.", 1);
        }
        catch (OperationCanceledException)
        {
            Set(MapFetchStage.Cancelled, "Download stopped.", 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException
            or System.Text.Json.JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Set(MapFetchStage.Failed, "Could not fetch the map: " + ex.Message, 0);
        }
    }

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";

    /// <summary>Reports on the calling thread (Progress&lt;T&gt; would need a synchronization context).</summary>
    private sealed class Counter<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public enum MapFetchStage
{
    Listing,
    Downloading,
    Converting,
    Done,
    Failed,
    Cancelled,
}
