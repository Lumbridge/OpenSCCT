namespace OpenScct.Core.Uc;

/// <summary>Kind of an UnrealScript property, after the generator's type mapping.</summary>
public enum UcPropertyKind : byte
{
    Bool,
    Byte,

    /// <summary>A byte with an enum type.</summary>
    Enum,
    Int,
    Float,
    Name,
    String,
    Struct,

    /// <summary>A reference to an actor or another non-content object (typed C# reference).</summary>
    Object,

    /// <summary>A reference to a content object, held as an <see cref="AssetRef"/>.</summary>
    Asset,

    /// <summary>A class reference, held as a <see cref="ClassRef"/>.</summary>
    Class,

    /// <summary>A delegate, held as a <see cref="DelegateRef"/>.</summary>
    Delegate,

    /// <summary>A dynamic array, held as a List.</summary>
    Array,
}

/// <summary>
/// One script property of a class: its original name and type, flags and, where the reflection dumps have them,
/// the native layout of the game build (offset and element size in bytes).
/// </summary>
public sealed class UcPropertyInfo
{
    public UcPropertyInfo(string name, string type, UcPropertyKind kind, UcPropertyFlags flags, int arrayDim,
        string? category, int offset, int elementSize, int repOffset, string? csharpName = null, bool handWritten = false)
    {
        Name = name;
        Type = type;
        Kind = kind;
        Flags = flags;
        ArrayDim = arrayDim;
        Category = category;
        Offset = offset;
        ElementSize = elementSize;
        RepOffset = repOffset;
        CSharpName = csharpName ?? name;
        HandWritten = handWritten;
    }

    /// <summary>The UnrealScript name.</summary>
    public string Name { get; }

    /// <summary>The UnrealScript type as declared: <c>bool</c>, <c>Sound</c>, <c>array&lt;int&gt;</c>, <c>class&lt;Pawn&gt;</c>...</summary>
    public string Type { get; }

    public UcPropertyKind Kind { get; }

    public UcPropertyFlags Flags { get; }

    /// <summary>Static array size; 1 for a plain property.</summary>
    public int ArrayDim { get; }

    /// <summary>Editor category, or null.</summary>
    public string? Category { get; }

    /// <summary>Byte offset in the native object of the game build, or -1 when unknown.</summary>
    public int Offset { get; }

    /// <summary>Native size of one element in bytes, or 0 when unknown.</summary>
    public int ElementSize { get; }

    /// <summary>Offset of the replication condition in the class's replication script, or -1 when not replicated.</summary>
    public int RepOffset { get; }

    /// <summary>The C# member name (differs from <see cref="Name"/> only when the generator had to rename it).</summary>
    public string CSharpName { get; }

    /// <summary>True when the member is declared by hand in the runtime rather than by the generator.</summary>
    public bool HandWritten { get; }

    /// <summary>The class that declares the property.</summary>
    public UcClassInfo Class { get; internal set; } = null!;

    public bool IsReplicated => (Flags & UcPropertyFlags.CPF_Net) != 0;

    public bool IsStaticArray => ArrayDim > 1;

    /// <summary>The coverage unit of this property's replication, or null when it is not replicated.</summary>
    public string? RepUnitId => IsReplicated ? "rep:" + Class.Path + "." + Name : null;

    public override string ToString() => Class is null ? Name : Class.Path + "." + Name;
}
