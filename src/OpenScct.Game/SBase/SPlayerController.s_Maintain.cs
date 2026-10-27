using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using EK = OpenScct.Game.Equipment.K;
using K = OpenScct.Game.Objectives.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController hold-switch states (bMaintainsSwitch): the walk to the switch, the enter animation, the
// hold, the leave. Written from spec/objectives/switch-hack.md (rule 18); the damage handlers are in
// Combat/CombatDispatch.cs. Also the panel-state helpers shared with s_Hack.cs and s_Computer.cs: the quick gadget
// keys and ServerWillStartGrabOnMe (switch-hack.md rule 17, terminal-hack.md rule 26).
public partial class SPlayerController
{
    /// <summary>A state version of <c>ServerWillStartGrabOnMe</c> (server, on the grabbed player's controller).</summary>
    public interface IServerWillStartGrabOnMe
    {
        void ServerWillStartGrabOnMe(SPlayerController self);
    }

    /// <summary>The server tells the merc about to be grabbed; only s_Hack_Use does something (frees the panel).</summary>
    [Uc("SBase.SPlayerController.ServerWillStartGrabOnMe", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:ServerWillStartGrabOnMe", Spec = "spec/objectives/switch-hack.md#rules")]
    public void ServerWillStartGrabOnMe()
    {
        if (StateNode is IServerWillStartGrabOnMe s)
        {
            s.ServerWillStartGrabOnMe(this);
        }
    }

    /// <summary>
    /// The quick gadget key in the panel, terminal and hold states (switch-hack.md rule 17, terminal-hack.md rule 26),
    /// up to the grenade test: slot within the team's count, slot 1 refused while the cloak is selected, spies only
    /// and 2 s after the last quick launch; the slot is selected (by the pawn's SelectGagdet for the hold switch).
    /// True when the new selection is a grenade that can be quick-launched (the state then leaves); a cloak is used
    /// at once when <paramref name="cloakQuickUse"/>.
    /// </summary>
    private bool PanelQuickGadget(int i, bool pawnSelect, bool cloakQuickUse)
    {
        if (SBPawn is not { } pawn || i >= PawnInventory.TeamSlots(pawn))
        {
            return false;
        }

        if (i == 1 && pawn.SelectedGadget is SGadCloak)
        {
            return false;
        }

        if (pawn.SPawnType != PawnType.SPAWNOFFENSE || Level.TimeSeconds <= fDateLaunchedLastQuickGadget + EK.QuickLaunchCooldown)
        {
            return false;
        }

        bUnCrouchAndUnDraw = 1;
        if (pawnSelect)
        {
            PawnInventory.SelectGagdet(pawn, i);
        }
        else
        {
            SelectGadget(i);
        }

        switch (pawn.SelectedGadget)
        {
            case SGadGrenLauncher grenade:
                return grenade.CanQuickLaunch &&
                    (grenade.Charge > 0 || grenade.Charge == EK.UnlimitedCharge || GadgetWorld.CurLevelIsTutorial(pawn));
            case SGadCloak cloak when cloakQuickUse:
                cloak.QuickUse();
                break;
        }

        return false;
    }

    /// <summary>Walking to the hold switch's use spot (rule 18, as the hack walk; anyone with a weapon holsters).</summary>
    [Uc("SBase.SPlayerController.s_Maintain_GoTo")]
    public class s_Maintain_GoTo : UcActorState, IPlayerMove, INotifyBump, ITopSynchro, ICanActivateLaser
    {
        public static readonly s_Maintain_GoTo Instance = new(UcMeta.s_Maintain_GoTo, null);

        protected s_Maintain_GoTo(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Maintain_GoTo.BeginState", Sha1 = "9786c6c569b23cf830ae230f62356f5ff80ba095")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_GoTo.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.HolsterForUse();
            if (!SwitchHackRules.MaintainGoToBeginState(pc, out FVector facing))
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                pc.eventChangeState(K.s_PlayerWalkingSt0H);
                return;
            }

            pawn.SetInfoForVandamme(facing);
            pawn.ChangeEnumAction(SBasePawn.SA_REPOS);
        }

        [Uc("SBase.SPlayerController.s_Maintain_GoTo.NotifyBump", Sha1 = "47783fd4220b19eae85035865592141d054eeeee")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_GoTo.NotifyBump", Spec = "spec/objectives/switch-hack.md#rules")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.IAmOnClient())
            {
                self.SBPawn?.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                self.GotoWaitingState();
            }

            return true;
        }

        [Uc("SBase.SPlayerController.s_Maintain_GoTo.TopSynchro", Sha1 = "e432b9f09cba6a177320314cd1467025d4e369c2")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_GoTo.TopSynchro", Spec = "spec/objectives/switch-hack.md#rules")]
        public void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient())
            {
                self.eventChangeState(K.s_Maintain_Enter);
            }
        }

        [Uc("SBase.SPlayerController.s_Maintain_GoTo.PlayerMove", Sha1 = "67eb3dbab1b9f5206db5a01a3281ad7e4c93d6fe")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_GoTo.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.InteractionStillMove(DeltaTime);

        [Uc("SBase.SPlayerController.s_Maintain_GoTo.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_GoTo.CanActivateLaser", Spec = "spec/objectives/switch-hack.md#rules")]
        public bool CanActivateLaser(SPlayerController self) => false;
    }

    /// <summary>
    /// The enter animation; its end on the input machine starts the hold on the server (Interaction sub 2) and goes to
    /// s_Maintain_Use locally (the server stays here until the leave, with the same damage handler).
    /// </summary>
    [Uc("SBase.SPlayerController.s_Maintain_Enter")]
    public class s_Maintain_Enter : UcActorState, IPlayerMove, IClientDamage
    {
        public static readonly s_Maintain_Enter Instance = new(UcMeta.s_Maintain_Enter, null);

        protected s_Maintain_Enter(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Maintain_Enter.BeginState", Sha1 = "e459f305ad1b763156781e6e2ea32041345dacee")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Enter.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
            }

            pawn.ChangeEnumAction(SBasePawn.SA_SWM_ENTER);
        }

        [Uc("SBase.SPlayerController.s_Maintain_Enter.AnimEnd", Sha1 = "7dfed83a53dc7ce799de352af9a94fe8d33e858c")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Enter.AnimEnd", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && Channel == 0)
            {
                SwitchHackRules.EnterStart(pc);
                pc.GotoState(K.s_Maintain_Use);
            }
        }

        [Uc("SBase.SPlayerController.s_Maintain_Enter.ClientDamage", Sha1 = "6aaf1c58f82f9a279c953aa14a9df3b88d08761f")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Enter.ClientDamage", Spec = "spec/objectives/switch-hack.md#rules")]
        public void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
        {
            SwitchHackRules.PostAndForget(self, false);
            self.GlobalClientDamage(Momentum, DamageType, HitLocation, instigatedBy);
        }

        [Uc("SBase.SPlayerController.s_Maintain_Enter.PlayerMove", Sha1 = "6656d45671ba39ef74393bb5f7a1f54c65521d13")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Enter.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.InteractionStillMove(DeltaTime);
    }

    /// <summary>The hold (rule 18): the switch's event fired at its start; moving or a quick grenade lets go.</summary>
    [Uc("SBase.SPlayerController.s_Maintain_Use")]
    public class s_Maintain_Use : UcActorState, IPlayerMove, ICanBeDropAttacked, IClientDamage, IGadgetQuickChoice
    {
        public static readonly s_Maintain_Use Instance = new(UcMeta.s_Maintain_Use, null);

        protected s_Maintain_Use(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Maintain_Use.BeginState", Sha1 = "b8505be034a78895d565349781b76037ab8c5203")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient())
            {
                return;
            }

            pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_SWM_MAINTAIN);
            pc.UsedInterraction = pc.SelectedInteraction;
            SwitchHackRules.MaintainUseBeginState(pc);
        }

        [Uc("SBase.SPlayerController.s_Maintain_Use.EndState", Sha1 = "5442602d99533bcc42b3909a59cf4e59a94088e9")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.EndState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_SWM_BACK);
            }
        }

        [Uc("SBase.SPlayerController.s_Maintain_Use.PlayerMove", Sha1 = "653cf77c096b00edda3ca59b6882014603ba5525")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (SwitchHackRules.MaintainShouldCancel(self))
            {
                self.eventChangeState(K.s_Maintain_Leave, K.TriggerHack_Failed);
                return;
            }

            self.InteractionStillMove(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_Maintain_Use.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.CanBeDropAttacked", Spec = "spec/objectives/switch-hack.md#rules")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        /// <summary>A quick grenade lets go of the switch (the throw follows from the walking state).</summary>
        [Uc("SBase.SPlayerController.s_Maintain_Use.GadgetQuickChoice", Sha1 = "740f2ece28aeaa5c6d4b1c36012b6ac1d5e3d948")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.GadgetQuickChoice", Spec = "spec/objectives/switch-hack.md#rules")]
        public void GadgetQuickChoice(SPlayerController self, int i)
        {
            if (!self.PanelQuickGadget(i, pawnSelect: true, cloakQuickUse: false) || self.SBPawn is not { } pawn)
            {
                return;
            }

            self.bQuickGadgetQuery = true;
            pawn.ChangeEnumAction(SBasePawn.SA_SWM_BACK);
            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            }

            self.bCanCheckUsingButton = true;
            SwitchHackRules.PostAndForget(self, false);
            self.GotoWaitingState();
        }

        [Uc("SBase.SPlayerController.s_Maintain_Use.ClientDamage", Sha1 = "9b273706933b900e07c8bd4dba925ab91533ad92")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.ClientDamage", Spec = "spec/objectives/switch-hack.md#rules")]
        public void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
        {
            self.InteractionPost(self.SelectedInteraction, K.SubFailed);
            self.GlobalClientDamage(Momentum, DamageType, HitLocation, instigatedBy);
        }
    }

    /// <summary>
    /// The leave (rule 18): the label reports the end of the hold on the input machine (the event fires again), the
    /// back animation ends it.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Maintain_Leave")]
    public class s_Maintain_Leave : UcActorState, IPlayerMove, IClientDamage
    {
        public static readonly s_Maintain_Leave Instance = new(UcMeta.s_Maintain_Leave, null);

        private readonly int _successPc;
        private readonly int _failedPc;

        protected s_Maintain_Leave(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
            foreach ((string name, int pc) in info.LabelPcs)
            {
                if (name == K.TriggerHack_Successfull.ToString())
                {
                    _successPc = pc;
                }
                else if (name == K.TriggerHack_Failed.ToString())
                {
                    _failedPc = pc;
                }
            }
        }

        [Uc("SBase.SPlayerController.s_Maintain_Leave.BeginState", Sha1 = "26ba163beca195dff3683e5f793fc518a00259ed")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Leave.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_SWM_BACK);
            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            }
        }

        [Uc("SBase.SPlayerController.s_Maintain_Leave.EndState", Sha1 = "4983ca89688ac31291fcc1a2754e7612be6f11e5")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Leave.EndState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void EndState(Actor self) => ((SPlayerController)self).bCanCheckUsingButton = true;

        [Uc("SBase.SPlayerController.s_Maintain_Leave@code", Sha1 = "f9844fed8d222b1170ab7bd0e468c7a03714ba4e")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Leave@code", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            var pc = (SPlayerController)self;
            int at = frame.Pc;
            frame.Pc = LatentFrame.NoCode;
            if (pc.IAmOnClient() && (at == _successPc || at == _failedPc))
            {
                SwitchHackRules.PostAndForget(pc, at == _successPc);
            }
        }

        [Uc("SBase.SPlayerController.s_Maintain_Leave.AnimEnd", Sha1 = "f5a53b5a0bbac42901be74ec3f33ee6be64c5626")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Leave.AnimEnd", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && Channel == 0)
            {
                pc.GotoWaitingState();
            }
        }

        [Uc("SBase.SPlayerController.s_Maintain_Leave.ClientDamage", Sha1 = "8037cfd40a72beb65f28c52f8ed739f6bd7b6ab6")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Leave.ClientDamage", Spec = "spec/objectives/switch-hack.md#rules")]
        public void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
        {
            SwitchHackRules.PostAndForget(self, false);
            self.GlobalClientDamage(Momentum, DamageType, HitLocation, instigatedBy);
        }

        [Uc("SBase.SPlayerController.s_Maintain_Leave.PlayerMove", Sha1 = "9afe60e84ee8f34f33b957cbb2eb5fb1679abddd")]
        [PortOf("uc:SBase.SPlayerController:s_Maintain_Leave.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.InteractionStillMove(DeltaTime);
    }
}
