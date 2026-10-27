using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Mover constants taken from the original (spec/devices/mover.md).</summary>
public static class MoverK
{
    [Provenance(ProvenanceKind.Decoded, "Engine.Mover KeyPos[8], KeyRot[8]")]
    public const int MaxKeys = 8;

    [Provenance(ProvenanceKind.Decoded, "Engine.Mover.InterpolateTo minimum leg time")]
    public const float MinLegTime = 0.005f;

    [Provenance(ProvenanceKind.Decoded, "Engine.Mover.BeginPlay timer on a server or listen host")]
    public const float ServerTimer = 1f;

    [Provenance(ProvenanceKind.Decoded, "Engine.Mover.BeginPlay timer on a client")]
    public const float ClientTimer = 4f;

    [Provenance(ProvenanceKind.Decoded, "Engine.Mover.Timer client snap threshold")]
    public const float ClientSnapDistance = 3f;

    [Provenance(ProvenanceKind.Decoded, "Engine.Mover.EncroachingOn damage to controllerless pawns and decorations")]
    public const int CrushDamage = 10000;

    [Provenance(ProvenanceKind.Decoded, "Engine.Mover.EncroachingOn pawn damage = 1000 x EncroachDamage")]
    public const int EncroachDamageScale = 1000;

    [Provenance(ProvenanceKind.Decoded, "Engine.Mover s_WaitAndRetry and s_ReturnNoEncroach wait")]
    public const float RetryWait = 1f;

    /// <summary>
    /// Mover physics stamps fLastTimeMoverMoves while |Velocity.Z| is strictly below this (byte-checked: FABS, FCOMP
    /// 0x10c109e8 = 0.1, TEST AH 5, JP over the store; that is "less than", despite the field's name).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10a21d23..0x10a21d44: |Velocity.Z| < 0.1 stamps fLastTimeMoverMoves (lift.md open question 2)")]
    public const float VerticalMoveSpeed = 0.1f;
}
