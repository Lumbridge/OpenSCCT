using System;

namespace OpenScct.Content;

/// <summary>An SVMap file breaks the format rules (spec/svmap/1.0). <see cref="File"/> names the file when known.</summary>
public sealed class SvMapFormatException : Exception
{
    public SvMapFormatException(string message)
        : base(message)
    {
    }

    public SvMapFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SvMapFormatException(string? file, string message, Exception? innerException = null)
        : base(file is null ? message : $"{file}: {message}", innerException)
    {
        File = file;
    }

    /// <summary>Path of the offending file relative to the map root (<c>sim/gameplay.json</c>), when known.</summary>
    public string? File { get; }
}
