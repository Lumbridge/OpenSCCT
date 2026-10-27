using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController sticky blast (spec/combat/drop-attack.md rule 13): landing on or bumping into a
// pawn whose sticky shock is still active kills the jumper with a blast. The walking and falling physics find the
// pawn and raise StickyBlast (SPlayerController.Movement.Physics.cs routes it here unless the state ignores it).
public partial class SPlayerController
{
    private static readonly Name SNAME_TellClientToStickyBlast = new("TellClientToStickyBlast");

    /// <summary>
    /// The sticky blast on the owner: 200 SDTStickyBlast damage reported to the server (from the sticky pawn, along
    /// the line between them), the blast loop, a flight backward and down at twice BlastSpeedMin, then s_Blast. The
    /// server passes it to a remote owner.
    /// </summary>
    [Uc("SBase.SPlayerController.StickyBlast", Sha1 = "ea0661b15a8011d7cf0566d4894a88f97fb22f9d")]
    [PortOf("uc:SBase.SPlayerController:StickyBlast", Spec = "spec/combat/drop-attack.md#sticky-blast")]
    public void StickyBlast(Actor? Other)
    {
        if (!IAmOnClient())
        {
            if (IsRemotePlayer)
            {
                TellClientToStickyBlast(Other);
            }

            return;
        }

        if (SBPawn is not { } pawn || Other is null)
        {
            return;
        }

        DamagePawn_C2S(pawn, K.StickyBlastDamage, Other as SBasePawn, pawn.Location, pawn.Location - Other.Location,
            SDamageType.SDTStickyBlast);
        pawn.ChangeEnumAction(SBasePawn.SA_BLAST_LOOP);
        FVector dir = pawn.Rotation.Vector();
        dir.Z = K.StickyBlastDirZ;
        pawn.SetVelocityAndOrientationForBlast(dir.SafeNormal() * (K.StickyBlastSpeedFactor * BlastSpeedMin));
        eventChangeState(CNAME_s_Blast);
    }

    /// <summary>The server's sticky blast for a remote player (server to owner); the owner's state decides.</summary>
    [Uc("SBase.SPlayerController.TellClientToStickyBlast", Sha1 = "219a13545ecea860a82b94cb355e2f58704e41e2")]
    [PortOf("uc:SBase.SPlayerController:TellClientToStickyBlast", Spec = "spec/combat/drop-attack.md#sticky-blast")]
    [PortOf("rep:SBase.SPlayerController.TellClientToStickyBlast", Spec = "spec/combat/drop-attack.md#sticky-blast")]
    public void TellClientToStickyBlast(Actor? Other)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(SNAME_TellClientToStickyBlast) { Actor1 = Other });
            return;
        }

        eventStickyBlast(Other);
    }
}
