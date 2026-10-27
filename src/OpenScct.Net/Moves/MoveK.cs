using OpenScct.Provenance;

namespace OpenScct.Net.Moves;

/// <summary>Constants of the SCCT move protocol (spec/native/net-moves.md, spec/net/move-protocol.md).</summary>
public static class MoveK
{
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 2: batching window cap, game 0x10aad5c0")]
    public const float MaxPendingWindow = 0.09f;

    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [IpDrv.TcpNetDriver] maxAcceptableUnAckBunch; reader net-moves.md rule 2")]
    public const int MaxAcceptableUnAckBunch = 40;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 1: split time epsilon")]
    public const float SplitEpsilon = 1e-5f;

    /// <summary>OldTimeDelta is in 0.5 ms units: client × 2000, server × 0.0005.</summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rules 3.4 and 11.1")]
    public const float OldTimeDeltaUnit = 0.0005f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.1: old stamp margin, game 0x10aac4a0")]
    public const float OldStampMargin = 0.001f;

    /// <summary>Accelerations go over the wire times 10 (client × 10, server × 0.1).</summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 3.5")]
    public const float AccelWireScale = 10f;

    /// <summary>Velocities in big moves go over the wire times 100.</summary>
    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rule 6; net-moves.md rule 13")]
    public const float VelocityWireScale = 100f;

    /// <summary>Unit vectors in big move resends go over the wire times 1000.</summary>
    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rule 6")]
    public const float UnitWireScale = 1000f;

    [Provenance(ProvenanceKind.Decoded, "SBase.u SPlayerController NumberOfSavedPos; GetFreeSMove editor 0x111e7160")]
    public const int SavedMoveRing = 100;

    /// <summary>A big move sets the resend counter to this; resends go while it is below it.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.u SPlayerController C_MaxBSMReSend 4; net-moves.md rule 3.1")]
    public const int C_MaxBSMReSend = 4;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.3, game 0x10aac4a0")]
    public const float TimeMarginFactor = 1.01f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.3")]
    public const float MaxTimeMarginHigh = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.3; def:Engine.PlayerController MaxTimeMargin")]
    public const float MaxTimeMarginLow = 0.35f;

    [Provenance(ProvenanceKind.Decoded, "def:Engine.PlayerController MaxResponseTime; net-moves.md rule 11.2")]
    public const float MaxResponseTime = 0.7f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.6")]
    public const float SpyWalkCap = 423.5f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.6")]
    public const float SpyRollCap = 603.2f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.6")]
    public const float MercCap = 346.5f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.6")]
    public const float MercChargeCap = 770f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.6: 0.00033333 in game 0x10aac4a0")]
    public const float WalkSpeedDivisor = 3000f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.6: 0.005235602 in game 0x10aac4a0")]
    public const float LinearSpeedDivisor = 191f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 12")]
    public const float ForcedCorrectionInterval = 1.0f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 14, PCRot game 0x10aaec30")]
    public const float ForcedCorrectionIntervalPCRot = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 12")]
    public const float MinCorrectionGap = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 14")]
    public const float MinCorrectionGapPCRot = 0.1f;

    /// <summary>Corrections wait at least this many bytes' worth of the client's net speed (180 / NetSpeed s).</summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 12")]
    public const float CorrectionRateBytes = 180f;

    /// <summary>The error a forced correction counts (squared).</summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 12")]
    public const float ForcedCorrectionError = 10000f;

    /// <summary>No correction at or below this squared error (1.73 uu, the client's fErrorMax).</summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 12")]
    public const float CorrectionErrorSquared = 3f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 12: squared error times 10 in root motion")]
    public const float RootMotionErrorScale = 10f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 12: reinit above 160000 (400 uu)")]
    public const float ReinitErrorSquared = 160000f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.5: 3 s of physics mismatch")]
    public const float PhysicsMismatchReinit = 3f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 12 and move-protocol.md rule 20: mover windows of 3 s")]
    public const float MoverWindow = 3f;

    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 13: snap radius 100 (10000 squared)")]
    public const float BigMoveSnapRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rule 20: fErrorMax")]
    public const float fErrorMax = 1.73f;

    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rule 12: MSA match tolerance of the action RPCs")]
    public const float MsaTolerance = 0.01f;

    [Provenance(ProvenanceKind.Decoded, "move-protocol.md rule 1: camera position interval")]
    public const float CameraUpdateInterval = 0.14f;

    [Provenance(ProvenanceKind.Decoded, "def:Engine.PlayerController ClientUpdateTime")]
    public const float ClientUpdateTimeDefault = -10f;

    [Provenance(ProvenanceKind.Decoded, "def:SBase.SPlayerController fLastTimeOnAMover")]
    public const float LastTimeOnAMoverDefault = -3f;
}
