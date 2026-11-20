using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenScct.Maps;

public static partial class SteamLibraryManifest
{
    public static bool IsManifestInstall(DirectoryInfo game)
    {
        if (game.Parent is not { } common || !common.Name.Equals("common", StringComparison.OrdinalIgnoreCase)
            || common.Parent is not { } apps || !apps.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            return false;
        string manifest = Path.Combine(apps.FullName, "appmanifest_13570.acf");
        return ReadValues(manifest, "installdir").Any(name =>
            name.Equals(game.Name, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    // Steam writes these values as one quoted key/value pair per line, with escaped backslashes in paths.
    [GeneratedRegex("""^\s*"([^"]+)"\s+"((?:\\.|[^"\\])*)"\s*$""")]
    private static partial Regex Pair();

    public static IEnumerable<string> ReadValues(string path, string key)
    {
        try
        {
            return File.ReadAllLines(path).Select(line => Pair().Match(line))
                .Where(match => match.Success && match.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
                .Select(match => match.Groups[2].Value.Replace(@"\\", @"\").Replace("\\\"", "\"")).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
