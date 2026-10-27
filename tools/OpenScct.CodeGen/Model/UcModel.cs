using System;
using System.Collections.Generic;

namespace OpenScct.CodeGen.Model;

/// <summary>A resolved UnrealScript property type.</summary>
internal abstract record UType
{
    /// <summary>The type as UnrealScript writes it (short names).</summary>
    public abstract string UcText { get; }

    public static string ShortName(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[(dot + 1)..];
    }
}

internal sealed record UBool : UType
{
    public static readonly UBool Instance = new();

    public override string UcText => "bool";
}

internal sealed record UByte : UType
{
    public static readonly UByte Instance = new();

    public override string UcText => "byte";
}

internal sealed record UInt : UType
{
    public static readonly UInt Instance = new();

    public override string UcText => "int";
}

internal sealed record UFloat : UType
{
    public static readonly UFloat Instance = new();

    public override string UcText => "float";
}

internal sealed record UName : UType
{
    public static readonly UName Instance = new();

    public override string UcText => "name";
}

internal sealed record UString : UType
{
    public static readonly UString Instance = new();

    public override string UcText => "string";
}

/// <summary>A byte with an enum type; <paramref name="EnumPath"/> is Package.Class.Enum.</summary>
internal sealed record UEnum(string EnumPath) : UType
{
    public override string UcText => ShortName(EnumPath);
}

/// <summary>A struct; <paramref name="StructPath"/> is Package.Class.Struct.</summary>
internal sealed record UStruct(string StructPath) : UType
{
    public override string UcText => ShortName(StructPath);
}

/// <summary>An object reference; <paramref name="ClassPath"/> is Package.Class.</summary>
internal sealed record UObjectRef(string ClassPath) : UType
{
    public override string UcText => ShortName(ClassPath);
}

/// <summary>A class reference with its meta class.</summary>
internal sealed record UClassRef(string MetaClassPath) : UType
{
    public override string UcText => MetaClassPath == "Core.Object" ? "class" : "class<" + ShortName(MetaClassPath) + ">";
}

internal sealed record UDelegate(string FunctionPath) : UType
{
    public override string UcText => "delegate<" + ShortName(FunctionPath) + ">";
}

internal sealed record UArray(UType Inner) : UType
{
    public override string UcText => Inner is UArray or UClassRef ? "array<" + Inner.UcText + " >" : "array<" + Inner.UcText + ">";
}

/// <summary>A script property of a class or a member of a struct.</summary>
internal sealed class UcVar
{
    public required string Name { get; init; }

    /// <summary>Declared type text (from the decompiler index, or built from the resolved type).</summary>
    public required string TypeText { get; init; }

    public required UType Type { get; set; }

    public uint Flags { get; init; }

    public int ArrayDim { get; init; } = 1;

    public string? Category { get; init; }

    /// <summary>Native offset in the game build (editor build when the game dump lacks it), or -1.</summary>
    public int Offset { get; init; } = -1;

    public int ElementSize { get; init; }

    public uint BitMask { get; init; }

    public int RepOffset { get; init; } = -1;
}

internal sealed class UcParam
{
    public required string Name { get; init; }

    public required string TypeText { get; init; }

    public uint Flags { get; init; }
}

internal sealed class UcFunc
{
    public required string Name { get; init; }

    public required string Sha1 { get; init; }

    public uint Flags { get; init; }

    public int NativeIndex { get; init; }

    public int OperPrecedence { get; init; }

    public int RepOffset { get; init; } = -1;

    public int ScriptSize { get; init; }

    /// <summary>Source line in the decompiler output; only used to keep functions in source order.</summary>
    public int Line { get; init; }

    public string? ReturnType { get; init; }

    public List<UcParam> Params { get; } = new();

    public List<string> LatentCalls { get; } = new();
}

internal sealed class UcLabel
{
    public required string Name { get; init; }

    public int Offset { get; init; }
}

internal sealed class UcStateDef
{
    public required string Name { get; init; }

    public string? Extends { get; init; }

    public uint Flags { get; init; }

    public List<string> Ignores { get; } = new();

    public List<UcLabel> Labels { get; } = new();

    public required string Sha1 { get; init; }

    public int ScriptSize { get; init; }

    public List<UcFunc> Functions { get; } = new();

    public List<string> LatentCalls { get; } = new();
}

internal sealed class UcRepBlockDef
{
    public bool Reliable { get; init; }

    public int Offset { get; init; }

    public required string Condition { get; init; }

    /// <summary>Member name and whether it is a function.</summary>
    public List<(string Name, bool IsFunction)> Members { get; } = new();
}

internal sealed class UcConst
{
    public required string Name { get; init; }

    public required string ValueText { get; init; }
}

/// <summary>One UnrealScript class: from the decompiler index (script classes) or the reflection dumps (intrinsic).</summary>
internal sealed class UcClass
{
    public required string Path { get; init; }

    public string Package => Path[..Path.IndexOf('.', StringComparison.Ordinal)];

    public string Name => Path[(Path.IndexOf('.', StringComparison.Ordinal) + 1)..];

    public string? SuperPath { get; init; }

    /// <summary>True for classes with script (in the decompiler index); false for intrinsic C++ classes.</summary>
    public bool IsScript { get; init; }

    public uint Flags { get; set; }

    public string? Config { get; init; }

    public int PropertiesSize { get; set; }

    public List<UcVar> Vars { get; } = new();

    public List<UcFunc> Functions { get; } = new();

    public List<UcStateDef> States { get; } = new();

    public List<string> EnumNames { get; } = new();

    public List<string> StructNames { get; } = new();

    public List<UcConst> Consts { get; } = new();

    public List<UcRepBlockDef> Replication { get; } = new();

    public List<string> ReplicationInherited { get; } = new();

    public string? ReplicationSha1 { get; init; }

    public int ReplicationScriptSize { get; init; }

    public bool IsAbstract => (Flags & 0x1) != 0;

    public override string ToString() => Path;
}

internal sealed class UcEnumDef
{
    public required string Path { get; init; }

    public string Name => UType.ShortName(Path);

    /// <summary>Owner class path.</summary>
    public string Owner => Path[..Path.LastIndexOf('.')];

    public string Package => Path[..Path.IndexOf('.', StringComparison.Ordinal)];

    public List<string> Members { get; } = new();
}

internal sealed class UcStructDef
{
    public required string Path { get; init; }

    public string Name => UType.ShortName(Path);

    public string Owner => Path[..Path.LastIndexOf('.')];

    public string Package => Path[..Path.IndexOf('.', StringComparison.Ordinal)];

    public string? SuperPath { get; init; }

    public int PropertiesSize { get; init; }

    /// <summary>Own members (not the super struct's), in layout order.</summary>
    public List<UcVar> Members { get; } = new();
}

/// <summary>Everything the generator reads, resolved and cross-linked.</summary>
internal sealed class UcModel
{
    public Dictionary<string, UcClass> Classes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, UcEnumDef> Enums { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, UcStructDef> Structs { get; } = new(StringComparer.Ordinal);

    /// <summary>Effective class defaults (merged down the super chain, localized text applied), by class path.</summary>
    public Dictionary<string, Dictionary<string, Json.JValue>> Defaults { get; } = new(StringComparer.Ordinal);

    public UcClass? FindClass(string path) => Classes.GetValueOrDefault(path);

    /// <summary>The class and its ancestors, nearest first.</summary>
    public IEnumerable<UcClass> Chain(UcClass c)
    {
        for (UcClass? x = c; x is not null; x = x.SuperPath is null ? null : FindClass(x.SuperPath))
        {
            yield return x;
        }
    }

    public bool IsA(string classPath, string ancestorPath)
    {
        for (string? p = classPath; p is not null; p = FindClass(p)?.SuperPath)
        {
            if (p == ancestorPath)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A struct's members including those of its super structs, in layout order.</summary>
    public List<UcVar> AllStructMembers(UcStructDef s)
    {
        var result = new List<UcVar>();
        if (s.SuperPath is not null && Structs.TryGetValue(s.SuperPath, out UcStructDef? super))
        {
            result.AddRange(AllStructMembers(super));
        }

        result.AddRange(s.Members);
        return result;
    }
}
