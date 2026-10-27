using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Tr_Barrier_Above_Ledge: from the hang up onto a ledge just above, crouched.
// Written from spec/movement/ledge.md (Climbing up).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_Tr_Barrier_Above_Ledge")]
    public class s_Tr_Barrier_Above_Ledge : TopLedgeBarrier
    {
        public static new readonly s_Tr_Barrier_Above_Ledge Instance = new(UcMeta.s_Tr_Barrier_Above_Ledge, TopLedgeBarrier.Instance);

        protected s_Tr_Barrier_Above_Ledge(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Tr_Barrier_Above_Ledge.BeginState", Sha1 = "dc49c7475d277260f923f29a8b58480ad803065c")]
        [PortOf("uc:SBase.SPlayerController:s_Tr_Barrier_Above_Ledge.BeginState", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCamera(ECamMode.ECM_SmoothTransitionCentered);

        /// <summary>The crouch wish stays set after the climb.</summary>
        [Uc("SBase.SPlayerController.s_Tr_Barrier_Above_Ledge.EndState", Sha1 = "d71c810ea05e60d02a55dc79169976ac75fd8de4")]
        [PortOf("uc:SBase.SPlayerController:s_Tr_Barrier_Above_Ledge.EndState", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void EndState(Actor self) => ((SPlayerController)self).bCrouchWithRoll = true;

        [Uc("SBase.SPlayerController.s_Tr_Barrier_Above_Ledge.PlayerMove", Sha1 = "a59068067a41cd0d32f3b206f4be1ce242e6a014")]
        [PortOf("uc:SBase.SPlayerController:s_Tr_Barrier_Above_Ledge.PlayerMove", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The climb ended: crouched on the upper ledge.</summary>
        [Uc("SBase.SPlayerController.s_Tr_Barrier_Above_Ledge.AnimEnd", Sha1 = "0cd8ed276d8f5cc31f904d6a7649f32a4ee01d9f")]
        [PortOf("uc:SBase.SPlayerController:s_Tr_Barrier_Above_Ledge.AnimEnd", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(SmoothDefault);
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
            }

            pc.AnimEnd(Channel);
        }
    }
}
