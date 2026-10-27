using System;

namespace OpenScct.Provenance;

/// <summary>
/// Marks C# code as a port of native engine code, for example
/// <c>[UcNative("APawn::physOnPipeH", Exe = 0x10A1B2C0)]</c>.
/// Exe is the function address in the game executable; Editor is the address in the editor build when known.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property |
    AttributeTargets.Field | AttributeTargets.Constructor,
    AllowMultiple = true, Inherited = false)]
public sealed class UcNativeAttribute : Attribute
{
    public UcNativeAttribute(string unitPath)
    {
        UnitPath = unitPath;
    }

    /// <summary>Native unit name, usually Class::Function as in the engine guard strings.</summary>
    public string UnitPath { get; }

    /// <summary>Address in the game executable (0 when unknown).</summary>
    public uint Exe { get; set; }

    /// <summary>Address in the editor executable (0 when unknown).</summary>
    public uint Editor { get; set; }
}
