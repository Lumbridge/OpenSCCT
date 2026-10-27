using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state TopLedge: the standing pull-up from the hang (a root-motion clip). Written from
// spec/movement/ledge.md (Climbing up).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.TopLedge")]
    public class TopLedge : UcActorState, IPlayerMove
    {
        public static readonly TopLedge Instance = new(UcMeta.TopLedge, null);

        protected TopLedge(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.TopLedge.PlayerMove", Sha1 = "ee3a6b340d8e24de6a02fb3bf3e823ad0c2aece9")]
        [PortOf("uc:SBase.SPlayerController:TopLedge.PlayerMove", Spec = "spec/movement/ledge.md#climbing-up")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.LimitedViewByCamera(DeltaTime, 0, 0);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The pull-up ended: standing on the ledge top.</summary>
        [Uc("SBase.SPlayerController.TopLedge.AnimEnd", Sha1 = "ccdfa51ee5a57e8c6f971ee1d078491ffe2d03a9")]
        [PortOf("uc:SBase.SPlayerController:TopLedge.AnimEnd", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(SmoothDefault);
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
            }

            base.AnimEnd(self, Channel);
        }
    }
}
