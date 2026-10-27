using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    /// <summary>
    /// Opens when triggered at rest, holds StayOpenTime, closes (the SteeE shutters and merc doors). A trigger
    /// while moving is dropped; a trigger while holding open restarts the Open label.
    /// </summary>
    [Uc("Engine.Mover.TriggerOpenTimed")]
    public class TriggerOpenTimed : OpenTimedMover
    {
        public static new readonly TriggerOpenTimed Instance = new(UcMeta.TriggerOpenTimed, OpenTimedMover.Instance);

        protected TriggerOpenTimed(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.TriggerOpenTimed.Trigger", Sha1 = "981569ceaeec9046b30437416a583df1efe5b3c5")]
        [PortOf("uc:Engine.Mover:TriggerOpenTimed.Trigger", Spec = "spec/devices/mover.md#triggering")]
        public override void Trigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.Trigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerOpenTimed.UnTrigger", Sha1 = "fa02a42536d5f77fc0db3bc1af1f2c5f1a554512")]
        [PortOf("uc:Engine.Mover:TriggerOpenTimed.UnTrigger", Spec = "spec/devices/mover.md#triggering")]
        public override void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.UnTrigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerOpenTimed.SubTrigger", Sha1 = "262a45f74c5cfeeaf8e92c7f4184efda401c0286")]
        [PortOf("uc:Engine.Mover:TriggerOpenTimed.SubTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.SavedTrigger = Other;
            self.Instigator = EventInstigator;
            self.SavedTrigger?.eventBeginEvent();
            self.GotoState(nameof(TriggerOpenTimed), NAME_Open);
            self.bAllowLock = false;
        }

        [Uc("Engine.Mover.TriggerOpenTimed.DisableTrigger", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:TriggerOpenTimed.DisableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void DisableTrigger(Mover self)
        {
        }

        [Uc("Engine.Mover.TriggerOpenTimed.EnableTrigger", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:TriggerOpenTimed.EnableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void EnableTrigger(Mover self)
        {
        }
    }
}
