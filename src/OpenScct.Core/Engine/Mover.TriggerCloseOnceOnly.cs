using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int TriggerCloseOnceOnly_Close = 1;

    /// <summary>Closes on the first trigger (or untrigger) and then locks closed for the round.</summary>
    [Uc("Engine.Mover.TriggerCloseOnceOnly")]
    public class TriggerCloseOnceOnly : MoverState
    {
        public static readonly TriggerCloseOnceOnly Instance = new(UcMeta.TriggerCloseOnceOnly, null);

        protected TriggerCloseOnceOnly(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.TriggerCloseOnceOnly@code", Sha1 = "b626762da30672cd8ba9c6eea93c81b9bc6e5c77")]
        [PortOf("uc:Engine.Mover:TriggerCloseOnceOnly@code", Spec = "spec/devices/mover.md#state-machines")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).TriggerCloseOnceOnly_Code(ref frame);

        [Uc("Engine.Mover.TriggerCloseOnceOnly.DisableTrigger", Sha1 = "03f0f25839844a11448ccd206b7f89e07b75a3c4")]
        [PortOf("uc:Engine.Mover:TriggerCloseOnceOnly.DisableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void DisableTrigger(Mover self)
        {
            self.Disable(NAME_Trigger);
            self.Disable(NAME_UnTrigger);
        }

        [Uc("Engine.Mover.TriggerCloseOnceOnly.EnableTrigger", Sha1 = "d3a46411012648be402506960d3b3959ca83081d")]
        [PortOf("uc:Engine.Mover:TriggerCloseOnceOnly.EnableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void EnableTrigger(Mover self)
        {
            self.Enable(NAME_Trigger);
            self.Enable(NAME_UnTrigger);
        }

        [Uc("Engine.Mover.TriggerCloseOnceOnly.SubTrigger", Sha1 = "92025c21c628ba65e0b7bbc4c7c9f81fc21ba529")]
        [PortOf("uc:Engine.Mover:TriggerCloseOnceOnly.SubTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.numTriggerEvents++;
            self.SavedTrigger = Other;
            self.Instigator = EventInstigator;
            self.SavedTrigger?.eventBeginEvent();
            self.GotoState(nameof(TriggerCloseOnceOnly), NAME_Close);
            self.bAllowLock = false;
        }

        [Uc("Engine.Mover.TriggerCloseOnceOnly.SubUnTrigger", Sha1 = "8651f8c47712c5ba12317ad14ab4909271bfe0e7")]
        [PortOf("uc:Engine.Mover:TriggerCloseOnceOnly.SubUnTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubUnTrigger(Mover self, Actor? Other, Pawn? EventInstigator) => SubTrigger(self, Other, EventInstigator);
    }

    private void TriggerCloseOnceOnly_Code(ref LatentFrame f)
    {
        int mark;
        switch (f.Pc)
        {
            case TriggerCloseOnceOnly_Close:
                mark = Segment(ref f, TriggerCloseOnceOnly_Close);
                eventDisableTrigger();
                if (Jumped(mark))
                {
                    return;
                }

                if (DelayTimeLastKey > 0f)
                {
                    bDelaying = true;
                    f.Pc = 2;
                    Sleep(DelayTimeLastKey);
                    return;
                }

                goto case 2;
            case 2:
                mark = Segment(ref f, 2);
                DoClose();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 3;
                FinishInterpolation();
                return;
            case 3:
                mark = Segment(ref f, 3);
                FinishedClosing();
                if (Jumped(mark))
                {
                    return;
                }

                eventLock();
                if (!Jumped(mark))
                {
                    f.Pc = LatentFrame.NoCode;
                }

                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }
}
