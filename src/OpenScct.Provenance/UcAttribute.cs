using System;

namespace OpenScct.Provenance;

/// <summary>
/// Marks C# code as a port of an UnrealScript unit, for example
/// <c>[Uc("SPlayerController.s_PlayerWalkingWeapon.PlayerMove", Sha1 = "...")]</c>.
/// The unit path is Class[.State].Function; Sha1 is the hash of the original bytecode the port was written from,
/// so the coverage tool can flag ports whose source changed.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property |
    AttributeTargets.Field | AttributeTargets.Constructor | AttributeTargets.Enum,
    AllowMultiple = true, Inherited = false)]
public sealed class UcAttribute : Attribute
{
    public UcAttribute(string unitPath)
    {
        UnitPath = unitPath;
    }

    /// <summary>Class[.State].Function path of the UnrealScript unit.</summary>
    public string UnitPath { get; }

    /// <summary>SHA-1 (hex) of the original function bytecode, or null when not recorded yet.</summary>
    public string? Sha1 { get; set; }
}
