using System;
using System.IO;

namespace OpenScct.Launcher.Install;

/// <summary>
/// The folders the launcher owns, all under one root (default %LOCALAPPDATA%\OpenSCCT):
/// <code>
/// install.json          which version is current and which is kept for rollback
/// launcher.json         launcher settings
/// token.dat             the GitHub token, encrypted with Windows DPAPI for the current user
/// versions\0.2.0\       one game build per version, side by side
/// downloads\            downloads in progress and finished zips
/// cache\                the release list and its ETag
/// content\              game content built from the player's own install (later)
/// maps\                 maps downloaded from the SCCT-Maps repository, read by the content build
/// </code>
/// The game's user:// data (settings, key bindings, saves) is in %APPDATA%\Godot\app_userdata\OpenSCCT and is never
/// touched by the launcher.
/// </summary>
public sealed class LauncherLayout
{
    public LauncherLayout(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        Root = Path.GetFullPath(root);
    }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSCCT");

    /// <summary>Godot's user:// folder for the game; listed so it can be shown, never written or deleted.</summary>
    public static string GameUserData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "OpenSCCT");

    public string Root { get; }

    public string Manifest => Path.Combine(Root, "install.json");

    public string Settings => Path.Combine(Root, "launcher.json");

    public string Token => Path.Combine(Root, "token.dat");

    public string Versions => Path.Combine(Root, "versions");

    public string Downloads => Path.Combine(Root, "downloads");

    public string Cache => Path.Combine(Root, "cache");

    public string Content => Path.Combine(Root, "content");

    /// <summary>Maps downloaded from the SCCT-Maps repository (<see cref="OpenScct.Maps.MapLibrary"/>).</summary>
    public string Maps => Path.Combine(Root, "maps");

    public string VersionDir(string version) => Path.Combine(Versions, version);
}
