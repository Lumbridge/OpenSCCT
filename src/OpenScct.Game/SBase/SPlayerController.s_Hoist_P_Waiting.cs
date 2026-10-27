using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Hoist_P_Waiting: the hanging helper offers the hoist (the waiting action makes him
// ready) until the pull clip ends or he cancels. Written from spec/movement/coop.md (hoist, helper, rule 1).
public partial class SPlayerController
{
    /// <remarks>
    /// The pull clips (CoopDynHstBP, and CoopDynHstBPf with no wall under the ledge): while one plays the helper no
    /// longer cancels.
    /// </remarks>
    [Uc("SBase.SPlayerController.s_Hoist_P_Waiting")]
    public class s_Hoist_P_Waiting : s_Hoist_P_Base, IGadgetQuickChoice, IClientDamage
    {
        public static new readonly s_Hoist_P_Waiting Instance = new(UcMeta.s_Hoist_P_Waiting, s_Hoist_P_Base.Instance);

        private static readonly Name CoopDynHstBP = new("CoopDynHstBP");
        private static readonly Name CoopDynHstBPf = new("CoopDynHstBPf");

        protected s_Hoist_P_Waiting(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        private static bool Pulling(SBasePawn pawn)
        {
            Name anim = pawn.PawnAnim[0].Sequence;
            return anim == CoopDynHstBP || anim == CoopDynHstBPf;
        }

        private static void Leave(SPlayerController pc, SBasePawn pawn)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_HOIST_P_LEAVE);
            pc.eventChangeState(MovementStateNames.OnLedge);
        }

        /// <summary>The controlling side plays the waiting hang (forced), which makes the helper ready.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_Waiting.BeginState", Sha1 = "bc00963237ea59a989aea3925fda2b9a7b9b7ee1")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Waiting.BeginState", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_HOIST_P_WAITING, true);
            }

            base.BeginState(self);
        }

        /// <summary>The key again (when ready, or with jump pressed) lets go of the offer.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_Waiting.PerformCoopMvt", Sha1 = "59c09f9592b1b8ef5ced3f24a6e63e9e29d9ec07")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Waiting.PerformCoopMvt", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override void PerformCoopMvt(SPlayerController self)
        {
            if (self.SBPawn is { } pawn && (pawn.bReadyForHoistClimb || self.bPressedJump))
            {
                Leave(self, pawn);
            }
        }

        /// <summary>Hit feedback, then a living helper lets go of the offer.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_Waiting.ClientDamage", Sha1 = "f1eee01e99bd31f01d252ae2ebcf879abc1283f6")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Waiting.ClientDamage", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public virtual void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
        {
            self.GlobalClientDamage(Momentum, DamageType, HitLocation, instigatedBy);
            if (self.SBPawn is { Health: > 0 } pawn)
            {
                Leave(self, pawn);
            }
        }

        /// <summary>A quick gadget key lets go of the offer.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_Waiting.GadgetQuickChoice", Sha1 = "7673f6a9fdd194179eed78f10cafc4f65fc4908b")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Waiting.GadgetQuickChoice", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public virtual void GadgetQuickChoice(SPlayerController self, int i)
        {
            if (self.SBPawn is { } pawn)
            {
                Leave(self, pawn);
            }
        }

        /// <summary>
        /// Any of reload, rate of fire, jump, fire mode, fire or the stick outside the 100 square cancels unless the
        /// pull has started; held still, the view turning.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_Waiting.PlayerMove", Sha1 = "539ba786d6ed514e15b20be4c5c326a80b097f4a")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Waiting.PlayerMove", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is { } pawn && self.CoopHelperCancels() && !Pulling(pawn))
            {
                Leave(self, pawn);
            }

            self.bToggleUsing = 0;
            self.bPressedJump = false;
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The pull clip ended on the controlling side: back to the plain hang.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_Waiting.AnimEnd", Sha1 = "0d697091f855d9c3e984bc55221508f17e70f409")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Waiting.AnimEnd", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient())
            {
                if (pc.SBPawn is { } pawn && Pulling(pawn))
                {
                    pc.bPressedJump = false;
                    pc.bCrouchWithRoll = false;
                    pawn.ChangeEnumAction(SBasePawn.SA_ONLEDGE);
                    pc.eventChangeState(MovementStateNames.OnLedge);
                }
            }
            else
            {
                base.AnimEnd(self, Channel);
            }
        }

        /// <summary>The controlling side stops offering (forced).</summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_Waiting.EndState", Sha1 = "174290156f31e3e71ee1e76e01dc411d3ec97b65")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Waiting.EndState", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_HOIST_P_STOP, true);
            }

            base.EndState(self);
        }
    }
}
