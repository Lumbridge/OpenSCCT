using System;

namespace OpenScct.Core.Uc;

/// <summary>A label of a state's latent code: its name and bytecode offset.</summary>
public readonly record struct UcLabelInfo(string Name, int Offset)
{
    /// <summary>
    /// The suggested entry Pc of the label for <see cref="ActorState"/> (offset + 1, so it is 1 or more and labels
    /// keep their order with room between them for resume points).
    /// </summary>
    public int Pc => Offset + 1;
}

/// <summary>
/// One UnrealScript state of a class: its name, the state it extends, flags, ignored functions, labels and the
/// functions it declares. Hand-written ports build their <see cref="ActorState"/> from it (see
/// <see cref="UcActorState"/>).
/// </summary>
public sealed class UcStateInfo
{
    public UcStateInfo(string name, string? extends, UcStateFlags flags, string[] ignores, UcLabelInfo[] labels,
        string sha1, int scriptSize, UcFunctionInfo[] functions, string[]? latentCalls = null)
    {
        Name = name;
        Extends = extends;
        Flags = flags;
        Ignores = ignores ?? [];
        Labels = labels ?? [];
        Sha1 = sha1;
        ScriptSize = scriptSize;
        Functions = functions ?? [];
        LatentCalls = latentCalls ?? [];
        var pcs = new (string Name, int Pc)[Labels.Length];
        for (int i = 0; i < Labels.Length; i++)
        {
            pcs[i] = (Labels[i].Name, Labels[i].Pc);
        }

        LabelPcs = pcs;
    }

    public string Name { get; }

    /// <summary>
    /// The super state: a state of the same class for <c>extends</c>, or the parent class's state of the same name
    /// when this one redefines it; null when the state has no super state.
    /// </summary>
    public string? Extends { get; }

    public UcStateFlags Flags { get; }

    /// <summary>Functions the state ignores (port them as empty overrides).</summary>
    public string[] Ignores { get; }

    /// <summary>Labels of the state's own latent code, in bytecode order.</summary>
    public UcLabelInfo[] Labels { get; }

    /// <summary>The labels with their suggested Pc values, ready for the <see cref="ActorState"/> constructor.</summary>
    public (string Name, int Pc)[] LabelPcs { get; }

    /// <summary>SHA-1 (hex) of the state's code bytecode.</summary>
    public string Sha1 { get; }

    public int ScriptSize { get; }

    /// <summary>Functions declared in the state.</summary>
    public UcFunctionInfo[] Functions { get; }

    /// <summary>Latent functions the state code calls, sorted.</summary>
    public string[] LatentCalls { get; }

    /// <summary>The declaring class.</summary>
    public UcClassInfo Class { get; internal set; } = null!;

    /// <summary>The declaring class's UnrealScript name, as <see cref="ActorState"/> takes it.</summary>
    public string ClassName => Class.Name;

    public bool IsAuto => (Flags & UcStateFlags.STATE_Auto) != 0;

    public bool IsSimulated => (Flags & UcStateFlags.STATE_Simulated) != 0;

    public bool IsEditable => (Flags & UcStateFlags.STATE_Editable) != 0;

    /// <summary>True when the state has latent code (labels).</summary>
    public bool HasCode => Labels.Length > 0;

    /// <summary>Coverage unit of the state code, <c>uc:Pkg.Class:State@code</c>, or null without code.</summary>
    public string? CodeUnitId => HasCode ? "uc:" + Class.Path + ":" + Name + "@code" : null;

    /// <summary>Path for a <c>[Uc]</c> on the state code port: <c>Pkg.Class.State@code</c>.</summary>
    public string CodeUcPath => Class.Path + "." + Name + "@code";

    /// <summary>True when the state ignores <paramref name="function"/> (ASCII case ignored).</summary>
    public bool IgnoresFunction(string function)
    {
        foreach (string f in Ignores)
        {
            if (string.Equals(f, function, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public UcFunctionInfo? FindFunction(string function)
    {
        foreach (UcFunctionInfo f in Functions)
        {
            if (string.Equals(f.Name, function, StringComparison.OrdinalIgnoreCase))
            {
                return f;
            }
        }

        return null;
    }

    public override string ToString() => Class is null ? Name : Class.Path + "." + Name;
}
