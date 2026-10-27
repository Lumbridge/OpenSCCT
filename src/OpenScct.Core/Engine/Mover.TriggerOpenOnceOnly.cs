using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int TriggerOpenOnceOnly_Open = 1;

    /// <summary>
    /// Opens on the first trigger (or untrigger) and then locks open for the round (the SteeE crane duct). Trigger and
    /// UnTrigger are disabled from the Open label on, so a second trigger during the leg is ignored.
    /// </summary>
    [Uc("Engine.Mover.TriggerOpenOnceOnly")]
    public class TriggerOpenOnceOnly : MoverState
    {
        public static readonly TriggerOpenOnceOnly Instance = new(UcMeta.TriggerOpenOnceOnly, null);

        protected TriggerOpenOnceOnly(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.TriggerOpenOnceOnly@code", Sha1 = "dfd7e76d949f08fbe2147fc0b10b7df49cc5e687")]
        [PortOf("uc:Engine.Mover:TriggerOpenOnceOnly@code", Spec = "spec/devices/mover.md#state-machines")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).TriggerOpenOnceOnly_Code(ref frame);

        [Uc("Engine.Mover.TriggerOpenOnceOnly.DisableTrigger", Sha1 = "03f0f25839844a11448ccd206b7f89e07b75a3c4")]
        [PortOf("uc:Engine.Mover:TriggerOpenOnceOnly.DisableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void DisableTrigger(Mover self)
        {
            self.Disable(NAME_Trigger);
            self.Disable(NAME_UnTrigger);
        }

        [Uc("Engine.Mover.TriggerOpenOnceOnly.EnableTrigger", Sha1 = "d3a46411012648be402506960d3b3959ca83081d")]
        [PortOf("uc:Engine.Mover:TriggerOpenOnceOnly.EnableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void EnableTrigger(Mover self)
        {
            self.Enable(NAME_Trigger);
            self.Enable(NAME_UnTrigger);
        }

        [Uc("Engine.Mover.TriggerOpenOnceOnly.SubTrigger", Sha1 = "a3d562d9e7c749fc9df9d859a9f0e1dd20799dd9")]
        [PortOf("uc:Engine.Mover:TriggerOpenOnceOnly.SubTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.numTriggerEvents++;
            self.SavedTrigger = Other;
            self.Instigator = EventInstigator;
            self.SavedTrigger?.eventBeginEvent();
            self.GotoState(nameof(TriggerOpenOnceOnly), NAME_Open);
            self.bAllowLock = false;
        }

        [Uc("Engine.Mover.TriggerOpenOnceOnly.SubUnTrigger", Sha1 = "7fb048dc29bfcb4f0ffcb514bfca7769b57d767e")]
        [PortOf("uc:Engine.Mover:TriggerOpenOnceOnly.SubUnTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubUnTrigger(Mover self, Actor? Other, Pawn? EventInstigator) => SubTrigger(self, Other, EventInstigator);
    }

    private void TriggerOpenOnceOnly_Code(ref LatentFrame f)
    {
        int mark;
        switch (f.Pc)
        {
            case TriggerOpenOnceOnly_Open:
                mark = Segment(ref f, TriggerOpenOnceOnly_Open);
                bClosed = false;
                eventDisableTrigger();
                if (Jumped(mark))
                {
                    return;
                }

                if (DelayTime > 0f)
                {
                    bDelaying = true;
                    f.Pc = 2;
                    Sleep(DelayTime);
                    return;
                }

                goto case 2;
            case 2:
                mark = Segment(ref f, 2);
                DoOpen();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 3;
                FinishInterpolation();
                return;
            case 3:
                mark = Segment(ref f, 3);
                FinishedOpening();
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
