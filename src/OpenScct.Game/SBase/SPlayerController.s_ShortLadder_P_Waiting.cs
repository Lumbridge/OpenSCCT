using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Provenance;
using EK = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_P_Waiting: the boost helper crouches with cupped hands; at the end of
// the begin clip he is ready. Written from spec/movement/coop.md (short ladder, helper, rule 2).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ShortLadder_P_Waiting")]
    public class s_ShortLadder_P_Waiting : s_ShortLadder_P_Base, IGadgetQuickChoice
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

        /// <summary>
        /// A quick gadget key while boosting (spies only, not during the boost clip, 2 s after the last quick launch,
        /// with no fire mode pending; slot 1 refused while the cloak is selected): the slot is selected; a grenade that
        /// can be quick-launched (a charge, or the tutorial) by a helper able to stand stops the help and is thrown
        /// from the walking state; a cloak is used at once and the help goes on.
        /// </summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_Waiting.GadgetQuickChoice", Sha1 = "a70e4f2a2439a4b75ee05b87dbfa36a39859d745")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Waiting.GadgetQuickChoice", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public void GadgetQuickChoice(SPlayerController self, int i)
        {
            if (self.SBPawn is not { } pawn || i >= PawnInventory.TeamSlots(pawn) || pawn.PawnAnim[0].Sequence == CoopCrAlHpEdP)
            {
                return;
            }

            if (i == 1 && pawn.SelectedGadget is SGadCloak)
            {
                return;
            }

            if (self.fDateFireMode != 0f || pawn.SPawnType != PawnType.SPAWNOFFENSE ||
                self.Level.TimeSeconds <= self.fDateLaunchedLastQuickGadget + EK.QuickLaunchCooldown)
            {
                return;
            }

            PawnInventory.SelectGagdet(pawn, i);
            switch (pawn.SelectedGadget)
            {
                case SGadGrenLauncher grenade when grenade.CanQuickLaunch &&
                    (grenade.Charge > 0 || GadgetWorld.CurLevelIsTutorial(pawn)) && (!pawn.bIsCrouched || pawn.CanUnCrouch()):
                    self.bUnCrouchAndUnDraw = 1;
                    self.bQuickGadgetQuery = true;
                    pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_STOP, true);
                    self.GotoWaitingState();
                    break;
                case SGadCloak cloak:
                    cloak.QuickUse();
                    break;
            }
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
