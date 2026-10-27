using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Objectives.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController Use menu states: the base interact state, the menu, and the press-button states, with the
// three reliable client-to-server interaction calls. Written from spec/objectives/interaction-system.md (rules 10 to
// 20); the objective rules they call are in Objectives/InteractionMenuRules.cs.
public partial class SPlayerController
{
    private static readonly Name INAME_s_InteractWithObjectWaitingEvent = new("s_InteractWithObjectWaitingEvent");
    private static readonly Name INAME_s_InteractWithObjectWaitingEventCrToSt = new("s_InteractWithObjectWaitingEventCrToSt");
    private static readonly Name INAME_s_Interacting = new("s_Interacting");

    // ---- the reliable client-to-server calls (rule 17) -----------------------------------------------------------

    /// <summary>InteractionInit(Obj, Index): on a client it goes to the server; there the object decides.</summary>
    [Uc("SBase.SPlayerController.InteractionInit", Sha1 = "dfb163e2813cc884d84449c5ff7e75c261c2e1ba")]
    [PortOf("rep:SBase.SPlayerController.InteractionInit", Spec = "spec/objectives/interaction-system.md#network")]
    public void InteractionInit(SInteractObject? Obj, int Index) => InteractionMenuRules.InteractionInit(this, Obj, Index);

    /// <summary>Interaction(Obj, Index): on a client it goes to the server.</summary>
    [Uc("SBase.SPlayerController.Interaction", Sha1 = "c68a94b766257c79fcfd1e115e1cb20ee9643210")]
    [PortOf("rep:SBase.SPlayerController.Interaction", Spec = "spec/objectives/interaction-system.md#network")]
    public void Interaction(SInteractObject? Obj, int Index) => InteractionMenuRules.Interaction(this, Obj, Index);

    /// <summary>InteractionPost(Obj, Index): on a client it goes to the server.</summary>
    [Uc("SBase.SPlayerController.InteractionPost", Sha1 = "99d1f0eb7238317b493200d0c9519e97d25dfffb")]
    [PortOf("rep:SBase.SPlayerController.InteractionPost", Spec = "spec/objectives/interaction-system.md#network")]
    public void InteractionPost(SInteractObject? Obj, int Index) => InteractionMenuRules.InteractionPost(this, Obj, Index);

    /// <summary>The menu entry the manager has selected, or null.</summary>
    internal SInteractObject? SelectedInteraction => IManager?.GetCurrentInteraction();

    /// <summary>The use states' move: no jump, the crouch wish kept in the air, the camera turns, no acceleration.</summary>
    internal void InteractionStillMove(float DeltaTime)
    {
        bPressedJump = false;
        ZoneHorsCrouch();
        UpdateCamRotation(DeltaTime);
        SendMove(DeltaTime, FVector.Zero);
    }

    // ---- s_InteractWithObject (rule 15, 16) ------------------------------------------------------------------------

    /// <summary>The base of the menu states: the pawn can be grabbed, drop-attacked and elbow-shocked.</summary>
    [Uc("SBase.SPlayerController.s_InteractWithObject")]
    public class s_InteractWithObject : UcActorState, ICanActivateLaser, CombatStateEvents.IDoInteraction, ICanBeDropAttacked,
        ICanBeGrabbed, ICanBeElbowShocked
    {
        public static readonly s_InteractWithObject Instance = new(UcMeta.s_InteractWithObject, null);

        protected s_InteractWithObject(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_InteractWithObject.BeginState", Sha1 = "7454da160008c81a3bb72a93bd6a016a1b37940a")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObject.BeginState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void BeginState(Actor self) => InteractBegin((SPlayerController)self);

        [Uc("SBase.SPlayerController.s_InteractWithObject.EndState", Sha1 = "ac5103434a355291b15c395ddc07a5602ae7dc72")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObject.EndState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void EndState(Actor self) => InteractEnd((SPlayerController)self);

        [Uc("SBase.SPlayerController.s_InteractWithObject.CanActivateLaser", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObject.CanActivateLaser", Spec = "spec/objectives/interaction-system.md#rules")]
        public bool CanActivateLaser(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_InteractWithObject.DoInteraction", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObject.DoInteraction", Spec = "spec/objectives/interaction-system.md#rules")]
        public bool DoInteraction(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_InteractWithObject.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObject.CanBeDropAttacked", Spec = "spec/objectives/interaction-system.md#rules")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_InteractWithObject.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObject.CanBeGrabbed", Spec = "spec/objectives/interaction-system.md#rules")]
        public bool CanBeGrabbed(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_InteractWithObject.CanBeElbowShocked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObject.CanBeElbowShocked", Spec = "spec/objectives/interaction-system.md#rules")]
        public bool CanBeElbowShocked(SPlayerController self) => true;
    }

    private static void InteractBegin(SPlayerController pc)
    {
        if (pc.SBPawn is { } pawn)
        {
            pawn.bCanBeGrabbed = true;
        }
    }

    private static void InteractEnd(SPlayerController pc)
    {
        pc.LastUpdateButton = pc.Level.TimeSeconds;
        if (pc.SBPawn is { } pawn)
        {
            pawn.bCanBeGrabbed = false;
        }
    }

    // ---- s_MenuInteraction (rules 12 to 15) ------------------------------------------------------------------------

    /// <summary>The Use menu: the selection moves with the up and down keys or the wheel, releasing Use acts.</summary>
    [Uc("SBase.SPlayerController.s_MenuInteraction")]
    public class s_MenuInteraction : s_InteractWithObject, ICanInteract, IWheel, IPlayerMove
    {
        public static new readonly s_MenuInteraction Instance = new(UcMeta.s_MenuInteraction, s_InteractWithObject.Instance);

        protected s_MenuInteraction(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_MenuInteraction.BeginState", Sha1 = "fbc03ce0cba4c5ed911195b86a67cdac068da460")]
        [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.BeginState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.Previous_BT_Up = 0;
            pc.Previous_BT_Down = 0;
            InteractionMenuRules.MenuBeginState(pc);
            pc.LastUpdateButton = pc.Level.TimeSeconds;
            base.BeginState(self);
        }

        [Uc("SBase.SPlayerController.s_MenuInteraction.EndState", Sha1 = "94256759fcb9bddf2d2eb7f429cf30cdb49711f0")]
        [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.EndState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { bStickyShocking: true } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_DMG_ELECTRIC, true);
            }

            pc.bCanCheckUsingButton = true;
            base.EndState(self);
        }

        [Uc("SBase.SPlayerController.s_MenuInteraction.CanInteract", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.CanInteract", Spec = "spec/objectives/interaction-system.md#rules")]
        public bool CanInteract(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_MenuInteraction.WheelUp", Sha1 = "f9924db23ec106de49f7ad47750ff8d06f4aca55")]
        [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.WheelUp", Spec = "spec/objectives/interaction-system.md#rules")]
        public void WheelUp(SPlayerController self) => self.IManager?.SelectPreviousItem();

        [Uc("SBase.SPlayerController.s_MenuInteraction.WheelDown", Sha1 = "9626099daf81efddfdb265b97956b5022b312e2b")]
        [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.WheelDown", Spec = "spec/objectives/interaction-system.md#rules")]
        public void WheelDown(SPlayerController self) => self.IManager?.SelectNextItem();

        /// <summary>
        /// Releasing Use acts on the selection (Exit, Give, a button press or InteractionInit to the server); the
        /// pawn stands still; a new press of up or down moves the selection.
        /// </summary>
        [Uc("SBase.SPlayerController.s_MenuInteraction.PlayerMove", Sha1 = "3a9193e29e3c3aea762145118b0c882a7fa34e1b")]
        [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.PlayerMove", Spec = "spec/objectives/interaction-system.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (InteractionMenuRules.MenuRelease(self) == EMenuRelease.PressButton)
            {
                bool trigger = self.SelectedInteraction is STriggerInteraction;
                self.eventChangeState(self.Pawn is { bIsCrouched: true } && trigger
                    ? INAME_s_InteractWithObjectWaitingEventCrToSt
                    : INAME_s_InteractWithObjectWaitingEvent, Name.None, true);
            }

            self.SendMove(DeltaTime, FVector.Zero);
            if (self.BT_Up > 0 && self.Previous_BT_Up == 0)
            {
                self.eventWheelUp();
            }
            else if (self.BT_Down > 0 && self.Previous_BT_Down == 0)
            {
                self.eventWheelDown();
            }

            self.Previous_BT_Up = self.BT_Up;
            self.Previous_BT_Down = self.BT_Down;
        }
    }

    // ---- press-button entries (rules 19, 20) -----------------------------------------------------------------------

    /// <summary>
    /// The press-button animation for a plain switch or a wall gadget: the player faces the entry; the interact
    /// channel's end tells the server to use it. The class metadata gives it no super state; the source extends
    /// s_InteractWithObject, whose functions it repeats.
    /// </summary>
    [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEvent")]
    public class s_InteractWithObjectWaitingEvent : UcActorState, IPlayerMove, ICanBeDropAttacked, ICanBeGrabbed, ICanBeElbowShocked,
        CombatStateEvents.IDoInteraction
    {
        public static readonly s_InteractWithObjectWaitingEvent Instance = new(UcMeta.s_InteractWithObjectWaitingEvent, null);

        protected s_InteractWithObjectWaitingEvent(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Provenance(ProvenanceKind.Decoded, "s_InteractWithObjectWaitingEvent.PlayerMove: DampRotation yaw 30000, pitch 100000")]
        private const float FaceYawRate = 30000f;

        [Provenance(ProvenanceKind.Decoded, "s_InteractWithObjectWaitingEvent.PlayerMove: DampRotation yaw 30000, pitch 100000")]
        private const float FacePitchRate = 100000f;

        [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEvent.BeginState", Sha1 = "4b000d1d0ddfe767045bca132adfa10096404fb2")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObjectWaitingEvent.BeginState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_HANDINTERACT, true);
            if (pc.SBPawn?.MyWeapon is { } weapon && pc.SPawnType == PawnType.SPAWNDEFENSE)
            {
                weapon.GotoState(INAME_s_Interacting);
            }

            InteractBegin(pc);
        }

        [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEvent.EndState", Sha1 = "8312f2e4cbb8498910b8bd5b1719bdb835289c09")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObjectWaitingEvent.EndState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Pawn is { bIsCrouched: true })
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            }

            InteractEnd(pc);
        }

        /// <summary>Faces the entry (yaw and pitch damped), stands still.</summary>
        [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEvent.PlayerMove", Sha1 = "29cb8844814e7dd9f5ff2d95f37e041a2a80dd55")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObjectWaitingEvent.PlayerMove", Spec = "spec/objectives/interaction-system.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is not { } pawn)
            {
                return;
            }

            FVector at = self.SelectedInteraction?.Location ?? pawn.Location + pawn.Rotation.Vector();
            FRotator rot = (at - pawn.Location - new FVector(0f, 0f, pawn.EyeHeight)).Rotation();
            rot.Yaw = DampRotation(pawn.Rotation.Yaw, rot.Yaw, FaceYawRate, DeltaTime);
            rot.Pitch = DampRotation(self.Rotation.Pitch, rot.Pitch, FacePitchRate, DeltaTime);
            self.MyDesiredRotation = rot;
            if ((self.SBPawn?.WeaponLoaded ?? 0) > 0)
            {
                self.Rotation = rot;
                if (self.SPawnType == PawnType.SPAWNDEFENSE)
                {
                    self.MyDesiredRotation = self.Rotation;
                    self.UpdateDesiredRotation(self.MyDesiredRotation);
                    self.LocalMyDesiredRotation = self.MyDesiredRotation;
                }
            }
            else
            {
                self.eventUpdateRotation(DeltaTime, 1f);
            }

            pawn.Rotation = new FRotator(0, rot.Yaw, 0);
            self.SendMove(DeltaTime, FVector.Zero);
        }

        /// <summary>The press animation ended on the interact channel: the server uses the entry, the player walks on.</summary>
        [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEvent.AnimEnd", Sha1 = "0f27e380c87135472794dfd551eb1eb29a2cd5df")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObjectWaitingEvent.AnimEnd", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && Channel == SBasePawn.INTERACTCHANNEL)
            {
                InteractionMenuRules.PressButtonAnimEnd(pc);
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_HANDINTERACT_BACK, true);
                pc.GotoWaitingState();
            }
            else
            {
                pc.AnimEnd(Channel);
            }
        }

        public bool CanBeDropAttacked(SPlayerController self) => true;

        public bool CanBeGrabbed(SPlayerController self) => true;

        public bool CanBeElbowShocked(SPlayerController self) => true;

        public bool DoInteraction(SPlayerController self) => false;
    }

    /// <summary>A crouched player stands up first, then presses.</summary>
    [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEventCrToSt")]
    public class s_InteractWithObjectWaitingEventCrToSt : s_InteractWithObjectWaitingEvent
    {
        public static new readonly s_InteractWithObjectWaitingEventCrToSt Instance =
            new(UcMeta.s_InteractWithObjectWaitingEventCrToSt, s_InteractWithObjectWaitingEvent.Instance);

        protected s_InteractWithObjectWaitingEventCrToSt(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEventCrToSt.BeginState", Sha1 = "4111c69e6f4b8dd0a3c84d45b89982bdd99fa783")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObjectWaitingEventCrToSt.BeginState", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
            pawn.ChangeEnumAction(pawn.WeaponLoaded == 0 ? SBasePawn.SA_WAIT_CR2ST_0H : SBasePawn.SA_WAIT_CR2ST_2H);
            pawn.bCanBeGrabbed = false;
        }

        [Uc("SBase.SPlayerController.s_InteractWithObjectWaitingEventCrToSt.AnimEnd", Sha1 = "9e37283216365d6f547c26add663a3cdccc15cb9")]
        [PortOf("uc:SBase.SPlayerController:s_InteractWithObjectWaitingEventCrToSt.AnimEnd", Spec = "spec/objectives/interaction-system.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && Channel == 0)
            {
                pc.eventChangeState(INAME_s_InteractWithObjectWaitingEvent, Name.None, true);
            }

            pc.AnimEnd(Channel);
        }
    }
}
