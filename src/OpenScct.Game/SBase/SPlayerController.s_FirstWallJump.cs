using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController wall jump states: the run-up (s_FirstWallJumpPrepare, ..FromJump, s_FirstWallJumpSaltoPrepareFromJump),
// the kick (s_FirstWallJump, s_FirstWallJumpSalto) and the climb or back flip off the wall (s_WallJumpSalto). Written
// from spec/movement/wall-jump-split-vandamme.md (wall jump).
public partial class SPlayerController
{
    /// <summary>
    /// The run-up frame of the wall jump states: full run (3000 along the facing, GroundSpeed RunningSpeed), the
    /// zero-extent probe 500 ahead (actors only for the salto), the kick under 105; then the view, the zones and the
    /// move. <paramref name="abortWithoutWall"/> and <paramref name="timeout"/> are the from-jump variants' aborts.
    /// </summary>
    internal void WallJumpRunUp(float DeltaTime, bool traceActors, Name kickState, bool abortWithoutWall, float timeout, bool pressedJumpLast)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        pawn.Rotation.GetAxes(out FVector x, out _, out _);
        FVector newAccel = x * MovementK.WallJumpRunAccel;
        FVector start = pawn.Location;
        if (MovementNatives.Trace(pawn, out FVector hitLocation, out _, start + x * fDistanceForContextualJump, start, traceActors) is not null)
        {
            if ((hitLocation - pawn.Location).Size() < MovementK.WallJumpKickDistance)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WLJP_JUMP1);
                eventChangeState(kickState);
            }
        }
        else if (abortWithoutWall)
        {
            eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
        }

        if (timeout > 0f && Level.TimeSeconds - m_LastSpecialMoveTime > timeout)
        {
            eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
        }

        eventUpdateRotation(DeltaTime, 1f);
        pawn.GroundSpeed = pawn.RunningSpeed * (newAccel.Size() / MovementK.SpeedDivisor);
        bToggleUsing = 0;
        pawn.ResetZones();
        bFindGE = false;
        eventCheckArmsZone();
        bTestBackFeetGrab = false;
        if (!bFindGE)
        {
            eventCheckFeetZone();
        }

        if (!pressedJumpLast)
        {
            bPressedJump = false;
        }

        SendMove(DeltaTime, newAccel);
        if (pressedJumpLast)
        {
            bPressedJump = false;
        }
    }

    [Uc("SBase.SPlayerController.s_FirstWallJumpPrepare")]
    public class s_FirstWallJumpPrepare : s_PlayerWalkingMario
    {
        public static new readonly s_FirstWallJumpPrepare Instance = new(UcMeta.s_FirstWallJumpPrepare, s_PlayerWalkingMario.Instance);

        protected s_FirstWallJumpPrepare(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Faces the wall (the reverse of vWallNormal, flat), then the run-up without abort or timeout.</summary>
        [Uc("SBase.SPlayerController.s_FirstWallJumpPrepare.PlayerMove", Sha1 = "5ae362cee5f20818bd471d54e4e36947ff0bb012")]
        [PortOf("uc:SBase.SPlayerController:s_FirstWallJumpPrepare.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#run-up-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is { } p)
            {
                FRotator face = (-self.vWallNormal).Rotation();
                p.Rotation = new FRotator(0, face.Yaw, 0);
            }

            self.WallJumpRunUp(DeltaTime, traceActors: false, MovementStateNames.s_FirstWallJump, abortWithoutWall: false, timeout: 0f,
                pressedJumpLast: true);
        }
    }

    [Uc("SBase.SPlayerController.s_FirstWallJumpPrepareFromJump")]
    public class s_FirstWallJumpPrepareFromJump : s_PlayerWalkingMario
    {
        public static new readonly s_FirstWallJumpPrepareFromJump Instance =
            new(UcMeta.s_FirstWallJumpPrepareFromJump, s_PlayerWalkingMario.Instance);

        protected s_FirstWallJumpPrepareFromJump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The run-up to the wall under a ledge: kick under 105, walk again without a wall ahead or after 2 s.</summary>
        [Uc("SBase.SPlayerController.s_FirstWallJumpPrepareFromJump.PlayerMove", Sha1 = "f238b36e14c2b442801ac45c20bc0e8f19da7043")]
        [PortOf("uc:SBase.SPlayerController:s_FirstWallJumpPrepareFromJump.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#run-up-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) =>
            self.WallJumpRunUp(DeltaTime, traceActors: false, MovementStateNames.s_FirstWallJump, abortWithoutWall: true,
                MovementK.WallJumpPrepareTimeout, pressedJumpLast: false);
    }

    [Uc("SBase.SPlayerController.s_FirstWallJumpSaltoPrepareFromJump")]
    public class s_FirstWallJumpSaltoPrepareFromJump : s_PlayerWalkingMario
    {
        public static new readonly s_FirstWallJumpSaltoPrepareFromJump Instance =
            new(UcMeta.s_FirstWallJumpSaltoPrepareFromJump, s_PlayerWalkingMario.Instance);

        protected s_FirstWallJumpSaltoPrepareFromJump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The run-up to a wall with no ledge in reach: kick under 105 (actors seen), walk again without a wall or after 3 s.</summary>
        [Uc("SBase.SPlayerController.s_FirstWallJumpSaltoPrepareFromJump.PlayerMove", Sha1 = "fd16efcce16e81cf8c010b85b0c39bcf05a99937")]
        [PortOf("uc:SBase.SPlayerController:s_FirstWallJumpSaltoPrepareFromJump.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#run-up-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) =>
            self.WallJumpRunUp(DeltaTime, traceActors: true, MovementStateNames.s_FirstWallJumpSalto, abortWithoutWall: true,
                MovementK.WallJumpSaltoPrepareTimeout, pressedJumpLast: false);
    }

    [Uc("SBase.SPlayerController.s_FirstWallJump")]
    public class s_FirstWallJump : UcActorState, IPlayerMove
    {
        public static readonly s_FirstWallJump Instance = new(UcMeta.s_FirstWallJump, null);

        protected s_FirstWallJump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The kick ended: Use held flips back; else up to the ledge by its height (up, up small, waist).</summary>
        [Uc("SBase.SPlayerController.s_FirstWallJump.AnimEnd", Sha1 = "c4840c7e4aca86b1fe71f00038d09b43fbebce3b")]
        [PortOf("uc:SBase.SPlayerController:s_FirstWallJump.AnimEnd", Spec = "spec/movement/wall-jump-split-vandamme.md#kick-and-climb")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pc.bUsing == 1)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WLJP_SALTO);
            }
            else
            {
                float d = pc.fGEZLocation - pawn.Location.Z;
                pawn.ChangeEnumAction(d > MovementK.WallJumpUpAbove ? SBasePawn.SA_WLJP_UP
                    : d > MovementK.WallJumpUpSmallAbove ? SBasePawn.SA_WLJP_UPSMALL : SBasePawn.SA_WLJP_UP_WAIST);
            }

            pc.eventChangeState(MovementStateNames.s_WallJumpSalto);
        }

        [Uc("SBase.SPlayerController.s_FirstWallJump.PlayerMove", Sha1 = "b5d2d6b734e57b51087c0bc240a90ac88dae977d")]
        [PortOf("uc:SBase.SPlayerController:s_FirstWallJump.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#kick-and-climb")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 1f);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_FirstWallJumpSalto")]
    public class s_FirstWallJumpSalto : UcActorState, IPlayerMove
    {
        public static readonly s_FirstWallJumpSalto Instance = new(UcMeta.s_FirstWallJumpSalto, null);

        protected s_FirstWallJumpSalto(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_FirstWallJumpSalto.AnimEnd", Sha1 = "aadf7ca7b3c5d6c3e8138621e94a00f6795648f3")]
        [PortOf("uc:SBase.SPlayerController:s_FirstWallJumpSalto.AnimEnd", Spec = "spec/movement/wall-jump-split-vandamme.md#kick-and-climb")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && Channel == 0 && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WLJP_SALTO);
                pc.eventChangeState(MovementStateNames.s_WallJumpSalto);
            }
        }

        [Uc("SBase.SPlayerController.s_FirstWallJumpSalto.PlayerMove", Sha1 = "c4a2df16e2a2f2cafbe322b3fd0f3935e6a9f9b3")]
        [PortOf("uc:SBase.SPlayerController:s_FirstWallJumpSalto.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#kick-and-climb")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 1f);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_WallJumpSalto")]
    public class s_WallJumpSalto : UcActorState, IPlayerMove, IWaistLedge, ILedge
    {
        public static readonly s_WallJumpSalto Instance = new(UcMeta.s_WallJumpSalto, null);

        private static readonly Name WljpStAlUmL = new("WljpStAlUmL");

        private static readonly Name WljpStAlUwL = new("WljpStAlUwL");

        protected s_WallJumpSalto(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>A waist-high ledge within reach during the climb: the waist ledge climb.</summary>
        [Uc("SBase.SPlayerController.s_WallJumpSalto.WaistLedge", Sha1 = "5938b88569d0585290735e900bb526f457c8246f")]
        [PortOf("uc:SBase.SPlayerController:s_WallJumpSalto.WaistLedge", Spec = "spec/movement/wall-jump-split-vandamme.md#kick-and-climb")]
        public virtual void WaistLedge(SPlayerController self, bool bCanGoNoCrouch)
        {
            if (self.SBPawn is not { } pawn || pawn.SPawnType != PawnType.SPAWNOFFENSE || pawn.ZipP1 == pawn.LastZipP || pawn.WeaponLoaded != 0)
            {
                return;
            }

            self.bWaistLedgeCanGoUnCrouch = bCanGoNoCrouch;
            self.bFindGE = true;
            pawn.ChangeEnumAction(SBasePawn.SA_WAISTLEDGE_DETECT);
            pawn.SendZipPToServer();
            self.eventChangeState(MovementStateNames.s_WaistLedgeDetect);
        }

        /// <summary>
        /// The ledge above the wall in reach (under 150 to it): the grab, except during the waist climb and the early
        /// part of the small climb.
        /// </summary>
        [Uc("SBase.SPlayerController.s_WallJumpSalto.Ledge", Sha1 = "b2280166e0835fb1741fb93858d12eaffcd3cce7")]
        [PortOf("uc:SBase.SPlayerController:s_WallJumpSalto.Ledge", Spec = "spec/movement/wall-jump-split-vandamme.md#kick-and-climb")]
        public virtual void Ledge(SPlayerController self)
        {
            if (self.SBPawn is not { } pawn || !(self.fGEZLocation - pawn.Location.Z < MovementK.WallJumpUpSmallAbove))
            {
                return;
            }

            Name seq = pawn.PawnAnim[0].Sequence;
            float frame = pawn.PawnAnim[0].Frame;
            if ((seq != WljpStAlUmL || frame > MovementK.WallJumpUpSmallCatchFrame) && seq != WljpStAlUwL)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_LEDGE_GRAB);
                pawn.SendZipPToServer();
                self.bFindGE = true;
                self.eventChangeState(MovementStateNames.GrabLedge);
            }
        }

        /// <summary>The climb or flip ended without a ledge: the long fall, with no air control.</summary>
        [Uc("SBase.SPlayerController.s_WallJumpSalto.AnimEnd", Sha1 = "48f4e6839a40be277870236a5b932901634561cd")]
        [PortOf("uc:SBase.SPlayerController:s_WallJumpSalto.AnimEnd", Spec = "spec/movement/wall-jump-split-vandamme.md#kick-and-climb")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.bBlockControlForJump = true;
                pawn.ChangeEnumAction(SBasePawn.SA_LONG_FALL_ST_0H);
                pc.eventChangeState(MovementStateNames.s_PlayerJump);
            }
        }

        [Uc("SBase.SPlayerController.s_WallJumpSalto.PlayerMove", Sha1 = "91b8d01a66e315102071b1766712d13a66cb961d")]
        [PortOf("uc:SBase.SPlayerController:s_WallJumpSalto.PlayerMove", Spec = "spec/movement/wall-jump-split-vandamme.md#kick-and-climb")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventCheckArmsZone();
            self.bPressedJump = false;
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }
    }
}
