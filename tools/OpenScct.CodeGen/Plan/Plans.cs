using System;
using System.Collections.Generic;
using OpenScct.CodeGen.Emit;
using OpenScct.CodeGen.HandWritten;
using OpenScct.CodeGen.Model;

namespace OpenScct.CodeGen.Plan;

/// <summary>Where one UnrealScript package goes: its C# namespace, output folder and project.</summary>
internal sealed record PackageTarget(string Package, string Namespace, string OutputDir, string Assembly)
{
    public static readonly PackageTarget Core = new("Core", "OpenScct.Core", "src/OpenScct.Core/Generated/Core", "OpenScct.Core");
    public static readonly PackageTarget Engine = new("Engine", "OpenScct.Core.Engine", "src/OpenScct.Core/Generated/Engine", "OpenScct.Core");
    public static readonly PackageTarget SBase = new("SBase", "OpenScct.Game.SBase", "src/OpenScct.Game/Generated/SBase", "OpenScct.Game");

    public static readonly PackageTarget[] Default = [Core, Engine, SBase];
}

/// <summary>Generator settings. The defaults are the project's; tests use smaller ones.</summary>
internal sealed class GeneratorOptions
{
    public IReadOnlyList<PackageTarget> Targets { get; init; } = PackageTarget.Default;

    /// <summary>Script properties the runtime keeps natively (object internals); never generated.</summary>
    public IReadOnlyDictionary<string, string[]> SkipVars { get; init; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Core.Object"] = ["Class", "Name", "ObjectFlags", "Outer", "ObjectInternal"],
    };

    /// <summary>
    /// Classes (with subclasses) whose objects are content: fields of these types hold an AssetRef path. Engine.MeshInstance
    /// and its subclasses are runtime objects and stay typed.
    /// </summary>
    public IReadOnlyList<string> AssetRoots { get; init; } =
    [
        "Engine.Material", "Engine.Primitive", "Engine.MeshAnimation", "Engine.Animation", "Engine.Sound", "Engine.Font",
        "Engine.Palette", "Engine.ParticleEmitter", "Engine.AnimNotify",
    ];

    public IReadOnlyList<string> AssetExceptions { get; init; } = ["Engine.MeshInstance"];

    /// <summary>UnrealScript structs mapped to hand-written runtime structs.</summary>
    public IReadOnlyDictionary<string, string> MappedStructs { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Core.Object.Vector"] = "OpenScct.Core.FVector",
        ["Core.Object.Rotator"] = "OpenScct.Core.FRotator",
        ["Core.Object.Coords"] = "OpenScct.Core.FCoords",
    };

    public string RootClass { get; init; } = "Core.Object";

    public string ActorClass { get; init; } = "Engine.Actor";
}

internal sealed class EnumPlan
{
    public required UcEnumDef Def { get; init; }

    public required PackageTarget Target { get; init; }

    public required string CsName { get; init; }

    /// <summary>True when the runtime already declares this enum by hand.</summary>
    public bool HandWritten { get; init; }

    public CsNamed Type => new(Target.Namespace, CsName, false, "enum");
}

internal sealed class StructPlan
{
    public required UcStructDef Def { get; init; }

    public required PackageTarget Target { get; init; }

    public required string CsNamespace { get; init; }

    public required string CsName { get; init; }

    /// <summary>True for structs mapped to hand-written runtime types (FVector, FRotator, FCoords).</summary>
    public bool HandWritten { get; init; }

    /// <summary>True for the mapped runtime structs, whose values are written with their member-order constructor.</summary>
    public bool Mapped { get; init; }

    public List<FieldPlan> Members { get; } = new();

    public CsNamed Type => new(CsNamespace, CsName, false, "struct");
}

/// <summary>A script const, as a C# const or static readonly field.</summary>
internal sealed class ConstPlan
{
    public required string Name { get; init; }

    public required string CsName { get; init; }

    /// <summary>The declaration after the access modifier, for example <c>const int X = 4</c>.</summary>
    public required string Declaration { get; init; }

    public bool New { get; set; }
}

/// <summary>One script property of a class or member of a struct, and what the generator does with it.</summary>
internal sealed class FieldPlan
{
    public required UcVar Var { get; init; }

    public required string CsName { get; set; }

    public required CsType CsType { get; init; }

    /// <summary>The OpenScct.Core.Uc.UcPropertyKind member name.</summary>
    public required string Kind { get; init; }

    /// <summary>True when the generator declares the member; false when it exists by hand or is skipped.</summary>
    public bool Generated { get; set; } = true;

    public bool New { get; set; }

    public string? Initializer { get; set; }

    /// <summary>The hand-written member this property binds to, if any.</summary>
    public HandMember? Hand { get; set; }

    /// <summary>The class whose hand-written part declares <see cref="Hand"/>.</summary>
    public ClassPlan? HandOwner { get; set; }

    /// <summary>True when SetDefaults may assign the member (generated, or hand-written, accessible and of the same type).</summary>
    public bool Assignable { get; set; } = true;

    /// <summary>Why the member is not generated (for the report), or null.</summary>
    public string? SkipReason { get; set; }

    /// <summary>True for script internals the runtime has no member for at all.</summary>
    public bool NoMember { get; set; }
}

/// <summary>One class to generate: names, hierarchy, hand-written part and member plans.</summary>
internal sealed class ClassPlan
{
    public required UcClass Class { get; init; }

    public required PackageTarget Target { get; init; }

    public required string CsName { get; init; }

    public ClassPlan? Super { get; set; }

    public HandType? Hand { get; set; }

    public bool IsActor { get; init; }

    public bool IsRoot { get; init; }

    public bool IsIntrinsic => !Class.IsScript;

    public bool IsAbstract => Class.IsAbstract || (Hand?.IsAbstract ?? false);

    public List<FieldPlan> Fields { get; } = new();

    public List<ConstPlan> Consts { get; } = new();

    /// <summary>Names that hide namespace types inside this class (members and nested types of the chain, script functions and states).</summary>
    public HashSet<string> Shadowing { get; } = new(StringComparer.Ordinal);

    /// <summary>Names that hide types in type positions (nested types of the chain and script states).</summary>
    public HashSet<string> TypeShadowing { get; } = new(StringComparer.Ordinal);

    /// <summary>The factory lambda for the class info, or null.</summary>
    public string? Factory { get; set; }

    public bool EmitSetDefaults { get; set; } = true;

    public bool EmitGetUcClass { get; set; } = true;

    public CsNamed Type => new(Target.Namespace, CsName, true, "class");

    public int Depth => Super is null ? 0 : Super.Depth + 1;

    public IEnumerable<ClassPlan> Chain()
    {
        for (ClassPlan? c = this; c is not null; c = c.Super)
        {
            yield return c;
        }
    }

    public override string ToString() => Class.Path;
}
