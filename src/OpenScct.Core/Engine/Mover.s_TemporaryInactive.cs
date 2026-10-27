using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    /// <summary>Locked: triggers are ignored until UnLock returns to InitialState (nothing in SteeE unlocks).</summary>
    [Uc("Engine.Mover.s_TemporaryInactive")]
    public class s_TemporaryInactive : MoverState
    {
        public static readonly s_TemporaryInactive Instance = new(UcMeta.s_TemporaryInactive, null);

        protected s_TemporaryInactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        // ignores Trigger, UnTrigger
        public override void Trigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
        }

        public override void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
        }

        [Uc("Engine.Mover.s_TemporaryInactive.BeginState", Sha1 = "d0bbb5574e886670ac5fd4e058e5af2eb710507c")]
        [PortOf("uc:Engine.Mover:s_TemporaryInactive.BeginState", Spec = "spec/devices/mover.md#interpolation")]
        public override void BeginState(Actor self) => ((Mover)self).bShouldLock = false;

        [Uc("Engine.Mover.s_TemporaryInactive.Lock", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:s_TemporaryInactive.Lock", Spec = "spec/devices/mover.md#interpolation")]
        public override void Lock(Mover self)
        {
        }

        [Uc("Engine.Mover.s_TemporaryInactive.UnLock", Sha1 = "7eed925316d750535ddba797e61b97533e37d915")]
        [PortOf("uc:Engine.Mover:s_TemporaryInactive.UnLock", Spec = "spec/devices/mover.md#interpolation")]
        public override void UnLock(Mover self) => self.GotoState(self.InitialState);
    }
}
