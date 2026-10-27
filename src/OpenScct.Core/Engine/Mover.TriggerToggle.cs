using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int TriggerToggle_Open = 1;
    private const int TriggerToggle_Close = 88;

    /// <summary>Each trigger at rest toggles between open and closed; a forced trigger while moving reverses it.</summary>
    [Uc("Engine.Mover.TriggerToggle")]
    public class TriggerToggle : MoverState
    {
        public static readonly TriggerToggle Instance = new(UcMeta.TriggerToggle, null);

        protected TriggerToggle(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.TriggerToggle@code", Sha1 = "de928eec5d2dcd0af26c85f31259712a4c83e0ee")]
        [PortOf("uc:Engine.Mover:TriggerToggle@code", Spec = "spec/devices/mover.md#state-machines")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).TriggerToggle_Code(ref frame);

        [Uc("Engine.Mover.TriggerToggle.Trigger", Sha1 = "5d163650f2f848b0f7aee2c0debd887fc2b8de4d")]
        [PortOf("uc:Engine.Mover:TriggerToggle.Trigger", Spec = "spec/devices/mover.md#triggering")]
        public override void Trigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.Trigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerToggle.UnTrigger", Sha1 = "142687da398a5956534326011ace95784a37cdc1")]
        [PortOf("uc:Engine.Mover:TriggerToggle.UnTrigger", Spec = "spec/devices/mover.md#triggering")]
        public override void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            if (self.IsTriggerable(Other))
            {
                self.UnTrigger(Other, EventInstigator);
            }
        }

        [Uc("Engine.Mover.TriggerToggle.SubTrigger", Sha1 = "414cec2f389075923ebd506c404a3296623d41c8")]
        [PortOf("uc:Engine.Mover:TriggerToggle.SubTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void SubTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
            self.SavedTrigger = Other;
            self.Instigator = EventInstigator;
            self.SavedTrigger?.eventBeginEvent();
            bool open = self.KeyNum == 0 || self.KeyNum < self.PrevKeyNum;
            self.GotoState(nameof(TriggerToggle), open ? NAME_Open : NAME_Close);
            self.bAllowLock = false;
        }
    }

    private void TriggerToggle_Code(ref LatentFrame f)
    {
        int mark;
        switch (f.Pc)
        {
            case TriggerToggle_Open:
                Segment(ref f, TriggerToggle_Open);
                bAllowLock = false;
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
                if (!Jumped(mark))
                {
                    f.Pc = LatentFrame.NoCode;
                }

                return;
            case TriggerToggle_Close:
                Segment(ref f, TriggerToggle_Close);
                if (DelayTimeLastKey > 0f)
                {
                    bDelaying = true;
                    f.Pc = 89;
                    Sleep(DelayTimeLastKey);
                    return;
                }

                goto case 89;
            case 89:
                mark = Segment(ref f, 89);
                bAllowLock = false;
                DoClose();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 90;
                FinishInterpolation();
                return;
            case 90:
                mark = Segment(ref f, 90);
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
