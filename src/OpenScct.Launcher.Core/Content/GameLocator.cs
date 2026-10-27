using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenScct.Launcher.Content;

/// <summary>
/// Finds the player's Splinter Cell Chaos Theory Versus folder without asking: the Steam copy (app 13570) in any of
/// their Steam libraries, then common folders. The content builder needs the Versus folder (System\_PC_ and
/// Packages); for a game root that has a Versus subfolder, that subfolder is used.
/// </summary>
public static partial class GameLocator
{
    /// <summary>Steam's app id of Splinter Cell: Chaos Theory.</summary>
    public const string SteamAppId = "13570";

    /// <summary>The first Versus folder found, or null.</summary>
    public static string? Find() => Candidates().Select(Normalize).FirstOrDefault(p => p is not null);

    /// <summary>
    /// The Versus folder for a path the player picked: the path itself when it has System and Packages, else its
    /// Versus subfolder when that has them; null when neither does.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (ContentBuildPlan.LooksLikeOriginalInstall(path))
        {
            return Path.GetFullPath(path);
        }

        string versus = Path.Combine(path, "Versus");
        return ContentBuildPlan.LooksLikeOriginalInstall(versus) ? Path.GetFullPath(versus) : null;
    }

    private static IEnumerable<string> Candidates()
    {
        foreach (string library in SteamLibraries())
        {
            string steamapps = Path.Combine(library, "steamapps");
            string manifest = Path.Combine(steamapps, $"appmanifest_{SteamAppId}.acf");
            string? installDir = File.Exists(manifest) ? VdfValue(SafeRead(manifest), "installdir") : null;
            if (installDir is not null)
            {
                yield return Path.Combine(steamapps, "common", installDir);
            }
        }

        string[] roots = [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ];
        foreach (string root in roots.Where(r => r.Length > 0))
        {
            yield return Path.Combine(root, "Ubisoft", "Tom Clancy's Splinter Cell Chaos Theory");
            yield return Path.Combine(root, "Steam", "steamapps", "common", "Splinter Cell Chaos Theory");
        }
    }

    /// <summary>Every Steam library folder: Steam's own folder (from the registry) and those in libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> SteamLibraries()
    {
        var steamRoots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
            {
                foreach (string name in new[] { "SteamPath", "InstallPath" })
                {
                    if (Microsoft.Win32.Registry.GetValue(key, name, null) is string path && path.Length > 0)
                    {
                        steamRoots.Add(path.Replace('/', Path.DirectorySeparatorChar));
                    }
                }
            }
        }

        steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        var libraries = new List<string>();
        foreach (string root in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            libraries.Add(root);
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in PathEntry().Matches(SafeRead(vdf)))
                {
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal));
                }
            }
        }

        return libraries.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? VdfValue(string text, string key)
    {
        Match m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();
}
