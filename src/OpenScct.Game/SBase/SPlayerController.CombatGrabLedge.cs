using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController ledge grab (spec/combat/spy-grab.md rules 30 to 36): a spy hanging from a ledge pulls a merc
// standing above him over the edge. The owner's search is in OnLedge (SPlayerController.OnLedge.cs); the pull clips
// are actions 312 to 314 (CombatActionAnimation) and the merc's fall starts at the end of GrbdStLgRail (PawnAnimEnd).
public partial class SPlayerController
{
    private static readonly Name LNAME_AskServerToGrabPawnFromLedge = new("AskServerToGrabPawnFromLedge");
    private static readonly Name LNAME_PCChangeClientStateForMercGrabbedFromLedge = new("PCChangeClientStateForMercGrabbedFromLedge");
    private static readonly Name LNAME_s_GrabbingFromLedge_Go = new("s_GrabbingFromLedge_Go");
    private static readonly Name LNAME_s_GrabbedFromLedge_Begin = new("s_GrabbedFromLedge_Begin");
    private static readonly Name LNAME_GrabLgBgRail = new("GrabLgBgRail");
    private static readonly Name LNAME_s_Selected = new("s_Selected");
    private static readonly Name LNAME_Vibration_Grab_Kill = new("Vibration_Grab_Kill");
    private static readonly AssetRef LSND_LedgeGrabAttack = new("Interface.Objectifs.Jingl_AsomDrop_Attac");
    private static readonly AssetRef LSND_LedgeGrabDefence = new("Interface.Objectifs.Jingl_AsomDrop_Def");

    /// <summary>A state version of SendAnEventToServer (the global one does nothing).</summary>
    public interface ISendAnEventToServer
    {
        void SendAnEventToServer(SPlayerController self, SentEventType_ Event);
    }

    /// <summary>
    /// The ledge box (rule 31): the merc inside the spy's forward and side box (times <paramref name="BoxScale"/>), at
    /// the height of a pawn standing on the ledge above the hands, in front of the spy. The facing term never fails
    /// (fGrabFromLedgeHalfAngle -1.1) and is kept as the original computes it.
    /// </summary>
    private bool LedgeGrabGeometry(Pawn Spy, SBasePawn Target, float BoxScale)
    {
        FVector target2D = Target.Location;
        target2D.Z = 0f;
        FVector spy2D = Spy.Location;
        spy2D.Z = 0f;
        float idealZ = Spy.Location.Z + K.LedgeGrabHeight - Spy.CollisionHeight + Target.CollisionHeight;
        FVector d = spy2D - target2D;
        Spy.Rotation.GetAxes(out FVector x, out FVector y, out _);
        FVector facing = Spy.Rotation.Vector().SafeNormal();
        return SvmMath.Abs(d | x) < fGrabFromLedgeDist1 * BoxScale && SvmMath.Abs(d | y) < fGrabFromLedgeDist2 * BoxScale &&
            SvmMath.Abs(idealZ - Target.Location.Z) < K.LedgeGrabHeightTolerance &&
            ((target2D - spy2D).SafeNormal() | facing) > K.GrabFrontCone &&
            (Target.Rotation.Vector().SafeNormal() | facing) < -fGrabFromLedgeHalfAngle;
    }

    /// <summary>How far in front of the hanging spy the merc is pulled to: arms zone X + 16 + radius + 4 (94).</summary>
    private static float LedgePullReach(SBasePawn P) =>
        P.m_LedgeGrabArmsZone.X + K.LedgeGrabArmsMargin + P.CollisionRadius + K.LedgeGrabRadiusMargin;

    /// <summary>
    /// The server's ledge grab (rule 32): a crouched spy is refused; a merc whose state cannot be grabbed gets no
    /// answer; the refusal test is the original's inverted one (only a merc woken within 3.8 s and not recently
    /// shocked is checked against the box); the merc needs a clear path to the pull spot. Then the merc is moved to
    /// the spot and both players enter their ledge grab states.
    /// </summary>
    [Uc("SBase.SPlayerController.AskServerToGrabPawnFromLedge", Sha1 = "aeab3bc5d498749c71bc7cfcd7473ff0a72484bf")]
    [PortOf("uc:SBase.SPlayerController:AskServerToGrabPawnFromLedge", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
    [PortOf("rep:SBase.SPlayerController.AskServerToGrabPawnFromLedge", Spec = "spec/combat/spy-grab.md#network")]
    public void AskServerToGrabPawnFromLedge(SBasePawn PawnToGrab)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(LNAME_AskServerToGrabPawnFromLedge) { Actor1 = PawnToGrab });
            return;
        }

        if (SBPawn is not { Health: > 0 } spy)
        {
            return;
        }

        if (spy.bIsCrouched)
        {
            ServerSendAnEvent(SentEventType_.SE_GrabFromLedgePb);
            return;
        }

        if (PawnToGrab.Controller is not SPlayerController merc || !CombatStateEvents.CanBeGrabbed(merc) ||
            (PawnToGrab.bIsCrouched && !PawnToGrab.CanUnCrouch()))
        {
            return;
        }

        float now = Level.TimeSeconds;
        if (!(now - PawnToGrab.iLastWakeTime > fWakeInvulnerabilityTime) &&
            merc.TimeShockForGrab < now - fGrabInvulneralbilityTimeAfterStickyShock &&
            LedgeGrabGeometry(spy, PawnToGrab, K.LedgeGrabServerBoxFactor))
        {
            ServerSendAnEvent(SentEventType_.SE_GrabFromLedgePb);
            return;
        }

        FVector spot = spy.Location + spy.Rotation.Vector() * LedgePullReach(spy);
        spot.Z = PawnToGrab.Location.Z;
        if (TraceHitZones.CollisionOf(this) is { } world &&
            world.LineCheck(out _, PawnToGrab, spot, PawnToGrab.Location, K.LedgeGrabPathExtent, ETraceFlags.TRACE_World))
        {
            ServerSendAnEvent(SentEventType_.SE_GrabFromLedgePb);
            return;
        }

        CombatPorts.ChangeEnumAction(PawnToGrab, SBasePawn.SA_SERVER_REPOS, false);
        merc.MirrorStateForMercGrabbedFromLedge(spy);
        GotoState(CNAME_s_GrabbingFromLedge_Begin);
        IncStat(this, static s => s.Spy_LedgeGrabs++);
        ClientSetMusic(LSND_LedgeGrabAttack, EMusicTransition.MTRAN_None);
        merc.ClientSetMusic(LSND_LedgeGrabDefence, EMusicTransition.MTRAN_None);
    }

    /// <summary>
    /// The merc's side of the ledge grab on the server (rule 32): the pull spot in front of the spy, facing him, the
    /// link, s_GrabbedFromLedge_Begin, and the same on the merc's own machine.
    /// </summary>
    [Uc("SBase.SPlayerController.MirrorStateForMercGrabbedFromLedge", Sha1 = "6319520827814a49b980e9ccb7da8bb297318449")]
    [PortOf("uc:SBase.SPlayerController:MirrorStateForMercGrabbedFromLedge", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
    public void MirrorStateForMercGrabbedFromLedge(SBasePawn Spy)
    {
        if (SBPawn is not { } merc)
        {
            return;
        }

        FVector spot = Spy.Location + Spy.Rotation.Vector() * LedgePullReach(merc);
        int yaw = Spy.Rotation.Yaw + K.HalfTurn;
        merc.vVandammePos = spot;
        merc.vWallJumpDir = new FRotator(0, yaw, 0).Vector();
        merc.m_AttackedBy = Spy;
        Spy.m_AttackTarget = merc;
        GotoState(LNAME_s_GrabbedFromLedge_Begin);
        if (Player is UViewport)
        {
            return;
        }

        float now = Level.TimeSeconds;
        if (now - MirrorStateAcknowledgementTime > OpenScct.Game.GameFlow.K.MirrorStateAckRefresh)
        {
            MirrorStateAcknowledgementTime = now;
        }

        if (IsRemotePlayer)
        {
            // The two times travel in Vector2 (the combat call has one float).
            CombatNet.ClientCall(this, new CombatCall(LNAME_PCChangeClientStateForMercGrabbedFromLedge)
            {
                Vector1 = spot,
                Vector2 = new FVector(MirrorStateAcknowledgementTime, now, 0f),
                Int1 = yaw,
                Actor1 = Spy,
            });
            return;
        }

        PCChangeClientStateForMercGrabbedFromLedge(MirrorStateAcknowledgementTime, now, Spy, spot, yaw);
    }

    /// <summary>
    /// The merc's machine follows (reliable server to owner, unless older than the last state order): the link, the
    /// pull spot and facing, the client's own repositioning (SA_REPOS) and s_GrabbedFromLedge_Begin.
    /// </summary>
    [Uc("SBase.SPlayerController.PCChangeClientStateForMercGrabbedFromLedge", Sha1 = "affb255935c20934965e950afcca8500e6888526")]
    [PortOf("uc:SBase.SPlayerController:PCChangeClientStateForMercGrabbedFromLedge", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
    [PortOf("rep:SBase.SPlayerController.PCChangeClientStateForMercGrabbedFromLedge", Spec = "spec/combat/spy-grab.md#network")]
    public void PCChangeClientStateForMercGrabbedFromLedge(float MSATime, float fNewTime, SBasePawn Spy, FVector PosToGo, int YawToGo)
    {
        if (MirrorStateLastTime > fNewTime)
        {
            return;
        }

        MirrorStateLastTime = fNewTime;
        MirrorStateAcknowledgementTime = MSATime;
        if (SBPawn is not { } merc)
        {
            return;
        }

        merc.m_AttackedBy = Spy;
        Spy.m_AttackTarget = merc;
        merc.vVandammePos = PosToGo;
        merc.vWallJumpDir = new FRotator(0, YawToGo, 0).Vector();
        merc.ChangeEnumAction(SBasePawn.SA_REPOS);
        GotoState(LNAME_s_GrabbedFromLedge_Begin);
    }

    /// <summary>An SE_* event handled by the current state on this machine (the global version does nothing).</summary>
    [Uc("SBase.SPlayerController.ReceiveAnEvent", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:ReceiveAnEvent", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
    public void ReceiveAnEvent(SentEventType_ Event)
    {
        if (StateNode is CombatStateEvents.IReceiveAnEvent s)
        {
            s.ReceiveAnEvent(this, Event);
        }
    }

    /// <summary>The owner-to-server event (the global version is empty; s_GrabbingFromLedge_Begin handles it).</summary>
    [Uc("SBase.SPlayerController.SendAnEventToServer", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:SendAnEventToServer", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
    public void SendAnEventToServer(SentEventType_ Event)
    {
        if (StateNode is ISendAnEventToServer s)
        {
            s.SendAnEventToServer(this, Event);
        }
    }

    /// <summary>
    /// The held merc's machine tells the spy (rule 34): the authority directly, a client through the spy's
    /// controller. A remote merc's machine has no controller for the spy, so nothing is sent from there, as in the
    /// original (the spy's own 1.5 s timeout ends his side).
    /// </summary>
    private void LedgeGrabTellSpy(SentEventType_ Event)
    {
        if (SBPawn?.m_AttackedBy?.Controller is not SPlayerController spy)
        {
            return;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            spy.ReceiveAnEvent(Event);
        }
        else
        {
            spy.SendAnEventToServer(Event);
        }
    }

    /// <summary>The ledge grab states' move: the view in the hang window, no acceleration.</summary>
    private void LedgeGrabHold(float DeltaTime)
    {
        LimitedViewByCamera(DeltaTime, 0, 0);
        if (Pawn is { } pawn)
        {
            pawn.Acceleration = FVector.Zero;
        }

        SendMove(DeltaTime, FVector.Zero);
    }

    // ---- states ----------------------------------------------------------------------------------------------

    /// <summary>s_GrabFromLedgeBase: the ledge grab states' base; its damage handler is the GrabFromLedge one.</summary>
    [Uc("SBase.SPlayerController.s_GrabFromLedgeBase")]
    public class s_GrabFromLedgeBase : s_GrabBase
    {
        public static new readonly s_GrabFromLedgeBase Instance = new(UcMeta.s_GrabFromLedgeBase, s_GrabBase.Instance);

        protected s_GrabFromLedgeBase(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }

    /// <summary>
    /// s_GrabbingFromLedge_Begin: the spy's reach clip (rule 33). The pull starts once both the clip has ended and the
    /// merc reported ready, whichever comes second; a refusal or 1.5 s on the owner return to the hang.
    /// </summary>
    [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Begin")]
    public class s_GrabbingFromLedge_Begin : s_GrabFromLedgeBase, IPlayerMove, CombatStateEvents.IReceiveAnEvent, ISendAnEventToServer
    {
        public static new readonly s_GrabbingFromLedge_Begin Instance =
            new(UcMeta.s_GrabbingFromLedge_Begin, s_GrabFromLedgeBase.Instance);

        protected s_GrabbingFromLedge_Begin(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Begin.BeginState", Sha1 = "2cabc0561a0404a9b621333f7e2469453a75f68c")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.BeginState", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bCanStop = false;
            if (pc.IAmOnClient())
            {
                pc.SetTimer(K.LedgeGrabTimeout, false);
            }
        }

        /// <summary>The server never answered: back to the hang.</summary>
        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Begin.Timer", Sha1 = "e348da0a9b934fc324a889cc9ef49ae084ac6590")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.Timer", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public override void Timer(Actor self) => ((SPlayerController)self).LedgeGrabBackToHang();

        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Begin.PlayerMove", Sha1 = "87f5e22ddbcb1e3a3f274a144db26975253cb165")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.PlayerMove", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.LedgeGrabHold(DeltaTime);

        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Begin.AnimEnd", Sha1 = "c613f60898bffa7c1e0ba9c4414cb2bd94d2aec8")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.AnimEnd", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn && pc.CombatPorts.AnimSequence(pawn, 0) == LNAME_GrabLgBgRail)
            {
                pc.LedgeGrabReadyHalf();
            }
        }

        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Begin.ReceiveAnEvent", Sha1 = "782e95c8176d77a439e4932179998e09111e3fbb")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.ReceiveAnEvent", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void ReceiveAnEvent(SPlayerController self, SentEventType_ Event)
        {
            switch (Event)
            {
                case SentEventType_.SE_GrabFromLedgeMercIsReady:
                    self.LedgeGrabReadyHalf();
                    break;
                case SentEventType_.SE_GrabFromLedgePb:
                    self.LedgeGrabBackToHang();
                    break;
            }
        }

        /// <summary>Handled where it is called: sent to a remote spy's owner, else received at once.</summary>
        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Begin.SendAnEventToServer", Sha1 = "26c76adb1cf326b515991850675f0b3ad2989e87")]
        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Begin.ServerSendAnEvent", Sha1 = "d45e0f975081077c269b4d8b514cd8a0ab97c752")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.SendAnEventToServer", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Begin.ServerSendAnEvent", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void SendAnEventToServer(SPlayerController self, SentEventType_ Event) => self.ServerSendAnEvent(Event);
    }

    /// <summary>One of the two pull conditions met (rule 33): the second one starts the pull.</summary>
    private void LedgeGrabReadyHalf()
    {
        if (!bCanStop)
        {
            bCanStop = true;
            return;
        }

        if (SBPawn is { } pawn)
        {
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_GRABFROMLEDGE_GO, false);
        }

        eventChangeState(LNAME_s_GrabbingFromLedge_Go);
    }

    /// <summary>The spy hangs again (SA_ONLEDGE and OnLedge).</summary>
    private void LedgeGrabBackToHang()
    {
        if (SBPawn is { } pawn)
        {
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_ONLEDGE, false);
        }

        eventChangeState(MovementStateNames.OnLedge);
    }

    /// <summary>s_GrabbingFromLedge_Go: the pull clip (the merc's plays with it); its end returns to the hang.</summary>
    [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Go")]
    public class s_GrabbingFromLedge_Go : s_GrabFromLedgeBase, IPlayerMove
    {
        public static new readonly s_GrabbingFromLedge_Go Instance = new(UcMeta.s_GrabbingFromLedge_Go, s_GrabFromLedgeBase.Instance);

        protected s_GrabbingFromLedge_Go(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Go.PlayerMove", Sha1 = "ee8be60cb111f8387310570ac4f40031ae9ac20a")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Go.PlayerMove", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.LedgeGrabHold(DeltaTime);

        [Uc("SBase.SPlayerController.s_GrabbingFromLedge_Go.AnimEnd", Sha1 = "3ff5ab555a7654d15508025ed28f7ec0b826a215")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbingFromLedge_Go.AnimEnd", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient())
            {
                pc.LedgeGrabBackToHang();
            }
        }
    }

    /// <summary>
    /// s_GrabbedFromLedge_Begin: the merc is moved to the pull spot (rule 34). The server reports him ready when the
    /// repositioning ends; a bump on the way, 1.5 s still repositioning, the spy gone or 3 s without the pull abort.
    /// No firing or gadgets meanwhile.
    /// </summary>
    /// <remarks>Not ported: bIsDamageVolumeSensitive (false here), damage volumes have no port.</remarks>
    [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin")]
    public class s_GrabbedFromLedge_Begin : s_GrabFromLedgeBase, IPlayerTick, IPlayerMove, ITopSynchro, INotifyBump, IGrabAborted,
        IFire, IAltFire, IGadgetQuickChoice, IGadLaserSwitch, IGadTorcheSwitch, ISwitchBinocularVision, ISwitchHeadset, ISwitchROF
    {
        public static new readonly s_GrabbedFromLedge_Begin Instance =
            // The class metadata gives this state no super state (the decompiled text says s_GrabFromLedgeBase); the
            // shared grab behaviour still comes through the C# base class.
            new(UcMeta.s_GrabbedFromLedge_Begin, null);

        protected s_GrabbedFromLedge_Begin(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.BeginState", Sha1 = "25ae278310f8df221902f40c3cc9f618382f158e")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.BeginState", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bBinocularVision = false;
            if (!pc.IAmOnClient())
            {
                return;
            }

            float now = pc.Level.TimeSeconds;
            pc.SetTimer(K.LedgeGrabTimeout, false);
            pc.bCanStop = false;
            CombatFx.Notify(pc, LNAME_Vibration_Grab_Kill);
            pc.TimerUSe = now;
            pc.fGrabFromLedgeStartTime = now;
            pc.fThrowForce = 0f;
            pc.SetBallisticValue(0f);
            pc.bUsingNow = false;
        }

        [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.EndState", Sha1 = "282693b32427df0891d11ceed1e39ad0358b0eac")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.EndState", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public override void EndState(Actor self) => ((SPlayerController)self).bUsing = 0;

        /// <summary>
        /// 1.5 s on the owner: still repositioning aborts; otherwise the merc watches from his head (camera on the
        /// head bone, rifle lowered, first person with the pawn).
        /// </summary>
        [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.Timer", Sha1 = "4f72ec87ce26d470caeaff0a83d97bcb90b69856")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.Timer", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } merc)
            {
                return;
            }

            if (merc.Physics == EPhysics.PHYS_Repositionnal)
            {
                merc.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                pc.LedgeGrabTellSpy(SentEventType_.SE_GrabFromLedgePb);
                pc.GrabAborted();
                return;
            }

            CombatFx.Notify(pc, CombatFx.HeadCamera, K.GrabbedCameraX, 0f, K.GrabbedCameraZ);
            if (merc.MyWeapon is SWeaponDefense rifle)
            {
                rifle.bDrawWeapon = false;
            }

            pc.bBehindView = true;
            pc.bNoReticule = true;
            pc.bFirstPersonWithPawn = true;
            pc.bCanStop = true;
        }

        /// <summary>The repositioning reached the pull spot (server): the merc stops and tells the spy he is ready.</summary>
        [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.TopSynchro", Sha1 = "bcf5b71bcdfdaf8e07ac77d551f9ca0c890023ed")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.TopSynchro", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void TopSynchro(SPlayerController self)
        {
            if (self.Role != ENetRole.ROLE_Authority || self.SBPawn is not { } merc)
            {
                return;
            }

            merc.SetPhysics(EPhysics.PHYS_None);
            (merc.m_AttackedBy?.Controller as SPlayerController)?.ServerSendAnEvent(SentEventType_.SE_GrabFromLedgeMercIsReady);
        }

        /// <summary>A bump on the way to the pull spot (server) aborts the grab on both sides.</summary>
        [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.NotifyBump", Sha1 = "645baaed9d756a90badd0c9ecefcb54c4def7ed9")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.NotifyBump", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.Role == ENetRole.ROLE_Authority && self.SBPawn is { Physics: EPhysics.PHYS_Repositionnal } merc)
            {
                (merc.m_AttackedBy?.Controller as SPlayerController)?.ServerSendAnEvent(SentEventType_.SE_GrabFromLedgePb);
                self.GrabAborted();
            }

            return true;
        }

        /// <summary>No fire while held.</summary>
        [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.PlayerTick", Sha1 = "bf318334dffbbb9ddfc2e204aaa9fddb6e017b7c")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.PlayerTick", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.PlayerTick(DeltaTime);
            pc.bFireMode = 0;
            pc.bFire = 0;
        }

        [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.GrabAborted", Sha1 = "dfe3686a2b997829e238773390c57ce279d00f61")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.GrabAborted", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void GrabAborted(SPlayerController self)
        {
            if (self.IAmOnClient() && self.Pawn is { Physics: not EPhysics.PHYS_RootMotion })
            {
                self.GotoWaitingStateCombat();
            }
        }

        /// <summary>
        /// The owner's abort (the spy gone, or 3 s without the pull clip): out of the repositioning, or the camera and
        /// rifle back and the waiting state, and the spy told. Until the head camera is on the view follows the pawn.
        /// </summary>
        [Uc("SBase.SPlayerController.s_GrabbedFromLedge_Begin.PlayerMove", Sha1 = "886b8d6a124f4b9bb83b8d2e9c5858685e9c2562")]
        [PortOf("uc:SBase.SPlayerController:s_GrabbedFromLedge_Begin.PlayerMove", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } merc)
            {
                return;
            }

            if (merc.m_AttackedBy is null ||
                (self.Level.TimeSeconds - self.fGrabFromLedgeStartTime > K.LedgeGrabbedAbortTime && merc.Physics != EPhysics.PHYS_RootMotion))
            {
                if (merc.Physics == EPhysics.PHYS_Repositionnal)
                {
                    merc.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                    self.GrabAborted();
                }
                else if (merc.Physics != EPhysics.PHYS_RootMotion)
                {
                    self.ResetMercCam();
                    CombatFx.Notify(self, CombatFx.HeadCamera);
                    if (merc.MyWeapon is SWeaponDefense rifle)
                    {
                        rifle.bDrawWeapon = true;
                        rifle.GotoState(LNAME_s_Selected);
                        rifle.AnimateWaiting();
                    }

                    self.GotoWaitingStateCombat();
                }

                if (merc.m_AttackedBy is not null)
                {
                    self.LedgeGrabTellSpy(SentEventType_.SE_GrabFromLedgePb);
                }
            }

            if (!self.bCanStop)
            {
                var r = new FRotator(self.Rotation.Pitch, merc.Rotation.Yaw, merc.Rotation.Roll);
                self.Rotation = r;
                self.MyDesiredRotation = r;
                self.UpdateDesiredRotation(self.MyDesiredRotation);
                self.LocalMyDesiredRotation = self.MyDesiredRotation;
            }

            if (self.bBehindView)
            {
                self.bFirstPersonWithPawn = true;
            }

            merc.Acceleration = FVector.Zero;
            self.SendMove(DeltaTime, FVector.Zero);
        }

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

    /// <summary>
    /// s_FallAfterGrabbedFromLedge: the merc falls from the ledge (rule 36). Landing on the owner kills him (200
    /// DamTypeGrabBreak credited to the spy); coming down on a head starts a sticky blast or a drop attack; more than
    /// 4 s in the air is the blast fall's SFell death.
    /// </summary>
    /// <remarks>
    /// The original extends s_BlastFallAfterObstacle (its damage handler is the Blast one by name) but lists its own
    /// ignores without StickyBlast, so a sticky-skinned head still blasts. Not ported: bIsDamageVolumeSensitive.
    /// </remarks>
    [Uc("SBase.SPlayerController.s_FallAfterGrabbedFromLedge")]
    public class s_FallAfterGrabbedFromLedge : UcActorState, IPlayerMove, IMyNotifyLanded, INotifyBump, IFire, IAltFire,
        IGadgetQuickChoice, IGadLaserSwitch, IGadTorcheSwitch, ISwitchHeadset, ISwitchROF, ITellClientServerWaitForFatalBlast
    {
        public static readonly s_FallAfterGrabbedFromLedge Instance = new(UcMeta.s_FallAfterGrabbedFromLedge, null);

        protected s_FallAfterGrabbedFromLedge(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The blast fall's move (4 s limit), the view kept first person with the pawn.</summary>
        [Uc("SBase.SPlayerController.s_FallAfterGrabbedFromLedge.PlayerMove", Sha1 = "e3ad52d9a291df1fee66da3011bb493df6140afc")]
        [PortOf("uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.PlayerMove", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is { } pawn && self.Level.TimeSeconds - self.m_LastSpecialMoveTime > K.BlastObstacleFallLimit)
            {
                self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_DIE_FALLING, false);
                self.DamagePawn_C2S(pawn, pawn.Health, pawn, self.Location, new FVector(0f, 0f, -1f), SDamageType.SFell);
                if (!self.CombatInGameEnded)
                {
                    self.eventChangeState(CNAME_s_Dead);
                }

                return;
            }

            self.BlastMove(DeltaTime);
            if (self.bBehindView)
            {
                self.bFirstPersonWithPawn = true;
            }
        }

        /// <summary>The landing on the owner: 200 grab-break damage from the spy, the landing clip, s_Dead.</summary>
        [Uc("SBase.SPlayerController.s_FallAfterGrabbedFromLedge.MyNotifyLanded", Sha1 = "803da2d2cd0ffad14c2e63ba0b4b4d05621816bf")]
        [PortOf("uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.MyNotifyLanded", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            self.MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return true;
            }

            self.DamagePawn_C2S(pawn, K.LedgeGrabFallDamage, pawn.m_AttackedBy, self.Location, new FVector(0f, 0f, -1f),
                SDamageType.DamTypeGrabBreak);
            self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_LAND_AFTER_BLAST_WALL, false);
            if (!self.CombatInGameEnded)
            {
                self.eventChangeState(CNAME_s_Dead);
            }

            return true;
        }

        /// <summary>Coming down on a head (owner): a sticky-skinned pawn blasts, anyone else is drop-attacked.</summary>
        [Uc("SBase.SPlayerController.s_FallAfterGrabbedFromLedge.NotifyBump", Sha1 = "70fb48bf272e643b91d59390529445603bcc7696")]
        [PortOf("uc:SBase.SPlayerController:s_FallAfterGrabbedFromLedge.NotifyBump", Spec = "spec/combat/spy-grab.md#ledge-grab-spy-hanging-merc-above")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (Other is SBasePawn below && self.Pawn is { } me && self.IAmOnClient() &&
                below.Location.Z + below.CollisionHeight * K.LedgeFallHeadShare <= me.Location.Z - me.CollisionHeight)
            {
                if (below.bStickySkinned)
                {
                    self.eventStickyBlast(below);
                }
                else
                {
                    self.TellServerToFallAttack(below);
                }
            }

            return true;
        }

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

        public void SwitchHeadset(SPlayerController self, int i)
        {
        }

        public void SwitchROF(SPlayerController self)
        {
        }

        public void TellClientServerWaitForFatalBlast(SPlayerController self)
        {
        }
    }
}
