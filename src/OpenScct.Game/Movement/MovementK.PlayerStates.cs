using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

// Constants of the back-to-wall fire states and the hand-item throw states (spec/movement/back-to-wall.md,
// spec/objectives/disk-flag.md).
public static partial class MovementK
{
    [Provenance(ProvenanceKind.Decoded, "s_BTWFireStandLeft/CrouchLeft.PlayerMove: yaw -10000..50 around pawn yaw + 32768")]
    public const int BtwFireLeftYawMin = -10000;

    [Provenance(ProvenanceKind.Decoded, "s_BTWFireStandLeft/CrouchLeft.PlayerMove: yaw -10000..50 around pawn yaw + 32768")]
    public const int BtwFireLeftYawMax = 50;

    [Provenance(ProvenanceKind.Decoded, "s_BTWFireStandRight/CrouchRight.PlayerMove: yaw -50..10000 around pawn yaw + 32768")]
    public const int BtwFireRightYawMin = -50;

    [Provenance(ProvenanceKind.Decoded, "s_BTWFireStandRight/CrouchRight.PlayerMove: yaw -50..10000 around pawn yaw + 32768")]
    public const int BtwFireRightYawMax = 10000;

    [Provenance(ProvenanceKind.Decoded, "s_BTWFire*.PlayerMove: pitch -10000..10000")]
    public const int BtwFirePitch = 10000;

    [Provenance(ProvenanceKind.Decoded, "s_ThrowGoTo / s_Throw PlayerMove: UpdatePawnRotationMario(DeltaTime, 1000, 0)")]
    public const int ThrowTurnForward = 1000;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.ThrowObject: (X + Z) * Square(fThrowForce + 1) * 500 / 2")]
    public const float ThrowObjectSpeed = 500f / 2f;
}
