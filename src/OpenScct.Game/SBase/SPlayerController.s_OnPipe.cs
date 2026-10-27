using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_OnPipe: climbing a vertical pipe. Written from spec/movement/pipe-vertical.md
// (s_OnPipe).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_OnPipe")]
    public class s_OnPipe : UcActorState, IPlayerMove, IZipLine, IHandOverHand, INotifyHitWall, ITopPipe, ITopPipeCrouch, ILedge,
        ICanUseCam, ICanBeDropAttacked
    {
        public static readonly s_OnPipe Instance = new(UcMeta.s_OnPipe, null);

        protected s_OnPipe(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_OnPipe.BeginState", Sha1 = "e31267c241c8c83bac4453b3cb9b8d6e53344393")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.BeginState", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bCrouchWithRoll = false;
            pc.MovementCamera(ECamMode.ECM_NoTargetCol);
            pc.bBlockControlForJump = true;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        /// <summary>
        /// Up and Down climb at 145 (a step started goes on until the native says it can stop), Down with Use
        /// slides, Left and Right at rest swap to a neighbouring pipe, crouch drops.
        /// </summary>
        [Uc("SBase.SPlayerController.s_OnPipe.PlayerMove", Sha1 = "80710cbb15a004ff4c5c43158c3375ae7577967b")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.PlayerMove", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.iRealMove = self.iMove;
            int move = pawn.bCanStop || pawn.Velocity.Z == 0f ? self.iMove : self.iOldMove;
            FVector accel = default;
            if ((move & cMoveUp) != 0)
            {
                accel = new FVector(0f, 0f, MovementK.PipeClimbSpeed);
                pawn.bCanStop = false;
            }
            else if ((move & cMoveDown) != 0)
            {
                if (self.bUsing == 1 && self.GetStateTable().Find(MovementStateNames.s_SpeedPipe) is not null)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_SPEEDPIPE);
                    self.bCrouchWithRoll = false;
                    self.eventChangeState(MovementStateNames.s_SpeedPipe);
                }

                accel = new FVector(0f, 0f, -MovementK.PipeClimbSpeed);
                pawn.bCanStop = false;
            }

            if (pawn.bCanStop && (move & (cMoveLeft | cMoveRight)) != 0)
            {
                bool toLeft = (move & cMoveLeft) != 0;
                if (self.CheckForOtherPipeV(out FVector p1, out FVector p2, out FVector n) && self.CheckRoomToChangePipeV(toLeft))
                {
                    self.bCrouchWithRoll = false;
                    pawn.ChangeEnumAction(toLeft ? SBasePawn.SA_CHANGE_PIPEV_L : SBasePawn.SA_CHANGE_PIPEV_R);
                    self.eventChangeState(MovementStateNames.s_ChangePipeV);
                    pawn.ZipP1 = p1;
                    pawn.ZipP2 = p2;
                    pawn.m_geoNormal = n;
                    pawn.SendZipPAndNormalToServer();
                }
            }

            self.iOldMove = move;
            self.bPressedJump = false;
            self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            if (self.bCrouchWithRoll)
            {
                self.bCrouchWithRoll = false;
                pawn.MyDoJump();
                pawn.ChangeEnumAction(SBasePawn.SA_FALL_ST_0H);
                self.eventChangeState(MovementStateNames.s_PlayerJump);
            }

            self.SendMove(DeltaTime, accel);
        }

        [Uc("SBase.SPlayerController.s_OnPipe.ZipLine", Sha1 = "ac7b2fcf61f27b49e8ef83c37cc8d357b7f0b578")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.ZipLine", Spec = "spec/movement/zip-line.md#getting-on")]
        public virtual void ZipLine(SPlayerController self, FVector locationEnd, FRotator orientationEnd)
        {
            if (self.SBPawn is not { } pawn || self.GetStateTable().Find(MovementStateNames.s_GrabZip) is null)
            {
                return;
            }

            pawn.SendZipPToServer();
            pawn.ChangeEnumAction(SBasePawn.SA_ZIP_GRAB);
            self.eventChangeState(MovementStateNames.s_GrabZip);
        }

        [Uc("SBase.SPlayerController.s_OnPipe.HandOverHand", Sha1 = "47ef15aad200c653776ff053ed6cf72a72d3c2ed")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.HandOverHand", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public virtual void HandOverHand(SPlayerController self) => self.PipeToHandOverHand();

        /// <summary>The feet reached the floor at the bottom: standing.</summary>
        [Uc("SBase.SPlayerController.s_OnPipe.NotifyHitWall", Sha1 = "325f316a2896923f3ce5169009e068780ee780a2")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.NotifyHitWall", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public virtual bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                self.bPressedJump = false;
                self.bCrouchWithRoll = false;
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                self.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
            }

            return true;
        }

        /// <summary>The top while climbing up: over onto the floor, standing.</summary>
        [Uc("SBase.SPlayerController.s_OnPipe.TopPipe", Sha1 = "09943fc357e55793ac9ab2f4d65a8d0ac6b9a65e")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.TopPipe", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public virtual void TopPipe(SPlayerController self, bool bLeft)
        {
            if ((self.iRealMove & cMoveUp) != 0 && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_TOPPIPE);
                self.eventChangeState(MovementStateNames.s_TopPipe);
            }
        }

        /// <summary>The top with crouch room only: over onto the floor, crouched.</summary>
        [Uc("SBase.SPlayerController.s_OnPipe.TopPipeCrouch", Sha1 = "5737edf43b19fc01b558902629292fe798f99d62")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.TopPipeCrouch", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public virtual void TopPipeCrouch(SPlayerController self, bool bLeft)
        {
            if ((self.iRealMove & cMoveUp) != 0 && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_TOPPIPE_CR);
                self.eventChangeState(MovementStateNames.s_TopPipeCr);
            }
        }

        /// <summary>A ledge beside the pipe: across to it on the side there is one.</summary>
        [Uc("SBase.SPlayerController.s_OnPipe.Ledge", Sha1 = "53150277d76a91f5afe8a9f37e8571f56b2251bc")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.Ledge", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public virtual void Ledge(SPlayerController self)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if (self.bCanTakeLedgeFromPipe(true))
            {
                pawn.ChangeEnumAction(SBasePawn.SA_TR_PIPELEDGE_R);
            }
            else if (self.bCanTakeLedgeFromPipe(false))
            {
                pawn.ChangeEnumAction(SBasePawn.SA_TR_PIPELEDGE_L);
            }
            else
            {
                return;
            }

            self.eventChangeState(MovementStateNames.s_TrPipe2Ledge);
        }

        [Uc("SBase.SPlayerController.s_OnPipe.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.CanUseCam", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_OnPipe.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipe.CanBeDropAttacked", Spec = "spec/movement/pipe-vertical.md#s_onpipe-climbing")]
        public virtual bool CanBeDropAttacked(SPlayerController self) => true;
    }
}
