using System;

namespace OpenScct.Provenance;

/// <summary>
/// Records where a constant or rule came from, for example
/// <c>[Provenance(ProvenanceKind.Decoded, "SBase.u SPlayerController defaults")]</c>.
/// Several attributes may be stacked when a value was both decoded and measured.
/// </summary>
[AttributeUsage(
    AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Class |
    AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Constructor,
    AllowMultiple = true, Inherited = false)]
public sealed class ProvenanceAttribute : Attribute
{
    public ProvenanceAttribute(ProvenanceKind kind, string source)
    {
        Kind = kind;
        Source = source;
    }

    public ProvenanceKind Kind { get; }

    /// <summary>Free text: package, address, scenario or document the value came from.</summary>
    public string Source { get; }
}
