using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Network role of an actor (Engine.Actor.ENetRole).</summary>
[Provenance(ProvenanceKind.Decoded, "Engine.u enum Actor.ENetRole")]
public enum ENetRole : byte
{
    ROLE_None = 0,
    ROLE_DumbProxy = 1,
    ROLE_SimulatedProxy = 2,
    ROLE_AutonomousProxy = 3,
    ROLE_Authority = 4,
}
