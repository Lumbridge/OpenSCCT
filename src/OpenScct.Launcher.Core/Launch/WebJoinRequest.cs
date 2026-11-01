using System;
using System.Diagnostics.CodeAnalysis;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Launch;

/// <summary>The only browser launch command accepted by the launcher.</summary>
public static class WebJoinRequest
{
    public const string UriPrefix = "openscct://join/";
    public const string GameArgumentPrefix = "--svm-join-server=";
    public static readonly SemVersion MinimumGameVersion = SemVersion.Parse("0.8.58");

    public static bool IsValidServerId([NotNullWhen(true)] string? serverId)
    {
        if (serverId is not { Length: >= 1 and <= 128 }) return false;
        foreach (char value in serverId)
            if (value is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '-' and not '_') return false;
        return true;
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out string? serverId)
    {
        serverId = null;
        // Check the raw URI instead of accepting URI normalisation, escaped separators, ports,
        // credentials or extra arguments. The server id is the entire, single path segment.
        if (value is null || !value.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        string candidate = value[UriPrefix.Length..];
        if (!IsValidServerId(candidate)) return false;
        serverId = candidate;
        return true;
    }
}
