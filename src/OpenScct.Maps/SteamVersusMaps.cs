using System;
using System.Collections.Generic;
using System.IO;

namespace OpenScct.Maps;

/// <summary>The Versus packages included in Steam app 13570, depot 13571. The local converter reads the same list.</summary>
public static class SteamVersusMaps
{
    public const string DefaultHostMap = "AQU03";

    public static IReadOnlyList<string> All { get; } = ReadRoster();
    private static readonly HashSet<string> Codes = new(All, StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string code) => Codes.Contains(code);

    public static bool IsHostMap(string code) => Contains(code) &&
        !code.StartsWith("TME", StringComparison.OrdinalIgnoreCase) &&
        !code.StartsWith("TSP", StringComparison.OrdinalIgnoreCase) &&
        !code.Equals("Entry", StringComparison.OrdinalIgnoreCase) && !code.Equals("Lobby", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadRoster()
    {
        using var reader = new StreamReader(typeof(SteamVersusMaps).Assembly.GetManifestResourceStream("SteamVersusMaps.txt")
            ?? throw new InvalidOperationException("Missing Steam Versus map roster."));
        return Array.AsReadOnly(reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }
}
