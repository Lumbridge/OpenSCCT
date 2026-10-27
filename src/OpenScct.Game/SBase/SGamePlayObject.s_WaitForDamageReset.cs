using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SGamePlayObject
{
    /// <summary>
    /// Destroyed with a reset time: everything is ignored until the timer restores the object completely (hit
    /// points, mesh, skin, lights, collision, OnResetEvent, spawned actors removed).
    /// </summary>
    [Uc("SBase.SGamePlayObject.s_WaitForDamageReset")]
    public class s_WaitForDamageReset : UcActorState, ITakeDamage, ISetLockedByChaff, IProcessDamage, INotifyPickup, IThrow
    {
        public static readonly s_WaitForDamageReset Instance = new(UcMeta.s_WaitForDamageReset, null);

        protected s_WaitForDamageReset(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SGamePlayObject.s_WaitForDamageReset.BeginState", Sha1 = "09ac37b44cee0a8f18a277a36928f3f68a200889")]
        [PortOf("uc:SBase.SGamePlayObject:s_WaitForDamageReset.BeginState", Spec = "spec/devices/gameplay-object.md#damage")]
        public override void BeginState(Actor self)
        {
            var gpo = (SGamePlayObject)self;
            gpo.SetTimer(gpo.ResetDamageTime, false);
            if (gpo.bPickable && gpo.PickupInteraction is not null)
            {
                gpo.PickupInteraction.bCanPickup = false;
            }
        }

        [Uc("SBase.SGamePlayObject.s_WaitForDamageReset.Timer", Sha1 = "70396b2e4281f76bc1963bf1f0f646488a808dda")]
        [PortOf("uc:SBase.SGamePlayObject:s_WaitForDamageReset.Timer", Spec = "spec/devices/gameplay-object.md#damage")]
        public override void Timer(Actor self) => ((SGamePlayObject)self).ResetAfterDamage();

        // ignores Trigger, UnTrigger, Touch, SetLockedByChaff, TakeDamage, ProcessDamage, NotifyPickup, Throw
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
        }

        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
        }

        public override void Touch(Actor self, Actor Other)
        {
        }

        public void SetLockedByChaff(SBasicGamePlayObject self, float Length)
        {
        }

        public void TakeDamage(Actor self, int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag)
        {
        }

        public void ProcessDamage(SGamePlayObject self, int Damage)
        {
        }

        public bool NotifyPickup(SGamePlayObject self, Controller Instigator) => false;

        public void Throw(SGamePlayObject self, Controller? Thrower, FVector ThrowVelocity)
        {
        }
    }
}
