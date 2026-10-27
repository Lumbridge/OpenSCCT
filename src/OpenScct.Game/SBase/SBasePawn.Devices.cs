using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The SBasePawn functions the map devices call.
public partial class SBasePawn
{
    /// <summary>
    /// A crushing mover ran into this pawn: a pawn it is grabbing dies first, then this pawn, each with this pawn's
    /// Health + 100 of SDTBase damage from the instigator, and each kill the instigator causes counts as an
    /// environmental kill.
    /// </summary>
    [Uc("SBase.SBasePawn.CrushedBy", Sha1 = "cfd8f0620805440a1a5232b978bff272b13bcf5e")]
    [PortOf("uc:SBase.SBasePawn:CrushedBy", Spec = "spec/devices/mover.md#encroachment")]
    public override void CrushedBy(Pawn? EventInstigator)
    {
        var up = new FVector(0f, 0f, 1f);
        if (m_AttackTarget is { } grabbed && ReferenceEquals(grabbed.m_AttackedBy, this))
        {
            bool grabbedWasAlive = grabbed.Health > 0;
            (grabbed.Controller as SPlayerController)?.PawnTakeDamage(Health + DeviceCrushExtra, EventInstigator, FVector.Zero, up, SDamageType.SDTBase);
            if (grabbedWasAlive && grabbed.Health <= 0 && EventInstigator is not null && Level.Game is SGameInfo game)
            {
                game.CountEnvironmentalKill(EventInstigator.Controller, grabbed.Controller);
            }
        }

        bool wasAlive = Health > 0;
        (Controller as SPlayerController)?.PawnTakeDamage(Health + DeviceCrushExtra, EventInstigator, FVector.Zero, up, SDamageType.SDTBase);
        if (wasAlive && Health <= 0 && EventInstigator is not null && Level.Game is SGameInfo info)
        {
            info.CountEnvironmentalKill(EventInstigator.Controller, Controller);
        }
    }

    /// <summary>Crush damage is the crushed pawn's Health plus this much.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.SBasePawn.CrushedBy damage Health + 100")]
    private const int DeviceCrushExtra = 100;
}
