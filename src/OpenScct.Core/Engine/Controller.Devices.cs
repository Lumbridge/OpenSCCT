using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.Controller functions the map devices call.
public abstract partial class Controller
{
    /// <summary>
    /// Damage to this controller's pawn (crushing movers, explosions, bumps from flying props). Empty on Controller;
    /// SPlayerController applies it.
    /// </summary>
    [Uc("Engine.Controller.PawnTakeDamage", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.Controller:PawnTakeDamage", Spec = "spec/devices/mover.md#encroachment")]
    public virtual void PawnTakeDamage(int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0, int PercentDamageRefillable = 0)
    {
    }
}
