using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state TopLedgeBarrier: the vault from the hang over a thin wall top, landing beyond it.
// Written from spec/movement/ledge.md (Climbing up).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.TopLedgeBarrier")]
    public class TopLedgeBarrier : UcActorState, IPlayerMove
    {
        public static readonly TopLedgeBarrier Instance = new(UcMeta.TopLedgeBarrier, null);

        protected TopLedgeBarrier(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.TopLedgeBarrier.BeginState", Sha1 = "dc49c7475d277260f923f29a8b58480ad803065c")]
        [PortOf("uc:SBase.SPlayerController:TopLedgeBarrier.BeginState", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCamera(ECamMode.ECM_SmoothTransitionCentered);

        [Uc("SBase.SPlayerController.TopLedgeBarrier.PlayerMove", Sha1 = "c3b2a56cbfe1dd192fe7ec90457c082a6b87f12a")]
        [PortOf("uc:SBase.SPlayerController:TopLedgeBarrier.PlayerMove", Spec = "spec/movement/ledge.md#climbing-up")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.LimitedViewByCamera(DeltaTime, 0, 0);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>Over the wall top: falling beyond it.</summary>
        [Uc("SBase.SPlayerController.TopLedgeBarrier.AnimEnd", Sha1 = "76c7848595907fde18bb3827406ccbee6f06ccc2")]
        [PortOf("uc:SBase.SPlayerController:TopLedgeBarrier.AnimEnd", Spec = "spec/movement/ledge.md#climbing-up")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_FALL_AFTERLEDGEBARRIER);
                pc.eventChangeState(MovementStateNames.s_PlayerJump);
            }

            base.AnimEnd(self, Channel);
        }
    }
}
