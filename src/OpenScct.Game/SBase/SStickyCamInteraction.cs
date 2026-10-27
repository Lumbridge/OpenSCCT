using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SStickyCamInteraction: picking a sticky cam back up (spies only; spec/equipment/sticky-cam.md rule 17).
/// </summary>
public partial class SStickyCamInteraction
{
    /// <summary>A 75 x 75 cylinder based on the cam (PostNetBeginPlay later sets the interaction's own 30 x 50).</summary>
    [Uc("SBase.SStickyCamInteraction.PostBeginPlay", Sha1 = "55ae88be843b306e7a2eaafcd7216130e27710ad")]
    [PortOf("uc:SBase.SStickyCamInteraction:PostBeginPlay", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    protected override void PostBeginPlay()
    {
        MovementNatives.SetCollisionSize(this, K.StickyCamInteractionCollision, K.StickyCamInteractionCollision);
        SetBase(Owner);
    }

    /// <summary>The pick-up: the cam takes a marked hit from the picker, which hands it back to its owner.</summary>
    [Uc("SBase.SStickyCamInteraction.InitInteract", Sha1 = "94ae15cea3cd0e83210a169d71f4878a442e825b")]
    [PortOf("uc:SBase.SStickyCamInteraction:InitInteract", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction) =>
        (Owner as SStickyCam)?.eventTakeDamage(1, Instigator.Pawn, K.StickyPickupMarker, new FVector(0f, 0f, 1f), SDamageType.SDTBase);
}
