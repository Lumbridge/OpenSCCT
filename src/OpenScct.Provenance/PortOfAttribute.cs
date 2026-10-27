using System;

namespace OpenScct.Provenance;

/// <summary>
/// Links C# code to a coverage unit id (uc:, nat:, eng:, rep:, def:, fmt:, rl:) and optionally to the spec anchor
/// it was written from, for example <c>[PortOf("nat:APawn::physWalking", Spec = "spec/physics/walking.md#rules")]</c>.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property |
    AttributeTargets.Field | AttributeTargets.Constructor | AttributeTargets.Enum,
    AllowMultiple = true, Inherited = false)]
public sealed class PortOfAttribute : Attribute
{
    public PortOfAttribute(string unitId)
    {
        UnitId = unitId;
    }

    /// <summary>Coverage unit id.</summary>
    public string UnitId { get; }

    /// <summary>Path and anchor of the behaviour spec, or null.</summary>
    public string? Spec { get; set; }
}
