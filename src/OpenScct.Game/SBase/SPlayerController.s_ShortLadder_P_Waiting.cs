using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_P_Waiting: the boost helper crouches with cupped hands; at the end of
// the begin clip he is ready. Written from spec/movement/coop.md (short ladder, helper, rule 2).
public partial class SPlayerController
{
    /// <remarks>
    /// Not ported here: the quick gadget key (GadgetQuickChoice: the grenade launcher's quick launch, which stops the
    /// help, and the cloak's quick use; equipment spec).
    /// </remarks>
    [Uc("SBase.SPlayerController.s_ShortLadder_P_Waiting")]
    public class s_ShortLadder_P_Waiting : s_ShortLadder_P_Base
    {
        public static new readonly s_ShortLadder_P_Waiting Instance = new(UcMeta.s_ShortLadder_P_Waiting, s_ShortLadder_P_Base.Instance);

        private static readonly Name CoopCrAlHpEdP = new("CoopCrAlHpEdP");
        private static readonly Name CoopCrAlHpBgP = new("CoopCrAlHpBgP");

        protected s_ShortLadder_P_Waiting(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The controlling side plays the begin clip (forced) and crouches a standing helper (latch on).</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_Waiting.BeginState", Sha1 = "23db223ee77356d7a660630548c296b3f9940b26")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Waiting.BeginState", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_BEGIN, true);
                if (!pawn.bIsCrouched)
                {
                    pc.MovementCameraSmooth(CameraSmoothDefault);
                    pc.bCrouchWithRoll = true;
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                }
            }

            base.BeginState(self);
        }

        /// <summary>The key again (when ready, or with jump pressed): the helper stands down.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_Waiting.PerformCoopMvt", Sha1 = "ed0d6fa8527e4d2356aea1c66f83d035e6fa081e")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Waiting.PerformCoopMvt", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void PerformCoopMvt(SPlayerController self)
        {
            if (self.SBPawn is { } pawn && (pawn.bReadyForShortLadder || self.bPressedJump))
            {
                pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_LEAVE);
                self.eventChangeState(MovementStateNames.s_ShortLadder_P_Out);
            }
        }

        /// <summary>
        /// Any of reload, rate of fire, jump, fire mode, fire or the stick outside the 100 square stops the help unless
        /// the boost clip plays; held still, the view turning.
        /// </summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_Waiting.PlayerMove", Sha1 = "f71525997822b573c402baeb665b233637809093")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Waiting.PlayerMove", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is { } pawn && self.CoopHelperCancels() && pawn.PawnAnim[0].Sequence != CoopCrAlHpEdP)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_STOP, true);
                self.GotoWaitingState();
            }

            self.bToggleUsing = 0;
            self.bPressedJump = false;
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The begin clip ended (controlling side): the waiting hold, which makes the helper ready.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_Waiting.AnimEnd", Sha1 = "dc371a86e881affe8adf1a7e651155b0c55351ba")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Waiting.AnimEnd", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient())
            {
                base.AnimEnd(self, Channel);
                return;
            }

            if (pc.SBPawn is { } pawn && pawn.PawnAnim[0].Sequence == CoopCrAlHpBgP)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_WAITING);
            }
        }
    }
}
