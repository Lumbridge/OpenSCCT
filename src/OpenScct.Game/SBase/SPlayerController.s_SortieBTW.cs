using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_SortieBTW: the clip off the wall. Written from spec/movement/back-to-wall.md
// (Leaving).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_SortieBTW")]
    public class s_SortieBTW : UcActorState, IPlayerMove
    {
        public static readonly s_SortieBTW Instance = new(UcMeta.s_SortieBTW, null);

        protected s_SortieBTW(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_SortieBTW.EndState", Sha1 = "8ac002da8bea7576c7adbbf9fcc5174ee7ffa4f8")]
        [PortOf("uc:SBase.SPlayerController:s_SortieBTW.EndState", Spec = "spec/movement/back-to-wall.md#leaving-s_sortiebtw-fire-disabled")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pc.FaceWall(pawn);
            }
        }

        /// <summary>The stick out of the dead square leaves at once.</summary>
        [Uc("SBase.SPlayerController.s_SortieBTW.PlayerMove", Sha1 = "89471bee14d6aee437d5397d2ddcde37a7f5b8be")]
        [PortOf("uc:SBase.SPlayerController:s_SortieBTW.PlayerMove", Spec = "spec/movement/back-to-wall.md#leaving-s_sortiebtw-fire-disabled")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.eventUpdateRotation(DeltaTime, 1f);
            float sq = MovementK.StickDeadSquare;
            bool stick = !(self.aForward > -sq && self.aForward < sq && self.aStrafe > -sq && self.aStrafe < sq);
            if (stick && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_BTW_OUT);
                self.GotoWaitingState();
            }

            self.SendMove(DeltaTime, default);
        }

        /// <summary>The exit clip ended: off the wall, resting.</summary>
        [Uc("SBase.SPlayerController.s_SortieBTW.AnimEnd", Sha1 = "862a10bd5fa92d9fb6ab795f205f431056804531")]
        [PortOf("uc:SBase.SPlayerController:s_SortieBTW.AnimEnd", Spec = "spec/movement/back-to-wall.md#leaving-s_sortiebtw-fire-disabled")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_BTW_OUT);
            bool cr = pawn.bIsCrouched;
            pawn.ChangeEnumAction(cr ? SBasePawn.SA_WALK_CR_0H : SBasePawn.SA_WALK_ST_0H);
            pc.eventChangeState(cr ? MovementStateNames.s_PlayerWalkingCr0H : MovementStateNames.s_PlayerWalkingSt0H);
        }
    }
}
