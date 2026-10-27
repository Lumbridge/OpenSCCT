using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingMario_T_2HToOH: the spy holstering (Mario walking goes on during the
// clip, on the draw channel). Written from spec/movement/walking.md (transition and resting states).
//
// The source declares it as extending s_PlayerWalkingMario; the generated state metadata lists no super state, so
// the state chain follows the metadata while the C# class keeps the Mario behaviour.
public partial class SPlayerController
{
    /// <remarks>Not ported here: the ignored SwitchBinocularVision (equipment spec).</remarks>
    [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_2HToOH")]
    public class s_PlayerWalkingMario_T_2HToOH : s_PlayerWalkingMario
    {
        public static new readonly s_PlayerWalkingMario_T_2HToOH Instance = new(UcMeta.s_PlayerWalkingMario_T_2HToOH, null);

        protected s_PlayerWalkingMario_T_2HToOH(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_2HToOH.BeginState", Sha1 = "4b61bd6d4d1454a3d6cca5ef4b435140e91be0df")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_2HToOH.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(pc.SBPawn is { bIsCrouched: true } ? ECamMode.ECM_WalkingCr : ECamMode.ECM_Walking);
        }

        /// <summary>Mario walking without a jump, the crouch latch held at the current stance.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_2HToOH.PlayerMove", Sha1 = "7fe26d14340a83491fbbda69e8f40ff54e896b88")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_2HToOH.PlayerMove", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.bCrouchWithRoll = self.SBPawn?.bIsCrouched ?? false;
            base.PlayerMove(self, DeltaTime);
        }

        /// <summary>The holster clip ended (draw channel): the unarmed resting state for the stance.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_2HToOH.AnimEnd", Sha1 = "7a0654b325483242b7a7b8dda69d57e210e39881")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_2HToOH.AnimEnd", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != SBasePawn.DRAWCHANNEL || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_2HToOH.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_2HToOH.CanInteract", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanInteract(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_2HToOH.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_2HToOH.CanIUseBinocular", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanIUseBinocular(SPlayerController self) => false;
    }
}
