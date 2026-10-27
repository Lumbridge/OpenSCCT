using System;

namespace OpenScct.Core.Uc;

/// <summary>
/// A reference to an UnrealScript class by its path, <c>Package.Class</c> (UnrealScript <c>class&lt;T&gt;</c>
/// values). <c>default</c> is None. Resolve it with <see cref="UcClassIndex.Find(ClassRef)"/>. Paths compare
/// ignoring ASCII case.
/// </summary>
public readonly struct ClassRef : IEquatable<ClassRef>
{
    public ClassRef(string? path)
    {
        Path = string.IsNullOrEmpty(path) || string.Equals(path, "None", StringComparison.OrdinalIgnoreCase) ? null : path;
    }

    /// <summary>The None reference.</summary>
    public static ClassRef None => default;

    /// <summary>The class path, or null for None.</summary>
    public string? Path { get; }

    public bool IsNone => Path is null;

    /// <summary>The package part, or an empty string when the path has none.</summary>
    public string Package
    {
        get
        {
            int dot = Path?.IndexOf('.', StringComparison.Ordinal) ?? -1;
            return dot < 0 ? string.Empty : Path![..dot];
        }
    }

    /// <summary>The class name (the part after the last dot).</summary>
    public string Name => Path is null ? string.Empty : Path[(Path.LastIndexOf('.') + 1)..];

    public static bool operator ==(ClassRef a, ClassRef b) => a.Equals(b);

    public static bool operator !=(ClassRef a, ClassRef b) => !a.Equals(b);

    public bool Equals(ClassRef other) => string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is ClassRef other && Equals(other);

    public override int GetHashCode() => Path is null ? 0 : StableHash.OfNameCaseless(Path);

    public override string ToString() => Path ?? "None";
}

/// <summary>An UnrealScript delegate value: the object and the name of the function it calls (None when unbound).</summary>
public readonly record struct DelegateRef(UObject? Object, Name FunctionName)
{
    public bool IsNone => FunctionName.IsNone;
}
