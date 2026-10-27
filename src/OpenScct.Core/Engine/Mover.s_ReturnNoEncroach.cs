using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int s_ReturnNoEncroach_Begin = 1;

    /// <summary>
    /// Return-on-encroach: go back to the key the leg left, wait 1 s, send the group's opening or closing event (a
    /// leader only), then return to the remembered state without a label, so the mover rests there with no state
    /// code until it is triggered again. Triggers are ignored meanwhile; nothing blocks the way back.
    /// </summary>
    [Uc("Engine.Mover.s_ReturnNoEncroach")]
    public class s_ReturnNoEncroach : MoverState, IEncroachingOn
    {
        public static readonly s_ReturnNoEncroach Instance = new(UcMeta.s_ReturnNoEncroach, null);

        protected s_ReturnNoEncroach(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.s_ReturnNoEncroach@code", Sha1 = "d58db0482947cb8b32d93b985c3aa100babfd40d")]
        [PortOf("uc:Engine.Mover:s_ReturnNoEncroach@code", Spec = "spec/devices/mover.md#encroachment")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).s_ReturnNoEncroach_Code(ref frame);

        // ignores Trigger, UnTrigger
        public override void Trigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
        }

        public override void UnTrigger(Mover self, Actor? Other, Pawn? EventInstigator)
        {
        }

        [Uc("Engine.Mover.s_ReturnNoEncroach.EncroachingOn", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:Engine.Mover:s_ReturnNoEncroach.EncroachingOn", Spec = "spec/devices/mover.md#encroachment")]
        public bool EncroachingOn(Actor self, Actor? Other) => false;

        [Uc("Engine.Mover.s_ReturnNoEncroach.SendClosingEvent", Sha1 = "ef7e333dae2ab7a570ad77e4a15c30cd7015b03c")]
        [PortOf("uc:Engine.Mover:s_ReturnNoEncroach.SendClosingEvent", Spec = "spec/devices/mover.md#encroachment")]
        public static void SendClosingEvent(Mover self)
        {
            if (!self.bIsLeader)
            {
                return;
            }

            for (Mover? m = self.Leader; m is not null; m = m.Follower)
            {
                m.TriggerEvent(m.ClosingEvent, m, self.Instigator);
            }
        }

        [Uc("Engine.Mover.s_ReturnNoEncroach.SendOpeningEvent", Sha1 = "51eae6744a3f27a4b12d91e88738c7f4d5049f15")]
        [PortOf("uc:Engine.Mover:s_ReturnNoEncroach.SendOpeningEvent", Spec = "spec/devices/mover.md#encroachment")]
        public static void SendOpeningEvent(Mover self)
        {
            if (!self.bIsLeader)
            {
                return;
            }

            for (Mover? m = self.Leader; m is not null; m = m.Follower)
            {
                m.TriggerEvent(m.OpeningEvent, m, self.Instigator);
            }
        }
    }

    private void s_ReturnNoEncroach_Code(ref LatentFrame f)
    {
        // Pc 2 and 3 return from a closing leg, 4 and 5 from an opening one.
        switch (f.Pc)
        {
            case s_ReturnNoEncroach_Begin:
                Segment(ref f, s_ReturnNoEncroach_Begin);
                bool closing = KeyNum < PrevKeyNum;
                InterpolateTo(PrevKeyNum, MoveTime);
                f.Pc = closing ? 2 : 4;
                FinishInterpolation();
                return;
            case 2:
            case 4:
                f.Pc++;
                Sleep(MoverK.RetryWait);
                return;
            case 3:
                s_ReturnNoEncroach.SendClosingEvent(this);
                GotoState(CurrentState);
                return;
            case 5:
                s_ReturnNoEncroach.SendOpeningEvent(this);
                GotoState(CurrentState);
                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }
}
