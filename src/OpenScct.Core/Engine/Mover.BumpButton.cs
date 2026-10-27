using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int BumpButton_Open = 1;
    private const int BumpButton_Close = 90;

    /// <summary>A button: a bump opens it, it stays open while its event's receiver works (BeginEvent / EndEvent).</summary>
    [Uc("Engine.Mover.BumpButton")]
    public class BumpButton : MoverState, IBeginEvent, IEndEvent
    {
        public static readonly BumpButton Instance = new(UcMeta.BumpButton, null);

        protected BumpButton(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.BumpButton@code", Sha1 = "7c01ecc6a04738168cded49193ec5dc875189451")]
        [PortOf("uc:Engine.Mover:BumpButton@code", Spec = "spec/devices/mover.md#state-machines")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).BumpButton_Code(ref frame);

        [Uc("Engine.Mover.BumpButton.Bump", Sha1 = "775362fadd0c6702abf8ef75645246a115e0145a")]
        [PortOf("uc:Engine.Mover:BumpButton.Bump", Spec = "spec/devices/mover.md#state-machines")]
        public override void Bump(Mover self, Actor Other)
        {
            if (!self.PassesBumpType(Other))
            {
                return;
            }

            self.Bump(Other);
            self.SavedTrigger = Other;
            self.Instigator = Other as Pawn;

            // Instigator.Controller.WaitForMover(self) is an empty bot navigation hook in this build.
            self.GotoState(nameof(BumpButton), NAME_Open);
        }

        [Uc("Engine.Mover.BumpButton.BeginEvent", Sha1 = "0ae3c62b3a44964e58e689b6805d286bd5b96517")]
        [PortOf("uc:Engine.Mover:BumpButton.BeginEvent", Spec = "spec/devices/mover.md#state-machines")]
        public void BeginEvent(Actor self) => ((Mover)self).bSlave = true;

        [Uc("Engine.Mover.BumpButton.EndEvent", Sha1 = "feabc2f22d9415ce35960821e88608e5f8ff9a14")]
        [PortOf("uc:Engine.Mover:BumpButton.EndEvent", Spec = "spec/devices/mover.md#state-machines")]
        public void EndEvent(Actor self)
        {
            var mover = (Mover)self;
            mover.bSlave = false;
            mover.Instigator = null;
            mover.GotoState(nameof(BumpButton), NAME_Close);
        }
    }

    private void BumpButton_Code(ref LatentFrame f)
    {
        int mark;
        switch (f.Pc)
        {
            case BumpButton_Open:
                Segment(ref f, BumpButton_Open);
                bClosed = false;
                Disable(NAME_Bump);
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

                if (bTriggerOnceOnly)
                {
                    GotoState(Name.None);
                    return;
                }

                if (bSlave)
                {
                    f.Pc = LatentFrame.NoCode;
                    return;
                }

                goto case BumpButton_Close;
            case BumpButton_Close:
                Segment(ref f, BumpButton_Close);
                if (DelayTimeLastKey > 0f)
                {
                    bDelaying = true;
                    f.Pc = 91;
                    Sleep(DelayTimeLastKey);
                    return;
                }

                goto case 91;
            case 91:
                mark = Segment(ref f, 91);
                DoClose();
                if (Jumped(mark))
                {
                    return;
                }

                f.Pc = 92;
                FinishInterpolation();
                return;
            case 92:
                mark = Segment(ref f, 92);
                FinishedClosing();
                if (Jumped(mark))
                {
                    return;
                }

                Enable(NAME_Bump);
                f.Pc = LatentFrame.NoCode;
                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }
}
