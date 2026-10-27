using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingWeapon_T_StToCr: crouching down in weapon movement (the merc, the
// armed spy). Written from spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_StToCr")]
    public class s_PlayerWalkingWeapon_T_StToCr : s_PlayerWalkingWeapon
    {
        public static new readonly s_PlayerWalkingWeapon_T_StToCr Instance =
            new(UcMeta.s_PlayerWalkingWeapon_T_StToCr, s_PlayerWalkingWeapon.Instance);

        protected s_PlayerWalkingWeapon_T_StToCr(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_StToCr.BeginState", Sha1 = "1f2f74c6974002530e133cac7de757928b7369f0")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon_T_StToCr.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not SPAWNDEFENSE)
            {
                pc.MovementCamera(ECamMode.ECM_SecondPersonCr);
            }
        }

        /// <summary>Weapon walking without a jump.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_StToCr.PlayerMove", Sha1 = "73c23b4d8a79373058f197e24987bac782bf4e20")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon_T_StToCr.PlayerMove", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            base.PlayerMove(self, DeltaTime);
        }

        /// <summary>The crouch clip ended: the crouched resting state for the hands.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_StToCr.AnimEnd", Sha1 = "9ddc67194135808c812a8fb4c2946a327ce203ef")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon_T_StToCr.AnimEnd", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void AnimEnd(Actor self, int Channel) => ((SPlayerController)self).CrouchClipEnded(Channel);

        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_StToCr.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon_T_StToCr.CanInteract", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanInteract(SPlayerController self) => false;
    }
}
