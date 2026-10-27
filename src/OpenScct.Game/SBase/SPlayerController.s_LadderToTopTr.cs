using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController states of the climb out at the ladder top: s_LadderToTopTr (one more tick of climbing),
// s_LadderToTop (the hands come over the top), s_OnLadderTop (hanging at the top), s_LadderTop and s_LadderTop_Cr
// (onto the floor, standing or crouched). Written from spec/movement/ladder.md (climbing out at the top).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_LadderToTopTr")]
    public class s_LadderToTopTr : s_LadderBase, IPlayerMove
    {
        public static new readonly s_LadderToTopTr Instance = new(UcMeta.s_LadderToTopTr, s_LadderBase.Instance);

        private readonly int _beginPc;

        protected s_LadderToTopTr(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
            foreach ((string name, int pc) in info.LabelPcs)
            {
                if (name == "Begin")
                {
                    _beginPc = pc;
                }
            }
        }

        [Uc("SBase.SPlayerController.s_LadderToTopTr.PlayerMove", Sha1 = "5de3853721da78891c901136f15fcc30035620a9")]
        [PortOf("uc:SBase.SPlayerController:s_LadderToTopTr.PlayerMove", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) =>
            self.LadderViewMove(DeltaTime, new FVector(0f, 0f, MovementK.SpeedForLadder));

        /// <summary>Begin: wait one tick, then on the controlling side the hands-over-the-top clip.</summary>
        [Uc("SBase.SPlayerController.s_LadderToTopTr@code", Sha1 = "2a42b31b6683bd01c962246fda64a833e06bdc26")]
        [PortOf("uc:SBase.SPlayerController:s_LadderToTopTr@code", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            var pc = (SPlayerController)self;
            if (frame.Pc == _beginPc)
            {
                frame.Pc = _beginPc + 1;
                pc.Sleep(0f);
                return;
            }

            frame.Pc = LatentFrame.NoCode;
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(pawn.bIsLeftPart ? SBasePawn.SA_LADDER_TOTOP_L : SBasePawn.SA_LADDER_TOTOP_R);
                pc.eventChangeState(MovementStateNames.s_LadderToTop);
            }
        }
    }

    [Uc("SBase.SPlayerController.s_LadderToTop")]
    public class s_LadderToTop : s_LadderBase, IPlayerMove
    {
        public static new readonly s_LadderToTop Instance = new(UcMeta.s_LadderToTop, s_LadderBase.Instance);

        protected s_LadderToTop(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_LadderToTop.AnimEnd", Sha1 = "b1ce619f39c54f224bb3560c3349fe458130885c")]
        [PortOf("uc:SBase.SPlayerController:s_LadderToTop.AnimEnd", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_LADDERTOP);
                pc.eventChangeState(MovementStateNames.s_OnLadderTop);
            }
        }

        [Uc("SBase.SPlayerController.s_LadderToTop.PlayerMove", Sha1 = "4477240db90bb3847befaeee3af91886c0cfc866")]
        [PortOf("uc:SBase.SPlayerController:s_LadderToTop.PlayerMove", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.MercLadderFirstPerson();
            self.LadderView(DeltaTime);
            self.bPressedJump = false;
            self.bCrouchWithRoll = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_OnLadderTop")]
    public class s_OnLadderTop : s_LadderBase, IPlayerMove
    {
        public static new readonly s_OnLadderTop Instance = new(UcMeta.s_OnLadderTop, s_LadderBase.Instance);

        protected s_OnLadderTop(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_OnLadderTop.BeginState", Sha1 = "67302bfde16e41ed6037f3d26a31e5e61e3eceab")]
        [PortOf("uc:SBase.SPlayerController:s_OnLadderTop.BeginState", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn?.SPawnType == PawnType.SPAWNDEFENSE)
            {
                pc.MovementCameraSmooth(CameraSmoothDefault);
                pc.MovementCamera(ECamMode.ECM_OnLadder);
            }
        }

        /// <summary>
        /// Hands at the top: Up climbs out when there is room (standing or crouched), Down goes back to the climb,
        /// crouch lets go.
        /// </summary>
        [Uc("SBase.SPlayerController.s_OnLadderTop.PlayerMove", Sha1 = "89ffc87b8c3f12eae0ec4413304123c9389bf49f")]
        [PortOf("uc:SBase.SPlayerController:s_OnLadderTop.PlayerMove", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.MercLadderFirstPerson();
            self.bPressedJump = false;
            if (self.bCrouchWithRoll)
            {
                JumpFromLadder(self);
            }
            else if ((self.iMove & cMoveUp) != 0)
            {
                if (self.bTestTopLadder(out int iCanUncrouch))
                {
                    self.bPressedJump = false;
                    if (iCanUncrouch == 1)
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_LADDER_TOP);
                        self.eventChangeState(MovementStateNames.s_LadderTop);
                    }
                    else
                    {
                        self.MovementCamera(ECamMode.ECM_SmoothTransitionCentered);
                        pawn.ChangeEnumAction(SBasePawn.SA_LADDER_TOP_CR);
                        self.eventChangeState(MovementStateNames.s_LadderTop_Cr);
                    }
                }
            }
            else if ((self.iMove & cMoveDown) != 0)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_BACK_LADDER_TOP);
                self.eventChangeState(MovementStateNames.s_Back_LadderTop);
            }

            if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
            {
                self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            }
            else
            {
                self.MercLadderAim();
                self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            }

            self.SendMove(DeltaTime, MovementK.LadderApproachAccel);
        }
    }

    [Uc("SBase.SPlayerController.s_LadderTop")]
    public class s_LadderTop : s_LadderBase, IPlayerMove
    {
        public static new readonly s_LadderTop Instance = new(UcMeta.s_LadderTop, null);

        protected s_LadderTop(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The climb-out clip ended: walking on the floor, crouched or standing, unarmed spy or armed merc.</summary>
        [Uc("SBase.SPlayerController.s_LadderTop.AnimEnd", Sha1 = "344bdcb89dc183b80435c299bdd8c4777b8520b8")]
        [PortOf("uc:SBase.SPlayerController:s_LadderTop.AnimEnd", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.MovementCameraSmooth(CameraSmoothDefault);
            if (pc.SPawnType == PawnType.SPAWNDEFENSE)
            {
                pc.ResetMercCam();
            }

            bool spy = pawn.SPawnType == PawnType.SPAWNOFFENSE;
            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(spy ? SBasePawn.SA_WALK_CR_0H : SBasePawn.SA_WALK_CR_2H);
                pc.eventChangeState(spy ? MovementStateNames.s_PlayerWalkingCr0H : MovementStateNames.s_PlayerWalkingCr2H);
            }
            else
            {
                pawn.ChangeEnumAction(spy ? SBasePawn.SA_WALK_ST_0H : SBasePawn.SA_WALK_ST_2H);
                pc.eventChangeState(spy ? MovementStateNames.s_PlayerWalkingSt0H : MovementStateNames.s_PlayerWalkingSt2H);
            }
        }

        /// <summary>The spy's view limited around the pawn; an unarmed merc reports his view; an armed merc in the behind view resets it.</summary>
        [Uc("SBase.SPlayerController.s_LadderTop.PlayerMove", Sha1 = "0012a4b58f112c1b94c07ff1d750b05dffa888a3")]
        [PortOf("uc:SBase.SPlayerController:s_LadderTop.PlayerMove", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.bPressedJump = false;
            self.bCrouchWithRoll = false;
            if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
            {
                self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            }
            else if (pawn.WeaponLoaded == 0)
            {
                self.bFirstPersonWithPawn = true;
                self.MyDesiredRotation = self.Rotation;
                self.UpdateDesiredRotation(self.MyDesiredRotation);
                self.LocalMyDesiredRotation = self.MyDesiredRotation;
            }
            else if (self.bBehindView)
            {
                self.ResetMercCam();
            }

            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_LadderTop_Cr")]
    public class s_LadderTop_Cr : s_LadderTop
    {
        public static new readonly s_LadderTop_Cr Instance = new(UcMeta.s_LadderTop_Cr, s_LadderTop.Instance);

        protected s_LadderTop_Cr(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The crouched climb-out ended: crouched walking.</summary>
        [Uc("SBase.SPlayerController.s_LadderTop_Cr.AnimEnd", Sha1 = "659a36bb6f527d153c41150341ce8074b5bf6f58")]
        [PortOf("uc:SBase.SPlayerController:s_LadderTop_Cr.AnimEnd", Spec = "spec/movement/ladder.md#climbing-out-at-the-top")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pc.SPawnType == PawnType.SPAWNDEFENSE)
            {
                pc.ResetMercCam();
            }

            pc.MovementCameraSmooth(CameraSmoothDefault);
            pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            bool spy = pawn.SPawnType == PawnType.SPAWNOFFENSE;
            pawn.ChangeEnumAction(spy ? SBasePawn.SA_WALK_CR_0H : SBasePawn.SA_WALK_CR_2H);
            pc.eventChangeState(spy ? MovementStateNames.s_PlayerWalkingCr0H : MovementStateNames.s_PlayerWalkingCr2H);
        }
    }
}
