using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private const int s_WaitAndRetry_Begin = 1;

    /// <summary>
    /// Wait-and-retry on encroach: go back the way it came, wait 1 s, then retry the interrupted leg from the
    /// remembered state's Close (it was closing) or Open label. Nothing blocks the way back.
    /// </summary>
    [Uc("Engine.Mover.s_WaitAndRetry")]
    public class s_WaitAndRetry : MoverState, IEncroachingOn
    {
        public static readonly s_WaitAndRetry Instance = new(UcMeta.s_WaitAndRetry, null);

        protected s_WaitAndRetry(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.s_WaitAndRetry@code", Sha1 = "35d9719402cc7a205fe7cf42948331e7459bcd91")]
        [PortOf("uc:Engine.Mover:s_WaitAndRetry@code", Spec = "spec/devices/mover.md#encroachment")]
        public override void Code(Actor self, ref LatentFrame frame) => ((Mover)self).s_WaitAndRetry_Code(ref frame);

        [Uc("Engine.Mover.s_WaitAndRetry.EncroachingOn", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:Engine.Mover:s_WaitAndRetry.EncroachingOn", Spec = "spec/devices/mover.md#encroachment")]
        public bool EncroachingOn(Actor self, Actor? Other) => false;
    }

    private void s_WaitAndRetry_Code(ref LatentFrame f)
    {
        // Pc 2 and 3 handle a closing leg, 4 and 5 an opening one.
        switch (f.Pc)
        {
            case s_WaitAndRetry_Begin:
                Segment(ref f, s_WaitAndRetry_Begin);
                if (KeyNum < PrevKeyNum)
                {
                    if (PhysAlpha == 0f)
                    {
                        KeyNum = PrevKeyNum;
                    }

                    InterpolateTo(1, MoveTime);
                    f.Pc = 2;
                }
                else
                {
                    if (PhysAlpha == 0f)
                    {
                        KeyNum = PrevKeyNum;
                    }

                    InterpolateTo((byte)SvmMath.Max(0, KeyNum - 1), MoveTime);
                    f.Pc = 4;
                }

                FinishInterpolation();
                return;
            case 2:
            case 4:
                f.Pc++;
                Sleep(MoverK.RetryWait);
                return;
            case 3:
                GotoState(CurrentState, NAME_Close);
                return;
            case 5:
                GotoState(CurrentState, NAME_Open);
                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }
}
