using System;
using System.Linq;

namespace OpenScct.Net.Discovery;

/// <summary>The last server-granted kit in a round. Older reports omit this metadata.</summary>
public sealed record RecordedLoadout
{
    public string Weapon { get; init; } = string.Empty;
    public string[] Gadgets { get; init; } = [];

    /// <summary>Bounds persisted metadata and takes an independent snapshot of its mutable array.</summary>
    public RecordedLoadout Snapshot() => new()
    {
        Weapon = ClassName(Weapon),
        Gadgets = (Gadgets ?? []).Take(8).Select(ClassName).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
    };

    private static string ClassName(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_') ? value : string.Empty;
}
