using System;
using System.IO;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Settings;

/// <summary>
/// A queued import of the player's Enhanced SCCT Versus settings: one file in the game's user data naming the install
/// folder (an empty file means the profile and key layouts in ProgramData only). The game carries the import out the
/// next time it starts and removes the file (game: Options/EnhancedImportStartup), so the launcher needs none of the
/// game's settings code.
/// </summary>
public static class EnhancedImportRequest
{
    /// <summary>The same name the game looks for.</summary>
    public const string FileName = "enhanced_import.request";

    /// <summary>The queued folder, empty for the profile-only import, or null when nothing is queued.</summary>
    public static string? Pending(string userDataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDataDirectory);
        try
        {
            return File.ReadAllText(Path.Combine(userDataDirectory, FileName)).Trim();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>Queues <paramref name="folder"/> (empty: profile only), or with null removes a queued import.</summary>
    public static void Save(string userDataDirectory, string? folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDataDirectory);
        string path = Path.Combine(userDataDirectory, FileName);
        if (folder is null)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(userDataDirectory);
        AtomicFile.WriteAllText(path, folder.Trim());
    }

    /// <summary>
    /// Whether <paramref name="folder"/> is inside an Enhanced install: it or a folder above it holds
    /// <c>System\SCCT_Versus.exe</c> or Reloaded's <c>System\SCCT_Versus.config</c> (the game's own test).
    /// </summary>
    public static bool LooksLikeInstall(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        var dir = new DirectoryInfo(folder);
        for (int depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            string system = Path.Combine(dir.FullName, "System");
            if (File.Exists(Path.Combine(system, "SCCT_Versus.config")) || File.Exists(Path.Combine(system, "SCCT_Versus.exe")) ||
                File.Exists(Path.Combine(system, "Default_kbd_0.ini")))
                return true;
        }

        return false;
    }
}
