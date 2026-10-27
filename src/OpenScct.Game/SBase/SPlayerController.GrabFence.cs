using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController fence states: GrabFence (the grab clip), OnFence (climbing), OnTopFence and
// BackFromTopFence (over the top). Written from spec/movement/fence.md. The blast reactions on the fence
// (CanBeBlasted, ExplosionHit) are the combat port's (its fence blast kind).
public partial class SPlayerController
{
    /// <summary>The fence view: yaw and pitch within 15000 of the pawn's yaw.</summary>
    private void FenceView(float DeltaTime)
    {
        if (Pawn is { } p)
        {
            UpdateLimitedRotation(DeltaTime, -MovementK.FenceView, MovementK.FenceView, -MovementK.FenceView, MovementK.FenceView, p.Rotation.Yaw, 0);
        }
    }

    [Uc("SBase.SPlayerController.GrabFence")]
    public class GrabFence : UcActorState, IPlayerMove
    {
        public static readonly GrabFence Instance = new(UcMeta.GrabFence, null);

        protected GrabFence(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.GrabFence.BeginState", Sha1 = "dc19e5d9f5a5d2ec8caa75fb405eaf170871d7ee")]
        [PortOf("uc:SBase.SPlayerController:GrabFence.BeginState", Spec = "spec/movement/fence.md#grabfence-simulated")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_NoTargetCol);
            pc.SBPawn?.SetFenceZones();
            pc.ZoneHorsCrouch();
            pc.bCrouchWithRoll = false;
        }

        [Uc("SBase.SPlayerController.GrabFence.AnimEnd", Sha1 = "0ade3eabf703f4803857190f2f9b0b579dc74daa")]
        [PortOf("uc:SBase.SPlayerController:GrabFence.AnimEnd", Spec = "spec/movement/fence.md#grabfence-simulated")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ONFENCE);
                pc.eventChangeState(MovementStateNames.OnFence);
            }
        }

        [Uc("SBase.SPlayerController.GrabFence.PlayerMove", Sha1 = "dc444cfcacd1d176d2d83050f4f204c0d82dfab1")]
        [PortOf("uc:SBase.SPlayerController:GrabFence.PlayerMove", Spec = "spec/movement/fence.md#grabfence-simulated")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.FenceView(DeltaTime);
            self.bPressedJump = false;
            self.bCrouchWithRoll = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.OnFence")]
    public class OnFence : UcActorState, IPlayerMove, IFenceTop, IMyNotifyLanded, ICanUseCam, ICanBeDropAttacked
    {
        public static readonly OnFence Instance = new(UcMeta.OnFence, null);

        protected OnFence(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.OnFence.BeginState", Sha1 = "54f380fd8394f793e7fb2069857b5f8a9db6625b")]
        [PortOf("uc:SBase.SPlayerController:OnFence.BeginState", Spec = "spec/movement/fence.md#climbing-onfence-simulated")]
        public override void BeginState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.OnFence.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:OnFence.CanUseCam", Spec = "spec/movement/fence.md#climbing-onfence-simulated")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.OnFence.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:OnFence.EndState", Spec = "spec/movement/fence.md#climbing-onfence-simulated")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        [Uc("SBase.SPlayerController.OnFence.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:OnFence.CanBeDropAttacked", Spec = "spec/movement/fence.md#climbing-onfence-simulated")]
        public virtual bool CanBeDropAttacked(SPlayerController self) => true;

        /// <summary>The hands reached the fence's top line: over it.</summary>
        [Uc("SBase.SPlayerController.OnFence.FenceTop", Sha1 = "e132dba34952c84894f69f2a3983712309e78788")]
        [PortOf("uc:SBase.SPlayerController:OnFence.FenceTop", Spec = "spec/movement/fence.md#climbing-onfence-simulated")]
        public virtual void FenceTop(SPlayerController self, FVector locationEnd, FRotator orientationEnd)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_TOPFENCE);
                self.eventChangeState(MovementStateNames.OnTopFence);
            }
        }

        /// <summary>Forward and Back climb at 120, Left and Right move along the fence, diagonals no faster; crouch lets go.</summary>
        [Uc("SBase.SPlayerController.OnFence.PlayerMove", Sha1 = "7602ac601b574db4dc3552e12250994b7c1813a3")]
        [PortOf("uc:SBase.SPlayerController:OnFence.PlayerMove", Spec = "spec/movement/fence.md#climbing-onfence-simulated")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.FenceView(DeltaTime);
            pawn.Rotation.GetAxes(out _, out FVector y, out _);
            FVector newAccel = (self.iMove & cMoveForward) != 0 ? new FVector(0f, 0f, MovementK.FenceClimbSpeed)
                : (self.iMove & cMoveBack) != 0 ? new FVector(0f, 0f, -MovementK.FenceClimbSpeed) : default;
            float straf = (self.iMove & cMoveRight) != 0 ? 1f : (self.iMove & cMoveLeft) != 0 ? -1f : 0f;
            if (straf != 0f)
            {
                newAccel += y * (MovementK.FenceClimbSpeed * straf);
                newAccel *= MovementK.FenceClimbSpeed / newAccel.Size();
            }

            pawn.Acceleration = newAccel;
            if (self.bCrouchWithRoll)
            {
                self.bCrouchWithRoll = false;
                self.bPressedJump = false;
                pawn.MyDoJump();
                pawn.ChangeEnumAction(SBasePawn.SA_LEDGEFALL);
                self.eventChangeState(MovementStateNames.s_PlayerJump);
            }

            self.SendMove(DeltaTime, newAccel);
        }

        /// <summary>The feet on the floor while climbing down: walking (the global landing part does not run).</summary>
        [Uc("SBase.SPlayerController.OnFence.MyNotifyLanded", Sha1 = "fca666c0492a175ca53bbbbb08bc49abd98cc9d4")]
        [PortOf("uc:SBase.SPlayerController:OnFence.MyNotifyLanded", Spec = "spec/movement/fence.md#climbing-onfence-simulated")]
        public virtual bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return false;
            }

            self.bPressedJump = false;
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
            self.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
            return true;
        }
    }

    [Uc("SBase.SPlayerController.OnTopFence")]
    public class OnTopFence : UcActorState, IPlayerMove
    {
        public static readonly OnTopFence Instance = new(UcMeta.OnTopFence, null);

        protected OnTopFence(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Over the top: hanging on the far side.</summary>
        [Uc("SBase.SPlayerController.OnTopFence.AnimEnd", Sha1 = "a9dea68c35430ebb24910e172f25bad565a9908d")]
        [PortOf("uc:SBase.SPlayerController:OnTopFence.AnimEnd", Spec = "spec/movement/fence.md#crossing-the-top")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(CameraSmoothDefault);
                pawn.ChangeEnumAction(SBasePawn.SA_ONFENCE);
                pc.bCrouchWithRoll = false;
                pc.eventChangeState(MovementStateNames.OnFence);
            }
        }

        [Uc("SBase.SPlayerController.OnTopFence.PlayerMove", Sha1 = "51715450ced7b669c1683c0d0d3acc395cfcb64d")]
        [PortOf("uc:SBase.SPlayerController:OnTopFence.PlayerMove", Spec = "spec/movement/fence.md#crossing-the-top")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.FenceView(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.BackFromTopFence")]
    public class BackFromTopFence : UcActorState, IPlayerMove
    {
        public static readonly BackFromTopFence Instance = new(UcMeta.BackFromTopFence, null);

        protected BackFromTopFence(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.BackFromTopFence.AnimEnd", Sha1 = "b624bb25e16c25e6fac1d5dddf3de431ced92992")]
        [PortOf("uc:SBase.SPlayerController:BackFromTopFence.AnimEnd", Spec = "spec/movement/fence.md#crossing-the-top")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(CameraSmoothDefault);
                pawn.ChangeEnumAction(SBasePawn.SA_ONFENCE);
                pc.eventChangeState(MovementStateNames.OnFence);
            }
        }

        [Uc("SBase.SPlayerController.BackFromTopFence.PlayerMove", Sha1 = "708452fab92bc9b52eee9d8c14afb2357e88a297")]
        [PortOf("uc:SBase.SPlayerController:BackFromTopFence.PlayerMove", Spec = "spec/movement/fence.md#crossing-the-top")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.FenceView(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }
}
