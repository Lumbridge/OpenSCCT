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
