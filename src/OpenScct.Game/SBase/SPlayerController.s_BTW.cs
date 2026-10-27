using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BTW: on the wall (the parent of the standing, crouched, peek and fire states).
// Written from spec/movement/back-to-wall.md (On the wall). Not ported here: CanBeBlasted and ExplosionHit (the
// blast off the wall, combat port).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_BTW")]
    public class s_BTW : s_BTW_Base, INotifyHitWall, ICanBeDropAttacked, ICanActivateLaser
    {
        public static new readonly s_BTW Instance = new(UcMeta.s_BTW, s_BTW_Base.Instance);

        protected s_BTW(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BTW.BeginState", Sha1 = "4f12c4821326fbabf5b8d99babd4178cb9ee048a")]
        [PortOf("uc:SBase.SPlayerController:s_BTW.BeginState", Spec = "spec/movement/back-to-wall.md#on-the-wall-s_btw_up-standing-s_btw_down-crouched-both-extend-s_btw")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pc.FaceWall(pawn);
            }

            pc.MovementCamera(ECamMode.ECM_BTW);
            ReinitIfTazered(pc);
        }

        /// <summary>
        /// The move on the wall: draw (off the wall), leave (key held or the wall ended), the sidestep along the
        /// wall at 130.9 standing or 81.2 crouched, and the stop or peek at its end.
        /// </summary>
        [Uc("SBase.SPlayerController.s_BTW.MyPlayerMove", Sha1 = "f1ad7235fb23888f62a3befbb78fb8d326cbc284")]
        [PortOf("uc:SBase.SPlayerController:s_BTW.MyPlayerMove", Spec = "spec/movement/back-to-wall.md#on-the-wall-s_btw_up-standing-s_btw_down-crouched-both-extend-s_btw")]
        public virtual void MyPlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.bPressedJump = false;
            self.FaceWall(pawn);
            self.eventUpdateRotation(DeltaTime, 1f);
            bool cr = pawn.bIsCrouched;
            if (self.bForceFireMode && !pawn.bStickyShocking)
            {
                if (self.bForceLaserFireMode)
                {
                    MovementService.HooksOf(self).SwitchLaser(self, true);
                }

                self.bForceFireMode = false;
                pawn.ChangeEnumAction(SBasePawn.SA_BTW_OUT);
                if (cr)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0Hto2H);
                }
                else if (pawn.HandItem is null)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0Hto2H);
                }

                self.eventChangeState(MovementStateNames.s_PlayerWalkingMario_T_0HTo2H);
                self.SendMove(DeltaTime, default);
                return;
            }

            if (self.bUseBackToWall == 1 || !self.CheckBTW())
            {
                if (pawn.Velocity.Size() > MovementK.BtwLeaveSpeed)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_BTW_OUT);
                    pawn.ChangeEnumAction(cr ? SBasePawn.SA_WALK_CR_0H : SBasePawn.SA_WALK_ST_0H);
                    self.eventChangeState(cr ? MovementStateNames.s_PlayerWalkingCr0H : MovementStateNames.s_PlayerWalkingSt0H);
                }
                else
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_BTW_END);
                    self.eventChangeState(MovementStateNames.s_SortieBTW);
                }

                self.SendMove(DeltaTime, default);
                return;
            }

            float stick = (self.iMove & cMoveLeft) != 0 ? MovementK.StickFullScale : (self.iMove & cMoveRight) != 0 ? -MovementK.StickFullScale : 0f;
            if (cr)
            {
                stick *= MovementK.BtwCrouchedStick;
            }

            self.aStrafe = stick;
            pawn.Rotation.GetAxes(out _, out FVector y, out _);
            FVector accel = y * -stick;
            accel.Z = 0f;
            if (accel.Size() < 1f)
            {
                accel = default;
            }

            accel *= MovementK.BtwAccelScale;
            float speed = pawn.RunningSpeed * accel.Size() / MovementK.BtwSpeedDivisor;
            if (speed < (cr ? MovementK.BtwMinSpeedCrouched : MovementK.BtwMinSpeedStanding))
            {
                speed = 0f;
            }

            int r = self.CheckBTWEyes(self.aStrafe > 0f);
            if (r == 0 && speed > 0f && SvmMath.Abs(self.aStrafe) > MovementK.BtwPeekStick)
            {
                speed = 0f;
                bool left = self.aStrafe < 0f;
                pawn.ChangeEnumAction(left ? SBasePawn.SA_BTW_LEFT : SBasePawn.SA_BTW_RIGHT);
                self.eventChangeState(cr
                    ? (left ? MovementStateNames.s_BTW_CrouchLeft : MovementStateNames.s_BTW_CrouchRight)
                    : (left ? MovementStateNames.s_BTW_StandLeft : MovementStateNames.s_BTW_StandRight));
            }
            else if (r == -1)
            {
                speed = 0f;
            }

            if (speed == 0f)
            {
                accel = default;
            }

            pawn.GroundSpeed = speed;
            self.SendMove(DeltaTime, accel);
        }

        /// <summary>A wall met while sliding: an inside corner turns the spy onto it (2 s after the last special move).</summary>
        [Uc("SBase.SPlayerController.s_BTW.NotifyHitWall", Sha1 = "0578ff0f1a71615c1412ca9357d723c654b082d4")]
        [PortOf("uc:SBase.SPlayerController:s_BTW.NotifyHitWall", Spec = "spec/movement/back-to-wall.md#on-the-wall-s_btw_up-standing-s_btw_down-crouched-both-extend-s_btw")]
        public virtual bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            float now = self.Level.TimeSeconds;
            if (self.IAmOnClient() && self.SBPawn is { } pawn && now - self.m_LastSpecialMoveTime > MovementK.BtwCornerCooldown &&
                self.CheckBTWCorner(HitNormal))
            {
                self.vWallNormal = new FVector(HitNormal.X, HitNormal.Y, 0f).SafeNormal();
                pawn.SetInfoForVandamme(self.vWallNormal);
                pawn.ChangeEnumAction(SBasePawn.SA_BTW_CORNER);
                self.m_LastSpecialMoveTime = now;
                self.eventChangeState(MovementStateNames.s_BTWCorner);
            }

            return true;
        }

        [Uc("SBase.SPlayerController.s_BTW.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_BTW.CanBeDropAttacked", Spec = "spec/movement/back-to-wall.md#on-the-wall-s_btw_up-standing-s_btw_down-crouched-both-extend-s_btw")]
        public virtual bool CanBeDropAttacked(SPlayerController self) => true;

        /// <summary>The laser is allowed when armed and not drawing or holstering.</summary>
        [Uc("SBase.SPlayerController.s_BTW.CanActivateLaser", Sha1 = "dbdaa67ae10467e3b9c9ad54a6c00a90d1c9998d")]
        [PortOf("uc:SBase.SPlayerController:s_BTW.CanActivateLaser", Spec = "spec/movement/back-to-wall.md#on-the-wall-s_btw_up-standing-s_btw_down-crouched-both-extend-s_btw")]
        public virtual bool CanActivateLaser(SPlayerController self) => self.SBPawn is { WeaponLoaded: > 0, bWeaponTransit: false };
    }
}
