using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ChangePipeV: the swap clip to a neighbouring vertical pipe. Written from
// spec/movement/pipe-vertical.md (s_GrabPipe).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ChangePipeV")]
    public class s_ChangePipeV : s_GrabPipe
    {
        public static new readonly s_ChangePipeV Instance = new(UcMeta.s_ChangePipeV, s_GrabPipe.Instance);

        protected s_ChangePipeV(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The swap ended: holding the new pipe at rest.</summary>
        [Uc("SBase.SPlayerController.s_ChangePipeV.AnimEnd", Sha1 = "cce6dbfd1e3e05e6cc81baf8c55b8a8606eabef7")]
        [PortOf("uc:SBase.SPlayerController:s_ChangePipeV.AnimEnd", Spec = "spec/movement/pipe-vertical.md#s_grabpipe-ignores-bump-seeplayer-hearnoise-binoculars-disabled")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_AFTER_CHANGE_PIPEV);
            pc.eventChangeState(MovementStateNames.s_OnPipe);
        }
    }
}
