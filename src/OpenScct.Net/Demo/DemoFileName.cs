using System;
using System.Globalization;
using System.Linq;

namespace OpenScct.Net;

/// <summary>Readable, portable names for locally saved recordings; the identity prevents same-second collisions.</summary>
public static class DemoFileName
{
    public static string Personal(DateTimeOffset start, string map, int team, string identity)
    {
        static string Part(string value) => new(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(64).ToArray());
        string mapCode = Part(map);
        if (mapCode.Length == 0) mapCode = "UnknownMap";
        string side = team switch { 0 => "Spy", 1 => "Merc", _ => "Spectator" };
        return start.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + mapCode + "-" + side +
            "-" + Part(identity) + DemoFile.Extension;
    }
}
