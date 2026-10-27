using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController finishing move and revive (spec/combat/knockout-and-terminate.md rules 32 to 46): the merc's
// terminate search and the server check, the merc's terminate states, the downed spy's terminated states and the
// reviver states. Not ported here: the desync recovery of rule 46 (ReinitPawnBecauseOfDivergences, netcode).
public partial class SPlayerController
{
    private static readonly Name TNAME_AskServerToTerminatePawn = new("AskServerToTerminatePawn");
    private static readonly Name TNAME_ServerSendAnEvent = new("ServerSendAnEvent");
    private static readonly Name TNAME_MirrorStateForRepos = new("MirrorStateForRepos");
    private static readonly Name TNAME_s_TerminatePawn_ReposBegin = new("s_TerminatePawn_ReposBegin");
    private static readonly Name TNAME_s_TerminatePawn = new("s_TerminatePawn");
    private static readonly Name TNAME_s_TerminatePawn_Maintain = new("s_TerminatePawn_Maintain");
    private static readonly Name TNAME_s_TerminatePawn_Kill = new("s_TerminatePawn_Kill");
    private static readonly Name TNAME_s_TransitionTPK = new("s_TransitionTPK");
    private static readonly Name TNAME_s_TerminatedPawn = new("s_TerminatedPawn");
    private static readonly Name TNAME_s_PlayerWalkingCr2H = new("s_PlayerWalkingCr2H");

    /// <summary>A state version of <see cref="MercCantTerminate"/>.</summary>
    public interface IMercCantTerminate
    {
        void MercCantTerminate(SPlayerController self);
    }

    /// <summary>A state version of <see cref="bIsTerminatingPawn"/>.</summary>
    public interface IbIsTerminatingPawn
    {
        bool bIsTerminatingPawn(SPlayerController self);
    }

    /// <summary>MercCantTerminate through the current state.</summary>
    public void eventMercCantTerminate()
    {
        if (StateNode is IMercCantTerminate s)
        {
            s.MercCantTerminate(this);
        }
    }

    /// <summary>True while this merc is finishing a downed spy.</summary>
    public bool eventbIsTerminatingPawn() => StateNode is IbIsTerminatingPawn s && s.bIsTerminatingPawn(this);

    [Uc("SBase.SPlayerController.MercCantTerminate", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:MercCantTerminate", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
    public void MercCantTerminate()
    {
    }

    // ---- search and server check ------------------------------------------------------------------------------

    /// <summary>
    /// The merc's Use search (rules 32 and 36): a sleeping team mate is revived first; else the first downed pawn in
    /// reach that can be terminated, in front and at the same floor height, with a clear box to the finishing spot.
    /// </summary>
    [Uc("SBase.SPlayerController.FindPawnToReanimateOrTerminate", Sha1 = "0d1df0d2a2fbd8f0f6db34fe3a2f84a7d96999c0")]
    [PortOf("uc:SBase.SPlayerController:FindPawnToReanimateOrTerminate", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
    public bool FindPawnToReanimateOrTerminate()
    {
        if (FindPawnToReanimate())
        {
            return true;
        }

        if (SBPawn is not SPAWNDEFENSE me)
        {
            return false;
        }

        FVector facing = me.Rotation.Vector().SafeNormal();
        foreach (Pawn p in XLevel.DynamicActors<Pawn>())
        {
            if (p is not SBasePawn target || ReferenceEquals(target, me) || target.Health <= 0 || !target.bCanBeTerminated ||
                !(FVector.Dist(target.Location, me.Location) < K.ReviveRange) ||
                !(((target.Location - me.Location).SafeNormal() | facing) > K.ReviveFacing))
            {
                continue;
            }

            float myFeet = me.Location.Z - me.CollisionHeight;
            float itsFeet = target.Location.Z - target.CollisionHeight;
            if (!(SvmMath.Abs(myFeet - itsFeet) < K.TerminateHeightTolerance) || !TerminateSpot(me, target, out FVector spot))
            {
                continue;
            }

            spot.Z += me.CollisionHeight;
            me.vVandammePos = spot;
            me.vWallJumpDir = target.Rotation.Vector();
            CombatPorts.SetInfoForVandamme(me, spot, target.Rotation);
            CombatPorts.ChangeEnumAction(me, SBasePawn.SA_FAST_REPOS_SANS_RECAL, false);
            me.m_PawnToTerminate = target;
            GotoState(TNAME_s_TerminatePawn_ReposBegin);
            AskServerToTerminatePawn(target);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The finishing spot 110 uu behind the downed pawn's facing at his feet, reachable by a clear (35, 35, 80) box
    /// from the merc with both centres 95 uu above the feet.
    /// </summary>
    private bool TerminateSpot(SBasePawn me, SBasePawn target, out FVector spot)
    {
        FVector feet = target.Location - new FVector(0f, 0f, target.CollisionHeight);
        spot = feet - target.Rotation.Vector() * K.TerminateStandOffset;
        if (TraceHitZones.CollisionOf(this) is not { } world)
        {
            return true;
        }

        FVector from = me.Location - new FVector(0f, 0f, me.CollisionHeight) + new FVector(0f, 0f, K.TerminateClearanceRaise);
        FVector to = spot + new FVector(0f, 0f, K.TerminateClearanceRaise);
        return !world.LineCheck(out _, me, to, from, K.TerminateClearanceExtent, ETraceFlags.TRACE_AllColliding);
    }

    /// <summary>
    /// The server's check (rule 37): alive, within 350 uu (2D) of the downed pawn's feet, the pawn alive and
    /// terminable, the same clear box; then the merc repositions and both pawns are linked. Refused: the merc's owner
    /// is told SE_TerminatePawnPb.
    /// </summary>
    [Uc("SBase.SPlayerController.AskServerToTerminatePawn", Sha1 = "015c81a858b97220ebdb2cb65a68eca891cf4fdc")]
    [PortOf("uc:SBase.SPlayerController:AskServerToTerminatePawn", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
    public void AskServerToTerminatePawn(SBasePawn Target)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(TNAME_AskServerToTerminatePawn) { Actor1 = Target });
            return;
        }

        bool valid = false;
        FVector spot = default;
        if (SBPawn is { Health: > 0 } me && Target is { Health: > 0, bCanBeTerminated: true })
        {
            FVector feet = Target.Location - new FVector(0f, 0f, Target.CollisionHeight);
            FVector d = feet - me.Location;
            d.Z = 0f;
            valid = d.Size() < K.TerminateServerRange && TerminateSpot(me, Target, out spot);
            if (valid)
            {
                spot.Z += me.CollisionHeight;
                me.vVandammePos = spot;
                me.vWallJumpDir = Target.Rotation.Vector();
                CombatPorts.SetInfoForVandamme(me, spot, Target.Rotation);
                GotoState(TNAME_s_TerminatePawn_ReposBegin);
                me.m_PawnToTerminate = Target;
                Target.m_PawnToTerminate = me;
            }
        }

        if (!valid)
        {
            ServerSendAnEvent(SentEventType_.SE_TerminatePawnPb);
        }
    }

    /// <summary>An event to this player's owner (reliable server to owner); the owner's state handles it.</summary>
    [Uc("SBase.SPlayerController.ServerSendAnEvent", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:ServerSendAnEvent", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
    public void ServerSendAnEvent(SentEventType_ Event)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(TNAME_ServerSendAnEvent) { Int1 = (int)Event });
            return;
        }

        if (StateNode is CombatStateEvents.IReceiveAnEvent s)
        {
            s.ReceiveAnEvent(this, Event);
        }
    }

    /// <summary>
    /// The server enters a state after a repositioning and the owner follows at the server's position and yaw.
    /// </summary>
    [Uc("SBase.SPlayerController.MirrorStateForRepos", Sha1 = "fb1557657c6c68c3fdbce525917918d3e82e9113")]
    [PortOf("uc:SBase.SPlayerController:MirrorStateForRepos", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
    public void MirrorStateForRepos(Name NewState, FVector Location, int Yaw)
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            GotoState(NewState);
            if (IsRemotePlayer)
            {
                CombatNet.ClientCall(this, new CombatCall(TNAME_MirrorStateForRepos) { Name1 = NewState, Vector1 = Location, Int1 = Yaw });
            }

            return;
        }

        if (Pawn is { } pawn)
        {
            pawn.SetLocation(Location);
            pawn.Rotation = new FRotator(pawn.Rotation.Pitch, Yaw, pawn.Rotation.Roll);
        }

        GotoState(NewState);
    }

    [Uc("SBase.SPlayerController.bIsTerminatingPawn", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SPlayerController:bIsTerminatingPawn", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
    public bool bIsTerminatingPawn() => false;

    /// <summary>The downed pawn the merc is finishing is let go (its released state).</summary>
    private void ReleaseTerminated()
    {
        if (SBPawn is { m_PawnToTerminate: { } victim } me)
        {
            me.m_PawnToTerminate = null;
            if (victim.Health > 0 && victim.Controller is SPlayerController vpc)
            {
                victim.m_PawnToTerminate = null;
                vpc.eventMirrorState(CNAME_s_TerminatedPawn_Released);
            }
        }
    }

    /// <summary>
    /// The finishing merc's view and reticle back (the reticle stays off while the laser was on), and the rifle drawn
    /// again in s_Selected with its idle animation (s_TerminatePawn_Kill and _Fall EndState).
    /// </summary>
    private void TerminateRestoreView()
    {
        if (!IsRemotePlayer)
        {
            CombatFx.Notify(this, CombatFx.HeadCamera);
            bBehindView = false;
            if (!bLaserEtaitAllume)
            {
                bNoReticule = false;
            }
        }

        if (SBPawn?.MyWeapon is SWeaponDefense rifle)
        {
            rifle.bDrawWeapon = true;
            rifle.GotoState(CNAME_s_Selected);
            rifle.AnimateWaiting();
        }
    }

    // ---- merc states ----------------------------------------------------------------------------------------

    /// <summary>
    /// The finishing states' common part: no interaction, laser off, can be dropped on; a fall starts the fall
    /// variant; the server's refusal returns to armed walking.
    /// </summary>
    [Uc("SBase.SPlayerController.s_TerminatePawn_Base")]
    public class s_TerminatePawn_Base : UcActorState, IPlayerMove, IMyFalling, ICanBeDropAttacked, ICanActivateLaser, ICanInteract,
        CombatStateEvents.IDoInteraction, CombatStateEvents.IReceiveAnEvent, IbIsTerminatingPawn, IFire, IAltFire, IGadgetQuickChoice,
        IGadLaserSwitch, ISwitchROF
    {
        public static readonly s_TerminatePawn_Base Instance = new(UcMeta.s_TerminatePawn_Base, null);

        protected s_TerminatePawn_Base(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.BeginState", Sha1 = "80238c0efc4c4a8f5c5ba7afa3709d109eb75f58")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        [Provenance(ProvenanceKind.Estimated, "BeginState body not specced: the laser off and the merc grabbable")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.EteintLaser();
            if (pc.SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = true;
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.bIsTerminatingPawn", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.bIsTerminatingPawn", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public bool bIsTerminatingPawn(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.MyFalling", Sha1 = "c82475e05eb8cdfeee99910b610da9759d745c1b")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.MyFalling", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public virtual void MyFalling(SPlayerController self, float LeftTime) => self.eventChangeState(new Name("s_TerminatePawn_Fall"));

        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.PlayerMove", Sha1 = "03095770835821be42770c6c908708c0f73c77ea")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.PlayerMove", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        /// <summary>SE_TerminatePawnPb: back to armed walking.</summary>
        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.ReceiveAnEvent", Sha1 = "eb67fc91f26edbd094a8b7626f13a40500e63a2d")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.ReceiveAnEvent", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public void ReceiveAnEvent(SPlayerController self, SentEventType_ Event)
        {
            if (Event != SentEventType_.SE_TerminatePawnPb || self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.m_PawnToTerminate = null;
            self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_2H, false);
            self.eventChangeState(CNAME_s_PlayerWalkingSt2H);
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.CanBeDropAttacked", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.CanActivateLaser", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.CanInteract", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public bool CanInteract(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_TerminatePawn_Base.DoInteraction", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.DoInteraction", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public bool DoInteraction(SPlayerController self) => false;

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

        public void SwitchROF(SPlayerController self)
        {
        }
    }

    /// <summary>s_TerminatePawn_ReposBegin: the merc walks to the finishing spot (2 s limit on the owner).</summary>
    [Uc("SBase.SPlayerController.s_TerminatePawn_ReposBegin")]
    public class s_TerminatePawn_ReposBegin : s_TerminatePawn_Base, ITopSynchro
    {
        public static new readonly s_TerminatePawn_ReposBegin Instance = new(UcMeta.s_TerminatePawn_ReposBegin, null);

        protected s_TerminatePawn_ReposBegin(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_ReposBegin.BeginState", Sha1 = "f2d7834833cd8b54b73e02790f953e76868556a4")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_ReposBegin.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var pc = (SPlayerController)self;
            if (!pc.IsRemotePlayer)
            {
                pc.SetTimer(K.TerminateApproachTimeout, false);
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_ReposBegin.Timer", Sha1 = "a6b431404d7db3a26d7278252dbd566338701d81")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_ReposBegin.Timer", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void Timer(Actor self)
        {
            // The approach gave up: the spy never left s_LyingOnGround, so he is left alone (no release).
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pawn.m_PawnToTerminate = null;
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_2H, false);
            }

            pc.eventChangeState(CNAME_s_PlayerWalkingSt2H);
        }

        /// <summary>Arrived (server): the finishing clip starts on both machines at the server's position.</summary>
        [Uc("SBase.SPlayerController.s_TerminatePawn_ReposBegin.TopSynchro", Sha1 = "910578b8e4897a2edbb1e14c0bbd44f0e6322a6d")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_ReposBegin.TopSynchro", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public void TopSynchro(SPlayerController self)
        {
            if (self.Role == ENetRole.ROLE_Authority && self.Pawn is { } pawn)
            {
                self.MirrorStateForRepos(TNAME_s_TerminatePawn, pawn.Location, pawn.Rotation.Yaw);
            }
        }
    }

    /// <summary>s_TerminatePawn: the finishing clip starts; the victim goes to s_TerminatedPawn (rule 39).</summary>
    [Uc("SBase.SPlayerController.s_TerminatePawn")]
    public class s_TerminatePawn : s_TerminatePawn_Base, ICanBeGrabbed
    {
        public static new readonly s_TerminatePawn Instance = new(UcMeta.s_TerminatePawn, null);

        protected s_TerminatePawn(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn.BeginState", Sha1 = "95a72b4a1e427d481d15ad21008cc4df78bb7db7")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_TERMINATEPAWN_BEGIN, false);
            if (pc.Role == ENetRole.ROLE_Authority && pawn.m_PawnToTerminate is { Health: > 0, Controller: SPlayerController victim })
            {
                victim.eventMirrorState(TNAME_s_TerminatedPawn);
            }

            if (!pc.IsRemotePlayer)
            {
                CombatFx.Notify(pc, CombatFx.HeadCamera, K.GrabbedCameraX, 0f, K.GrabbedCameraZ);
                if (pawn.MyWeapon is SWeaponDefense rifle)
                {
                    rifle.bDrawWeapon = false;
                }

                // The head view with the body drawn (the script sets bFirstPersonWithPawn again in every PlayerMove).
                pc.bBehindView = true;
                pc.bNoReticule = true;
                pc.bFirstPersonWithPawn = true;
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn.AnimEnd", Sha1 = "5656bc25bae8eefb59e0396ce54bc2f74de5d81a")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn.AnimEnd", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && !pc.IsRemotePlayer)
            {
                pc.eventChangeState(TNAME_s_TerminatePawn_Maintain);
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn.PlayerMove", Sha1 = "a549a414c7a139f4202756e21a64546588b3f218")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn.PlayerMove", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        [Uc("SBase.SPlayerController.s_TerminatePawn.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn.CanBeGrabbed", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public bool CanBeGrabbed(SPlayerController self) => true;
    }

    /// <summary>s_TerminatePawn_Maintain: the merc holds the downed spy until Use, 10 s or his death (rule 40).</summary>
    [Uc("SBase.SPlayerController.s_TerminatePawn_Maintain")]
    public class s_TerminatePawn_Maintain : s_TerminatePawn_Base, ICanBeGrabbed
    {
        public static new readonly s_TerminatePawn_Maintain Instance = new(UcMeta.s_TerminatePawn_Maintain, null);

        protected s_TerminatePawn_Maintain(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Maintain.BeginState", Sha1 = "625fb8623fc45a836f8a61a7c565113094541fb0")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Maintain.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var pc = (SPlayerController)self;
            pc.oldbUsing = pc.bUsing;
            if (!pc.IsRemotePlayer)
            {
                pc.SetTimer(pc.fMaxTimeMaintainingPawn, false);
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Maintain.Timer", Sha1 = "6a8d788f12abc0d97e47afead684572126851d66")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Maintain.Timer", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void Timer(Actor self) => ((SPlayerController)self).TerminateKill();

        /// <summary>A new Use press, or the victim's death, finishes him.</summary>
        [Uc("SBase.SPlayerController.s_TerminatePawn_Maintain.PlayerMove", Sha1 = "fe1ce6bb6b5d7c5fdb3f987aa779ffeac4202101")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Maintain.PlayerMove", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.GrabHoldStill(DeltaTime);
            if (self.IsRemotePlayer)
            {
                return;
            }

            bool pressed = self.bUsing != 0 && self.oldbUsing == 0;
            self.oldbUsing = self.bUsing;
            if (pressed || self.SBPawn?.m_PawnToTerminate is not { Health: > 0 })
            {
                self.TerminateKill();
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Maintain.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Maintain.CanBeGrabbed", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public bool CanBeGrabbed(SPlayerController self) => true;
    }

    /// <summary>The kill clip and its state (rule 40).</summary>
    private void TerminateKill()
    {
        if (SBPawn is { } pawn)
        {
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_TERMINATEPAWN_KILL, false);
        }

        eventChangeState(TNAME_s_TerminatePawn_Kill);
    }

    /// <summary>s_TerminatePawn_Kill: the kill clip; its end gives s_TransitionTPK (rule 41).</summary>
    [Uc("SBase.SPlayerController.s_TerminatePawn_Kill")]
    public class s_TerminatePawn_Kill : s_TerminatePawn_Base, ICanBeGrabbed
    {
        public static new readonly s_TerminatePawn_Kill Instance = new(UcMeta.s_TerminatePawn_Kill, null);

        protected s_TerminatePawn_Kill(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Kill.BeginState", Sha1 = "7454da160008c81a3bb72a93bd6a016a1b37940a")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Kill.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            ((SPlayerController)self).SetTimer(0f, false);
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Kill.EndState", Sha1 = "cfc70ea8c51b16e66259d69e57a1817bad9cfd5d")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Kill.EndState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void EndState(Actor self) => ((SPlayerController)self).TerminateRestoreView();

        [Uc("SBase.SPlayerController.s_TerminatePawn_Kill.AnimEnd", Sha1 = "5da18d738e9d3d2320a25b509a022d9fe9eb94da")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Kill.AnimEnd", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && !pc.IsRemotePlayer)
            {
                pc.eventChangeState(TNAME_s_TransitionTPK);
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Kill.PlayerMove", Sha1 = "691856d11cfb6a2a86465e682533f33440b1d2a9")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Kill.PlayerMove", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        [Uc("SBase.SPlayerController.s_TerminatePawn_Kill.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Kill.CanBeGrabbed", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public bool CanBeGrabbed(SPlayerController self) => true;
    }

    /// <summary>s_TerminatePawn_Fall: the merc fell during the move: the victim is released and the fall is normal.</summary>
    [Uc("SBase.SPlayerController.s_TerminatePawn_Fall")]
    public class s_TerminatePawn_Fall : s_TerminatePawn_Base
    {
        public static new readonly s_TerminatePawn_Fall Instance = new(UcMeta.s_TerminatePawn_Fall, null);

        protected s_TerminatePawn_Fall(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Fall.BeginState", Sha1 = "4cd351b1b3080ecc99e36dee61f35e365e92ed8e")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Fall.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                pc.ReleaseTerminated();
            }

            if (pc.IAmOnClient())
            {
                pc.MyFalling(0f);
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatePawn_Fall.EndState", Sha1 = "cfc70ea8c51b16e66259d69e57a1817bad9cfd5d")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Fall.EndState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void EndState(Actor self) => ((SPlayerController)self).TerminateRestoreView();

        [Uc("SBase.SPlayerController.s_TerminatePawn_Fall.MyFalling", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Fall.MyFalling", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void MyFalling(SPlayerController self, float LeftTime)
        {
        }
    }

    /// <summary>s_TransitionTPK: back to armed walking; the server deals the 300 hp finishing hit (rule 42).</summary>
    [Uc("SBase.SPlayerController.s_TransitionTPK")]
    public class s_TransitionTPK : UcActorState
    {
        public static readonly s_TransitionTPK Instance = new(UcMeta.s_TransitionTPK, null);

        protected s_TransitionTPK(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TransitionTPK.BeginState", Sha1 = "b5c63bcee07830f2725ce178e972541180e231b5")]
        [PortOf("uc:SBase.SPlayerController:s_TransitionTPK.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            SBasePawn? victim = pawn.m_PawnToTerminate;
            bool crouched = pawn.bIsCrouched;
            pc.CombatPorts.ChangeEnumAction(pawn, crouched ? SBasePawn.SA_WALK_CR_2H : SBasePawn.SA_WALK_ST_2H, false);
            pc.eventChangeState(crouched ? TNAME_s_PlayerWalkingCr2H : CNAME_s_PlayerWalkingSt2H);
            if (pc.Role == ENetRole.ROLE_Authority && victim is { Health: > 0, Controller: SPlayerController vpc })
            {
                vpc.PawnTakeDamage(K.TerminateDamage, pawn, FVector.Zero, new FVector(0f, 0f, 1f), SDamageType.SDTBase);
            }

            pawn.m_PawnToTerminate = null;
        }
    }

    // ---- victim states ------------------------------------------------------------------------------------------

    /// <summary>s_TerminatedPawn: the downed spy being finished; released when the merc lets go (rule 44).</summary>
    [Uc("SBase.SPlayerController.s_TerminatedPawn")]
    public class s_TerminatedPawn : s_Inactive, IMercCantTerminate, IDropAttack, IMyFalling, IPlayAnimDeath
    {
        public static new readonly s_TerminatedPawn Instance = new(UcMeta.s_TerminatedPawn, null);

        protected s_TerminatedPawn(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TerminatedPawn.BeginState", Sha1 = "9059c5d3060308631246eae68ba137a1408aeae9")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatedPawn.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                pc.SetTimer(K.TerminatedCheckPeriod, true);
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatedPawn.Timer", Sha1 = "257de1b7f2c64a952c02acb72dcb7a1116334c6f")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatedPawn.Timer", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { m_PawnToTerminate: null, Health: > 0 })
            {
                pc.eventMirrorState(CNAME_s_TerminatedPawn_Released);
            }
        }

        [Uc("SBase.SPlayerController.s_TerminatedPawn.MercCantTerminate", Sha1 = "64511f589fe566fb69318df0fdbe3c7a41612726")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatedPawn.MercCantTerminate", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public void MercCantTerminate(SPlayerController self)
        {
            if (self.Role == ENetRole.ROLE_Authority && self.SBPawn is { Health: > 0 } pawn)
            {
                pawn.m_PawnToTerminate = null;
                self.eventMirrorState(CNAME_s_TerminatedPawn_Released);
            }
        }

        public void DropAttack(SPlayerController self, SBasePawn PawnToAttack)
        {
        }

        public void MyFalling(SPlayerController self, float LeftTime)
        {
        }

        public void PlayAnimDeath(SPlayerController self, FVector Momentum, int PawnHealth)
        {
        }
    }

    /// <summary>s_TerminatedPawn_Released: the merc let go; the spy lies down for 1 s more (rule 45).</summary>
    [Uc("SBase.SPlayerController.s_TerminatedPawn_Released")]
    public class s_TerminatedPawn_Released : UcActorState
    {
        public static readonly s_TerminatedPawn_Released Instance = new(UcMeta.s_TerminatedPawn_Released, null);

        protected s_TerminatedPawn_Released(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TerminatedPawn_Released.BeginState", Sha1 = "95860d5515d266677272535193afc4446be8dce6")]
        [PortOf("uc:SBase.SPlayerController:s_TerminatedPawn_Released.BeginState", Spec = "spec/combat/knockout-and-terminate.md#the-finishing-move-merc-terminates-a-downed-spy")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IsRemotePlayer)
            {
                return;
            }

            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_TERMINATEPAWN_RELEASE, false);
            }

            pc.fLyingOnGroundDuration = K.TerminateReleasedLyingTime;
            pc.eventChangeState(CNAME_s_LyingOnGround);
        }
    }

    // ---- reviver states --------------------------------------------------------------------------------------

    /// <summary>s_ReanimateFriend_0H: the unarmed reviver (rule 35).</summary>
    [Uc("SBase.SPlayerController.s_ReanimateFriend_0H")]
    public class s_ReanimateFriend_0H : s_PlayerWalkingSt0H, IPlayerMove
    {
        public static new readonly s_ReanimateFriend_0H Instance = new(UcMeta.s_ReanimateFriend_0H, s_PlayerWalkingSt0H.Instance);

        protected s_ReanimateFriend_0H(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ReanimateFriend_0H.BeginState", Sha1 = "2f35668eed5caf3b78557c399732118c4e7126f6")]
        [PortOf("uc:SBase.SPlayerController:s_ReanimateFriend_0H.BeginState", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_REANIMATE, false);
            (pawn.MyWeapon as SWeaponDefense)?.AnimateReanimate();
        }

        [Uc("SBase.SPlayerController.s_ReanimateFriend_0H.AnimEnd", Sha1 = "225bab928218046d373b4fb1aa7088c644c29403")]
        [PortOf("uc:SBase.SPlayerController:s_ReanimateFriend_0H.AnimEnd", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && !pc.IsRemotePlayer)
            {
                pc.GotoWaitingStateCombat();
            }
        }

        [Uc("SBase.SPlayerController.s_ReanimateFriend_0H.PlayerMove", Sha1 = "cfe9d03b0481b7fa8e13017056fb64704edc57f5")]
        [PortOf("uc:SBase.SPlayerController:s_ReanimateFriend_0H.PlayerMove", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.GrabHoldStill(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_ReanimateFriend_0H.CanUseCam", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ReanimateFriend_0H.CanUseCam", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
        public override bool CanUseCam(SPlayerController self) => false;
    }

    /// <summary>s_ReanimateFriend_2H: the armed reviver (rule 35).</summary>
    [Uc("SBase.SPlayerController.s_ReanimateFriend_2H")]
    public class s_ReanimateFriend_2H : s_PlayerWalkingSt2H, IPlayerMove
    {
        public static new readonly s_ReanimateFriend_2H Instance = new(UcMeta.s_ReanimateFriend_2H, s_PlayerWalkingSt2H.Instance);

        protected s_ReanimateFriend_2H(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ReanimateFriend_2H.BeginState", Sha1 = "32e3ee316e5d68e7fda18a441b70694d0e404b10")]
        [PortOf("uc:SBase.SPlayerController:s_ReanimateFriend_2H.BeginState", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_REANIMATE, true);
            }
        }

        [Uc("SBase.SPlayerController.s_ReanimateFriend_2H.AnimEnd", Sha1 = "981cff8fac2378f23eb1a9ffef5b98159b9e33db")]
        [PortOf("uc:SBase.SPlayerController:s_ReanimateFriend_2H.AnimEnd", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && !pc.IsRemotePlayer)
            {
                pc.GotoWaitingStateCombat();
            }
        }

        [Uc("SBase.SPlayerController.s_ReanimateFriend_2H.PlayerMove", Sha1 = "7719d795fc3acfa2353aeda3309f3c39b0a6c413")]
        [PortOf("uc:SBase.SPlayerController:s_ReanimateFriend_2H.PlayerMove", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.GrabHoldStill(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_ReanimateFriend_2H.CanUseCam", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ReanimateFriend_2H.CanUseCam", Spec = "spec/combat/knockout-and-terminate.md#reviving-a-downed-team-mate")]
        public override bool CanUseCam(SPlayerController self) => false;
    }
}
