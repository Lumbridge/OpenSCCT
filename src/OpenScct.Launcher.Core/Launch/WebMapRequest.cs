using System;
using System.Diagnostics.CodeAnalysis;

namespace OpenScct.Launcher.Launch;

/// <summary>A website link selects a map from the launcher's own catalogue.</summary>
public static class WebMapRequest
{
    public const string UriPrefix = "openscct://maps/";

    public static bool IsValidCode([NotNullWhen(true)] string? code) =>
        code is { Length: <= 64 } && WebJoinRequest.IsValidServerId(code) && char.IsAsciiLetterOrDigit(code[0]);

    public static bool TryParse(string? value, [NotNullWhen(true)] out string? code)
    {
        code = null;
        if (value is null || !value.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        string candidate = value[UriPrefix.Length..];
        if (!IsValidCode(candidate)) return false;
        code = candidate;
        return true;
    }
}
