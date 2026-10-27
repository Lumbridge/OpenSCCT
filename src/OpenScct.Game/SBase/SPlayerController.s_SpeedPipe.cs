using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_SpeedPipe: the fast slide down a vertical pipe (Down with Use). Written from
// spec/movement/pipe-vertical.md (Fast slide).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_SpeedPipe")]
    public class s_SpeedPipe : UcActorState, IPlayerMove, ITopSynchro, INotifyHitWall
    {
        public static readonly s_SpeedPipe Instance = new(UcMeta.s_SpeedPipe, null);

        protected s_SpeedPipe(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// Down held speeds the slide (slide term -524), released brakes it (+524) until the native stops the spy,
        /// who then holds the pipe again; crouch drops.
        /// </summary>
        [Uc("SBase.SPlayerController.s_SpeedPipe.PlayerMove", Sha1 = "226b3aecad2c96de3b6d94a6a36adb5fe0fa35e3")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedPipe.PlayerMove", Spec = "spec/movement/pipe-vertical.md#fast-slide-s_speedpipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            FVector accel;
            if ((self.iMove & cMoveDown) == 0)
            {
                accel = new FVector(MovementK.PipeSlideAccel, 0f, -MovementK.PipeClimbSpeed);
                if (pawn.Velocity.Z == 0f)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPE);
                    pawn.bCanStop = true;
                    self.aForward = 0f;
                    self.eventChangeState(MovementStateNames.s_OnPipe);
                }
            }
            else
            {
                accel = new FVector(-MovementK.PipeSlideAccel, 0f, -MovementK.PipeClimbSpeed);
            }

            self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            self.bPressedJump = false;
            if (self.bCrouchWithRoll)
            {
                self.bCrouchWithRoll = false;
                pawn.MyDoJump();
                pawn.ChangeEnumAction(SBasePawn.SA_FALL_ST_0H);
                self.eventChangeState(MovementStateNames.s_PlayerJump);
            }

            self.SendMove(DeltaTime, accel);
        }

        /// <summary>The slide passed the bottom of the pipe in the air: drop on the next frame.</summary>
        [Uc("SBase.SPlayerController.s_SpeedPipe.TopSynchro", Sha1 = "5e8623cbf3a6e18bc9d6af96ae9fe5dc30e78164")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedPipe.TopSynchro", Spec = "spec/movement/pipe-vertical.md#fast-slide-s_speedpipe")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient())
            {
                self.bCrouchWithRoll = true;
            }
        }

        /// <summary>The floor at the bottom: a low landing.</summary>
        [Uc("SBase.SPlayerController.s_SpeedPipe.NotifyHitWall", Sha1 = "5d576ef44ddd029414ef1c5d93d0c7889044ec58")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedPipe.NotifyHitWall", Spec = "spec/movement/pipe-vertical.md#fast-slide-s_speedpipe")]
        public virtual bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                pawn.ChangeEnumAction(SBasePawn.SA_LAND_ST_0H_LOW);
                self.eventChangeState(MovementStateNames.PlayerLanding);
            }

            return true;
        }

        /// <summary>No acceleration or velocity is kept (the slide sounds stop with the sound port).</summary>
        [Uc("SBase.SPlayerController.s_SpeedPipe.EndState", Sha1 = "7437f0cb4da5543649782d2a6b89d6af9231e0be")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedPipe.EndState", Spec = "spec/movement/pipe-vertical.md#fast-slide-s_speedpipe")]
        public override void EndState(Actor self)
        {
            if (((SPlayerController)self).Pawn is { } pawn)
            {
                pawn.Acceleration = default;
                pawn.Velocity = default;
            }
        }
    }
}
