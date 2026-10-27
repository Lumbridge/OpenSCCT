using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController sticky cam view states: s_UseObject (the base of the object views), the sticky cam menu,
// the smooth entry, the view and the leave. Written from spec/equipment/sticky-cam.md rules 9 to 12 and 26. The damage
// handlers are the combat port's (Combat/CombatDispatch.cs).
//
// The class metadata misses the extends of these states (the game's reflection has them: SmoothToPosition extends
// s_UseObject, the view extends SmoothToPosition, the menu extends s_PlayerWalking, the leave s_PlayerWalkingWeapon),
// so the super states are not passed; the C# classes inherit their functions, and the inherited ignores are empty
// overrides here.
public partial class SPlayerController
{
    /// <summary>The cam view's turn (spec/devices/cam-network.md rule 7) for the object in use.</summary>
    [Uc("SBase.SPlayerController.s_UseObject.UpdateRotation", Sha1 = "a020d3f18122430159981a8b0fbb41e58dcc66d2")]
    [PortOf("uc:SBase.SPlayerController:s_UseObject.UpdateRotation", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
    internal void UseObjectUpdateRotation(float DeltaTime)
    {
        if (SBPawn?.UsedObject is not { } used)
        {
            return;
        }

        var o = used as SOrientableObject;
        int pitch = o?.iPitch ?? 0;
        int yaw = o?.iYaw ?? 0;
        int oldYaw = yaw;
        int oldPitch = pitch;
        float scale = K.CamTurnScale * fSensibiliteSouris * DeltaTime * DesiredFOV / K.CamTurnFovReference;
        if (SvmMath.Abs(aTurn) > K.CamInputDeadZone)
        {
            yaw += SvmMath.Trunc(scale * fTurnValue);
        }

        if (SvmMath.Abs(aLookUp) > K.CamInputDeadZone)
        {
            pitch += SvmMath.Trunc(scale * fLookUpValue);
        }

        pitch &= 65535;
        yaw &= 65535;
        if (o is not null)
        {
            if (o.PitchConstraint.Y != 0f && o.PitchConstraint.Z != 0f)
            {
                pitch = ClampRotation(SvmMath.Trunc(o.PitchConstraint.X), pitch, SvmMath.Trunc(o.PitchConstraint.Y), SvmMath.Trunc(o.PitchConstraint.Z));
            }

            if (o.YawConstraint.Y != 0f && o.YawConstraint.Z != 0f)
            {
                yaw = ClampRotation(SvmMath.Trunc(o.YawConstraint.X), yaw, SvmMath.Trunc(o.YawConstraint.Y), SvmMath.Trunc(o.YawConstraint.Z));
            }

            if (SvmMath.Abs(oldYaw - yaw) > K.CamRotationSoundStep || SvmMath.Abs(oldPitch - pitch) > K.CamRotationSoundStep)
            {
                o.bPlayRotationSound = true;
            }

            Rotation = new FRotator(pitch, yaw, 0);
        }

        UpdateOrientableRotation(o, pitch, yaw);
    }

    /// <summary>The still move of the object views: crouch flag from the stance and a zero-acceleration move.</summary>
    internal void UseObjectStillMove(float DeltaTime)
    {
        bCrouchWithRoll = Pawn is { bIsCrouched: true };
        SendMove(DeltaTime, FVector.Zero);
    }

    /// <summary>Leaves a cam view: back to the previous state on a pipe, a wall or in Vandamme, else the leave state.</summary>
    internal void LeaveStickyCamView(bool bChange)
    {
        if (SBPawn is not { } pawn || pawn.Health <= 0)
        {
            return;
        }

        bool previous = pawn.Physics is EPhysics.PHYS_OnPipeH or EPhysics.PHYS_Vandamme || pawn.BTW != BTW_.NOBTW;
        Name target = previous ? PreviousState : GadgetStates.s_UseObject_StickyCam_Leave;
        if (bChange)
        {
            eventChangeState(target, Name.None, true);
        }
        else
        {
            eventMirrorState(target, Name.None, true);
        }
    }

    /// <summary>s_UseObject: looking through an object (the base of the sticky cam and camera network views).</summary>
    [Uc("SBase.SPlayerController.s_UseObject")]
    public class s_UseObject : UcActorState, IPlayerTick, IPlayerMove, IUpdateRotation, ITopSynchro, IStickyCamWasDestroyed,
        IAltFire, IFire, IPrevStickyCam
    {
        public static readonly s_UseObject Instance = new(UcMeta.s_UseObject, null);

        protected s_UseObject(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The cam is gone: leave the view, then the class version (rule 9).</summary>
        [Uc("SBase.SPlayerController.s_UseObject.StickyCamWasDestroyed", Sha1 = "513046a5bb8fe1bf001027ae8c1243a51c233620")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject.StickyCamWasDestroyed", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public virtual void StickyCamWasDestroyed(SPlayerController self, SPlayerController? Instigator, int NbGasCharge)
        {
            self.LeaveStickyCamView(false);
            self.StickyCamWasDestroyed(Instigator, NbGasCharge);
        }

        [Uc("SBase.SPlayerController.s_UseObject.PlayerTick", Sha1 = "cccbb22c64f68e925e5df4eb7bfc294c6e149287")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject.PlayerTick", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public virtual void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.bUsingNow = false;
            pc.PlayerTick(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_UseObject.BeginState", Sha1 = "136bf54c12ecabd67577ff375e5e1ccb17da1e65")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject.BeginState", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void BeginState(Actor self) => GadgetHud.SetHudState((SPlayerController)self, GadgetStates.s_Hud_UseObject);

        [Uc("SBase.SPlayerController.s_UseObject.TopSynchro", Sha1 = "2cbcce8616a2a2afe3ed65af052db5b337b0ba25")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject.TopSynchro", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient() && self.Pawn is { Physics: EPhysics.PHYS_Repositionnal })
            {
                self.SBPawn?.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H);
            }
        }

        /// <summary>Turning, the Use key leaves, a still move.</summary>
        [Uc("SBase.SPlayerController.s_UseObject.PlayerMove", Sha1 = "b7c006d8686443a52b33bd7bab70aa2feda45d65")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject.PlayerMove", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 0f);
            if (self.bUsing != 0)
            {
                self.GotoWaitingState();
            }

            self.UseObjectStillMove(DeltaTime);
        }

        public virtual void UpdateRotation(SPlayerController self, float DeltaTime, float maxPitch) => self.UseObjectUpdateRotation(DeltaTime);

        /// <summary>The view ends: HUD back, no used object, the interaction post, back to waiting.</summary>
        [Uc("SBase.SPlayerController.s_UseObject.EndState", Sha1 = "23e76df8cba1d12c2b6c0e3978484ba907ed3a9b")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject.EndState", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            GadgetHud.SetHudState(pc, Name.None);
            if (pc.SBPawn is { } pawn)
            {
                pawn.UsedObject = null;
            }

            pc.UsedInterraction = null;
            pc.AmbientSound = AssetRef.None;
            if (pc.IManager is { } manager)
            {
                pc.InteractionPost(manager.GetCurrentInteraction(), manager.SelectedSubInteractions);
            }

            pc.GotoWaitingState();
        }

        public virtual void AltFire(SPlayerController self, float F)
        {
        }

        public virtual void Fire(SPlayerController self, float F)
        {
        }

        public virtual void PrevStickyCam(SPlayerController self)
        {
        }
    }

    /// <summary>The sticky cam menu (rule 26): new cam, previous cam, exit. Nothing in 4.0 enters it.</summary>
    [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction")]
    public class s_StickyCam_MenuInteraction : s_PlayerWalking, IPlayerMove, IWheel, ICanBeGrabbed, IClientDamage, IAltFire, IFire,
        IGadgetQuickChoice, IGadLaserSwitch, IGadTorcheSwitch, IPrevStickyCam
    {
        public static new readonly s_StickyCam_MenuInteraction Instance = new(UcMeta.s_StickyCam_MenuInteraction, null);

        protected s_StickyCam_MenuInteraction(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        private static SGadStickyCam? Gadget(SPlayerController self) => self.SBPawn?.SelectedGadget as SGadStickyCam;

        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.WheelUp", Sha1 = "0d5f4c0a96619d4919db9e20f0c7dda25c50bf65")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.WheelUp", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public void WheelUp(SPlayerController self)
        {
            if (Gadget(self) is not { } g)
            {
                return;
            }

            g.iCurrentSelectedMenu--;
            if (g.iCurrentSelectedMenu == 1 && self.SBPawn?.UsedObject is null)
            {
                g.iCurrentSelectedMenu--;
            }

            if (g.iCurrentSelectedMenu < 0)
            {
                g.iCurrentSelectedMenu = K.StickyMenuItems - 1;
            }
        }

        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.WheelDown", Sha1 = "7b9c250d30574286c314f549b65643d175685383")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.WheelDown", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public void WheelDown(SPlayerController self)
        {
            if (Gadget(self) is not { } g)
            {
                return;
            }

            g.iCurrentSelectedMenu++;
            if (g.iCurrentSelectedMenu == 1 && self.SBPawn?.UsedObject is null)
            {
                g.iCurrentSelectedMenu++;
            }

            if (g.iCurrentSelectedMenu >= K.StickyMenuItems)
            {
                g.iCurrentSelectedMenu = 0;
            }
        }

        /// <summary>When Alt-fire is released the selected item runs: new cam, previous cam or exit.</summary>
        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.PlayerTick", Sha1 = "4ad665ef3986b35e1198780b28730709635b0e2b")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.PlayerTick", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.bUsingNow = false;
            if (Gadget(pc) is not { } g)
            {
                pc.GotoWaitingState();
                return;
            }

            base.PlayerTick(self, DeltaTime);
            if (!ReferenceEquals(pc.StateNode, this))
            {
                return;
            }

            if (g.iCurrentSelectedMenu == 1 && pc.SBPawn?.UsedObject is null)
            {
                g.iCurrentSelectedMenu = 0;
            }

            if (pc.bAltFire != 0)
            {
                return;
            }

            switch (g.iCurrentSelectedMenu)
            {
                case 0:
                    g.iNbSmokeChargeForCurrentSticky = -1;
                    if (g.iCanUse[0] == 0)
                    {
                        GadgetHud.AddMessageToAuxiliaryStack(pc, pc.TextCantSpawnStickyCam, K.StickyMessageTime);
                        pc.GotoWaitingState();
                    }
                    else
                    {
                        g.UseStickyCam(false);
                    }

                    break;
                case 1:
                    if (g.iCanUse[1] == 0)
                    {
                        GadgetHud.AddMessageToAuxiliaryStack(pc, pc.TextNoStickyCamAvailable, K.StickyMessageTime);
                    }
                    else
                    {
                        pc.UsePreviousStickyCam(pc.SBPawn?.UsedObject);
                    }

                    break;
                default:
                    pc.GotoWaitingState();
                    break;
            }

            g.iCurrentSelectedMenu = 0;
        }

        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.CanIUseBinocular", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public override bool CanIUseBinocular(SPlayerController self) => false;

        /// <summary>The button edges reset, grabs allowed; on the input machine whether each item can be used.</summary>
        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.BeginState", Sha1 = "58480ea0af0658b36b45d49e98f8234adb4fb2a7")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.BeginState", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.Previous_BT_Up = 0;
            pc.Previous_BT_Down = 0;
            pc.LastUpdateButton = pc.Level.TimeSeconds;
            if (pc.SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = true;
            }

            if (pc.IAmOnClient() && Gadget(pc) is { } g)
            {
                g.iCanUse[1] = pc.SBPawn?.UsedObject is null ? 0 : 1;
                g.iCanUse[0] = g.UseStickyCam(true) ? 1 : 0;
            }
        }

        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.EndState", Sha1 = "46e7f81c391ca024e87f7a0192018b0da9fc235e")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.EndState", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public override void EndState(Actor self)
        {
            base.EndState(self);
            if (((SPlayerController)self).SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = false;
            }
        }

        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.CanBeGrabbed", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public bool CanBeGrabbed(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.ClientDamage", Sha1 = "24d42c44ca05bb5525a92b328c7537ee4645a2d8")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.ClientDamage", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy) =>
            self.GotoWaitingState();

        /// <summary>No jump; a still move; the up and down keys step the menu on their press edge.</summary>
        [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.PlayerMove", Sha1 = "82315dec0ede1c2d607309b937f9b82f7cc670b6")]
        [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.PlayerMove", Spec = "spec/equipment/sticky-cam.md#menu-previous-cam-refills")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.SendMove(DeltaTime, FVector.Zero);
            if (self.BT_Up > 0 && self.Previous_BT_Up == 0)
            {
                WheelUp(self);
            }
            else if (self.BT_Down > 0 && self.Previous_BT_Down == 0)
            {
                WheelDown(self);
            }

            self.Previous_BT_Up = self.BT_Up;
            self.Previous_BT_Down = self.BT_Down;
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        public void Fire(SPlayerController self, float F)
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

        public void PrevStickyCam(SPlayerController self)
        {
        }
    }

    /// <summary>The short entry of a cam view (rule 10): the cam faces the pawn, a held Use queues the gas.</summary>
    [Uc("SBase.SPlayerController.s_UseObject_StickyCam_SmoothToPosition")]
    public class s_UseObject_StickyCam_SmoothToPosition : s_UseObject
    {
        public static new readonly s_UseObject_StickyCam_SmoothToPosition Instance = new(UcMeta.s_UseObject_StickyCam_SmoothToPosition, null);

        protected s_UseObject_StickyCam_SmoothToPosition(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_StickyCam_SmoothToPosition.PrevStickyCam", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.PrevStickyCam", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void PrevStickyCam(SPlayerController self)
        {
        }

        /// <summary>The cam is gone: leave, clean the view, then the base version.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_StickyCam_SmoothToPosition.StickyCamWasDestroyed", Sha1 = "d9326d135af711f7a893a89f46e89333c15e590e")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.StickyCamWasDestroyed", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void StickyCamWasDestroyed(SPlayerController self, SPlayerController? Instigator, int NbGasCharge)
        {
            self.LeaveStickyCamView(false);
            self.CleanStickyCamOnClient();
            base.StickyCamWasDestroyed(self, Instigator, NbGasCharge);
        }

        /// <summary>FOV 110, the palm clip on the input machine, the cam's FOV, the sticky cam HUD.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_StickyCam_SmoothToPosition.BeginState", Sha1 = "999be3019eb1e0b6622e01ff3045548e50b5459b")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.BeginState", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.ScopeSetFOV(K.CamEntryFov);
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(pawn.Physics == EPhysics.PHYS_Walking ? SBasePawn.SA_PALM_USING : SBasePawn.SA_NO_IK);
            }

            if (pc.SBPawn?.UsedObject is SStickyCam cam)
            {
                cam.fVisionFOV = pc.DesiredFOV;
            }

            GadgetHud.SetHudState(pc, GadgetStates.s_Hud_UseObject_StickyCam);
        }

        /// <summary>The cam turns to face the pawn; a held Use queues the gas; a still move.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_StickyCam_SmoothToPosition.PlayerMove", Sha1 = "410e633da7964072ff77953bfcc161033196d1dc")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.PlayerMove", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 0f);
            self.bCrouchWithRoll = self.PreviousState == GadgetStates.s_PipH_Fire4;
            if (self.Pawn is { } pawn && self.SBPawn?.UsedObject is { } used)
            {
                FRotator face = (pawn.Location - used.Location).Rotation();
                self.UpdateOrientableRotation(used as SOrientableObject, face.Pitch, face.Yaw);
            }

            if (self.bUsing > 0)
            {
                self.bStickyCamGasQueued = true;
            }

            self.SendMove(DeltaTime, FVector.Zero);
        }

        [Uc("SBase.SPlayerController.s_UseObject_StickyCam_SmoothToPosition.EndState", Sha1 = "79046b160cd4e87666e3fd668e212522b701ea80")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.EndState", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void EndState(Actor self) => ((SPlayerController)self).AmbientSound = AssetRef.None;
    }

    /// <summary>The sticky cam view (rule 11): zoom, gas, turn, Alt-fire leaves.</summary>
    [Uc("SBase.SPlayerController.s_UseObject_StickyCam")]
    public class s_UseObject_StickyCam : s_UseObject_StickyCam_SmoothToPosition, ICanBeDropAttacked, ICanBeElbowShocked, IWheel
    {
        public static new readonly s_UseObject_StickyCam Instance = new(UcMeta.s_UseObject_StickyCam, null);

        protected s_UseObject_StickyCam(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_StickyCam.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.CanBeDropAttacked", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_UseObject_StickyCam.CanBeElbowShocked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.CanBeElbowShocked", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public bool CanBeElbowShocked(SPlayerController self) => true;

        /// <summary>The end of a 2-hand landing clip on channel 0 brings back the 2-hand walk without IK.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_StickyCam.AnimEnd", Sha1 = "343112c8bbcc20b43a9dd2279d0ede9c74f89fae")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.AnimEnd", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn && IsLanding2H(pawn))
            {
                pawn.ChangeEnumAction(pawn.bIsCrouched ? SBasePawn.SA_WALK_CR_2H : SBasePawn.SA_WALK_ST_2H, true);
                pawn.ChangeEnumAction(SBasePawn.SA_NO_IK);
            }

            base.AnimEnd(self, Channel);
        }

        private static bool IsLanding2H(SBasePawn pawn)
        {
            Name a = pawn.PawnAnim[0].Sequence;
            return a == pawn.ALandHi2H || a == pawn.ALandQuiet2H || a == pawn.ALandLow2H;
        }

        /// <summary>Alt-fire leaves the view, unless a 2-hand landing clip plays.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_StickyCam.AltFire", Sha1 = "3f35cce3bf0637174184d4d2828b36b993eff168")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.AltFire", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void AltFire(SPlayerController self, float F)
        {
            if (self.SBPawn is not { } pawn || IsLanding2H(pawn))
            {
                return;
            }

            bool previous = pawn.Physics != EPhysics.PHYS_Walking || pawn.BTW != BTW_.NOBTW || pawn.Physics == EPhysics.PHYS_Vandamme;
            self.eventChangeState(previous ? self.PreviousState : GadgetStates.s_UseObject_StickyCam_Leave, Name.None, true);
        }

        [Uc("SBase.SPlayerController.s_UseObject_StickyCam.WheelUp", Sha1 = "c70fb0a249792e19ebf9e1259f7e49cf08a7a111")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.WheelUp", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public virtual void WheelUp(SPlayerController self) => self.StickyCamZoom(-K.CamZoomWheelRate * self.Level.m_dT, false);

        [Uc("SBase.SPlayerController.s_UseObject_StickyCam.WheelDown", Sha1 = "d0ae8c20aeaabc468c87745f6907c920de85edbb")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.WheelDown", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public virtual void WheelDown(SPlayerController self) => self.StickyCamZoom(K.CamZoomWheelRate * self.Level.m_dT, false);

        /// <summary>No fire; the zoom keys; the gas on a queued or new Use; turning; the cam's ambient sound; a still move.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_StickyCam.PlayerMove", Sha1 = "2ee4c343f08e1051bfc031b25fd3f068b9095bc3")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.PlayerMove", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bFire = 0;
            if (self.bZoomIn > 0)
            {
                self.StickyCamZoom(-K.CamZoomKeyRate * self.Level.m_dT, false);
            }
            else if (self.bZoomOut > 0)
            {
                self.StickyCamZoom(K.CamZoomKeyRate * self.Level.m_dT, false);
            }

            if (self.bStickyCamGasQueued || (self.bUsing > 0 && !self.bStickyCamGasHeld))
            {
                self.bStickyCamGasQueued = false;
                self.UseStickyCamGas();
            }

            self.bStickyCamGasHeld = self.bUsing > 0;
            self.eventUpdateRotation(DeltaTime, 0f);
            if (self.SBPawn?.UsedObject is SOrientableObject o)
            {
                self.AmbientSound = o.AmbientSound;
            }

            self.SendMove(DeltaTime, FVector.Zero);
        }

        /// <summary>Ambient off, stance flags, no jump, HUD back, the view levelled, FOV reset, the palm clip ended.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_StickyCam.EndState", Sha1 = "f35e55bf37db8419e82e76ac2d886f77bc215c2f")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.EndState", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.AmbientSound = AssetRef.None;
            pc.LeaveCamView();
        }
    }

    /// <summary>
    /// The zoom of the used sticky cam (or camera): its FOV moves by <paramref name="Delta"/> x fCoeffZoomSpeed within
    /// its limits and becomes the view FOV; <paramref name="bSendToServer"/> for a network camera.
    /// </summary>
    internal void StickyCamZoom(float Delta, bool bSendToServer)
    {
        if (SBPawn?.UsedObject is not SStickyCam cam)
        {
            return;
        }

        cam.fVisionFOV = SvmMath.Clamp(cam.fVisionFOV + Delta * cam.fCoeffZoomSpeed, cam.fMinFOV, cam.fMaxFOV);
        ScopeSetFOV(cam.fVisionFOV);
        if (bSendToServer)
        {
            CamNetworkSetFovOnServer(cam, cam.fVisionFOV);
        }
    }

    /// <summary>Leaving a cam view (rule 12): the palm clip's end holsters a drawn pistol or goes back to waiting.</summary>
    [Uc("SBase.SPlayerController.s_UseObject_StickyCam_Leave")]
    public class s_UseObject_StickyCam_Leave : s_PlayerWalkingWeapon, IAltFire, IFire, IPrevStickyCam
    {
        public static new readonly s_UseObject_StickyCam_Leave Instance = new(UcMeta.s_UseObject_StickyCam_Leave, null);

        protected s_UseObject_StickyCam_Leave(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_UseObject_StickyCam_Leave.PlayerTick", Sha1 = "a01503b5fc8d08c3ed8bfc1ae69f796d41eb6a0c")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_Leave.PlayerTick", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void PlayerTick(PlayerController self, float DeltaTime) => ((SPlayerController)self).PlayerTick(DeltaTime);

        /// <summary>On the input machine: a spy with the pistol out holsters, anyone else goes back to waiting.</summary>
        [Uc("SBase.SPlayerController.s_UseObject_StickyCam_Leave.AnimEnd", Sha1 = "0fb3fd721945f283da05a354cea21028fa34f4e7")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_Leave.AnimEnd", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.AmbientSound = AssetRef.None;
            if (pawn.SPawnType == PawnType.SPAWNOFFENSE && pawn.WeaponLoaded == 2)
            {
                pawn.ChangeEnumAction(pawn.bIsCrouched ? SBasePawn.SA_WALK_CR_2Hto0H : SBasePawn.SA_WALK_ST_2Hto0H);
                pc.eventChangeState(GadgetStates.s_PlayerWalkingMario_T_2HToOH);
            }
            else
            {
                pc.GotoWaitingState();
            }
        }

        [Uc("SBase.SPlayerController.s_UseObject_StickyCam_Leave.PlayerMove", Sha1 = "b0cafdd4aa4f085c9883e6cdafbffc3d7a03a2f8")]
        [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_Leave.PlayerMove", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 0f);
            self.UseObjectStillMove(DeltaTime);
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        public void Fire(SPlayerController self, float F)
        {
        }

        public void PrevStickyCam(SPlayerController self)
        {
        }
    }
}
