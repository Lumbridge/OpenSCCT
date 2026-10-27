using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingWeapon_T_CrToSt: standing up in weapon movement (the merc, the armed
// spy). Written from spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_CrToSt")]
    public class s_PlayerWalkingWeapon_T_CrToSt : s_PlayerWalkingWeapon
    {
        public static new readonly s_PlayerWalkingWeapon_T_CrToSt Instance =
            new(UcMeta.s_PlayerWalkingWeapon_T_CrToSt, s_PlayerWalkingWeapon.Instance);

        protected s_PlayerWalkingWeapon_T_CrToSt(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_CrToSt.BeginState", Sha1 = "8d30f8d0a9e3f87345a2d26c5932c58d9551995b")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon_T_CrToSt.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not SPAWNDEFENSE)
            {
                pc.MovementCamera(ECamMode.ECM_SecondPerson);
            }
        }

        /// <summary>Weapon walking without a jump.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_CrToSt.PlayerMove", Sha1 = "0f607ea2183c6c24efeb84c82f37124cc7dca549")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon_T_CrToSt.PlayerMove", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            base.PlayerMove(self, DeltaTime);
        }

        /// <summary>The stand-up clip ended: standing armed, or holstering when a stand-and-holster is pending.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_CrToSt.AnimEnd", Sha1 = "b50ae62bf83442c0c14515f737c3a44da9bc5247")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon_T_CrToSt.AnimEnd", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pc.bUnCrouchAndUnDraw > 0)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2Hto0H);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingMario_T_2HToOH);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt2H);
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon_T_CrToSt.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon_T_CrToSt.CanInteract", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanInteract(SPlayerController self) => false;
    }
}
