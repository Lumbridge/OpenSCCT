using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SGamePlayObject
{
    /// <summary>Carried by a player: no collision, damage ignored.</summary>
    [Uc("SBase.SGamePlayObject.s_PickedUp")]
    public class s_PickedUp : UcActorState, ITakeDamage
    {
        public static readonly s_PickedUp Instance = new(UcMeta.s_PickedUp, null);

        protected s_PickedUp(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SGamePlayObject.s_PickedUp.BeginState", Sha1 = "005c2d6e1f91efd033db289611863234d54374e5")]
        [PortOf("uc:SBase.SGamePlayObject:s_PickedUp.BeginState", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
        public override void BeginState(Actor self) => self.SetCollision(false, false, false);

        [Uc("SBase.SGamePlayObject.s_PickedUp.TakeDamage", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SGamePlayObject:s_PickedUp.TakeDamage", Spec = "spec/devices/gameplay-object.md#pushable-and-pickable-props")]
        public void TakeDamage(Actor self, int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag)
        {
        }
    }
}
