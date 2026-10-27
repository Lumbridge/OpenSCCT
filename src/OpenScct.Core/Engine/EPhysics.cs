using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Actor physics modes (Engine.Actor.EPhysics). Values are the SCCT Versus ones, not stock UE2.</summary>
[Provenance(ProvenanceKind.Decoded, "Engine.u enum Actor.EPhysics")]
public enum EPhysics : byte
{
    PHYS_None = 0,
    PHYS_Walking = 1,
    PHYS_Falling = 2,
    PHYS_Rotating = 3,
    PHYS_Projectile = 4,
    PHYS_RootMotion = 5,
    PHYS_OnPipe = 6,

    /// <summary>Zip line.</summary>
    PHYS_Linear = 7,

    /// <summary>Fence climbing.</summary>
    PHYS_OnVerticalPlane = 8,
    PHYS_Ledge = 9,

    /// <summary>Split jump.</summary>
    PHYS_Vandamme = 10,
    PHYS_Repositionnal = 11,
    PHYS_FollowParent = 12,
    PHYS_OnLadder = 13,

    /// <summary>Hand over hand pipe.</summary>
    PHYS_OnPipeH = 14,
    PHYS_MovingBrush = 15,
    PHYS_Flying = 16,
}
