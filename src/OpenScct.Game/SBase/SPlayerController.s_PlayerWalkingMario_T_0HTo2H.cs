using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingMario_T_0HTo2H: the spy drawing the weapon: he keeps moving on the
// stick in view axes while the pawn turns to the view, then goes to the armed resting state. Written from
// spec/movement/walking.md (transition and resting states).
//
// The source declares it as extending s_PlayerWalkingMario; the generated state metadata lists no super state, so
// the state chain follows the metadata while the C# class keeps the Mario events.
public partial class SPlayerController
{
    /// <remarks>
    /// Not ported here: the auto-target turn (FindAttClosestTarget and bAttTargetEnemy, combat spec), SetCamMode1st
    /// (camera) and the ignored AltFire, Fire, GadgetQuickChoice and SwitchBinocularVision.
    /// </remarks>
    [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_0HTo2H")]
    public class s_PlayerWalkingMario_T_0HTo2H : s_PlayerWalkingMario
    {
        public static new readonly s_PlayerWalkingMario_T_0HTo2H Instance = new(UcMeta.s_PlayerWalkingMario_T_0HTo2H, null);

        [Provenance(ProvenanceKind.Decoded, "walking.md s_PlayerWalkingMario_T_0HTo2H.PlayerMove: pawn yaw within 300 of the view")]
        private const int FacingTolerance = 300;

        [Provenance(ProvenanceKind.Decoded, "walking.md s_PlayerWalkingMario_T_0HTo2H.PlayerMove: pawn turn 65000 x dt")]
        private const float DrawTurnRate = 65000f;

        protected s_PlayerWalkingMario_T_0HTo2H(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_0HTo2H.BeginState", Sha1 = "401c609a529187b8718a731c2bb2296eda388b34")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_0HTo2H.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bTransitionFinished = false;
            pc.bRollAction = false;
        }

        /// <summary>
        /// The stick moves the pawn in view axes (no Mario turn, no standing boost), the pawn turns to the view, and
        /// once facing it with the draw finished the spy is armed.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_0HTo2H.PlayerMove", Sha1 = "4e4d28dd5b543e1edd22b9f07d94664039b0e14a")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_0HTo2H.PlayerMove", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.bPressedJump = false;
            self.SetLocation(pawn.Location);
            self.ClampStick();
            if (pawn.bIsCrouched)
            {
                self.aForward *= pawn.CrouchedPct;
                self.aStrafe *= pawn.CrouchedPct;
            }

            self.aForward *= MovementK.AccelRequestScale;
            self.aStrafe *= MovementK.AccelRequestScale;
            self.eventUpdateRotation(DeltaTime, 1f);
            MovementMath.FlatAxes(self.Rotation, out FVector x, out FVector y);
            FVector newAccel = x.SafeNormal() * self.aForward + y.SafeNormal() * self.aStrafe;
            self.MarioGroundSpeed(pawn, ref newAccel);
            if (pawn.bIsCrouched && self.CeilingAbove(pawn))
            {
                pawn.GroundSpeed = 0f;
            }

            int diff = (self.Rotation - pawn.Rotation).Normalize().Yaw;
            if ((diff < 0 ? -diff : diff) > FacingTolerance)
            {
                FRotator r = pawn.Rotation;
                r.Yaw = ClampRotation(r.Yaw, self.Rotation.Yaw, -MovementMath.Int(DrawTurnRate * DeltaTime),
                    MovementMath.Int(DrawTurnRate * DeltaTime));
                pawn.Rotation = r;
            }
            else if (self.bTransitionFinished)
            {
                if (pawn.bIsCrouched)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_2H);
                    self.eventChangeState(MovementStateNames.s_PlayerWalkingCr2H);
                }
                else
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H);
                    self.eventChangeState(MovementStateNames.s_PlayerWalkingSt2H);
                }
            }

            self.SendMove(DeltaTime, newAccel);
        }

        /// <summary>The draw clip ended (draw channel): the transition can finish (a low ceiling asks to holster again).</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_0HTo2H.AnimEnd", Sha1 = "4b30ef0a4583064f61de1c15b746cabb65fe004c")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_0HTo2H.AnimEnd", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != SBasePawn.DRAWCHANNEL)
            {
                return;
            }

            pc.bTransitionFinished = true;
            if (pc.SBPawn is { bIsCrouched: true } pawn && pc.CeilingAbove(pawn))
            {
                pc.bForceExitFireMode = true;
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_0HTo2H.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_0HTo2H.CanInteract", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanInteract(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_0HTo2H.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_0HTo2H.CanIUseBinocular", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanIUseBinocular(SPlayerController self) => false;
    }
}
