using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingMario_T_StToCr: the spy crouching down unarmed (Mario walking goes
// on during the clip). Written from spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_StToCr")]
    public class s_PlayerWalkingMario_T_StToCr : s_PlayerWalkingMario
    {
        public static new readonly s_PlayerWalkingMario_T_StToCr Instance =
            new(UcMeta.s_PlayerWalkingMario_T_StToCr, s_PlayerWalkingMario.Instance);

        protected s_PlayerWalkingMario_T_StToCr(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_StToCr.BeginState", Sha1 = "640ebb0f7bea35a3a8ab7d86eac5f5fc2f0f1076")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_StToCr.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.oldbUsing = pc.bUsing;
            pc.MovementCamera((pc.SBPawn?.WeaponLoaded ?? 0) == 0 ? ECamMode.ECM_WalkingCr : ECamMode.ECM_SecondPersonCr);
        }

        /// <summary>Mario walking without a jump.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_StToCr.PlayerMove", Sha1 = "1c682ce6542b45d64ae7e5e86314405575d57477")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_StToCr.PlayerMove", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            base.PlayerMove(self, DeltaTime);
        }

        /// <summary>The crouch clip ended: the crouched resting state for the hands.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_StToCr.AnimEnd", Sha1 = "facd028482260091b9849a100a0b7ba6ec652ca0")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_StToCr.AnimEnd", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void AnimEnd(Actor self, int Channel) => ((SPlayerController)self).CrouchClipEnded(Channel);

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_StToCr.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_StToCr.CanInteract", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanInteract(SPlayerController self) => false;
    }
}
