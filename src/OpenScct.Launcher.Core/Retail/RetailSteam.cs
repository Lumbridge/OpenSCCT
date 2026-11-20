using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using OpenScct.Maps;
using Microsoft.Win32;

namespace OpenScct.Launcher.Retail;

internal static class RetailSteam
{
    public static RetailInstallation? Find() => Find(SteamFolders());

    internal static RetailInstallation? Find(IEnumerable<string> steamFolders)
    {
        var searched = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string steam in steamFolders)
        {
            string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            foreach (string library in SteamLibraryManifest.ReadValues(libraries, "path").Prepend(steam))
            {
                if (!Path.IsPathFullyQualified(library) || !searched.Add(library)) continue;
                string apps = Path.Combine(library, "steamapps");
                string? installDir = SteamLibraryManifest.ReadValues(Path.Combine(apps, "appmanifest_13570.acf"), "installdir").FirstOrDefault();
                if (string.IsNullOrWhiteSpace(installDir) || installDir is "." or ".." || Path.GetFileName(installDir) != installDir)
                    continue;
                var found = RetailGame.Inspect(Path.Combine(apps, "common", installDir));
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static IEnumerable<string> SteamFolders()
    {
        if (OperatingSystem.IsWindows())
        {
            string? registered = null;
            try { registered = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
            if (!string.IsNullOrWhiteSpace(registered)) yield return registered;
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
        }
    }

}
