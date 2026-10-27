using System;

namespace OpenScct.Core.Uc;

/// <summary>
/// A reference to a content object (material, texture, static mesh, mesh, animation, sound, font, particle emitter
/// template...) by its original object path, <c>Package[.Group].Name</c>. Generated fields of those types hold one
/// of these instead of an object: the simulation never loads content, presentation code resolves the path.
/// <c>default</c> is None. Paths compare ignoring ASCII case, as UE2 object names do.
/// </summary>
public readonly struct AssetRef : IEquatable<AssetRef>
{
    public AssetRef(string? path)
    {
        Path = string.IsNullOrEmpty(path) || string.Equals(path, "None", StringComparison.OrdinalIgnoreCase) ? null : path;
    }

    /// <summary>The None reference.</summary>
    public static AssetRef None => default;

    /// <summary>The object path, or null for None.</summary>
    public string? Path { get; }

    public bool IsNone => Path is null;

    /// <summary>The first path segment (the package), or an empty string for None.</summary>
    public string Package => Path is null ? string.Empty : Path[..IndexOrLength(Path.IndexOf('.', StringComparison.Ordinal), Path)];

    /// <summary>The last path segment (the object name), or an empty string for None.</summary>
    public string Name => Path is null ? string.Empty : Path[(Path.LastIndexOf('.') + 1)..];

    public static bool operator ==(AssetRef a, AssetRef b) => a.Equals(b);

    public static bool operator !=(AssetRef a, AssetRef b) => !a.Equals(b);

    public bool Equals(AssetRef other) => string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is AssetRef other && Equals(other);

    public override int GetHashCode() => Path is null ? 0 : StableHash.OfNameCaseless(Path);

    public override string ToString() => Path ?? "None";

    private static int IndexOrLength(int index, string text) => index < 0 ? text.Length : index;
}
