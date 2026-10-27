using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController grab states (spec/combat/spy-grab.md): the spy's approach, catch, hold and release states and
// the merc's caught, held and released states. Their damage handlers are in SPlayerController.Combat.cs (the state
// tables of CombatDispatch route PawnTakeDamage to them).
public partial class SPlayerController
{
    private static readonly Name GNAME_GrabStAlBg0 = new("GrabStAlBg0");
    private static readonly Name GNAME_BHead = new("B Head");
    private static readonly Name GNAME_GrbdStAlNt0 = new("GrbdStAlNt0");

    /// <summary>A grab state's pawn stays where it is: the view still turns, no acceleration.</summary>
    private void GrabHoldStill(float DeltaTime)
    {
        eventUpdateRotation(DeltaTime, 1f);
        if (Pawn is { } pawn)
        {
            pawn.Acceleration = FVector.Zero;
        }

        SendMove(DeltaTime, FVector.Zero);
    }

    /// <summary>The walking action and state after a grab, by the weapon stance (0 or 1 unarmed, else armed).</summary>
    private void GrabBackToWalking(SBasePawn pawn)
    {
        bool armed = pawn.WeaponLoaded is not (0 or 1);
        CombatPorts.ChangeEnumAction(pawn, armed ? SBasePawn.SA_WALK_ST_2H : SBasePawn.SA_WALK_ST_0H, false);
        eventChangeState(armed ? CNAME_s_PlayerWalkingSt2H : CNAME_s_PlayerWalkingSt0H);
    }

    /// <summary>
    /// The spy's carry move (rule 13): the stick clamped to 6000 and scaled to the grabbing speed, the pawn turning
    /// toward the move at 48000 units/s with 0.6 of the input, the ground speed from the acceleration, no jump, and the
    /// Use press releasing with a neck break.
    /// </summary>
    [Uc("SBase.SPlayerController.s_GrabbingPlayer.PlayerMove", Sha1 = "bc46839caa7b87a95fe7f28c8b9a4e13f4c67088")]
    [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.PlayerMove", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
    private void GrabCarryMove(float DeltaTime)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        float len = SvmMath.Sqrt(aForward * aForward + aStrafe * aStrafe);
        if (len > K.GrabInputClamp)
        {
            aForward *= K.GrabInputClamp / len;
            aStrafe *= K.GrabInputClamp / len;
        }

        float speedScale = pawn.RunningSpeed > 0f ? pawn.GrabbingSpeed / pawn.RunningSpeed : 0f;
        aForward *= speedScale;
        aStrafe *= speedScale;
        eventUpdateRotation(DeltaTime, 1f);
        FVector newAccel = GrabCarryRotation(DeltaTime, ForwardRequest, StrafeRequest);
        pawn.GroundSpeed = pawn.RunningSpeed * newAccel.Size() / K.GrabSpeedDivisor;
        if (pawn.GroundSpeed < K.GrabStopFactor * pawn.GrabbingSpeed)
        {
            pawn.GroundSpeed = 0f;
        }

        bPressedJump = false;
        if (bUsing != oldbUsing)
        {
            bToggleUsing = bUsing;
            oldbUsing = bUsing;
            if (bToggleUsing == 1)
            {
                CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_.Normal);
            }
        }

        SendMove(DeltaTime, newAccel);
    }

    /// <summary>
    /// The carry turn (UpdatePawnRotationMario in s_GrabbingPlayer): the pawn yaw follows the move direction at
    /// 48000 units/s (the step capped at 32000 / 48000 s) from 0.6 of the inputs.
    /// </summary>
    [Uc("SBase.SPlayerController.s_GrabbingPlayer.UpdatePawnRotationMario", Sha1 = "8a9e4706fb2fad1f79a7f368f4d21a2e9a2765fd")]
    [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.UpdatePawnRotationMario", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
    private FVector GrabCarryRotation(float DeltaTime, int Forward, int Strafe)
    {
        if (Pawn is not { } pawn)
        {
            return default;
        }

        new FRotator(0, Rotation.Yaw, 0).GetAxes(out FVector x, out FVector y, out _);
        FVector a = x * (Forward * K.GrabCarryInputShare) + y * (Strafe * K.GrabCarryInputShare);
        a.Z = 0f;
        if (a.IsZero())
        {
            return default;
        }

        float dt = SvmMath.Min(DeltaTime, K.GrabCarryMaxStep / K.GrabCarryTurnRate);
        FRotator r = pawn.Rotation;
        r.Yaw = DampRotation(r.Yaw, a.Rotation().Yaw, K.GrabCarryTurnRate, dt);
        pawn.Rotation = r;
        return a;
    }

    /// <summary>The grab camera on the owner (a view effect).</summary>
    private void GrabCamera(bool On)
    {
        if (!IsRemotePlayer)
        {
            MovementCamera(On ? ECamMode.ECM_Grab : ECamMode.ECM_Walking);
        }
    }

    // ---- spy approach -----------------------------------------------------------------------------------------

    /// <summary>s_Grabbing_UncrouchForGrab: a crouched spy stands up before the approach (rule 7).</summary>
    [Uc("SBase.SPlayerController.s_Grabbing_UncrouchForGrab")]
    public class s_Grabbing_UncrouchForGrab : UcActorState, IPlayerMove, IFire, IAltFire, CombatStateEvents.IDoInteraction
    {
        public static readonly s_Grabbing_UncrouchForGrab Instance = new(UcMeta.s_Grabbing_UncrouchForGrab, null);

        protected s_Grabbing_UncrouchForGrab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Grabbing_UncrouchForGrab.BeginState", Sha1 = "14167b67d144461a4e25a82c918977a1aadcd01e")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_UncrouchForGrab.BeginState", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (!pawn.CanUnCrouch())
            {
                pc.AskServerToUnGrabPawn(GrabReleaseMethode_.Grabbing_Stopped);
                pc.GotoWaitingStateCombat();
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WANTS_TO_UNCROUCH, false);
            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WAIT_CR2ST_0H, false);
        }

        [Uc("SBase.SPlayerController.s_Grabbing_UncrouchForGrab.AnimEnd", Sha1 = "86e0d4b8344cc9af886f97d4eae9df91bf7ada9a")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_UncrouchForGrab.AnimEnd", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.IsRemotePlayer || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_0H, false);
            pc.AskToGrabPawn();
            pc.eventChangeState(GNAME_s_Grabbing_GotoPosition);
        }

        [Uc("SBase.SPlayerController.s_Grabbing_UncrouchForGrab.PlayerMove", Sha1 = "118f977d40b45fb25c061b590ed4f208602d4274")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_UncrouchForGrab.PlayerMove", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_Grabbing_UncrouchForGrab.DoInteraction", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_UncrouchForGrab.DoInteraction", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public bool DoInteraction(SPlayerController self) => false;
    }

    /// <summary>
    /// s_Grabbing_GotoPosition: the spy moves to the grab spot behind the merc (rule 8); a bump or the end of the
    /// repositioning starts the hold on the server; 3 s or a 32 uu height gap cancel it on the owner.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Grabbing_GotoPosition")]
    public class s_Grabbing_GotoPosition : UcActorState, IPlayerTick, IPlayerMove, INotifyBump, ITopSynchro, IFire, IAltFire,
        CombatStateEvents.IDoInteraction
    {
        public static readonly s_Grabbing_GotoPosition Instance = new(UcMeta.s_Grabbing_GotoPosition, null);

        protected s_Grabbing_GotoPosition(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Grabbing_GotoPosition.BeginState", Sha1 = "fe5400ffeeb31003312f81219369395ce9b12aa7")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GotoPosition.BeginState", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.fGrabDate = pc.Level.TimeSeconds;
        }

        [Uc("SBase.SPlayerController.s_Grabbing_GotoPosition.EndState", Sha1 = "251eb518022ac943f50b500eb5cfa0fd83ece294")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GotoPosition.EndState", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        [Provenance(ProvenanceKind.Estimated, "EndState body not specced: the repositioning stops")]
        public override void EndState(Actor self)
        {
            if (((SPlayerController)self).Pawn is { Physics: EPhysics.PHYS_Repositionnal } pawn)
            {
                pawn.SetPhysics(EPhysics.PHYS_Walking);
            }
        }

        /// <summary>The owner's approach: timeout, the reposition target behind the merc, the height abort.</summary>
        [Uc("SBase.SPlayerController.s_Grabbing_GotoPosition.PlayerTick", Sha1 = "d396cd2595feb0cc3b5f40e7bbd7e2be46a593f8")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GotoPosition.PlayerTick", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.PlayerTick(DeltaTime);
            if (pc.SBPawn is not { } spy || pc.GetStateName() != GNAME_s_Grabbing_GotoPosition)
            {
                return;
            }

            if (pc.Level.TimeSeconds - pc.fGrabDate > K.GrabApproachTimeout)
            {
                pc.AskServerToUnGrabPawn(GrabReleaseMethode_.Grabbing_Cancelled);
                pc.GotoWaitingStateCombat();
                return;
            }

            if (spy.m_AttackTarget is not { } target)
            {
                if (spy.Physics == EPhysics.PHYS_Repositionnal)
                {
                    pc.GotoWaitingStateCombat();
                }

                return;
            }

            if (spy.Physics != EPhysics.PHYS_Repositionnal)
            {
                pc.CombatPorts.ChangeEnumAction(spy, SBasePawn.SA_GRAB_REPOS, false);
            }

            (target.Location - spy.Location).Rotation().GetAxes(out FVector x, out FVector y, out FVector z);
            FVector off = spy.vGrabOffset;
            FVector goal = target.Location - (x * off.X + y * off.Y + z * off.Z);
            spy.vVandammePos = goal;
            pc.CombatPorts.SetInfoForVandamme(spy, goal, (target.Location - goal).Rotation());
            spy.SetInfoForVandamme((target.Location - goal).SafeNormal());
            if (SvmMath.Abs(target.Location.Z - spy.Location.Z) > K.GrabApproachHeightAbort)
            {
                pc.AskServerToUnGrabPawn(GrabReleaseMethode_.Grabbing_Stopped);
                pc.GotoWaitingStateCombat();
            }
        }

        [Uc("SBase.SPlayerController.s_Grabbing_GotoPosition.PlayerMove", Sha1 = "4e7da55168a7c56a48d69bfa3f4bc4d8e9f5cca5")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GotoPosition.PlayerMove", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        [Uc("SBase.SPlayerController.s_Grabbing_GotoPosition.NotifyBump", Sha1 = "29af6a24bf2d25650666db4b0eae56b6b84d15a0")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GotoPosition.NotifyBump", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.Role == ENetRole.ROLE_Authority && self.SBPawn is { } spy && ReferenceEquals(Other, spy.m_AttackTarget))
            {
                self.GrabInPosition(spy);
            }

            return false;
        }

        [Uc("SBase.SPlayerController.s_Grabbing_GotoPosition.TopSynchro", Sha1 = "fb9953df4d5a314191113d58985026c05f257258")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GotoPosition.TopSynchro", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void TopSynchro(SPlayerController self)
        {
            if (self.Role == ENetRole.ROLE_Authority && self.SBPawn is { } spy)
            {
                self.GrabInPosition(spy);
            }
        }

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_Grabbing_GotoPosition.DoInteraction", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GotoPosition.DoInteraction", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public bool DoInteraction(SPlayerController self) => false;
    }

    /// <summary>The spy reached the merc (server): both alive start the hold, else the grab is cancelled.</summary>
    private void GrabInPosition(SBasePawn spy)
    {
        if (spy.m_AttackTarget is { Health: > 0, Controller: SPlayerController merc } && spy.Health > 0)
        {
            merc.eventMirrorState(GNAME_s_GrabbedByPlayer, default, false, spy);
            eventMirrorState(GNAME_s_GrabbingPlayer_Begin);
            return;
        }

        AskServerToUnGrabPawn(GrabReleaseMethode_.Grabbing_Cancelled);
        if (spy.Health > 0)
        {
            GotoWaitingStateCombat();
        }
    }

    /// <summary>s_Grab_GotoPosition: the base of the spy's repositioning before a release.</summary>
    [Uc("SBase.SPlayerController.s_Grab_GotoPosition")]
    public class s_Grab_GotoPosition : UcActorState, IPlayerMove, INotifyBump, ITopSynchro, IFire, IAltFire
    {
        public static readonly s_Grab_GotoPosition Instance = new(UcMeta.s_Grab_GotoPosition, null);

        protected s_Grab_GotoPosition(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Grab_GotoPosition.BeginState", Sha1 = "508a2d95d73c463e2f55d138ef321a19e0e51329")]
        [PortOf("uc:SBase.SPlayerController:s_Grab_GotoPosition.BeginState", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
        [Provenance(ProvenanceKind.Estimated, "BeginState body not specced: the spy starts repositioning to vVandammePos")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_GRAB_REPOS, false);
            }
        }

        [Uc("SBase.SPlayerController.s_Grab_GotoPosition.EndState", Sha1 = "251eb518022ac943f50b500eb5cfa0fd83ece294")]
        [PortOf("uc:SBase.SPlayerController:s_Grab_GotoPosition.EndState", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
        public override void EndState(Actor self)
        {
            if (((SPlayerController)self).Pawn is { Physics: EPhysics.PHYS_Repositionnal } pawn)
            {
                pawn.SetPhysics(EPhysics.PHYS_Walking);
            }
        }

        [Uc("SBase.SPlayerController.s_Grab_GotoPosition.PlayerMove", Sha1 = "7ad8fc65d9911bac6446b48097a5aac84f402fb8")]
        [PortOf("uc:SBase.SPlayerController:s_Grab_GotoPosition.PlayerMove", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        [Uc("SBase.SPlayerController.s_Grab_GotoPosition.NotifyBump", Sha1 = "d0897923ffb719f820e8f20472dedb93146a0509")]
        [PortOf("uc:SBase.SPlayerController:s_Grab_GotoPosition.NotifyBump", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
        public bool NotifyBump(SPlayerController self, Actor Other) => false;

        [Uc("SBase.SPlayerController.s_Grab_GotoPosition.TopSynchro", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_Grab_GotoPosition.TopSynchro", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
        public virtual void TopSynchro(SPlayerController self)
        {
        }

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    /// <summary>s_Grabbing_GoToPosToFreeGuy: the spy repositions; at the end the owner sends the release once.</summary>
    [Uc("SBase.SPlayerController.s_Grabbing_GoToPosToFreeGuy")]
    public class s_Grabbing_GoToPosToFreeGuy : s_Grab_GotoPosition
    {
        public static new readonly s_Grabbing_GoToPosToFreeGuy Instance = new(UcMeta.s_Grabbing_GoToPosToFreeGuy, s_Grab_GotoPosition.Instance);

        protected s_Grabbing_GoToPosToFreeGuy(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Grabbing_GoToPosToFreeGuy.BeginState", Sha1 = "2a02bb25a7f3f16b3d039f45f75b75602391208f")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GoToPosToFreeGuy.BeginState", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            ((SPlayerController)self).btoCought = false;
        }

        [Uc("SBase.SPlayerController.s_Grabbing_GoToPosToFreeGuy.TopSynchro", Sha1 = "6a8f9cd60d65599fe2df80b9caba5cd96460b87e")]
        [PortOf("uc:SBase.SPlayerController:s_Grabbing_GoToPosToFreeGuy.TopSynchro", Spec = "spec/combat/spy-grab.md#choosing-where-to-release-owner")]
        public override void TopSynchro(SPlayerController self)
        {
            if (self.IsRemotePlayer || self.btoCought)
            {
                return;
            }

            self.btoCought = true;
            self.AskServerToUnGrabPawnAndFreeGuy(self.GrabReleaseMethode);
        }
    }

    // ---- spy catch and hold -------------------------------------------------------------------------------

    /// <summary>s_GrabbingPlayer_Begin: the spy waits for the merc to stand (rule 10).</summary>
    [Uc("SBase.SPlayerController.s_GrabbingPlayer_Begin")]
    public class s_GrabbingPlayer_Begin : UcActorState, IPlayerMove, IFire, IAltFire
    {
        public static readonly s_GrabbingPlayer_Begin Instance = new(UcMeta.s_GrabbingPlayer_Begin, null);

        protected s_GrabbingPlayer_Begin(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbingPlayer_Begin.PlayerMove", Sha1 = "e9a3f786e838523d369db622f6e2d8bde3bdc436")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer_Begin.PlayerMove", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.GrabHoldStill(DeltaTime);
            if (self.SBPawn?.m_AttackTarget is { bIsCrouched: false })
            {
                self.eventChangeState(GNAME_s_GrabbingPlayer_RealBegin);
            }
        }

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    /// <summary>s_GrabbingPlayer_RealBegin: the catch clip; its end starts the hold (rule 10).</summary>
    [Uc("SBase.SPlayerController.s_GrabbingPlayer_RealBegin")]
    public class s_GrabbingPlayer_RealBegin : UcActorState, IPlayerMove, IFire, IAltFire, IGrabAborted
    {
        public static readonly s_GrabbingPlayer_RealBegin Instance = new(UcMeta.s_GrabbingPlayer_RealBegin, null);

        protected s_GrabbingPlayer_RealBegin(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbingPlayer_RealBegin.BeginState", Sha1 = "93e3d1f76970ab8d2c7884ea72fcb53e329f0ab3")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.BeginState", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.GrabCamera(true);
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_GRAB_CATCH, false);
            }

            pc.GrabReleaseMethode = GrabReleaseMethode_.NoRelease;
        }

        [Uc("SBase.SPlayerController.s_GrabbingPlayer_RealBegin.AnimEnd", Sha1 = "2c6043a9b40d65c0b7ef7cdd7c5d1ffb5770d874")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.AnimEnd", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn && pc.CombatPorts.AnimSequence(pawn, 0) == GNAME_GrabStAlBg0)
            {
                pc.eventChangeState(GNAME_s_GrabbingPlayer);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbingPlayer_RealBegin.PlayerMove", Sha1 = "23f8738ec26c1e59989b5c94e50f51a02465e1f4")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.PlayerMove", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        [Uc("SBase.SPlayerController.s_GrabbingPlayer_RealBegin.GrabAborted", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer_RealBegin.GrabAborted", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void GrabAborted(SPlayerController self)
        {
        }

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    /// <summary>
    /// s_GrabbingPlayer: the spy holds the merc (rules 11 to 14): slow carry, Use breaks the neck, Fire knocks him
    /// out, 15 s choke him, a vanished merc ends it.
    /// </summary>
    [Uc("SBase.SPlayerController.s_GrabbingPlayer")]
    public class s_GrabbingPlayer : UcActorState, IPlayerTick, IPlayerMove, IUpdateRotation, IFire, IAltFire, IGoInSmoke,
        ICanUseCam, ICanBeDropAttacked, ICanActivateLaser, ICanUncrouchAndUndraw, ICanInteract, ICanIUseBinocular,
        ICanBeGrabbed, CombatStateEvents.IDoInteraction
    {
        public static readonly s_GrabbingPlayer Instance = new(UcMeta.s_GrabbingPlayer, null);

        protected s_GrabbingPlayer(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.BeginState", Sha1 = "a660f104ed431c33eb69d1a2b2cada29e6d796a8")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.BeginState", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.GrabCamera(true);
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_GRAB, false);
            }

            pc.btoCought = false;
            pc.SetTimer(K.GrabHoldLimit, false);
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                pc.bCanTalkToGuy = false;
                (pc.Level.Game as SGameInfo)?.AddPoint(0, pc, K.AP_A_Grab_Begin);
            }

            pc.iGrabMyCounterGrabValue = 0;
            pc.fGrabMatchStartedTime = pc.Level.TimeSeconds;
        }

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.EndState", Sha1 = "ac9678144656d8ab2d4110102085f054130473ba")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.EndState", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.SetTimer(0f, false);
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                pc.bCanTalkToGuy = false;
            }
        }

        /// <summary>The 15 s hold limit: the owner chokes the merc (rule 14).</summary>
        [Uc("SBase.SPlayerController.s_GrabbingPlayer.Timer", Sha1 = "63720da1f2e2fdecfb66c7a85e74ac5705c578a5")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.Timer", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IsRemotePlayer || pc.GrabReleaseMethode != GrabReleaseMethode_.NoRelease)
            {
                return;
            }

            pc.CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_.Timed);
            pc.GrabReleaseMethode = GrabReleaseMethode_.Timed;
        }

        /// <summary>A vanished merc releases at once; a pending release is retried (rule 14).</summary>
        [Uc("SBase.SPlayerController.s_GrabbingPlayer.PlayerTick", Sha1 = "17d02442be4fdc458162000d2f9d09c4b81a6517")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.PlayerTick", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.PlayerTick(DeltaTime);
            if (pc.GetStateName() != GNAME_s_GrabbingPlayer)
            {
                return;
            }

            if (pc.SBPawn?.m_AttackTarget is null)
            {
                pc.eventChangeState(GNAME_s_ReleaseGrabbedPlayer_LD);
                return;
            }

            if (pc.GrabReleaseMethode != GrabReleaseMethode_.NoRelease)
            {
                pc.CheckForRoomToFreeGrabbedGuy(pc.GrabReleaseMethode);
            }
        }

        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabCarryMove(DeltaTime);

        /// <summary>The free view while holding: yaw and pitch from the inputs, pitch kept in 15000 up and 50536 down.</summary>
        [Uc("SBase.SPlayerController.s_GrabbingPlayer.UpdateRotation", Sha1 = "c2a440aa483733d3e4695e26ab7ab98e66bced1a")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.UpdateRotation", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public void UpdateRotation(SPlayerController self, float DeltaTime, float maxPitch)
        {
            FRotator v = self.Rotation;
            float k = K.GrabViewRate * DeltaTime * self.fSensibiliteSouris;
            v.Yaw += SvmMath.Trunc(self.fTurnValue * k);
            v.Pitch = (v.Pitch + SvmMath.Trunc(self.fLookUpValue * k)) & 65535;
            if (v.Pitch > K.GrabViewPitchUp && v.Pitch < K.GrabViewPitchDown)
            {
                v.Pitch = v.Pitch < (K.GrabViewPitchUp + K.GrabViewPitchDown) / 2 ? K.GrabViewPitchUp : K.GrabViewPitchDown;
            }

            self.Rotation = v;
            self.aTurnPrevious = self.aTurn;
        }

        /// <summary>The knock-out key (rule 14).</summary>
        [Uc("SBase.SPlayerController.s_GrabbingPlayer.Fire", Sha1 = "80a05189861561ca0ad3fe87bb802247ae5d2f40")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.Fire", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public void Fire(SPlayerController self, float F)
        {
            if (!self.bIsInInventoryMode)
            {
                self.CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_.KO);
            }
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        /// <summary>Smoke on the holding spy: after the faint time he releases with a knock-out (rule 14).</summary>
        [Uc("SBase.SPlayerController.s_GrabbingPlayer.GoInSmoke", Sha1 = "6c4a135adc4822674f0822907cb21a887fd619c9")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.GoInSmoke", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        [Provenance(ProvenanceKind.Estimated, "The spy's faint countdown is the merc's (knockout rule 29) without the merc-only gate")]
        public void GoInSmoke(SPlayerController self, float tCough, float tFaint, float tHud, float blur)
        {
            self.SmokeCough(tCough, tFaint, tHud, blur);
            if (self.Level.TimeSeconds > self.fSmoke_FallDate)
            {
                self.CheckForRoomToFreeGrabbedGuy(GrabReleaseMethode_.KO);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.CanUseCam", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.CanUseCam", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public bool CanUseCam(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.CanBeDropAttacked", Spec = "spec/combat/spy-grab.md#damage-while-holding")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.CanActivateLaser", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.CanUncrouchAndUndraw", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.CanUncrouchAndUndraw", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public bool CanUncrouchAndUndraw(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.CanInteract", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public bool CanInteract(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.DoInteraction", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.DoInteraction", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public bool DoInteraction(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_GrabbingPlayer.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.CanIUseBinocular", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public bool CanIUseBinocular(SPlayerController self) => false;

        /// <summary>A holding spy can be grabbed in turn (the evidence table).</summary>
        public bool CanBeGrabbed(SPlayerController self) => true;
    }

    // ---- merc caught and held ---------------------------------------------------------------------------------

    /// <summary>
    /// s_GrabbedByPlayer_Begin: the merc is being caught (rule 9): he turns away from the spy; a run above 500 uu/s
    /// escapes on the server; a vanished spy releases him.
    /// </summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Begin")]
    public class s_GrabbedByPlayer_Begin : UcActorState, IPlayerTick, IPlayerMove, IGrabAborted, IFire, IAltFire, IGadgetQuickChoice,
        IGadLaserSwitch, IGadTorcheSwitch, ISwitchBinocularVision, ISwitchHeadset, ISwitchROF
    {
        public static readonly s_GrabbedByPlayer_Begin Instance = new(UcMeta.s_GrabbedByPlayer_Begin, null);

        protected s_GrabbedByPlayer_Begin(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Begin.BeginState", Sha1 = "3815ebde82a38bdfdc382a45b48e31035f201ac2")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.BeginState", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role != ENetRole.ROLE_Authority && pc.SBPawn is { } linked && pc.ActorPassedByMirrorState is SBasePawn attacker)
            {
                linked.m_AttackedBy = attacker;
            }

            if (!pc.IsRemotePlayer && pc.SBPawn is { } pawn)
            {
                if (pawn.bIsCrouched)
                {
                    pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WANTS_TO_UNCROUCH, false);
                }

                CombatFx.Notify(pc, CombatFx.Vibration_Grab);
                pc.bUsing = 0;
            }

            if (pc.Role == ENetRole.ROLE_Authority)
            {
                pc.SetTimer(K.GrabEscapeCheckPeriod, true);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Begin.EndState", Sha1 = "282693b32427df0891d11ceed1e39ad0358b0eac")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.EndState", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void EndState(Actor self) => ((SPlayerController)self).SetTimer(0f, false);

        /// <summary>The escape check on the server: a merc faster than 500 uu/s gets away.</summary>
        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Begin.Timer", Sha1 = "a7caace00266f71f5161ccdc18d5ad45a96d5ffd")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.Timer", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Pawn is { } pawn && pawn.Velocity.Size() > K.GrabEscapeSpeed)
            {
                pc.AskServerToUnGrabPawn(GrabReleaseMethode_.GrabbedAvoid);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Begin.PlayerTick", Sha1 = "466dd3a3b9582530d2e6fa086cb3d90ec8d51f56")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.PlayerTick", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.PlayerTick(DeltaTime);
            if (!pc.IsRemotePlayer && pc.SBPawn is { m_AttackedBy: null } && pc.GetStateName() == GNAME_s_GrabbedByPlayer_Begin)
            {
                pc.eventChangeState(GNAME_s_GrabbedByPlayer_Released_Grabbing_LD);
            }
        }

        /// <summary>The merc's yaw turns away from the spy at 20000 units/s.</summary>
        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Begin.PlayerMove", Sha1 = "3206944ee7c312ab5d9111a3f1605b4615c33c3a")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.PlayerMove", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is { m_AttackedBy: { } spy } merc)
            {
                FRotator r = merc.Rotation;
                r.Yaw = DampRotation(r.Yaw, (merc.Location - spy.Location).Rotation().Yaw, K.GrabbedTurnRate, DeltaTime);
                merc.Rotation = r;
            }

            self.GrabHoldStill(DeltaTime);
        }

        /// <summary>The grab was stopped: back to walking.</summary>
        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Begin.GrabAborted", Sha1 = "be504a43ed1619d6565826e440e27bfde26018d5")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Begin.GrabAborted", Spec = "spec/combat/spy-grab.md#release-resolution-server-askservertoungrabpawnway")]
        public void GrabAborted(SPlayerController self) => self.GotoWaitingStateCombat();

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        public void GadgetQuickChoice(SPlayerController self, int i)
        {
        }

        public void GadLaserSwitch(SPlayerController self)
        {
        }

        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        public void SwitchBinocularVision(SPlayerController self)
        {
        }

        public void SwitchHeadset(SPlayerController self, int i)
        {
        }

        public void SwitchROF(SPlayerController self)
        {
        }
    }

    /// <summary>s_GrabbedByPlayer: the merc is held (rule 12): no input, head camera, weapon lowered.</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer")]
    public class s_GrabbedByPlayer : UcActorState, IPlayerTick, IPlayerMove, IGoInSmoke, IFire, IAltFire, IGadgetQuickChoice,
        IGadLaserSwitch, IGadTorcheSwitch, ISwitchBinocularVision, ISwitchHeadset, ISwitchROF
    {
        public static readonly s_GrabbedByPlayer Instance = new(UcMeta.s_GrabbedByPlayer, null);

        protected s_GrabbedByPlayer(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer.BeginState", Sha1 = "06d3392b15787a79d0a533f38e5bfb69a280af4a")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer.BeginState", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            float now = pc.Level.TimeSeconds;
            pc.m_LastSpecialMoveTime = now;
            if (pc.Role != ENetRole.ROLE_Authority && pc.SBPawn is { } linked && pc.ActorPassedByMirrorState is SBasePawn attacker)
            {
                linked.m_AttackedBy = attacker;
            }

            if (!pc.IsRemotePlayer && pc.SBPawn is { } merc)
            {
                // The catch and held clips run directly (not relayed), the held one with m_AttackedBy set to the
                // merc himself so it plays even before the link replicates; the real attacker is put back after.
                if (merc.Physics != EPhysics.PHYS_FollowParent)
                {
                    merc.ExecuteAction(SBasePawn.SA_GRABBED_CATCH);
                    SBasePawn? saved = merc.m_AttackedBy;
                    merc.m_AttackedBy = merc;
                    merc.ExecuteAction(SBasePawn.SA_GRABBED);
                    merc.m_AttackedBy = saved;
                }

                if (merc is SPAWNDEFENSE defender && merc.WeaponLoaded != 0)
                {
                    PawnWeapons.NotifyUnDrawWeapon_GRAB(defender);
                }

                pc.SetHudPerturbationLength(K.GrabbedHudPerturbation);
                CombatFx.Notify(pc, CombatFx.HeadCamera, K.GrabbedCameraX, 0f, K.GrabbedCameraZ);
            }

            pc.fGrabMatchStartedTime = now;
            pc.iGrabMyCounterGrabValue = 0;
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer.EndState", Sha1 = "2a3c7360a6f979bed599e9cce9afc1b69005dfae")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer.EndState", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        [Provenance(ProvenanceKind.Estimated, "EndState body not specced: the head camera is released")]
        public override void EndState(Actor self) => CombatFx.Notify(self, CombatFx.HeadCamera);

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer.PlayerTick", Sha1 = "199a0600dbb92a32125d52726df41dd891fce916")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer.PlayerTick", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.PlayerTick(DeltaTime);
            CombatFx.Notify(pc, CombatFx.Vibration_Grab);
            if (!pc.IsRemotePlayer && pc.SBPawn is { m_AttackedBy: null } && pc.GetStateName() == GNAME_s_GrabbedByPlayer)
            {
                pc.eventChangeState(GNAME_s_GrabbedByPlayer_Released_Grabbing_LD);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer.PlayerMove", Sha1 = "e8f34a25a444668a95db2c633dda0e9be4bfffbb")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer.PlayerMove", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        /// <summary>The held loop again.</summary>
        [Uc("SBase.SPlayerController.s_GrabbedByPlayer.AnimEnd", Sha1 = "feb4ee0d15ac51181cb3515c30e3d5585f22e423")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer.AnimEnd", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && !pc.IsRemotePlayer && pc.SBPawn is { } pawn &&
                pc.m_LastSpecialMoveTime < pc.Level.TimeSeconds - K.GrabbedReloopDelay &&
                pc.CombatPorts.AnimSequence(pawn, 0) != GNAME_GrbdStAlNt0)
            {
                SBasePawn? saved = pawn.m_AttackedBy;
                pawn.m_AttackedBy = pawn;
                pawn.ExecuteAction(SBasePawn.SA_GRABBED);
                pawn.m_AttackedBy = saved;
            }
        }

        /// <summary>Smoke only blurs the held merc's HUD.</summary>
        public void GoInSmoke(SPlayerController self, float tCough, float tFaint, float tHud, float blur) =>
            self.GrabbedGoInSmoke(tHud, blur);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        public void GadgetQuickChoice(SPlayerController self, int i)
        {
        }

        public void GadLaserSwitch(SPlayerController self)
        {
        }

        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        public void SwitchBinocularVision(SPlayerController self)
        {
        }

        public void SwitchHeadset(SPlayerController self, int i)
        {
        }

        public void SwitchROF(SPlayerController self)
        {
        }
    }

    /// <summary>s_GrabbedByPlayer_AvoidGrab: the merc got away (rule 9): the escape clip, then walking.</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_AvoidGrab")]
    public class s_GrabbedByPlayer_AvoidGrab : UcActorState, IPlayerMove, IFire, IAltFire
    {
        public static readonly s_GrabbedByPlayer_AvoidGrab Instance = new(UcMeta.s_GrabbedByPlayer_AvoidGrab, null);

        protected s_GrabbedByPlayer_AvoidGrab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_AvoidGrab.BeginState", Sha1 = "ece59abba6834be655aa5e9d6bc035a53dcfc802")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_AvoidGrab.BeginState", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_GRABBED_AVOID, false);
                pc.GrabBackToWalking(pawn);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_AvoidGrab.PlayerMove", Sha1 = "e65eedba9122b3686357635036d1038627ba21f6")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_AvoidGrab.PlayerMove", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    /// <summary>s_Grab_Avoided: the spy's grab missed (rule 9): the miss clip, then unarmed walking.</summary>
    [Uc("SBase.SPlayerController.s_Grab_Avoided")]
    public class s_Grab_Avoided : UcActorState, IPlayerMove, IFire, IAltFire
    {
        public static readonly s_Grab_Avoided Instance = new(UcMeta.s_Grab_Avoided, null);

        protected s_Grab_Avoided(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Grab_Avoided.BeginState", Sha1 = "2eb685f3569df5dcaec66ef0d5045d6006dac311")]
        [PortOf("uc:SBase.SPlayerController:s_Grab_Avoided.BeginState", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pawn.m_AttackTarget = null;
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_GRAB_AVOIDED, false);
            }

            pc.eventChangeState(CNAME_s_PlayerWalkingSt0H);
        }

        [Uc("SBase.SPlayerController.s_Grab_Avoided.PlayerMove", Sha1 = "5cd53885415b34b4076898e59558c7fc98923557")]
        [PortOf("uc:SBase.SPlayerController:s_Grab_Avoided.PlayerMove", Spec = "spec/combat/spy-grab.md#server-validation-and-approach")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    /// <summary>s_AbortGrab: both players go back to their waiting state.</summary>
    [Uc("SBase.SPlayerController.s_AbortGrab")]
    public class s_AbortGrab : UcActorState
    {
        public static readonly s_AbortGrab Instance = new(UcMeta.s_AbortGrab, null);

        protected s_AbortGrab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_AbortGrab.BeginState", Sha1 = "24d42c44ca05bb5525a92b328c7537ee4645a2d8")]
        [PortOf("uc:SBase.SPlayerController:s_AbortGrab.BeginState", Spec = "spec/combat/spy-grab.md#release-resolution-server-askservertoungrabpawnway")]
        public override void BeginState(Actor self) => ((SPlayerController)self).GotoWaitingStateCombat();
    }

    // ---- merc released ------------------------------------------------------------------------------------

    /// <summary>s_GrabbedByPlayer_Released: the merc's release clip, then armed walking (rule 23).</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released")]
    public class s_GrabbedByPlayer_Released : UcActorState, IPlayerMove, IFire, IAltFire, IGadgetQuickChoice, IGadLaserSwitch,
        IGadTorcheSwitch, ISwitchBinocularVision, ISwitchHeadset, ISwitchROF
    {
        public static readonly s_GrabbedByPlayer_Released Instance = new(UcMeta.s_GrabbedByPlayer_Released, null);

        protected s_GrabbedByPlayer_Released(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released.BeginState", Sha1 = "f4d5aa11cfdb1eb632b1186a1692e2391280e984")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IsRemotePlayer)
            {
                CombatFx.Notify(pc, CombatFx.WeirdEffect, 1f);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released.AnimEnd", Sha1 = "8004eb56b370286b2dd12e19b69d34e7e51a3204")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_2H, false);
                pc.eventChangeState(CNAME_s_PlayerWalkingSt2H);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released.PlayerMove", Sha1 = "70281e1e0abcfde6659ca34205b6e5a302d83ec4")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released.PlayerMove", Spec = "spec/combat/spy-grab.md#release-states")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        public void GadgetQuickChoice(SPlayerController self, int i)
        {
        }

        public void GadLaserSwitch(SPlayerController self)
        {
        }

        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        public void SwitchBinocularVision(SPlayerController self)
        {
        }

        public void SwitchHeadset(SPlayerController self, int i)
        {
        }

        public void SwitchROF(SPlayerController self)
        {
        }
    }

    /// <summary>s_GrabbedByPlayer_Released_KO: knocked out; once unlinked the merc lies down for 7 s.</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_KO")]
    public class s_GrabbedByPlayer_Released_KO : s_GrabbedByPlayer_Released
    {
        public static new readonly s_GrabbedByPlayer_Released_KO Instance = new(UcMeta.s_GrabbedByPlayer_Released_KO, s_GrabbedByPlayer_Released.Instance);

        protected s_GrabbedByPlayer_Released_KO(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_KO.BeginState", Sha1 = "9f6a9ffadce2a9136067c96642558d00eddc84f0")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_KO.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var pc = (SPlayerController)self;
            pc.Goggle?.ResetVisionMode(true);
            if (pc.SBPawn?.MyWeapon is SWeaponDefense rifle)
            {
                rifle.bDrawWeapon = false;
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_KO.AnimEnd", Sha1 = "f6a9003144f4099020a4e7906e3193f6574a3e9c")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_KO.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { m_AttackedBy: null })
            {
                return;
            }

            CombatFx.Notify(pc, CombatFx.OverlayFade, 0f, 255f, K.KoReleaseOverlayTime);
            pc.fLyingOnGroundDuration = K.DefaultLyingTime;
            pc.eventChangeState(CNAME_s_LyingOnGround);
        }
    }

    /// <summary>
    /// s_GrabbedByPlayer_Released_STRANGLE: neck break or choke; after 4 s the view turns, the head camera detaches
    /// and the owner goes to s_Dead.
    /// </summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_STRANGLE")]
    public class s_GrabbedByPlayer_Released_STRANGLE : s_GrabbedByPlayer_Released, IPlayerTick
    {
        public static new readonly s_GrabbedByPlayer_Released_STRANGLE Instance =
            new(UcMeta.s_GrabbedByPlayer_Released_STRANGLE, s_GrabbedByPlayer_Released.Instance);

        protected s_GrabbedByPlayer_Released_STRANGLE(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_STRANGLE.BeginState", Sha1 = "f798ae54df92578f3ee82819ddb79434bafde187")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_STRANGLE.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var pc = (SPlayerController)self;
            pc.fGrabDate = pc.Level.TimeSeconds;
            pc.fTempsAvantClignote = K.StrangleBlinkDelay;
            CombatFx.Notify(pc, CombatFx.Vibration_Grab);
            if (pc.SBPawn?.MyWeapon is SWeaponDefense rifle)
            {
                rifle.bDrawWeapon = false;
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_STRANGLE.EndState", Sha1 = "c320be0000e6c4aea3d9b6c16dbbdf8a2d4c7929")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_STRANGLE.EndState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void EndState(Actor self) => ((SPlayerController)self).SetHudPerturbationLength(0f);

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_STRANGLE.PlayerTick", Sha1 = "0bdf8c11ee20dcc516f013821641639f04c0b956")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_STRANGLE.PlayerTick", Spec = "spec/combat/spy-grab.md#release-states")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.PlayerTick(DeltaTime);
            if (pc.IsRemotePlayer || pc.GetStateName() != GNAME_s_GrabbedByPlayer_Released_STRANGLE ||
                !(pc.Level.TimeSeconds - pc.fGrabDate > K.StrangleToDead))
            {
                return;
            }

            FRotator r = pc.Rotation;
            r.Yaw += K.HalfTurn;
            pc.Rotation = r;
            CombatFx.Notify(pc, CombatFx.HeadCamera);
            pc.eventChangeState(CNAME_s_Dead);
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_STRANGLE.AnimEnd", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_STRANGLE.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
        }
    }

    /// <summary>s_GrabbedByPlayer_Released_DMG: killed while held; once unlinked, s_Dead.</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_DMG")]
    public class s_GrabbedByPlayer_Released_DMG : s_GrabbedByPlayer_Released
    {
        public static new readonly s_GrabbedByPlayer_Released_DMG Instance =
            new(UcMeta.s_GrabbedByPlayer_Released_DMG, s_GrabbedByPlayer_Released.Instance);

        protected s_GrabbedByPlayer_Released_DMG(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_DMG.AnimEnd", Sha1 = "2d486231b7d2f9d5951dae539744c5973c48bcb3")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_DMG.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { m_AttackedBy: null })
            {
                pc.eventChangeState(CNAME_s_Dead);
            }
        }
    }

    /// <summary>s_GrabbedByPlayer_Released_Grabbing_LD: the spy vanished; the merc steps back by the grab offset.</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_Grabbing_LD")]
    public class s_GrabbedByPlayer_Released_Grabbing_LD : s_GrabbedByPlayer_Released
    {
        public static new readonly s_GrabbedByPlayer_Released_Grabbing_LD Instance =
            new(UcMeta.s_GrabbedByPlayer_Released_Grabbing_LD, s_GrabbedByPlayer_Released.Instance);

        protected s_GrabbedByPlayer_Released_Grabbing_LD(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_Grabbing_LD.BeginState", Sha1 = "5fbe97a5c24d4d9f995fb691087996608c46860f")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_Grabbing_LD.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bGrabIsReleasing = false;
            pc.SetHudPerturbationLength(0f);
            CombatFx.Notify(pc, CombatFx.WeirdEffect, 0f);
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_GRABBED_RELEASE_GRABBING_LD, false);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_Grabbing_LD.EndState", Sha1 = "7e13035c7dbf405609fbd7a62ee3f7d29f866485")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_Grabbing_LD.EndState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } merc)
            {
                merc.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
                FVector off = merc.vGrabOffset;
                merc.SetLocation(merc.Location - (x * off.X + y * off.Y + z * off.Z));
            }

            pc.GrabRestoreView();
        }
    }

    /// <summary>s_GrabbedByPlayer_Released_GrabbingDMG: the spy was killed; once unlinked, back to walking.</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_GrabbingDMG")]
    public class s_GrabbedByPlayer_Released_GrabbingDMG : s_GrabbedByPlayer_Released
    {
        public static new readonly s_GrabbedByPlayer_Released_GrabbingDMG Instance =
            new(UcMeta.s_GrabbedByPlayer_Released_GrabbingDMG, s_GrabbedByPlayer_Released.Instance);

        protected s_GrabbedByPlayer_Released_GrabbingDMG(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_GrabbingDMG.BeginState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_GrabbingDMG.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_GrabbingDMG.AnimEnd", Sha1 = "99af78d68442f9d87d5887f1fba0bee044854b5a")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_GrabbingDMG.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { m_AttackedBy: null } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_2H, false);
                pc.eventChangeState(CNAME_s_PlayerWalkingSt2H);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_Released_GrabbingDMG.EndState", Sha1 = "8c701b03b1a9cd30f0646e077e35373c6b19bd05")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_Released_GrabbingDMG.EndState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void EndState(Actor self) => ((SPlayerController)self).GrabRestoreView();
    }

    /// <summary>s_GrabbedByPlayer_CounterGrab: the merc won the mash; the counter clip, then walking.</summary>
    [Uc("SBase.SPlayerController.s_GrabbedByPlayer_CounterGrab")]
    public class s_GrabbedByPlayer_CounterGrab : s_GrabbedByPlayer_Released
    {
        public static new readonly s_GrabbedByPlayer_CounterGrab Instance =
            new(UcMeta.s_GrabbedByPlayer_CounterGrab, s_GrabbedByPlayer_Released.Instance);

        protected s_GrabbedByPlayer_CounterGrab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_CounterGrab.BeginState", Sha1 = "8b6979974a242f78ad2d791c8893da34eced8f57")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_CounterGrab.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_COUNTER_GRAB_GO, false);
            }
        }

        /// <summary>The view stays locked to the pawn.</summary>
        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_CounterGrab.PlayerMove", Sha1 = "c7113eaad869b769a11bccb99d95426283db0033")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_CounterGrab.PlayerMove", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is { } pawn)
            {
                self.Rotation = new FRotator(self.Rotation.Pitch, pawn.Rotation.Yaw, 0);
                pawn.Acceleration = FVector.Zero;
            }

            self.SendMove(DeltaTime, FVector.Zero);
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_CounterGrab.AnimEnd", Sha1 = "1272d3fa6ce5e7c8f075556b4df4eabd3e7bd13c")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_CounterGrab.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_2H, false);
                pc.eventChangeState(CNAME_s_PlayerWalkingSt2H);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbedByPlayer_CounterGrab.EndState", Sha1 = "bd922bc038bad867973799eb9d85a5a9ee369ed3")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer_CounterGrab.EndState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.GrabRestoreView();
            pc.SetHudPerturbationLength(0f);
        }
    }

    /// <summary>The held merc's camera and weapon back (the head camera off, the rifle raised).</summary>
    private void GrabRestoreView()
    {
        CombatFx.Notify(this, CombatFx.HeadCamera);
        if (SBPawn is SPAWNDEFENSE merc && merc.MyWeapon is SWeaponDefense rifle)
        {
            rifle.bDrawWeapon = true;
            if (merc.WeaponLoaded == 0)
            {
                PawnWeapons.NotifyDrawWeapon_GRAB(merc);
            }
        }
    }

    // ---- spy release ----------------------------------------------------------------------------------------

    /// <summary>s_ReleaseGrabbedPlayer: the spy's release clip, then walking (rule 22).</summary>
    [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer")]
    public class s_ReleaseGrabbedPlayer : UcActorState, IPlayerMove, IFire, IAltFire
    {
        public static readonly s_ReleaseGrabbedPlayer Instance = new(UcMeta.s_ReleaseGrabbedPlayer, null);

        protected s_ReleaseGrabbedPlayer(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The release action of this release type.</summary>
        protected virtual int ReleaseAction => SBasePawn.SA_GRAB_RELEASE;

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer.BeginState", Sha1 = "cdff05f56941c7ce5b0d9c8a85282185586d3669")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.GrabCamera(false);
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, ReleaseAction, false);
            }
        }

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer.EndState", Sha1 = "9b72f5fb50386e0968a6bd0a61f02dba9848f700")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.EndState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IsRemotePlayer)
            {
                return;
            }

            pc.bGrabIsReleasing = false;
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, pawn.WeaponLoaded is 0 or 1 ? SBasePawn.SA_WALK_ST_0H : SBasePawn.SA_WALK_ST_2H, false);
            }
        }

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer.AnimEnd", Sha1 = "7cacc9f9285d8673ce25004997bc7ba04f9b1fd5")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.GrabBackToWalking(pawn);
            }
        }

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer.PlayerMove", Sha1 = "5adb09980ad29505e8d2bfffee590297d030a154")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.PlayerMove", Spec = "spec/combat/spy-grab.md#release-states")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_LD")]
    public class s_ReleaseGrabbedPlayer_LD : s_ReleaseGrabbedPlayer
    {
        public static new readonly s_ReleaseGrabbedPlayer_LD Instance = new(UcMeta.s_ReleaseGrabbedPlayer_LD, s_ReleaseGrabbedPlayer.Instance);

        protected s_ReleaseGrabbedPlayer_LD(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override int ReleaseAction => SBasePawn.SA_GRAB_RELEASE_GRABBED_LD;

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_LD.BeginState", Sha1 = "a79ef2a4c177bdabb643e8ca8616b47bd3030254")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_LD.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self) => base.BeginState(self);
    }

    [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_KO")]
    public class s_ReleaseGrabbedPlayer_KO : s_ReleaseGrabbedPlayer
    {
        public static new readonly s_ReleaseGrabbedPlayer_KO Instance = new(UcMeta.s_ReleaseGrabbedPlayer_KO, s_ReleaseGrabbedPlayer.Instance);

        protected s_ReleaseGrabbedPlayer_KO(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override int ReleaseAction => SBasePawn.SA_GRAB_RELEASE_KO;

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_KO.BeginState", Sha1 = "e7c8438f53d36b85fbc26228b91cc884550e9088")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_KO.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self) => base.BeginState(self);
    }

    [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_Strangle")]
    public class s_ReleaseGrabbedPlayer_Strangle : s_ReleaseGrabbedPlayer
    {
        public static new readonly s_ReleaseGrabbedPlayer_Strangle Instance =
            new(UcMeta.s_ReleaseGrabbedPlayer_Strangle, s_ReleaseGrabbedPlayer.Instance);

        protected s_ReleaseGrabbedPlayer_Strangle(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override int ReleaseAction => SBasePawn.SA_GRAB_RELEASE_STRANGLE;

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_Strangle.BeginState", Sha1 = "3467805257baf07f090f1723537bfd279326ee31")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_Strangle.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self) => base.BeginState(self);
    }

    [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_Grabbed_DMGED")]
    public class s_ReleaseGrabbedPlayer_Grabbed_DMGED : s_ReleaseGrabbedPlayer
    {
        public static new readonly s_ReleaseGrabbedPlayer_Grabbed_DMGED Instance =
            new(UcMeta.s_ReleaseGrabbedPlayer_Grabbed_DMGED, s_ReleaseGrabbedPlayer.Instance);

        protected s_ReleaseGrabbedPlayer_Grabbed_DMGED(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override int ReleaseAction => SBasePawn.SA_GRAB_RELEASE_GRABBED_DMGED;

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_Grabbed_DMGED.BeginState", Sha1 = "922f083d62e3dbc8cae6c882e1c5c25517c2dc48")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_Grabbed_DMGED.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self) => base.BeginState(self);
    }

    /// <summary>s_ReleaseGrabbedPlayer_DMG: the spy was killed while holding; at the clip end, s_Dead.</summary>
    [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_DMG")]
    public class s_ReleaseGrabbedPlayer_DMG : s_ReleaseGrabbedPlayer
    {
        public static new readonly s_ReleaseGrabbedPlayer_DMG Instance = new(UcMeta.s_ReleaseGrabbedPlayer_DMG, s_ReleaseGrabbedPlayer.Instance);

        protected s_ReleaseGrabbedPlayer_DMG(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override int ReleaseAction => SBasePawn.SA_GRAB_RELEASE_DMG;

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_DMG.BeginState", Sha1 = "dc3e179347aedba71e269a84714949b8f74d0007")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_DMG.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self) => base.BeginState(self);

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_DMG.AnimEnd", Sha1 = "03cff8bd0c881c851ef3159132392afd5459ad7c")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_DMG.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            if (Channel == 0)
            {
                ((SPlayerController)self).eventChangeState(CNAME_s_Dead);
            }
        }

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_DMG.EndState", Sha1 = "3aaee52030066ce53197508a6917ab45aaacb1ca")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_DMG.EndState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void EndState(Actor self) => ((SPlayerController)self).bGrabIsReleasing = false;
    }

    /// <summary>s_ReleaseGrabbedPlayer_BothDMG: both died; at the clip end, s_Dead.</summary>
    [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_BothDMG")]
    public class s_ReleaseGrabbedPlayer_BothDMG : s_ReleaseGrabbedPlayer_DMG
    {
        public static new readonly s_ReleaseGrabbedPlayer_BothDMG Instance =
            new(UcMeta.s_ReleaseGrabbedPlayer_BothDMG, s_ReleaseGrabbedPlayer_DMG.Instance);

        protected s_ReleaseGrabbedPlayer_BothDMG(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        protected override int ReleaseAction => SBasePawn.SA_GRAB_RELEASE_BOTH_DIE;

        [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer_BothDMG.BeginState", Sha1 = "68eb7ea0d1400023f826a82b24ccd1d0694a4b1b")]
        [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer_BothDMG.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self) => base.BeginState(self);
    }

    /// <summary>
    /// s_GrabRelease_Counter: the merc won the counter-grab; the spy's knock-back clip ends in a 3 s lie down (or
    /// s_Dead when dead).
    /// </summary>
    [Uc("SBase.SPlayerController.s_GrabRelease_Counter")]
    public class s_GrabRelease_Counter : s_ReleaseGrabbedPlayer
    {
        public static new readonly s_GrabRelease_Counter Instance = new(UcMeta.s_GrabRelease_Counter, s_ReleaseGrabbedPlayer.Instance);

        protected s_GrabRelease_Counter(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabRelease_Counter.BeginState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_GrabRelease_Counter.BeginState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void BeginState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabRelease_Counter.AnimEnd", Sha1 = "b1c8df34a71a0cfcd36a4f7a9c8b44ef3e98582b")]
        [PortOf("uc:SBase.SPlayerController:s_GrabRelease_Counter.AnimEnd", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.Pawn is not { } pawn || pawn.Physics == EPhysics.PHYS_RootMotion)
            {
                return;
            }

            if (pawn.Health > 0)
            {
                pc.fLyingOnGroundDuration = K.CounterGrabLyingTime;
                pc.eventChangeState(CNAME_s_LyingOnGround);
            }
            else
            {
                pc.eventChangeState(CNAME_s_Dead);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabRelease_Counter.EndState", Sha1 = "3aaee52030066ce53197508a6917ab45aaacb1ca")]
        [PortOf("uc:SBase.SPlayerController:s_GrabRelease_Counter.EndState", Spec = "spec/combat/spy-grab.md#release-states")]
        public override void EndState(Actor self) => ((SPlayerController)self).bGrabIsReleasing = false;
    }
}
