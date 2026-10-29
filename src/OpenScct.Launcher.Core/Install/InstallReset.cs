using System;
using System.Collections.Generic;
using System.IO;

namespace OpenScct.Launcher.Install;

/// <summary>
/// Puts the launcher back to a fresh install: removes every game version, download, cache, the content built from the
/// player's install, downloaded maps and content packs, and install.json. Keeps launcher.json and token.dat (the
/// player's settings) and never touches the game's user:// data, which is not under the root. A folder holding the
/// running launcher exe is left alone.
/// </summary>
public static class InstallReset
{
    /// <summary>Deletes what the launcher installed. Returns the paths it could not delete (in use or denied).</summary>
    public static IReadOnlyList<string> Run(LauncherLayout layout, string? launcherExe)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string? exeDir = launcherExe is null ? null : Path.GetDirectoryName(Path.GetFullPath(launcherExe));
        var failed = new List<string>();

        foreach (string dir in new[] { layout.Versions, layout.Downloads, layout.Cache, layout.Content, layout.Maps, layout.Packs })
        {
            if (exeDir is not null && IsSameOrUnder(exeDir, dir))
            {
                continue;
            }

            try
            {
                SafeDelete.Tree(dir, layout.Root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(dir);
            }
        }

        try
        {
            File.Delete(layout.Manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed.Add(layout.Manifest);
        }

        return failed;
    }

    private static bool IsSameOrUnder(string path, string dir)
    {
        string p = Path.TrimEndingDirectorySeparator(path);
        string d = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        return p.Equals(d, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
