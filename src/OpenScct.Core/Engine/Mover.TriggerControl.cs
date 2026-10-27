using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int TriggerControl_Open = 1;
    private const int TriggerControl_Close = 89;

    /// <summary>Open while triggered; closes when the last trigger is released.</summary>
    [Uc("Engine.Mover.TriggerControl")]
    public class TriggerControl : MoverState
    {
        public static readonly TriggerControl Instance = new(UcMeta.TriggerControl, null);

        protected TriggerControl(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.TriggerControl@code", Sha1 = "ee3470a71d02dd3cba49dfb098ae0540542ffe9a")]
        [PortOf("uc:Engine.Mover:TriggerControl@code", Spec = "spec/devices/mover.md#state-machines")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).TriggerControl_Code(ref frame);

        [Uc("Engine.Mover.TriggerControl.BeginState", Sha1 = "985a57088ba974bfbcbdf3a6e7220cf0bc3d8a83")]
        [PortOf("uc:Engine.Mover:TriggerControl.BeginState", Spec = "spec/devices/mover.md#state-machines")]
        public override void BeginState(Actor self) => ((Mover)self).numTriggerEvents = 0;

        [Uc("Engine.Mover.TriggerControl.Trigger", Sha1 = "53b486b26f9fc2fb2868112dca988e86c1284793")]
        [PortOf("uc:Engine.Mover:TriggerControl.Trigger", Spec = "spec/devices/mover.md#triggering")]
        public override void Trigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.Trigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerControl.UnTrigger", Sha1 = "834f78083073ae0ac88668c31f2a096a704bec37")]
        [PortOf("uc:Engine.Mover:TriggerControl.UnTrigger", Spec = "spec/devices/mover.md#triggering")]
        public override void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.UnTrigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerControl.SubTrigger", Sha1 = "e706bfa75f9bd53a415aa09384a59ce4d0b25385")]
        [PortOf("uc:Engine.Mover:TriggerControl.SubTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.numTriggerEvents++;
            self.SavedTrigger = Other;
            self.Instigator = EventInstigator;
            self.SavedTrigger?.eventBeginEvent();
            self.GotoState(nameof(TriggerControl), NAME_Open);
            self.bAllowLock = false;
        }

        /// <summary>At the last release: BeginEvent on the releasing sender, then Close.</summary>
        [Uc("Engine.Mover.TriggerControl.SubUnTrigger", Sha1 = "893a9dc8d9abe0d92f1dd62edcb53b4a2562c2e1")]
        [PortOf("uc:Engine.Mover:TriggerControl.SubUnTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubUnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.numTriggerEvents--;
            if (self.numTriggerEvents <= 0)
            {
                self.numTriggerEvents = 0;
                self.SavedTrigger = Other;
                self.Instigator = EventInstigator;

                // The original makes this call without a None check; a None sender only logs an access warning.
                self.SavedTrigger?.eventBeginEvent();
                self.GotoState(nameof(TriggerControl), NAME_Close);
                self.bAllowLock = false;
            }
        }
    }

    private void TriggerControl_Code(ref LatentFrame f)
    {
        int mark;
        switch (f.Pc)
        {
            case TriggerControl_Open:
                Segment(ref f, TriggerControl_Open);
                bClosed = false;
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

                SavedTrigger?.eventEndEvent();
                if (Jumped(mark))
                {
                    return;
                }

                if (bTriggerOnceOnly)
                {
                    GotoState(Name.None);
                    return;
                }

                f.Pc = LatentFrame.NoCode;
                return;
            case TriggerControl_Close:
                Segment(ref f, TriggerControl_Close);
                if (DelayTimeLastKey > 0f)
                {
                    bDelaying = true;
                    f.Pc = 90;
                    Sleep(DelayTimeLastKey);
                    return;
                }

                goto case 90;
            case 90:
                mark = Segment(ref f, 90);
                DoClose();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 91;
                FinishInterpolation();
                return;
            case 91:
                mark = Segment(ref f, 91);
                FinishedClosing();
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
