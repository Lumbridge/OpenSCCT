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

    /// <summary>
    /// Killed by the world on behalf of <paramref name="EventInstigator"/> (a kill zone, or a crush that Actor routes
    /// here): Health + 100 of SDTBase damage through the controller, so the normal death path runs.
    /// </summary>
    [Uc("SBase.SBasePawn.KilledBy", Sha1 = "4d214f5ae7d03cdd5d2b53195c93e6e7f2e1e82b")]
    [PortOf("uc:SBase.SBasePawn:KilledBy", Spec = "spec/combat/environmental-damage.md#movers-and-lifts")]
    public override void KilledBy(Pawn? EventInstigator) =>
        (Controller as SPlayerController)?.PawnTakeDamage(Health + DeviceCrushExtra, EventInstigator, FVector.Zero, new FVector(0f, 0f, 1f),
            SDamageType.SDTBase);

    /// <summary>Crush damage is the crushed pawn's Health plus this much.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.SBasePawn.CrushedBy damage Health + 100")]
    private const int DeviceCrushExtra = 100;
}
