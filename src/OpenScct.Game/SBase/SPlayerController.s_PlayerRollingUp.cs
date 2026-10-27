using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerRollingUp: the spy's dive roll from a run, which ends standing when there is
// room and the crouch latch is off. Written from spec/movement/roll.md.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PlayerRollingUp")]
    public class s_PlayerRollingUp : s_PlayerRolling
    {
        public static new readonly s_PlayerRollingUp Instance = new(UcMeta.s_PlayerRollingUp, s_PlayerRolling.Instance);

        protected s_PlayerRollingUp(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The dive clip ended (roll channel): stand up through the stand transition, or end crouched.</summary>
        [Uc("SBase.SPlayerController.s_PlayerRollingUp.AnimEnd", Sha1 = "d718c8f2f53d553830ba44dba05b1bf1d11dee02")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerRollingUp.AnimEnd", Spec = "spec/movement/roll.md#rolling-s_playerrolling-also-the-parent-of-s_playerrollingup")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != MovementK.DoubleJumpChannel || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.m_LastRollingTime = pc.Level.TimeSeconds;
            if (pawn.CanUnCrouch() && !pc.bCrouchWithRoll)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                if (pawn.WeaponLoaded == 0)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WAIT_CR2ST_0H);
                    if (pc.bRemindDrawWeapon)
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0Hto2H);
                        pc.eventChangeState(MovementStateNames.s_PlayerWalkingMario_T_0HTo2H);
                    }
                    else
                    {
                        pc.eventChangeState(MovementStateNames.s_PlayerWalkingMario_T_CrToSt);
                    }
                }
                else
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WAIT_CR2ST_2H);
                    pc.eventChangeState(MovementStateNames.s_PlayerWalkingWeapon_T_CrToSt);
                }

                return;
            }

            pc.EndRollCrouched(pawn);
        }
    }
}
