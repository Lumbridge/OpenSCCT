using System.Collections.Generic;

namespace OpenScct.Net.Discovery;

/// <summary>The account's selected appearance, independently versioned from controls and other game settings.</summary>
public sealed record AccountAppearance
{
    public long Revision { get; init; }

    public IReadOnlyList<string> Spy { get; init; } = [];

    public IReadOnlyList<string> Merc { get; init; } = [];

    public string? Title { get; init; }
}

/// <summary>A complete appearance selection, conditional on the last version the editor read.</summary>
public sealed record AppearanceUpdateRequest
{
    public long ExpectedRevision { get; init; }

    public IReadOnlyList<string> Spy { get; init; } = [];

    public IReadOnlyList<string> Merc { get; init; } = [];

    public string? Title { get; init; }
}

/// <summary>A save result. A conflict includes the current server selection so the editor can reload or reapply.</summary>
public sealed record AppearanceUpdateResult
{
    public AccountAppearance Appearance { get; init; } = new();

    public string? Error { get; init; }

    public bool Conflict { get; init; }

    public bool Success => Error is null;
}
