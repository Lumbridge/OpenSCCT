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

    /// <summary>OpenSCCT: how often a server samples a spinning mover's rotation for its clients (seconds).</summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: once a second bounds a client's spin drift after a stalled frame; the original sends a spinning mover's rotation never")]
    public const float SpinSampleSeconds = 1f;

    /// <summary>OpenSCCT: a client leaves its spinning mover alone within this many seconds of spin of the server's sample.</summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: above the arrival jitter of a sample (a few ticks), below what a walking rider notices")]
    public const float SpinSlackSeconds = 0.05f;

    /// <summary>OpenSCCT: the least spin error (rotation units) a client corrects, whatever the rate.</summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: 128 units is 0.7 degrees")]
    public const int SpinSlack = 128;

    /// <summary>
    /// OpenSCCT: a client that started a leg itself keeps it when the server's start of the same leg arrives no more
    /// than this many seconds of travel behind it.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Ours: above network delay and jitter; a client further ahead than this is on a stale leg and restarts")]
    public const float ClientLegLeadSeconds = 0.5f;
}
