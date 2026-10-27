using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_downTopPipe: the clip from the floor at the top down onto a vertical pipe.
// Written from spec/movement/pipe-vertical.md (Getting on from the top).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_downTopPipe")]
    public class s_downTopPipe : UcActorState, IPlayerMove
    {
        public static readonly s_downTopPipe Instance = new(UcMeta.s_downTopPipe, null);

        protected s_downTopPipe(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_downTopPipe.BeginState", Sha1 = "dc49c7475d277260f923f29a8b58480ad803065c")]
        [PortOf("uc:SBase.SPlayerController:s_downTopPipe.BeginState", Spec = "spec/movement/pipe-vertical.md#getting-on-from-the-top-s_waitattoppipe-s_downtoppipe")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCamera(ECamMode.ECM_SmoothTransitionCentered);

        [Uc("SBase.SPlayerController.s_downTopPipe.PlayerMove", Sha1 = "fbea64413e0d6897f642ea11a665cf51efd92d19")]
        [PortOf("uc:SBase.SPlayerController:s_downTopPipe.PlayerMove", Spec = "spec/movement/pipe-vertical.md#getting-on-from-the-top-s_waitattoppipe-s_downtoppipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>On the pipe: holding it, on the side the spy came down.</summary>
        [Uc("SBase.SPlayerController.s_downTopPipe.AnimEnd", Sha1 = "45d7d02b5535f7511ad86b896eb9405bdb7ba43d")]
        [PortOf("uc:SBase.SPlayerController:s_downTopPipe.AnimEnd", Spec = "spec/movement/pipe-vertical.md#getting-on-from-the-top-s_waitattoppipe-s_downtoppipe")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.MovementCameraSmooth(SmoothDefault);
            pc.bCrouchWithRoll = false;
            pawn.ChangeEnumAction(pawn.bIsLeftPart ? SBasePawn.SA_ON_PIPE2L : SBasePawn.SA_ON_PIPE2);
            pc.eventChangeState(MovementStateNames.s_OnPipe);
        }
    }
}
