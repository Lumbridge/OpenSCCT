using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Hoist_S_Jump: the hoist climber's jump to the hanging mate's hands, then the
// finish clip of the climb type, ending crouched on top. Written from spec/movement/coop.md (hoist, climber,
// rule 3).
//
// The source declares it as extending s_Hoist_S_Base; the generated state metadata lists no super state, so the
// state chain follows the metadata while the C# class keeps s_Hoist_S_Base's behaviour.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_Hoist_S_Jump")]
    public class s_Hoist_S_Jump : s_Hoist_S_Base, IPlayerMove, IClientStickyShock
    {
        public static new readonly s_Hoist_S_Jump Instance = new(UcMeta.s_Hoist_S_Jump, null);

        private static readonly Name CoopDynHstBSBg = new("CoopDynHstBSBg");
        private static readonly Name CoopDynHstBSBgf = new("CoopDynHstBSBgf");

        protected s_Hoist_S_Jump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        private static bool Jumping(SBasePawn pawn)
        {
            Name anim = pawn.PawnAnim[0].Sequence;
            return anim == CoopDynHstBSBg || anim == CoopDynHstBSBgf;
        }

        /// <summary>A sticky shock during the jump clip drops the climber (shocked with the animation), else only shocks him.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_Jump.ClientStickyShock", Sha1 = "752aa1e37a975c425a252b6d350a0b01ff223347")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_Jump.ClientStickyShock", Spec = "spec/movement/coop.md#hoist-climber")]
        public virtual void ClientStickyShock(SPlayerController self, int PillTag, bool bPlayAnim)
        {
            if (self.SBPawn is { } pawn && Jumping(pawn))
            {
                self.GlobalClientStickyShock(PillTag, true);
                pawn.ChangeEnumAction(SBasePawn.SA_JUMP_ST_0H);
                self.eventChangeState(MovementStateNames.s_PlayerJump);
            }
            else
            {
                self.GlobalClientStickyShock(PillTag, false);
            }
        }

        /// <summary>No jump, the view; the mate gone or dead drops the climber into a jump.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_Jump.PlayerMove", Sha1 = "bcc261a382421d3f69f9da6b99053ce0fded4b49")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_Jump.PlayerMove", Spec = "spec/movement/coop.md#hoist-climber")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.UpdateCamRotation(DeltaTime);
            if (self.SBPawn is { } pawn && pawn.m_InteractionTarget is not { Health: > 0 })
            {
                pawn.ChangeEnumAction(SBasePawn.SA_JUMP_ST_0H);
                self.eventChangeState(MovementStateNames.s_PlayerJump);
            }

            self.SendMove(DeltaTime, default);
        }

        /// <summary>
        /// The controlling side: the centred smooth camera, the climb type under the mate (ignoring pawns, quick
        /// barrier test) and the jump clip, the no-wall one for types 3 and 4.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_Jump.BeginState", Sha1 = "3ed1c1069dae7e898b32f3708ba9f9a842b7edab")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_Jump.BeginState", Spec = "spec/movement/coop.md#hoist-climber")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.MovementCamera(ECamMode.ECM_SmoothTransitionCentered);
            pc.iHoistClimbType = pawn.m_InteractionTarget?.FindHoistClimbType(true, true) ?? SBasePawn.SHL_None;
            pawn.ChangeEnumAction(pc.iHoistClimbType is SBasePawn.SHL_StandardNoWall or SBasePawn.SHL_BarrierNoWall
                ? SBasePawn.SA_HOIST_S_JUMP_NOWALL
                : SBasePawn.SA_HOIST_S_JUMP);
        }

        /// <summary>On the server the helper still waiting for the hoist goes back to the plain hang.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_Jump.EndState", Sha1 = "09399cb63b35dea6043f3ee8914b1f671cf32310")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_Jump.EndState", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role == ENetRole.ROLE_Authority && pc.SBPawn?.m_InteractionTarget?.Controller is SPlayerController mate &&
                mate.IsInState(MovementStateNames.s_Hoist_P_WaitForHoist))
            {
                mate.eventMirrorState(MovementStateNames.OnLedge);
            }
        }

        /// <summary>
        /// The controlling side: after the jump clip the finish clip of the climb type; after the finish clip,
        /// crouched (latch on) in s_PlayerWalkingCr0H.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_Jump.AnimEnd", Sha1 = "bb38128420899f03eb110667821f8661d9d7bb18")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_Jump.AnimEnd", Spec = "spec/movement/coop.md#hoist-climber")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient())
            {
                base.AnimEnd(self, Channel);
                return;
            }

            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (Jumping(pawn))
            {
                switch (pc.iHoistClimbType)
                {
                    case SBasePawn.SHL_Standard:
                        pawn.ChangeEnumAction(SBasePawn.SA_HOIST_S_FINISH);
                        break;
                    case SBasePawn.SHL_Barrier:
                        pawn.ChangeEnumAction(SBasePawn.SA_HOIST_S_BARRIER);
                        break;
                    case SBasePawn.SHL_StandardNoWall:
                        pawn.ChangeEnumAction(SBasePawn.SA_HOIST_S_FINISH_NOWALL);
                        break;
                    case SBasePawn.SHL_BarrierNoWall:
                        pawn.ChangeEnumAction(SBasePawn.SA_HOIST_S_BARRIER_NOWALL);
                        break;
                }

                return;
            }

            if (!pawn.bIsCrouched)
            {
                pc.bCrouchWithRoll = true;
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            }

            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H, true);
            pc.eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
        }
    }
}
