using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BTW_up: standing with the back to the wall. Written from
// spec/movement/back-to-wall.md (On the wall).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_BTW_up")]
    public class s_BTW_up : s_BTW
    {
        public static new readonly s_BTW_up Instance = new(UcMeta.s_BTW_up, s_BTW.Instance);

        protected s_BTW_up(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The crouch latch crouches on the wall (0.5 s apart, with room), else it is cleared; then the wall move.</summary>
        [Uc("SBase.SPlayerController.s_BTW_up.PlayerMove", Sha1 = "74a8765912a30160586e1aec94bef3d37820a5d4")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_up.PlayerMove", Spec = "spec/movement/back-to-wall.md#on-the-wall-s_btw_up-standing-s_btw_down-crouched-both-extend-s_btw")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.bCrouchWithRoll && self.SBPawn is { } pawn)
            {
                float now = self.Level.TimeSeconds;
                if (pawn.bCanCrouch && now - self.m_LastSpecialMoveTime > MovementK.BtwStanceCooldown)
                {
                    self.m_LastSpecialMoveTime = now;
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                    pawn.ChangeEnumAction(SBasePawn.SA_BTW_CR);
                    self.eventChangeState(MovementStateNames.s_BTW_down);
                }
                else
                {
                    self.bCrouchWithRoll = false;
                }
            }

            MyPlayerMove(self, DeltaTime);
        }
    }
}
