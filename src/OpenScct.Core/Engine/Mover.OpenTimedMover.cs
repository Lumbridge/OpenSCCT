using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int OpenTimedMover_Open = 1;
    private const int OpenTimedMover_Close = 87;

    /// <summary>
    /// Shared code of TriggerOpenTimed, BumpOpenTimed and StandOpenTimed. Open: delay, open, wait, FinishedOpening,
    /// hold StayOpenTime (once-only movers leave the state here). Close: delay, close, wait, FinishedClosing,
    /// EnableTrigger. The code then ends and the mover idles in the same state.
    /// </summary>
    [Uc("Engine.Mover.OpenTimedMover")]
    public class OpenTimedMover : MoverState
    {
        public static readonly OpenTimedMover Instance = new(UcMeta.OpenTimedMover, null);

        protected OpenTimedMover(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.OpenTimedMover@code", Sha1 = "0b8ee20b3f3234e6c2fff6ba5a7eea0919db512f")]
        [PortOf("uc:Engine.Mover:OpenTimedMover@code", Spec = "spec/devices/mover.md#state-machines")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).OpenTimedMover_Code(ref frame);

        [Uc("Engine.Mover.OpenTimedMover.DisableTrigger", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:OpenTimedMover.DisableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void DisableTrigger(Mover self)
        {
        }

        [Uc("Engine.Mover.OpenTimedMover.EnableTrigger", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.Mover:OpenTimedMover.EnableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void EnableTrigger(Mover self)
        {
        }
    }

    private void OpenTimedMover_Code(ref LatentFrame f)
    {
        int mark;
        switch (f.Pc)
        {
            case OpenTimedMover_Open:
                mark = Segment(ref f, OpenTimedMover_Open);
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

                f.Pc = 4;
                Sleep(StayOpenTime);
                return;
            case 4:
                if (bTriggerOnceOnly)
                {
                    GotoState(Name.None);
                    return;
                }

                goto case OpenTimedMover_Close;
            case OpenTimedMover_Close:
                Segment(ref f, OpenTimedMover_Close);
                if (DelayTimeLastKey > 0f)
                {
                    bDelaying = true;
                    f.Pc = 88;
                    Sleep(DelayTimeLastKey);
                    return;
                }

                goto case 88;
            case 88:
                mark = Segment(ref f, 88);
                DoClose();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 89;
                FinishInterpolation();
                return;
            case 89:
                mark = Segment(ref f, 89);
                FinishedClosing();
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
