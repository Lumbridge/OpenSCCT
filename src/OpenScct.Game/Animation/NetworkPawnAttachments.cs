using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Animation;

/// <summary>Attachment selection at the pawn's displayed time, including a weapon frozen in hand during a knockout.</summary>
public readonly record struct NetworkPawnAttachments(byte ActiveSlot, bool? WeaponInHand)
{
    public static NetworkPawnAttachments Capture(SBasePawn pawn) => new((byte)pawn.ActiveWeaponSlot,
        pawn is SPAWNDEFENSE { WeaponDummy: { } dummy } ? dummy.InHand : null);

    public static NetworkPawnAttachments For(SBasePawn pawn) =>
        pawn.Role == ENetRole.ROLE_SimulatedProxy && pawn.NetworkAttachments is { } state ? state : Capture(pawn);
}
