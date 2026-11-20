using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Content;

/// <summary>Finds the offline content converter shipped with this game version.</summary>
public static class SteamBuildTools
{
    public static async Task<string> ResolveAsync(string versionDirectory, LauncherLayout layout, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        string root = Path.Combine(versionDirectory, ContentBuildPlan.ToolsFolder);
        string python = layout.Platform.IsLinux ? Path.Combine(root, "python", "bin", "python3") : Path.Combine(root, "python", "python.exe");
        string marker = Path.Combine(root, "steam-builder-v1");
        if (!File.Exists(marker) || !File.Exists(python) || !File.Exists(Path.Combine(root, "repo", "content", "svmbuild", "__main__.py"))
            || (await File.ReadAllTextAsync(marker, cancel).ConfigureAwait(false)).Trim() != layout.Platform.RuntimeIdentifier)
            throw new InvalidOperationException("Update or repair the Godot game to install its Steam content builder.");
        if (layout.Platform.IsLinux && OperatingSystem.IsLinux() && !File.GetUnixFileMode(python).HasFlag(UnixFileMode.UserExecute))
            layout.Platform.MakeExecutable(python);
        return root;
    }
}
