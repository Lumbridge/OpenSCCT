using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerHit: a hit reaction clip on the damage channel; the player only turns the
// view until it ends. Written from spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PlayerHit")]
    public class s_PlayerHit : s_PlayerWalkingMario
    {
        public static new readonly s_PlayerHit Instance = new(UcMeta.s_PlayerHit, s_PlayerWalkingMario.Instance);

        protected s_PlayerHit(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>No input movement: the view turns and the pawn gets no acceleration.</summary>
        [Uc("SBase.SPlayerController.s_PlayerHit.PlayerMove", Sha1 = "df5ad36c13a1f15f9623e6203eda03aaad7a0750")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerHit.PlayerMove", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The hit clip ended (damage channel): back to the resting state for the stance and hands.</summary>
        [Uc("SBase.SPlayerController.s_PlayerHit.AnimEnd", Sha1 = "79bc90055a0f70bd9839532dc44f957bff65eda4")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerHit.AnimEnd", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        [Provenance(ProvenanceKind.Estimated, "The resting state is chosen as GotoWaitingState does (the spec names the result only)")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == SBasePawn.DAMAGECHANNEL && pc.IAmOnClient())
            {
                pc.GotoWaitingState();
            }
        }
    }
}
