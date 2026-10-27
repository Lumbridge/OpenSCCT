using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController states s_BTWFireStandLeft, s_BTWFireStandRight, s_BTWFireCrouchLeft, s_BTWFireCrouchRight:
// the pistol out round the end of the wall. Written from spec/movement/back-to-wall.md (Firing round the corner). The
// shot itself is the global Fire (the states do not override it); the draw clip and its notifies are in
// SBasePawn.BtwFireNotifies.cs. Like the peek states, these run on the input machine and reach the server through
// ChangeState and the replicated actions.
public partial class SPlayerController
{
    /// <summary>
    /// Room to lean out and fire round the wall end on that side. The native is not decoded (back-to-wall.md open
    /// question 4); this stands in with the wall-end probe the peek was entered on: refused only when the side is
    /// blocked or over a drop.
    /// </summary>
    [UcNative("ASPlayerController::execbTestCanFireInBTW", Exe = 0x10ab22a0, Editor = 0x111e8690)]
    [PortOf("nat:SBase.SPlayerController.bTestCanFireInBTW", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
    [Provenance(ProvenanceKind.Estimated, "bTestCanFireInBTW not decoded: CheckBTWEyes toward the peek side is not -1 (Left states look round the spy's left, CheckBTWEyes(false))")]
    public bool bTestCanFireInBTW(bool bIsLeft) => CheckBTWEyes(!bIsLeft) != -1;

    /// <summary>
    /// The peek states' fire branch: the request is used up; with room to lean out the laser goes on when asked and
    /// the draw transition starts the fire state for the side and stance.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_BTW_StandLeft.PlayerMove", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
    internal void BtwPeekFire(SBasePawn pawn, bool bLeft, bool bCrouched)
    {
        bForceFireMode = false;
        if (!bTestCanFireInBTW(bLeft))
        {
            return;
        }

        if (bForceLaserFireMode)
        {
            MovementService.HooksOf(this).SwitchLaser(this, true);
        }

        if (bLeft)
        {
            pawn.ChangeEnumAction(bCrouched ? SBasePawn.SA_BTW_FIRE_CR_LEFT_TR : SBasePawn.SA_BTW_FIRE_ST_LEFT_TR);
            eventChangeState(bCrouched ? MovementStateNames.s_BTWFireCrouchLeft : MovementStateNames.s_BTWFireStandLeft);
        }
        else
        {
            pawn.ChangeEnumAction(bCrouched ? SBasePawn.SA_BTW_FIRE_CR_RIGHT_TR : SBasePawn.SA_BTW_FIRE_ST_RIGHT_TR);
            eventChangeState(bCrouched ? MovementStateNames.s_BTWFireCrouchRight : MovementStateNames.s_BTWFireStandRight);
        }
    }

    /// <summary>
    /// The fire states' move: the view limited to the open side of the corner; unless drawing, holster back to the
    /// peek state on a holster request, a second fire-mode press, the side key released or the BTW key; the stance
    /// change 0.5 s apart keeps the pistol out. The spy does not move.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_BTWFireStandLeft.PlayerMove", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
    internal void BtwFireMove(float DeltaTime, bool bLeft, bool bCrouched)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        UpdateLimitedRotation(DeltaTime, bLeft ? MovementK.BtwFireLeftYawMin : MovementK.BtwFireRightYawMin,
            bLeft ? MovementK.BtwFireLeftYawMax : MovementK.BtwFireRightYawMax, -MovementK.BtwFirePitch, MovementK.BtwFirePitch,
            pawn.Rotation.Yaw + 32768, 0);
        if (!pawn.bWeaponTransit)
        {
            int key = bLeft ? cMoveRight : cMoveLeft;
            float now = Level.TimeSeconds;
            if ((bForceExitFireMode || (bTestBoutonLache && bFireMode != 0) || (iMove & key) == 0 || bUseBackToWall == 1) &&
                !pawn.bStickyShocking)
            {
                bForceFireMode = false;
                bForceExitFireMode = false;
                bTestBoutonLache = false;
                if (bLeft)
                {
                    pawn.ChangeEnumAction(bCrouched ? SBasePawn.SA_BTW_FIRE_CR_LEFT_UNTR : SBasePawn.SA_BTW_FIRE_ST_LEFT_UNTR);
                    eventChangeState(bCrouched ? MovementStateNames.s_BTW_CrouchLeft : MovementStateNames.s_BTW_StandLeft);
                }
                else
                {
                    pawn.ChangeEnumAction(bCrouched ? SBasePawn.SA_BTW_FIRE_CR_RIGHT_UNTR : SBasePawn.SA_BTW_FIRE_ST_RIGHT_UNTR);
                    eventChangeState(bCrouched ? MovementStateNames.s_BTW_CrouchRight : MovementStateNames.s_BTW_StandRight);
                }
            }
            else if (!bCrouched && bCrouchWithRoll)
            {
                if (pawn.bCanCrouch && now - m_LastSpecialMoveTime > MovementK.BtwStanceCooldown)
                {
                    m_LastSpecialMoveTime = now;
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                    pawn.ChangeEnumAction(bLeft ? SBasePawn.SA_BTW_FIRE_LEFT : SBasePawn.SA_BTW_FIRE_RIGHT);
                    eventChangeState(bLeft ? MovementStateNames.s_BTWFireCrouchLeft : MovementStateNames.s_BTWFireCrouchRight);
                }
                else
                {
                    bCrouchWithRoll = false;
                }
            }
            else if (bCrouched && !bCrouchWithRoll)
            {
                if (pawn.CanUnCrouch() && now - m_LastSpecialMoveTime > MovementK.BtwStanceCooldown)
                {
                    m_LastSpecialMoveTime = now;
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                    pawn.ChangeEnumAction(bLeft ? SBasePawn.SA_BTW_FIRE_LEFT : SBasePawn.SA_BTW_FIRE_RIGHT);
                    eventChangeState(bLeft ? MovementStateNames.s_BTWFireStandLeft : MovementStateNames.s_BTWFireStandRight);
                }
                else
                {
                    bCrouchWithRoll = true;
                }
            }
        }

        SendMove(DeltaTime, default);
    }

    /// <summary>
    /// A hit on the wall (owner side): the heartbeat, then off the wall facing along it to the resting state for the
    /// stance; <paramref name="bKeepWeapon"/> (s_BTW_Base, so the fire states) keeps a drawn pistol, the peek states
    /// always go to the unarmed rest.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_BTW_Base.PlayAnimDmg", Spec = "spec/movement/back-to-wall.md#s_btw_base-common")]
    [PortOf("uc:SBase.SPlayerController:s_BTW_StandLeft.PlayAnimDmg", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
    internal void BtwPlayAnimDmg(bool bKeepWeapon)
    {
        CombatStartHeartBeat();
        if (SBPawn is not { } pawn)
        {
            return;
        }

        FaceWall(pawn);
        pawn.ChangeEnumAction(SBasePawn.SA_BTW_OUT);
        bool armed = bKeepWeapon && pawn.WeaponLoaded != 0;
        if (bCrouchWithRoll || pawn.bIsCrouched)
        {
            pawn.ChangeEnumAction(armed ? SBasePawn.SA_WALK_CR_2H : SBasePawn.SA_WALK_CR_0H);
            eventChangeState(armed ? MovementStateNames.s_PlayerWalkingCr2H : MovementStateNames.s_PlayerWalkingCr0H);
        }
        else
        {
            pawn.ChangeEnumAction(armed ? SBasePawn.SA_WALK_ST_2H : SBasePawn.SA_WALK_ST_0H);
            eventChangeState(armed ? MovementStateNames.s_PlayerWalkingSt2H : MovementStateNames.s_PlayerWalkingSt0H);
        }
    }

    /// <summary>The fire states' shared parts: the first-person corner camera, the tazer check, the laser key.</summary>
    public abstract class s_BTWFire_Shared : s_BTW, ISwitchLaser
    {
        protected s_BTWFire_Shared(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>True for the states that lean out on the spy's left (entered with the Right key).</summary>
        protected abstract bool bLeft { get; }

        protected abstract bool bCrouched { get; }

        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(bLeft ? ECamMode.ECM_BTWRightFP : ECamMode.ECM_BTWLeftFP);
            ReinitIfTazered(pc);
        }

        public virtual void SwitchLaser(SPlayerController self) => self.WeaponSwitchLaser();

        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.BtwFireMove(DeltaTime, bLeft, bCrouched);
    }

    [Uc("SBase.SPlayerController.s_BTWFireStandLeft")]
    public class s_BTWFireStandLeft : s_BTWFire_Shared
    {
        public static new readonly s_BTWFireStandLeft Instance = new(UcMeta.s_BTWFireStandLeft, s_BTW.Instance);

        protected s_BTWFireStandLeft(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override bool bLeft => true;

        protected override bool bCrouched => false;

        [Uc("SBase.SPlayerController.s_BTWFireStandLeft.BeginState", Sha1 = "f7277e4c75d908fb470f54053ad6983e002248c3")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireStandLeft.BeginState", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void BeginState(Actor self) => base.BeginState(self);

        [Uc("SBase.SPlayerController.s_BTWFireStandLeft.SwitchLaser", Sha1 = "c7a4b9666591efeb2990524b26078f1157137c90")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireStandLeft.SwitchLaser", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void SwitchLaser(SPlayerController self) => base.SwitchLaser(self);

        [Uc("SBase.SPlayerController.s_BTWFireStandLeft.PlayerMove", Sha1 = "502e3bc0617ba85678bffae5b461bc01e94f37d2")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireStandLeft.PlayerMove", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => base.PlayerMove(self, DeltaTime);
    }

    [Uc("SBase.SPlayerController.s_BTWFireStandRight")]
    public class s_BTWFireStandRight : s_BTWFire_Shared
    {
        public static new readonly s_BTWFireStandRight Instance = new(UcMeta.s_BTWFireStandRight, s_BTW.Instance);

        protected s_BTWFireStandRight(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override bool bLeft => false;

        protected override bool bCrouched => false;

        [Uc("SBase.SPlayerController.s_BTWFireStandRight.BeginState", Sha1 = "193fda70b3aa40b0c851a94fb882295e12008afb")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireStandRight.BeginState", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void BeginState(Actor self) => base.BeginState(self);

        [Uc("SBase.SPlayerController.s_BTWFireStandRight.SwitchLaser", Sha1 = "c7a4b9666591efeb2990524b26078f1157137c90")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireStandRight.SwitchLaser", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void SwitchLaser(SPlayerController self) => base.SwitchLaser(self);

        [Uc("SBase.SPlayerController.s_BTWFireStandRight.PlayerMove", Sha1 = "4cc302ac1697983b16ab5a3386efb0246aa6f591")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireStandRight.PlayerMove", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => base.PlayerMove(self, DeltaTime);
    }

    [Uc("SBase.SPlayerController.s_BTWFireCrouchLeft")]
    public class s_BTWFireCrouchLeft : s_BTWFire_Shared
    {
        public static new readonly s_BTWFireCrouchLeft Instance = new(UcMeta.s_BTWFireCrouchLeft, s_BTW.Instance);

        protected s_BTWFireCrouchLeft(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override bool bLeft => true;

        protected override bool bCrouched => true;

        [Uc("SBase.SPlayerController.s_BTWFireCrouchLeft.BeginState", Sha1 = "f7277e4c75d908fb470f54053ad6983e002248c3")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireCrouchLeft.BeginState", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void BeginState(Actor self) => base.BeginState(self);

        [Uc("SBase.SPlayerController.s_BTWFireCrouchLeft.SwitchLaser", Sha1 = "c7a4b9666591efeb2990524b26078f1157137c90")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireCrouchLeft.SwitchLaser", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void SwitchLaser(SPlayerController self) => base.SwitchLaser(self);

        [Uc("SBase.SPlayerController.s_BTWFireCrouchLeft.PlayerMove", Sha1 = "6acf86400bec4bb5dd8206eaab319d40c954b3db")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireCrouchLeft.PlayerMove", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => base.PlayerMove(self, DeltaTime);
    }

    [Uc("SBase.SPlayerController.s_BTWFireCrouchRight")]
    public class s_BTWFireCrouchRight : s_BTWFire_Shared
    {
        public static new readonly s_BTWFireCrouchRight Instance = new(UcMeta.s_BTWFireCrouchRight, s_BTW.Instance);

        protected s_BTWFireCrouchRight(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override bool bLeft => false;

        protected override bool bCrouched => true;

        [Uc("SBase.SPlayerController.s_BTWFireCrouchRight.BeginState", Sha1 = "193fda70b3aa40b0c851a94fb882295e12008afb")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireCrouchRight.BeginState", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void BeginState(Actor self) => base.BeginState(self);

        [Uc("SBase.SPlayerController.s_BTWFireCrouchRight.SwitchLaser", Sha1 = "c7a4b9666591efeb2990524b26078f1157137c90")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireCrouchRight.SwitchLaser", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void SwitchLaser(SPlayerController self) => base.SwitchLaser(self);

        [Uc("SBase.SPlayerController.s_BTWFireCrouchRight.PlayerMove", Sha1 = "4a0ceeb12cc2147722938acddae64b552f39dbb8")]
        [PortOf("uc:SBase.SPlayerController:s_BTWFireCrouchRight.PlayerMove", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => base.PlayerMove(self, DeltaTime);
    }
}
