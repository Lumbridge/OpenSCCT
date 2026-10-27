using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BTW_down: crouched with the back to the wall. Written from
// spec/movement/back-to-wall.md (On the wall).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_BTW_down")]
    public class s_BTW_down : s_BTW
    {
        public static new readonly s_BTW_down Instance = new(UcMeta.s_BTW_down, s_BTW.Instance);

        protected s_BTW_down(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>A cleared crouch latch stands up on the wall (0.5 s apart, with room), else it is set again; then the wall move.</summary>
        [Uc("SBase.SPlayerController.s_BTW_down.PlayerMove", Sha1 = "4ef8bc009e3422c123a21435272a8dea9f6877f0")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_down.PlayerMove", Spec = "spec/movement/back-to-wall.md#on-the-wall-s_btw_up-standing-s_btw_down-crouched-both-extend-s_btw")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (!self.bCrouchWithRoll && self.SBPawn is { } pawn)
            {
                float now = self.Level.TimeSeconds;
                if (pawn.CanUnCrouch() && now - self.m_LastSpecialMoveTime > MovementK.BtwStanceCooldown)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                    pawn.ChangeEnumAction(SBasePawn.SA_BTW_ST);
                    self.m_LastSpecialMoveTime = now;
                    self.eventChangeState(MovementStateNames.s_BTW_up);
                }
                else
                {
                    self.bCrouchWithRoll = true;
                }
            }

            MyPlayerMove(self, DeltaTime);
        }
    }
}
