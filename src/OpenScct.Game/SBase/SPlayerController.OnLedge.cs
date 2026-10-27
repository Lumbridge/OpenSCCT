using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state OnLedge: hanging from a ledge; shimmy, climb up, drop, corners, and the grab of a merc
// standing above. Written from spec/movement/ledge.md (Hanging) and spec/combat/spy-grab.md (ledge grab).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.OnLedge")]
    public class OnLedge : UcActorState, IPlayerMove, IChangeLedge, INoGE, IMyNotifyLanded, ITopPipe, ICanBeDropAttacked, ICanUseCam,
        ICanHoistClimbHelp, IPerformCoopMvt
    {
        public static readonly OnLedge Instance = new(UcMeta.OnLedge, null);

        protected OnLedge(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Provenance(ProvenanceKind.Decoded, "ledge.md OnLedge.PlayerMove step 3: Acceleration = 80 straf Y")]
        public const float ShimmyAccel = MovementK.LedgeShimmyAccel;

        [Uc("SBase.SPlayerController.OnLedge.BeginState", Sha1 = "ebeaa6ad15dd8ec65af4a65e8def2fe8015d06c4")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.BeginState", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.ZoneHorsCrouch();
            pc.bCrouchWithRoll = false;
            pc.MovementCamera(ECamMode.ECM_OnLedge);
            pc.oldbUsing = pc.bUsing;
            pc.bToggleUsing = 0;
        }

        /// <summary>
        /// Climb (forward or jump: the native picks the climb), drop (back or crouch), shimmy (left and right at
        /// 80), the Use edge (the ledge grab), and the move with the shimmy acceleration.
        /// </summary>
        [Uc("SBase.SPlayerController.OnLedge.PlayerMove", Sha1 = "80e8b62d77ccfc89b7b2ce2d1677e7154df34ca3")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.PlayerMove", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.LimitedViewByCamera(DeltaTime, 0, 0);
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.Rotation.GetAxes(out _, out FVector y, out _);
            if ((self.iMove & cMoveForward) != 0 || self.bPressedJump)
            {
                switch (self.TestTopLedge(self.bPressedJump))
                {
                    case 4:
                        self.bCrouchWithRoll = false;
                        pawn.ChangeEnumAction(SBasePawn.SA_BARRIER_ABOVE_LEDGE_CR);
                        self.ClearClimbInputs();
                        self.eventChangeState(MovementStateNames.s_Tr_Barrier_Above_Ledge);
                        return;
                    case 3:
                        self.bCrouchWithRoll = false;
                        pawn.ChangeEnumAction(SBasePawn.SA_LEDGE_BARRIER);
                        self.ClearClimbInputs();
                        self.eventChangeState(MovementStateNames.TopLedgeBarrier);
                        return;
                    case 1:
                        self.bCrouchWithRoll = false;
                        pawn.ChangeEnumAction(SBasePawn.SA_TOPLEDGE);
                        self.ClearClimbInputs();
                        self.eventChangeState(MovementStateNames.TopLedge);
                        return;
                    case 2:
                        pawn.ChangeEnumAction(SBasePawn.SA_TOPLEDGECROUCH);
                        self.bPressedJump = false;
                        self.bCrouchWithRoll = false;
                        self.bUsing = 0;
                        self.eventChangeState(MovementStateNames.TopLedgeCrouch);
                        return;
                }
            }
            else if ((self.iMove & cMoveBack) != 0)
            {
                self.bCrouchWithRoll = true;
            }

            self.bPressedJump = false;
            float straf = (self.iMove & cMoveRight) != 0 ? 1f : (self.iMove & cMoveLeft) != 0 ? -1f : 0f;
            FVector accel = y * (ShimmyAccel * straf);
            pawn.Acceleration = accel;

            if (self.bUsing != self.oldbUsing)
            {
                self.bToggleUsing = self.bUsing;
                self.oldbUsing = self.bUsing;
            }
            else
            {
                self.bToggleUsing = 0;
            }

            if (self.bCrouchWithRoll)
            {
                self.bCrouchWithRoll = false;
                self.bPressedJump = false;
                self.bUsing = 0;
                pawn.MyDoJump();
                pawn.ChangeEnumAction(SBasePawn.SA_LEDGEFALL);
                pawn.LastZipP = pawn.ZipP1;
                self.eventChangeState(MovementStateNames.s_PlayerJump);
                return;
            }

            // The Use press: a merc above the ledge is pulled over the edge (spy-grab.md rule 30).
            if (self.bToggleUsing == 1 && self.bTestGrabLedgeGeo() && AskToGrabPawn(self) && pawn.m_AttackTarget is { } target)
            {
                self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_GRABFROMLEDGE_BEGIN, false);
                self.GotoState(CNAME_s_GrabbingFromLedge_Begin);
                self.AskServerToGrabPawnFromLedge(target);
            }

            self.SendMove(DeltaTime, accel);
        }

        /// <summary>
        /// The ledge reach (rule 31): a merc standing on the ledge above the hands, inside the 150 by 100 box in
        /// front of the spy, woken more than 3.8 s ago.
        /// </summary>
        [Uc("SBase.SPlayerController.OnLedge.CanGrabPawn", Sha1 = "45f9fce3670e33a0761febb3fa2f757b9e45deb0")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.CanGrabPawn", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public virtual bool CanGrabPawn(SPlayerController self, SBasePawn PawnToGrab)
        {
            if (self.Pawn is not { } me || (PawnToGrab.bIsCrouched && !PawnToGrab.CanUnCrouch()))
            {
                return false;
            }

            return self.Level.TimeSeconds - PawnToGrab.iLastWakeTime > self.fWakeInvulnerabilityTime &&
                self.LedgeGrabGeometry(me, PawnToGrab, 1f);
        }

        /// <summary>
        /// The owner's ledge grab search (rule 30): the first merc in reach that can be grabbed and is not shocking
        /// becomes the target.
        /// </summary>
        [Uc("SBase.SPlayerController.OnLedge.AskToGrabPawn", Sha1 = "a10766013798ff4d83a3f888c719d97044837efe")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.AskToGrabPawn", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public virtual bool AskToGrabPawn(SPlayerController self)
        {
            if (self.SBPawn is not { } spy || spy.bStickyShocking || self.GameReplicationInfo is { bAvlb_SPY_Grab: false })
            {
                return false;
            }

            foreach (Pawn p in self.XLevel.DynamicActors<Pawn>())
            {
                if (p is SPAWNDEFENSE target && !ReferenceEquals(target, spy) && CanGrabPawn(self, target) && target.bCanBeGrabbed &&
                    !target.bStickyShocking)
                {
                    spy.m_AttackTarget = target;
                    return true;
                }
            }

            return false;
        }

        /// <summary>A corner: the next ledge's points kept for the end of the turn clip, then the corner change.</summary>
        [Uc("SBase.SPlayerController.OnLedge.ChangeLedge", Sha1 = "1a00a4845c7bafec51a3cc4a11dec700906fd61b")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.ChangeLedge", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
        public virtual void ChangeLedge(SPlayerController self, int iType, FVector P1, FVector P2)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return;
            }

            int action = iType switch
            {
                0 => SBasePawn.SA_CHANGELEDGE_IR,
                1 => SBasePawn.SA_CHANGELEDGE_IL,
                2 => SBasePawn.SA_CHANGELEDGE_OR,
                3 => SBasePawn.SA_CHANGELEDGE_OL,
                _ => -1,
            };
            if (action < 0)
            {
                return;
            }

            pawn.vWallJumpDir = P1;
            pawn.vVandammePos = P2;
            pawn.ChangeEnumAction(action);
            self.eventChangeState(MovementStateNames.s_ChangeLedgeWait);
        }

        [Uc("SBase.SPlayerController.OnLedge.NoGE", Sha1 = "19424536ab48917d333749da8b5b85d5f718897f")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.NoGE", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
        public virtual void NoGE(SPlayerController self) => self.LeaveLedgeFalling();

        /// <summary>The feet touched a floor while hanging: back to the resting state.</summary>
        [Uc("SBase.SPlayerController.OnLedge.MyNotifyLanded", Sha1 = "abd4f7c1f1ae54de9e56c874a8dfd270b486fc0f")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.MyNotifyLanded", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
        public virtual bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            self.MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);
            if (self.IAmOnClient())
            {
                self.GotoWaitingState();
            }

            return true;
        }

        /// <summary>A pipe joins the ledge: move over to it.</summary>
        [Uc("SBase.SPlayerController.OnLedge.TopPipe", Sha1 = "72056ad15f62212ee005b2030b1786affbba9b9e")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.TopPipe", Spec = "spec/movement/pipe-vertical.md#getting-on")]
        public virtual void TopPipe(SPlayerController self, bool bLeft)
        {
            if (self.SBPawn is not { } pawn || self.GetStateTable().Find(MovementStateNames.s_GrabPipe) is null)
            {
                return;
            }

            pawn.ChangeEnumAction(bLeft ? SBasePawn.SA_TR_LEDGEPIPE_L : SBasePawn.SA_TR_LEDGEPIPE_R);
            pawn.SendZipPAndNormalToServer();
            self.eventChangeState(MovementStateNames.s_GrabPipe);
        }

        [Uc("SBase.SPlayerController.OnLedge.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.CanBeDropAttacked", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
        public virtual bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.OnLedge.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.CanUseCam", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.OnLedge.CanHoistClimbHelp", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.CanHoistClimbHelp", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
        public virtual bool CanHoistClimbHelp(SPlayerController self) => true;

        /// <summary>The co-op key: start a co-op move.</summary>
        [Uc("SBase.SPlayerController.OnLedge.PerformCoopMvt", Sha1 = "17d403d8148963821a153996fb981e55fff4b772")]
        [PortOf("uc:SBase.SPlayerController:OnLedge.PerformCoopMvt", Spec = "spec/movement/coop.md#starting-docoopinteraction-from-performcoopmvt-in-mario-walking-or-onledge")]
        public virtual void PerformCoopMvt(SPlayerController self) => self.DoCoopInteraction();
    }

    /// <summary>A climb from the hang clears the jump, the latch and the Use press.</summary>
    private void ClearClimbInputs()
    {
        bPressedJump = false;
        bUsing = 0;
    }
}
