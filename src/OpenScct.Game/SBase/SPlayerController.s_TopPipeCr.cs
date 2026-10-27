using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_TopPipeCr: over the top of a vertical pipe onto the floor, crouched. Written from
// spec/movement/pipe-vertical.md (Leaving at the top).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_TopPipeCr")]
    public class s_TopPipeCr : s_TopPipe
    {
        public static new readonly s_TopPipeCr Instance = new(UcMeta.s_TopPipeCr, s_TopPipe.Instance);

        protected s_TopPipeCr(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TopPipeCr.BeginState", Sha1 = "dc49c7475d277260f923f29a8b58480ad803065c")]
        [PortOf("uc:SBase.SPlayerController:s_TopPipeCr.BeginState", Spec = "spec/movement/pipe-vertical.md#leaving-at-the-top-s_toppipe-s_toppipecr")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCamera(ECamMode.ECM_SmoothTransitionCentered);

        /// <summary>Over the top: crouched on the floor.</summary>
        [Uc("SBase.SPlayerController.s_TopPipeCr.AnimEnd", Sha1 = "f74a7a5e26e6343b7071660e89a2d185abbc990e")]
        [PortOf("uc:SBase.SPlayerController:s_TopPipeCr.AnimEnd", Spec = "spec/movement/pipe-vertical.md#leaving-at-the-top-s_toppipe-s_toppipecr")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
            pc.eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
        }
    }
}
