using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_OnLadder: climbing a ladder rung by rung. Written from spec/movement/ladder.md
// (climbing).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_OnLadder")]
    public class s_OnLadder : s_LadderBase, IPlayerMove, ITopLadder, ICanUseCam
    {
        public static new readonly s_OnLadder Instance = new(UcMeta.s_OnLadder, s_LadderBase.Instance);

        protected s_OnLadder(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The camera only while resting on a rung.</summary>
        [Uc("SBase.SPlayerController.s_OnLadder.CanUseCam", Sha1 = "75d86965a3d40b08abc56c649c07f64d1142913a")]
        [PortOf("uc:SBase.SPlayerController:s_OnLadder.CanUseCam", Spec = "spec/movement/ladder.md#climbing-s_onladder")]
        public virtual bool CanUseCam(SPlayerController self) => self.iMove == 0 && self.bCanStop;

        [Uc("SBase.SPlayerController.s_OnLadder.BeginState", Sha1 = "c29b2d6c2864aa9728d7e166e84cc5b6a88c813b")]
        [PortOf("uc:SBase.SPlayerController:s_OnLadder.BeginState", Spec = "spec/movement/ladder.md#climbing-s_onladder")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bCrouchWithRoll = false;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
            pc.bCanStop = true;
        }

        /// <summary>
        /// One frame on the ladder: Up and Down climb at 126 and always finish on a rung (the last frame's speed is
        /// cut to land on it), Up stops under a ceiling, Use with Down slides, Left and Right at rest swing to the
        /// other side, crouch lets go.
        /// </summary>
        [Uc("SBase.SPlayerController.s_OnLadder.PlayerMove", Sha1 = "da074ea32c61e0a5323f9e221b1199f5630f6193")]
        [PortOf("uc:SBase.SPlayerController:s_OnLadder.PlayerMove", Spec = "spec/movement/ladder.md#climbing-s_onladder")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.MercLadderFirstPerson();
            self.bPressedJump = false;
            if (pawn.SPawnType == PawnType.SPAWNDEFENSE)
            {
                self.MercLadderAim();
            }

            self.LocalMyDesiredRotation = self.MyDesiredRotation;
            self.iRealMove = self.iMove;
            if ((self.iMove & cMoveUp) != 0 && !self.CheckCeilingForLadder())
            {
                self.iMove -= cMoveUp;
            }

            int move = self.bCanStop ? self.iMove : self.iOldMove;
            FVector newAccel = default;
            if (self.bCrouchWithRoll)
            {
                JumpFromLadder(self);
            }
            else
            {
                LadderEnds(pawn, out float bottom, out _);
                float above = pawn.Location.Z - bottom - MovementK.LadderRungOffset;
                float rungs = MovementMath.Int(above / MovementK.LadderRungSpacing);
                if (self.bUsing == 1 && (self.iMove & cMoveDown) != 0)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_SPEEDLADDER);
                    self.eventChangeState(MovementStateNames.s_SpeedLadder);
                }
                else if ((move & cMoveUp) != 0)
                {
                    float toNext = MovementK.LadderRungSpacing - (above - rungs * MovementK.LadderRungSpacing);
                    if ((self.iMove & cMoveUp) == 0)
                    {
                        newAccel = new FVector(0f, 0f, MovementK.SpeedForLadder);
                        if (toNext / DeltaTime < MovementK.SpeedForLadder)
                        {
                            newAccel.Z = toNext / DeltaTime;
                            self.bCanStop = true;
                        }
                        else
                        {
                            self.bCanStop = false;
                        }
                    }
                    else if (toNext / DeltaTime < MovementK.SpeedForLadder)
                    {
                        if (!self.CheckCeilingForLadder())
                        {
                            newAccel.Z = toNext / DeltaTime;
                            self.bCanStop = true;
                        }
                        else
                        {
                            newAccel = new FVector(0f, 0f, MovementK.SpeedForLadder);
                            self.bCanStop = false;
                        }
                    }
                    else
                    {
                        newAccel = new FVector(0f, 0f, MovementK.SpeedForLadder);
                        self.bCanStop = false;
                    }
                }
                else if ((move & cMoveDown) != 0)
                {
                    if ((self.iMove & cMoveDown) == 0)
                    {
                        float toCurrent = above - rungs * MovementK.LadderRungSpacing;
                        newAccel = new FVector(0f, 0f, -MovementK.SpeedForLadder);
                        if (-toCurrent / DeltaTime > -MovementK.SpeedForLadder)
                        {
                            newAccel.Z = -toCurrent / DeltaTime;
                            self.bCanStop = true;
                        }
                        else
                        {
                            self.bCanStop = false;
                        }
                    }
                    else
                    {
                        newAccel = new FVector(0f, 0f, -MovementK.SpeedForLadder);
                        self.bCanStop = false;
                    }
                }

                if (pawn.bCanStop && newAccel.Z == 0f && !self.bCrouchWithRoll && pawn.Physics != EPhysics.PHYS_Falling)
                {
                    if ((move & cMoveLeft) != 0)
                    {
                        if (self.CheckRoomToChangeSideLadder(true))
                        {
                            pawn.ChangeEnumAction(pawn.bIsLeftPart ? SBasePawn.SA_LADDER_TOTOP_L : SBasePawn.SA_LADDER_TOTOP_R);
                            self.eventChangeState(MovementStateNames.s_WaitForChangeSideLadderL);
                        }
                    }
                    else if ((move & cMoveRight) != 0 && self.CheckRoomToChangeSideLadder(false))
                    {
                        pawn.ChangeEnumAction(pawn.bIsLeftPart ? SBasePawn.SA_LADDER_TOTOP_L : SBasePawn.SA_LADDER_TOTOP_R);
                        self.eventChangeState(MovementStateNames.s_WaitForChangeSideLadderR);
                    }
                }
            }

            self.iOldMove = move;
            if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
            {
                self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            }

            self.SendMove(DeltaTime, newAccel);
        }

        /// <summary>The hands passed the ladder top while Up is held: the climb out.</summary>
        [Uc("SBase.SPlayerController.s_OnLadder.TopLadder", Sha1 = "19a8e6af5fef4fe6679ea8fcda2f0b55cd18c871")]
        [PortOf("uc:SBase.SPlayerController:s_OnLadder.TopLadder", Spec = "spec/movement/ladder.md#climbing-s_onladder")]
        public virtual void TopLadder(SPlayerController self)
        {
            if (self.IAmOnClient() && (self.iRealMove & cMoveUp) != 0)
            {
                self.eventChangeState(MovementStateNames.s_LadderToTopTr);
            }
        }
    }
}
