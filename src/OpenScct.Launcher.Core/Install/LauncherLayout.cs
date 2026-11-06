using System;
using System.IO;

namespace OpenScct.Launcher.Install;

/// <summary>
/// The folders the launcher owns, all under one root (default %LOCALAPPDATA%\OpenSCCT):
/// <code>
/// install.json          which version is current and which is kept for rollback
/// launcher.json         launcher settings
/// versions\0.2.0\       one game build per version, side by side
/// downloads\            downloads in progress and finished zips
/// cache\                the release list and its ETag
/// content\              game content built from the player's own install (later)
/// maps\                 maps downloaded from the SCCT-Maps repository, read by the content build
/// </code>
/// The game's user:// data (settings, key bindings, saves) is in %APPDATA%\Godot\app_userdata\OpenSCCT and is
/// kept on uninstall; the launcher can update its renderer override.
/// </summary>
public sealed class LauncherLayout
{
    public LauncherLayout(string root, LauncherPlatform? platform = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        Root = Path.GetFullPath(root);
        Platform = platform ?? LauncherPlatform.Current;
    }

    public static string DefaultRoot => GetDefaultRoot(LauncherPlatform.Current);

    public static string GetDefaultRoot(LauncherPlatform platform) => platform.IsLinux
        ? Path.Combine(XdgDataHome(), "OpenSCCT")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSCCT");

    /// <summary>Godot's user:// folder: shared renderer preference and settings-folder link; never deleted by the launcher.</summary>
    public static string GameUserData => GetGameUserData(LauncherPlatform.Current);

    public static string GetGameUserData(LauncherPlatform platform) => platform.IsLinux
        ? Path.Combine(XdgDataHome(), "godot", "app_userdata", "OpenSCCT")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "OpenSCCT");

    private static string XdgDataHome()
    {
        string? configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    public LauncherPlatform Platform { get; }

    public string Root { get; }

    public string Manifest => Path.Combine(Root, "install.json");

    public string Settings => Path.Combine(Root, "launcher.json");

    public string Versions => Path.Combine(Root, "versions");

    public string Downloads => Path.Combine(Root, "downloads");

    public string Cache => Path.Combine(Root, "cache");

    public string Content => Path.Combine(Root, "content");

    /// <summary>Maps downloaded from the SCCT-Maps repository (<see cref="OpenScct.Maps.MapLibrary"/>).</summary>
    public string Maps => Path.Combine(Root, "maps");

    /// <summary>Converted maps and shared packs downloaded from OpenSCCT-Content (the game reads them; docs/CONTENT-PACKS.md).</summary>
    public string Packs => Path.Combine(Root, "packs");

    /// <summary>The optional editor's stable installation, independent of game versions.</summary>
    public string Editor => Path.Combine(Root, "editor");

    public string EditorCache => Path.Combine(Root, "editor-cache");

    public string EditorDownloads => Path.Combine(Root, "editor-downloads");

    public string VersionDir(string version) => Path.Combine(Versions, version);
}
