using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state TopLedgeCrouch: the crouched pull-up from the hang (a root-motion clip). Written
// from spec/movement/ledge.md (Climbing up).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.TopLedgeCrouch")]
    public class TopLedgeCrouch : UcActorState, IPlayerMove
    {
        public static readonly TopLedgeCrouch Instance = new(UcMeta.TopLedgeCrouch, null);

        protected TopLedgeCrouch(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Provenance(ProvenanceKind.Decoded, "ledge.md TopLedgeCrouch AnimEnd: camera smoothing 80")]
        private const float EndSmooth = 80f;

        [Uc("SBase.SPlayerController.TopLedgeCrouch.BeginState", Sha1 = "0e47c659b00693e797811cb09d09683b298bbaa1")]
        [PortOf("uc:SBase.SPlayerController:TopLedgeCrouch.BeginState", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.MovementCamera(ECamMode.ECM_SmoothTransitionCentered);
            }

            pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
        }

        [Uc("SBase.SPlayerController.TopLedgeCrouch.PlayerMove", Sha1 = "f36cec19185257b883163770cb38b1e3cb7ca1a4")]
        [PortOf("uc:SBase.SPlayerController:TopLedgeCrouch.PlayerMove", Spec = "spec/movement/ledge.md#climbing-up")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The crouched pull-up ended: crouched on the ledge top with the latch on.</summary>
        [Uc("SBase.SPlayerController.TopLedgeCrouch.AnimEnd", Sha1 = "da33dbd1fccdbfc4f1e1d1fe1c9f898dc4888319")]
        [PortOf("uc:SBase.SPlayerController:TopLedgeCrouch.AnimEnd", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(EndSmooth);
                pc.bCrouchWithRoll = true;
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
            }

            base.AnimEnd(self, Channel);
        }
    }
}
