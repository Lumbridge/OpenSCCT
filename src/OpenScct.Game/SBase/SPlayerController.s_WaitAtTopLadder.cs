using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController states of getting on a ladder from its top: s_WaitAtTopLadder (the repositioning to the
// edge), s_downTopLadder (the turn and step down onto the ladder) and s_LadderDownFromTop. Written from
// spec/movement/ladder.md (getting on from the top).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_WaitAtTopLadder")]
    public class s_WaitAtTopLadder : UcActorState, IPlayerMove, ITopSynchro, INotifyBump
    {
        public static readonly s_WaitAtTopLadder Instance = new(UcMeta.s_WaitAtTopLadder, null);

        protected s_WaitAtTopLadder(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>No move while the repositioning walks the pawn to the ladder top; the spy's free view, the merc's ladder aim.</summary>
        [Uc("SBase.SPlayerController.s_WaitAtTopLadder.PlayerMove", Sha1 = "360892e33c8f0c885e67772a780accfdf08c2178")]
        [PortOf("uc:SBase.SPlayerController:s_WaitAtTopLadder.PlayerMove", Spec = "spec/movement/ladder.md#getting-on-from-the-top")]
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
                self.eventUpdateRotation(DeltaTime, 1f);
            }
            else
            {
                self.MercLadderAim();
                self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            }

            self.SendMove(DeltaTime, default);
        }

        /// <summary>At the ladder top facing it: the turn-and-step-down clip.</summary>
        [Uc("SBase.SPlayerController.s_WaitAtTopLadder.TopSynchro", Sha1 = "0dce2759a7789ee878dcc078e4ed375f10769a04")]
        [PortOf("uc:SBase.SPlayerController:s_WaitAtTopLadder.TopSynchro", Spec = "spec/movement/ladder.md#getting-on-from-the-top")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return;
            }

            self.MovementCamera(ECamMode.ECM_SmoothTransitionCentered);
            pawn.ChangeEnumAction(SBasePawn.SA_LADDER_DOWN_FROM_TOP);
            self.eventChangeState(MovementStateNames.s_downTopLadder);
        }

        /// <summary>Something in the way of the repositioning: give up and walk again.</summary>
        [Uc("SBase.SPlayerController.s_WaitAtTopLadder.NotifyBump", Sha1 = "47783fd4220b19eae85035865592141d054eeeee")]
        [PortOf("uc:SBase.SPlayerController:s_WaitAtTopLadder.NotifyBump", Spec = "spec/movement/ladder.md#getting-on-from-the-top")]
        public virtual bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                self.GotoWaitingState();
            }

            return true;
        }
    }

    [Uc("SBase.SPlayerController.s_downTopLadder")]
    public class s_downTopLadder : UcActorState, IPlayerMove
    {
        public static readonly s_downTopLadder Instance = new(UcMeta.s_downTopLadder, null);

        protected s_downTopLadder(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// Down onto the ladder: standing up if crouched, the ladder camera (the merc's on his head); the spy hangs at
        /// the top, the merc catches the ladder or, holding Down, slides from the top.
        /// </summary>
        [Uc("SBase.SPlayerController.s_downTopLadder.AnimEnd", Sha1 = "6a8ba028d062132555cb518f629550f017b6161b")]
        [PortOf("uc:SBase.SPlayerController:s_downTopLadder.AnimEnd", Spec = "spec/movement/ladder.md#getting-on-from-the-top")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
            }

            bool spy = pawn.SPawnType == PawnType.SPAWNOFFENSE;
            if (spy)
            {
                pc.MovementCameraSmooth(MovementK.LadderTopSmooth);
            }
            else
            {
                pc.MercLadderCamera();
            }

            pc.MovementCamera(ECamMode.ECM_OnLadder);
            if (spy)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_LADDERTOP);
                pc.eventChangeState(MovementStateNames.s_OnLadderTop);
            }
            else if ((pc.iMove & cMoveDown) == 0)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_LADDER_CATCH);
                pc.eventChangeState(MovementStateNames.s_LadderGrabFall);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_SPEED_LADDER_FROM_TOP);
                pc.eventChangeState(MovementStateNames.s_SpeedLadderFromTop);
            }
        }

        /// <summary>No crouch latch; the spy's free view; the merc's view locked to the pawn's yaw with the camera's pitch window.</summary>
        [Uc("SBase.SPlayerController.s_downTopLadder.PlayerMove", Sha1 = "25901824b48c08564915433ab95546a24227a8cd")]
        [PortOf("uc:SBase.SPlayerController:s_downTopLadder.PlayerMove", Spec = "spec/movement/ladder.md#getting-on-from-the-top")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.bCrouchWithRoll = false;
            if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
            {
                self.eventUpdateRotation(DeltaTime, 1f);
            }
            else
            {
                CameraWindow w = CameraWindows.WindowOf(self.MovementCamMode);
                self.UpdateLimitedRotation(DeltaTime, -1, 1, w.MinPitch, w.MaxPitch, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
                self.MyDesiredRotation = self.Rotation;
            }

            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_LadderDownFromTop")]
    public class s_LadderDownFromTop : UcActorState, IPlayerMove
    {
        public static readonly s_LadderDownFromTop Instance = new(UcMeta.s_LadderDownFromTop, null);

        protected s_LadderDownFromTop(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_LadderDownFromTop.AnimEnd", Sha1 = "3eb3c8ae018a0d40254c51e1929520b2588dac55")]
        [PortOf("uc:SBase.SPlayerController:s_LadderDownFromTop.AnimEnd", Spec = "spec/movement/ladder.md#getting-on-from-the-top")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pc.SPawnType == PawnType.SPAWNDEFENSE)
            {
                pc.MercLadderCamera();
            }

            pawn.ChangeEnumAction(SBasePawn.SA_ON_LADDER);
            pc.eventChangeState(MovementStateNames.s_OnLadder);
        }

        [Uc("SBase.SPlayerController.s_LadderDownFromTop.PlayerMove", Sha1 = "48496b944c834ec535cd73332d10f2ebeb9a01d4")]
        [PortOf("uc:SBase.SPlayerController:s_LadderDownFromTop.PlayerMove", Spec = "spec/movement/ladder.md#getting-on-from-the-top")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if (self.bUsing == 1 && (self.iMove & cMoveDown) == 0)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_SPEEDLADDER);
                self.eventChangeState(MovementStateNames.s_SpeedLadder);
            }

            self.bPressedJump = false;
            self.bCrouchWithRoll = false;
            if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
            {
                self.eventUpdateRotation(DeltaTime, 1f);
            }
            else
            {
                self.MercLadderAim();
                self.UpdateLimitedRotation(DeltaTime, -1, 0, -1, 0, pawn.Rotation.Yaw, 0);
            }

            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_SpeedLadder")]
    public class s_SpeedLadder : s_LadderBase, IPlayerMove, ITopSynchro
    {
        public static new readonly s_SpeedLadder Instance = new(UcMeta.s_SpeedLadder, s_LadderBase.Instance);

        protected s_SpeedLadder(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Sliding down: the slide term +524, faster (-524) while Down is held; crouch lets go.</summary>
        [Uc("SBase.SPlayerController.s_SpeedLadder.PlayerMove", Sha1 = "17f512a4f64ff8c604f76ec630129eb8fcb42101")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedLadder.PlayerMove", Spec = "spec/movement/ladder.md#fast-slide-down-s_speedladder-s_speedladderfromtop")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.MercLadderFirstPerson();
            float term = (self.iMove & cMoveDown) == 0 ? MovementK.LadderSlideTerm : -MovementK.LadderSlideTerm;
            var newAccel = new FVector(term, 0f, -MovementK.SpeedForLadder);
            self.bPressedJump = false;
            if (self.bCrouchWithRoll)
            {
                JumpFromLadder(self);
            }

            self.LadderView(DeltaTime);
            self.SendMove(DeltaTime, newAccel);
        }

        /// <summary>The slow slide reached a rung: catch the ladder there.</summary>
        [Uc("SBase.SPlayerController.s_SpeedLadder.TopSynchro", Sha1 = "f278da59b8e96d62f89fb27226dfe60c556cafce")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedLadder.TopSynchro", Spec = "spec/movement/ladder.md#fast-slide-down-s_speedladder-s_speedladderfromtop")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return;
            }

            self.MovementCameraSmooth(CameraSmoothDefault);
            pawn.ChangeEnumAction(SBasePawn.SA_LADDER_CATCH);
            self.bCanStop = true;
            self.aForward = 0f;
            self.eventChangeState(MovementStateNames.s_LadderGrabFall);
        }

        /// <summary>The floor at the bottom of the slide: the spy lands, the merc draws his weapon.</summary>
        [Uc("SBase.SPlayerController.s_SpeedLadder.NotifyHitWall", Sha1 = "89f989af9d630e45d3833ac46927535015c695c3")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedLadder.NotifyHitWall", Spec = "spec/movement/ladder.md#fast-slide-down-s_speedladder-s_speedladderfromtop")]
        public override bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return false;
            }

            self.oldbUseBackToWall = 1;
            if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_LAND_ST_0H_LOW);
                self.MovementCamera(ECamMode.ECM_Walking);
                self.eventChangeState(MovementStateNames.PlayerLanding);
            }
            else
            {
                self.ResetMercCam();
                pawn.ChangeEnumAction(SBasePawn.SA_LADDER_DOWNW);
                self.LocalMyDesiredRotation = self.MyDesiredRotation;
                self.eventChangeState(MovementStateNames.s_EndLadderDrawWeapon);
            }

            return true;
        }
    }

    [Uc("SBase.SPlayerController.s_SpeedLadderFromTop")]
    public class s_SpeedLadderFromTop : s_SpeedLadder
    {
        public static new readonly s_SpeedLadderFromTop Instance = new(UcMeta.s_SpeedLadderFromTop, null);

        protected s_SpeedLadderFromTop(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_SpeedLadderFromTop.TopSynchro", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedLadderFromTop.TopSynchro", Spec = "spec/movement/ladder.md#fast-slide-down-s_speedladder-s_speedladderfromtop")]
        public override void TopSynchro(SPlayerController self)
        {
        }

        /// <summary>The merc's slide from the top while Down is held (-525); releasing Down goes on as the normal slide.</summary>
        [Uc("SBase.SPlayerController.s_SpeedLadderFromTop.PlayerMove", Sha1 = "496329e8e242dc9960add852117d44f0aac80626")]
        [PortOf("uc:SBase.SPlayerController:s_SpeedLadderFromTop.PlayerMove", Spec = "spec/movement/ladder.md#fast-slide-down-s_speedladder-s_speedladderfromtop")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.MercLadderFirstPerson();
            FVector newAccel = default;
            if ((self.iMove & cMoveDown) == 0)
            {
                self.eventChangeState(MovementStateNames.s_SpeedLadder);
            }
            else
            {
                newAccel = new FVector(-MovementK.LadderSlideFromTopTerm, 0f, -MovementK.SpeedForLadder);
            }

            self.bPressedJump = false;
            if (self.bCrouchWithRoll)
            {
                JumpFromLadder(self);
            }

            self.LadderView(DeltaTime);
            self.SendMove(DeltaTime, newAccel);
        }
    }

    [Uc("SBase.SPlayerController.s_LadderGrabFall")]
    public class s_LadderGrabFall : s_LadderBase, IPlayerMove, ITopLadder
    {
        public static new readonly s_LadderGrabFall Instance = new(UcMeta.s_LadderGrabFall, null);

        protected s_LadderGrabFall(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The catch clip ended: at rest on a rung, or hanging at the top when within 130 of it.</summary>
        [Uc("SBase.SPlayerController.s_LadderGrabFall.AnimEnd", Sha1 = "01a571d5c9b26a0e537ce8e4e5ab18f4270c4557")]
        [PortOf("uc:SBase.SPlayerController:s_LadderGrabFall.AnimEnd", Spec = "spec/movement/ladder.md#catching-while-falling-s_laddergrabfall")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            LadderEnds(pawn, out _, out float top);
            if (pawn.Location.Z - top < -MovementK.LadderCatchNearTop)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_STOPLADDER);
                pc.MovementCamera(ECamMode.ECM_OnLadder);
                pc.eventChangeState(MovementStateNames.s_OnStopLadder);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_LADDERTOP);
                pc.MovementCamera(ECamMode.ECM_OnLadder);
                pc.eventChangeState(MovementStateNames.s_OnLadderTop);
            }
        }

        /// <summary>The top during the catch is ignored (the clip end decides).</summary>
        [Uc("SBase.SPlayerController.s_LadderGrabFall.TopLadder", Sha1 = "3ed6dc3a915116c1919795290c2d323c3438b848")]
        [PortOf("uc:SBase.SPlayerController:s_LadderGrabFall.TopLadder", Spec = "spec/movement/ladder.md#catching-while-falling-s_laddergrabfall")]
        public virtual void TopLadder(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.s_LadderGrabFall.PlayerMove", Sha1 = "8cb28800283329994e6faf0a102bf428c9fc02d6")]
        [PortOf("uc:SBase.SPlayerController:s_LadderGrabFall.PlayerMove", Spec = "spec/movement/ladder.md#catching-while-falling-s_laddergrabfall")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.MercLadderFirstPerson();
            self.LadderView(DeltaTime);
            self.bPressedJump = false;
            if (self.bCrouchWithRoll)
            {
                JumpFromLadder(self);
            }

            self.SendMove(DeltaTime, MovementK.LadderCatchAccel);
        }
    }

    [Uc("SBase.SPlayerController.s_EndLadderDrawWeapon")]
    public class s_EndLadderDrawWeapon : s_LadderBase, IPlayerMove
    {
        public static new readonly s_EndLadderDrawWeapon Instance = new(UcMeta.s_EndLadderDrawWeapon, null);

        protected s_EndLadderDrawWeapon(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The step off the ladder ended: the merc walks armed.</summary>
        [Uc("SBase.SPlayerController.s_EndLadderDrawWeapon.AnimEnd", Sha1 = "7b91e0edc7207b509d05e67cf55685bf4fba89df")]
        [PortOf("uc:SBase.SPlayerController:s_EndLadderDrawWeapon.AnimEnd", Spec = "spec/movement/ladder.md#merc-weapon-at-the-bottom-s_endladderdrawweapon")]
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

            pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H);
            pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt2H);
        }

        /// <summary>The merc's view snapped to the pawn's yaw while stepping off.</summary>
        [Uc("SBase.SPlayerController.s_EndLadderDrawWeapon.PlayerMove", Sha1 = "fa8bf9b0a6ade71f6ec76ef6f8ee26e3dee86721")]
        [PortOf("uc:SBase.SPlayerController:s_EndLadderDrawWeapon.PlayerMove", Spec = "spec/movement/ladder.md#merc-weapon-at-the-bottom-s_endladderdrawweapon")]
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
                self.eventUpdateRotation(DeltaTime, 1f);
            }
            else
            {
                self.bFirstPersonWithPawn = true;
                FRotator desired = self.Rotation;
                desired.Yaw = pawn.Rotation.Yaw;
                self.MyDesiredRotation = desired;
                self.UpdateDesiredRotation(self.MyDesiredRotation);
                self.LocalMyDesiredRotation = self.MyDesiredRotation;
                self.Rotation = self.MyDesiredRotation;
            }

            self.SendMove(DeltaTime, default);
        }
    }
}
