using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BTWCorner: turning round an inside corner onto the next wall. Written from
// spec/movement/back-to-wall.md (Corner).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_BTWCorner")]
    public class s_BTWCorner : UcActorState, IPlayerMove
    {
        public static readonly s_BTWCorner Instance = new(UcMeta.s_BTWCorner, null);

        protected s_BTWCorner(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The pawn turns to face along the new wall normal at 3 x 16384 per second, then holds the wall again.</summary>
        [Uc("SBase.SPlayerController.s_BTWCorner.PlayerMove", Sha1 = "145ff29c21057e4db38afaddf0c3c4b2b53e855d")]
        [PortOf("uc:SBase.SPlayerController:s_BTWCorner.PlayerMove", Spec = "spec/movement/back-to-wall.md#corner-s_btwcorner-fire-disabled")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            if (self.SBPawn is { } pawn)
            {
                int target = self.vWallNormal.Rotation().Yaw & 65535;
                int step = MovementMath.Int(MovementK.BtwCornerTurnRate * DeltaTime);
                int yaw = OpenScct.Physics.Movement.PhysicsDriver.FixedTurn(pawn.Rotation.Yaw, target, step);
                pawn.Rotation = new FRotator(0, yaw, 0);
                if (((yaw - target) & 65535) == 0)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_BTW_CORNEROFF);
                    self.eventChangeState(pawn.bIsCrouched ? MovementStateNames.s_BTW_down : MovementStateNames.s_BTW_up);
                }
            }

            self.SendMove(DeltaTime, default);
        }
    }
}
