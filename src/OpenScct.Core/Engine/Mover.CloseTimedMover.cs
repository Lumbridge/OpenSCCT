using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int CloseTimedMover_Close = 1;
    private const int CloseTimedMover_Open = 97;

    /// <summary>
    /// Mirror of OpenTimedMover for movers that start open: Close (only from key 1), hold, reopen. Shared code of
    /// TriggerCloseTimed.
    /// </summary>
    [Uc("Engine.Mover.CloseTimedMover")]
    public class CloseTimedMover : MoverState
    {
        public static readonly CloseTimedMover Instance = new(UcMeta.CloseTimedMover, null);

        protected CloseTimedMover(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.CloseTimedMover@code", Sha1 = "e060eb72eab72349fa13a0db3ebaa12c11209480")]
        [PortOf("uc:Engine.Mover:CloseTimedMover@code", Spec = "spec/devices/mover.md#state-machines")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).CloseTimedMover_Code(ref frame);

        [Uc("Engine.Mover.CloseTimedMover.DisableTrigger", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:CloseTimedMover.DisableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void DisableTrigger(Mover self)
        {
        }

        [Uc("Engine.Mover.CloseTimedMover.EnableTrigger", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:CloseTimedMover.EnableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void EnableTrigger(Mover self)
        {
        }
    }

    private void CloseTimedMover_Code(ref LatentFrame f)
    {
        int mark;
        switch (f.Pc)
        {
            case CloseTimedMover_Close:
                Segment(ref f, CloseTimedMover_Close);
                if (KeyNum != 1)
                {
                    GotoState(Name.None);
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

                f.Pc = 4;
                Sleep(StayOpenTime);
                return;
            case 4:
                if (bTriggerOnceOnly)
                {
                    GotoState(Name.None);
                    return;
                }

                goto case CloseTimedMover_Open;
            case CloseTimedMover_Open:
                mark = Segment(ref f, CloseTimedMover_Open);
                bClosed = false;
                eventDisableTrigger();
                if (Jumped(mark))
                {
                    return;
                }

                if (DelayTime > 0f)
                {
                    bDelaying = true;
                    f.Pc = 98;
                    Sleep(DelayTime);
                    return;
                }

                goto case 98;
            case 98:
                mark = Segment(ref f, 98);
                DoOpen();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 99;
                FinishInterpolation();
                return;
            case 99:
                mark = Segment(ref f, 99);
                FinishedOpening();
                if (Jumped(mark))
                {
                    return;
                }

                eventEnableTrigger();
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
