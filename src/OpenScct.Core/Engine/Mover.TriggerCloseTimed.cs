using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    /// <summary>Closes when triggered at rest (from key 1), holds, reopens.</summary>
    [Uc("Engine.Mover.TriggerCloseTimed")]
    public class TriggerCloseTimed : CloseTimedMover
    {
        public static new readonly TriggerCloseTimed Instance = new(UcMeta.TriggerCloseTimed, CloseTimedMover.Instance);

        protected TriggerCloseTimed(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.TriggerCloseTimed.Trigger", Sha1 = "c81ffedf6d45fca77ccf141b405e49f2e7eaf300")]
        [PortOf("uc:Engine.Mover:TriggerCloseTimed.Trigger", Spec = "spec/devices/mover.md#triggering")]
        public override void Trigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.Trigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerCloseTimed.UnTrigger", Sha1 = "29d0d8a14ee508a1da2846493ff9a4fa3251152a")]
        [PortOf("uc:Engine.Mover:TriggerCloseTimed.UnTrigger", Spec = "spec/devices/mover.md#triggering")]
        public override void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.UnTrigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerCloseTimed.SubTrigger", Sha1 = "4522d34f3f6dc31455e9b8ba8b91367570925a05")]
        [PortOf("uc:Engine.Mover:TriggerCloseTimed.SubTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.SavedTrigger = Other;
            self.Instigator = EventInstigator;
            self.SavedTrigger?.eventBeginEvent();
            self.GotoState(nameof(TriggerCloseTimed), NAME_Close);
            self.bAllowLock = false;
        }

        [Uc("Engine.Mover.TriggerCloseTimed.DisableTrigger", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:TriggerCloseTimed.DisableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void DisableTrigger(Mover self)
        {
        }

        [Uc("Engine.Mover.TriggerCloseTimed.EnableTrigger", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:TriggerCloseTimed.EnableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void EnableTrigger(Mover self)
        {
        }
    }
}
