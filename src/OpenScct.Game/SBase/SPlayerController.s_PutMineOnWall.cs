using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController mine placement states (spec/equipment/mine-launcher.md rules 3 to 5, 8 and 11): the reach to
// the wall, the menu, the placement and the withdrawal. The class metadata misses the extends of s_BasePutMineOnWall,
// _Grab, _InteractionMenu and s_PutMineOnWall (the game's reflection has them), so only the withdrawal passes its super
// state; the others inherit the base's functions through C#, and the base's ignores are empty overrides.
public partial class SPlayerController
{
    private static readonly AssetRef MineEmptySound = new("Armes_impacts.Armes.Mine_vide");

    /// <summary>The selected mine launcher, or null.</summary>
    private SGadMultiMinesLauncher? SelectedMineLauncher => SBPawn?.SelectedGadget as SGadMultiMinesLauncher;

    /// <summary>The merc's rifle follows the mine clips (its s_DropMine* states).</summary>
    private void MineRifleState(Name State)
    {
        if (SBPawn is { SPawnType: PawnType.SPAWNDEFENSE, MyWeapon: { } weapon } && weapon.GetStateTable().Find(State) is not null)
        {
            weapon.GotoState(State);
        }
    }

    /// <summary>The mine-on-wall base: no binoculars, grabs allowed, a still move, damage aborts.</summary>
    [Uc("SBase.SPlayerController.s_BasePutMineOnWall")]
    public class s_BasePutMineOnWall : s_PlayerWalking, IPlayerMove, ICanBeGrabbed, IClientDamage, IAltFire, IFire, IGadgetQuickChoice
    {
        public static new readonly s_BasePutMineOnWall Instance = new(UcMeta.s_BasePutMineOnWall, null);

        protected s_BasePutMineOnWall(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BasePutMineOnWall.PlayerTick", Sha1 = "d94129ec90eac8f93c0a66a5184086b38b8326a2")]
        [PortOf("uc:SBase.SPlayerController:s_BasePutMineOnWall.PlayerTick", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            ((SPlayerController)self).bUsingNow = false;
            base.PlayerTick(self, DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_BasePutMineOnWall.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_BasePutMineOnWall.CanIUseBinocular", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override bool CanIUseBinocular(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_BasePutMineOnWall.BeginState", Sha1 = "0cdf5e2f2df53ade597322c98eea739ea14fb1d2")]
        [PortOf("uc:SBase.SPlayerController:s_BasePutMineOnWall.BeginState", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            if (((SPlayerController)self).SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = true;
            }
        }

        [Uc("SBase.SPlayerController.s_BasePutMineOnWall.EndState", Sha1 = "46e7f81c391ca024e87f7a0192018b0da9fc235e")]
        [PortOf("uc:SBase.SPlayerController:s_BasePutMineOnWall.EndState", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override void EndState(Actor self)
        {
            base.EndState(self);
            if (((SPlayerController)self).SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = false;
            }
        }

        [Uc("SBase.SPlayerController.s_BasePutMineOnWall.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_BasePutMineOnWall.CanBeGrabbed", Spec = "spec/equipment/mine-launcher.md#placing")]
        public bool CanBeGrabbed(SPlayerController self) => true;

        /// <summary>Damage aborts: the ungrab clip, the rifle back, waiting.</summary>
        [Uc("SBase.SPlayerController.s_BasePutMineOnWall.ClientDamage", Sha1 = "d008c4ac1b16d2c720e635ef4f75bf3b403d167a")]
        [PortOf("uc:SBase.SPlayerController:s_BasePutMineOnWall.ClientDamage", Spec = "spec/equipment/mine-launcher.md#placing")]
        public void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
        {
            self.SBPawn?.ChangeEnumAction(SBasePawn.SA_DROPMINE_UNGRAB);
            self.MineRifleState(GadgetStates.s_DropMine_UnGrab);
            self.GotoWaitingState();
        }

        [Uc("SBase.SPlayerController.s_BasePutMineOnWall.PlayerMove", Sha1 = "21be423b473eaad677d98d8efe8e29a258394201")]
        [PortOf("uc:SBase.SPlayerController:s_BasePutMineOnWall.PlayerMove", Spec = "spec/equipment/mine-launcher.md#placing")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.SendMove(DeltaTime, FVector.Zero);
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
    }

    /// <summary>Reaching to the wall (rule 4): at the clip's end the menu (Alt-fire held or no charge) or the placement.</summary>
    [Uc("SBase.SPlayerController.s_PutMineOnWall_Grab")]
    public class s_PutMineOnWall_Grab : s_BasePutMineOnWall
    {
        public static new readonly s_PutMineOnWall_Grab Instance = new(UcMeta.s_PutMineOnWall_Grab, null);

        protected s_PutMineOnWall_Grab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall_Grab.BeginState", Sha1 = "2fad15a16362d96ae38cf1b3a20153a9c3ffe7bd")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_Grab.BeginState", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.MineRifleState(GadgetStates.s_DropMine_Grab);
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_DROPMINE_GRAB, true);
            }

            pc.TimeChoiceGadget = pc.Level.TimeSeconds + K.MineGrabChoiceDelay;
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall_Grab.AnimEnd", Sha1 = "839fe850d25c50ffd1da13022e1a9a070b1dc85e")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_Grab.AnimEnd", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || Channel != SBasePawn.INTERACTCHANNEL)
            {
                base.AnimEnd(self, Channel);
                return;
            }

            SGadget? gadget = pc.SBPawn?.SelectedGadget;
            bool empty = gadget is { Charge: <= 0 } && !GadgetWorld.CurLevelIsTutorial(pc);
            if (empty || (pc.bAltFire != 0 && gadget is SGadMultiMinesLauncher))
            {
                pc.MineRifleState(GadgetStates.s_DropMine_Loop);
                pc.eventChangeState(GadgetStates.s_PutMineOnWall_InteractionMenu);
            }
            else
            {
                pc.MineRifleState(GadgetStates.s_DropMine);
                pc.eventChangeState(GadgetStates.s_PutMineOnWall);
            }
        }
    }

    /// <summary>The mine menu (rule 8): pick a type, delete the oldest mine or leave; released Alt-fire acts.</summary>
    [Uc("SBase.SPlayerController.s_PutMineOnWall_InteractionMenu")]
    public class s_PutMineOnWall_InteractionMenu : s_BasePutMineOnWall, IWheel, IGadLaserSwitch, IGadTorcheSwitch
    {
        public static new readonly s_PutMineOnWall_InteractionMenu Instance = new(UcMeta.s_PutMineOnWall_InteractionMenu, null);

        protected s_PutMineOnWall_InteractionMenu(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Without a charge no type is usable; "Delete oldest" waits for the server's answer; grabs allowed.</summary>
        [Uc("SBase.SPlayerController.s_PutMineOnWall_InteractionMenu.BeginState", Sha1 = "ccc2be35bbc61d0b3907e5e02e77d9652baad429")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_InteractionMenu.BeginState", Spec = "spec/equipment/mine-launcher.md#the-menu")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.Previous_BT_Up = 0;
            pc.Previous_BT_Down = 0;
            if (pc.IAmOnClient() && pc.SelectedMineLauncher is { } launcher)
            {
                if (launcher.Charge <= 0 && !GadgetWorld.CurLevelIsTutorial(pc))
                {
                    for (int i = 0; i < launcher.NbIT - 1; i++)
                    {
                        launcher.CanUse[i] = 0;
                    }
                }

                launcher.CanUse[launcher.NbIT - 2] = 0;
                launcher.AskServerToRemoveOldestMine(pc, (byte)(launcher is SGadMultiPresDetectorLauncher ? 1 : 0), true);
            }

            pc.LastUpdateButton = pc.Level.TimeSeconds;
            if (pc.SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = true;
            }
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall_InteractionMenu.EndState", Sha1 = "9523e40ef07dbfa5620c836cc00e2da6ad635647")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_InteractionMenu.EndState", Spec = "spec/equipment/mine-launcher.md#the-menu")]
        public override void EndState(Actor self) => base.EndState(self);

        [Uc("SBase.SPlayerController.s_PutMineOnWall_InteractionMenu.WheelUp", Sha1 = "564f66e4d2e3e0302e5a1cb19fd1fdd4710f0515")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_InteractionMenu.WheelUp", Spec = "spec/equipment/mine-launcher.md#the-menu")]
        public void WheelUp(SPlayerController self) => self.SelectedMineLauncher?.SelectPreviousMineType();

        [Uc("SBase.SPlayerController.s_PutMineOnWall_InteractionMenu.WheelDown", Sha1 = "b3792a1928df90168ce4e378aefa14291f1e2310")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_InteractionMenu.WheelDown", Spec = "spec/equipment/mine-launcher.md#the-menu")]
        public void WheelDown(SPlayerController self) => self.SelectedMineLauncher?.SelectNextMineType();

        /// <summary>When Alt-fire is released: exit, delete the oldest, place a type with a charge, or the empty click.</summary>
        [Uc("SBase.SPlayerController.s_PutMineOnWall_InteractionMenu.PlayerMove", Sha1 = "1de8853e8b33c2b519722c0ad73d811c322518d7")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_InteractionMenu.PlayerMove", Spec = "spec/equipment/mine-launcher.md#the-menu")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.bAltFire == 0 && self.SelectedMineLauncher is { } launcher)
            {
                int item = launcher.iCurrentMineType;
                if (item == launcher.NbIT - 1)
                {
                    self.eventChangeState(GadgetStates.s_PutMineOnWall_UnGrab);
                }
                else if (item == launcher.NbIT - 2)
                {
                    launcher.AskServerToRemoveOldestMine(self, (byte)(launcher is SGadMultiPresDetectorLauncher ? 1 : 0));
                    self.eventChangeState(GadgetStates.s_PutMineOnWall_UnGrab);
                }
                else if (launcher.Charge > 0 || GadgetWorld.CurLevelIsTutorial(self))
                {
                    self.eventChangeState(GadgetStates.s_PutMineOnWall);
                }
                else
                {
                    self.Pawn?.PlaySound(MineEmptySound, ESoundSlot.SLOT_Misc, 1f, false, K.EmptyChargeRadius);
                    self.eventChangeState(GadgetStates.s_PutMineOnWall_UnGrab);
                }
            }

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

        public void GadLaserSwitch(SPlayerController self)
        {
        }

        public void GadTorcheSwitch(SPlayerController self)
        {
        }
    }

    /// <summary>Withdrawing from the wall (rule 11): the ungrab clip; its end goes back to waiting.</summary>
    [Uc("SBase.SPlayerController.s_PutMineOnWall_UnGrab")]
    public class s_PutMineOnWall_UnGrab : s_BasePutMineOnWall
    {
        public static new readonly s_PutMineOnWall_UnGrab Instance = new(UcMeta.s_PutMineOnWall_UnGrab, s_BasePutMineOnWall.Instance);

        protected s_PutMineOnWall_UnGrab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall_UnGrab.BeginState", Sha1 = "7acc6c431c49620509fe5902f18d027601dbc742")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_UnGrab.BeginState", Spec = "spec/equipment/mine-launcher.md#the-menu")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_DROPMINE_UNGRAB);
                pc.MineRifleState(GadgetStates.s_DropMine_UnGrab);
            }
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall_UnGrab.EndState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_UnGrab.EndState", Spec = "spec/equipment/mine-launcher.md#the-menu")]
        public override void EndState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall_UnGrab.AnimEnd", Sha1 = "bd40afe358addab38e4724b56fc7430c7c3caa71")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall_UnGrab.AnimEnd", Spec = "spec/equipment/mine-launcher.md#the-menu")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && Channel == SBasePawn.INTERACTCHANNEL)
            {
                pc.GotoWaitingState();
            }
        }
    }

    /// <summary>Placing the mine (rule 5): the placement clip; its interaction frame drops the mine; its end goes back to waiting.</summary>
    [Uc("SBase.SPlayerController.s_PutMineOnWall")]
    public class s_PutMineOnWall : s_BasePutMineOnWall, IAnimNotificationInteractNow
    {
        public static new readonly s_PutMineOnWall Instance = new(UcMeta.s_PutMineOnWall, null);

        protected s_PutMineOnWall(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall.BeginState", Sha1 = "aa5c4faa22b657fa328e086087054b0d1f67aecb")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall.BeginState", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_DROPMINE, true);
                pc.MineRifleState(GadgetStates.s_DropMine);
            }
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall.EndState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall.EndState", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override void EndState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall.AnimEnd", Sha1 = "f065fe3495246d3c59098298345553087b44ed0f")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall.AnimEnd", Spec = "spec/equipment/mine-launcher.md#placing")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && Channel == SBasePawn.INTERACTCHANNEL)
            {
                pc.GotoWaitingState();
            }
        }

        [Uc("SBase.SPlayerController.s_PutMineOnWall.AnimNotification_InteractNow", Sha1 = "20bc064ce501d100a9bb91cb34894f2805a89f19")]
        [PortOf("uc:SBase.SPlayerController:s_PutMineOnWall.AnimNotification_InteractNow", Spec = "spec/equipment/mine-launcher.md#placing")]
        public void AnimNotificationInteractNow(SPlayerController self)
        {
            if (self.IAmOnClient())
            {
                self.SelectedMineLauncher?.DropMineOnWall();
            }
        }
    }
}
