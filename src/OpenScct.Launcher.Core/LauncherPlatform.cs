using System;
using System.IO;

namespace OpenScct.Launcher;

/// <summary>The release and installed executable names for each supported desktop platform.</summary>
public sealed record LauncherPlatform
{
    private LauncherPlatform(string runtimeIdentifier, string launcherFileName, string gameFileName, bool isLinux)
    {
        RuntimeIdentifier = runtimeIdentifier;
        LauncherFileName = launcherFileName;
        GameFileName = gameFileName;
        IsLinux = isLinux;
    }

    public static LauncherPlatform WindowsX64 { get; } = new("win-x64", "OpenSCCT.Launcher.exe", "OpenSCCT.exe", false);
    public static LauncherPlatform LinuxX64 { get; } = new("linux-x64", "OpenSCCT.Launcher-linux-x64", "OpenSCCT.x86_64", true);
    public static LauncherPlatform Current => OperatingSystem.IsLinux() ? LinuxX64 : WindowsX64;

    /// <summary>
    /// Windows x64 for the Unreal Engine build's own install root (<see cref="Unreal.UnrealService"/>): the same
    /// runtime and launcher names, with the build's starter exe in place of the Godot game's.
    /// </summary>
    internal static LauncherPlatform UnrealWindowsX64 { get; } = new("win-x64", "OpenSCCT.Launcher.exe", "OpenSCCTUnreal.exe", false);

    public string RuntimeIdentifier { get; }
    public string LauncherFileName { get; }
    public string GameFileName { get; }
    public bool IsLinux { get; }
    public string ExecutableSuffix => IsLinux ? string.Empty : ".exe";
    public StringComparison PathComparison => IsLinux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    public StringComparer PathComparer => IsLinux ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>Downloads and delta blobs do not retain Unix permissions. Restore execute permission before use.</summary>
    public void MakeExecutable(string path)
    {
        if (IsLinux && OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        }
    }
}
