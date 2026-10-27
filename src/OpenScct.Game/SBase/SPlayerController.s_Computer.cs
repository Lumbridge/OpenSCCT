using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Objectives.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController terminal states: the walk to the use spot, the enter animation, the hack, the leaves.
// Written from spec/objectives/terminal-hack.md (rules 9 to 27); the objective rules are in
// Objectives/TerminalHackRules.cs, the damage handlers in Combat/CombatDispatch.cs.
public partial class SPlayerController
{
    /// <summary>Holsters a drawn spy weapon and sets the walking camera (the GoTo states' start, on the input machine).</summary>
    private void HolsterForUse()
    {
        if (SBPawn is not { WeaponLoaded: not 0 } pawn)
        {
            return;
        }

        pawn.ChangeEnumAction(pawn.bIsCrouched ? SBasePawn.SA_WALK_CR_2Hto0H : SBasePawn.SA_WALK_ST_2Hto0H);
        MovementCamera(ECamMode.ECM_Walking);
    }

    /// <summary>Walking to the terminal's use spot (rules 9 to 12).</summary>
    [Uc("SBase.SPlayerController.s_Computer_GoTo")]
    public class s_Computer_GoTo : UcActorState, IPlayerTick, IPlayerMove, INotifyBump, INotifyHitWall, ITopSynchro
    {
        public static readonly s_Computer_GoTo Instance = new(UcMeta.s_Computer_GoTo, null);

        protected s_Computer_GoTo(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Computer_GoTo.BeginState", Sha1 = "35d572e093aa1c3730c1fbd6e9ebeded194b1c0a")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient())
            {
                return;
            }

            pc.HolsterForUse();
            pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_REPOS);
        }

        /// <summary>Aims the repositioning at the first terminal within 300 uu, or gives up; then the normal tick.</summary>
        [Uc("SBase.SPlayerController.s_Computer_GoTo.PlayerTick", Sha1 = "7045751d7f3aa5b5a901a125fd4fcc12847eddfd")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.PlayerTick", Spec = "spec/objectives/terminal-hack.md#rules")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if (!TerminalHackRules.GoToPlayerTick(pc, out FVector facing))
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                pc.eventChangeState(K.s_PlayerWalkingSt0H);
                return;
            }

            pc.SBPawn?.SetInfoForVandamme(facing);
            pc.PlayerTick(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_Computer_GoTo.PlayerMove", Sha1 = "b5f1b3efef695e45ec0cf08a900fe2340df5dd72")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.PlayerMove", Spec = "spec/objectives/terminal-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.InteractionStillMove(DeltaTime);

        [Uc("SBase.SPlayerController.s_Computer_GoTo.NotifyBump", Sha1 = "aaff7e94b646dea91c5543a7d96b4f34eaa6566b")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.NotifyBump", Spec = "spec/objectives/terminal-hack.md#rules")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            AbortWalk(self, Other);
            return true;
        }

        [Uc("SBase.SPlayerController.s_Computer_GoTo.NotifyHitWall", Sha1 = "070342c323718459c639bb505493e54993fc72f4")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.NotifyHitWall", Spec = "spec/objectives/terminal-hack.md#rules")]
        public bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            AbortWalk(self, Wall);
            return true;
        }

        [Uc("SBase.SPlayerController.s_Computer_GoTo.TopSynchro", Sha1 = "057fdb7298a4b80b367db296dfbc6c5eee2dbf98")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_GoTo.TopSynchro", Spec = "spec/objectives/terminal-hack.md#rules")]
        public void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient())
            {
                self.eventChangeState(K.s_Computer_Enter);
            }
        }

        private static void AbortWalk(SPlayerController pc, Actor? other)
        {
            if (pc.IAmOnClient() && TerminalHackRules.GoToAbortsOn(other))
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                pc.GotoWaitingState();
            }
        }
    }

    /// <summary>The enter animation at the terminal; its end starts the hack (rule 13).</summary>
    [Uc("SBase.SPlayerController.s_Computer_Enter")]
    public class s_Computer_Enter : UcActorState, IPlayerMove, ICanBeDropAttacked
    {
        public static readonly s_Computer_Enter Instance = new(UcMeta.s_Computer_Enter, null);

        protected s_Computer_Enter(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Computer_Enter.BeginState", Sha1 = "82d25231aa42f09943cc6d2d02bb51dee50f4db5")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Enter.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
            }

            pawn.ChangeEnumAction(SBasePawn.SA_COMPUTER_ENTER);
        }

        [Uc("SBase.SPlayerController.s_Computer_Enter.AnimEnd", Sha1 = "214aa7c36c374892e3d8a976d4dcb249034f2557")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Enter.AnimEnd", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            if (Channel != 0)
            {
                return;
            }

            var pc = (SPlayerController)self;
            pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_COMPUTER_LOOP);
            pc.eventChangeState(K.s_Computer_Use);
        }

        [Uc("SBase.SPlayerController.s_Computer_Enter.PlayerMove", Sha1 = "448389dbf6cb531fee504ab58fab75a8ef6169d4")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Enter.PlayerMove", Spec = "spec/objectives/terminal-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.InteractionStillMove(DeltaTime);

        [Uc("SBase.SPlayerController.s_Computer_Enter.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Enter.CanBeDropAttacked", Spec = "spec/objectives/terminal-hack.md#rules")]
        public bool CanBeDropAttacked(SPlayerController self) => true;
    }

    /// <summary>The hack (rules 14, 20, 22, 24): the server's timer takes the terminal; moving or drawing cancels.</summary>
    [Uc("SBase.SPlayerController.s_Computer_Use")]
    public class s_Computer_Use : UcActorState, IPlayerMove, ICanBeDropAttacked, IGadgetQuickChoice
    {
        public static readonly s_Computer_Use Instance = new(UcMeta.s_Computer_Use, null);

        protected s_Computer_Use(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Computer_Use.BeginState", Sha1 = "df6d59a6cf8706a82c5b0a44b42de238e67b4f43")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Use.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void BeginState(Actor self) => TerminalHackRules.UseBeginState((SPlayerController)self);

        [Uc("SBase.SPlayerController.s_Computer_Use.EndState", Sha1 = "1911ecf3e5c65aa231513fe9a82abcae155627e4")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Use.EndState", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            TerminalHackRules.UseEndState(pc);
            if (ObjectiveNatives.IsAlive(pc))
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_COMPUTER_ENTERBACK);
            }
        }

        [Uc("SBase.SPlayerController.s_Computer_Use.Timer", Sha1 = "8fe0521f0e81bf530ca015788560503068e268a5")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Use.Timer", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                TerminalHackRules.UseTimer(pc);
            }
        }

        /// <summary>The input cancel (more than 1000 on an axis, or the draw button), then the still move.</summary>
        [Uc("SBase.SPlayerController.s_Computer_Use.PlayerMove", Sha1 = "ae3fae762eb1078e9926f86479ef66372a9a89ac")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Use.PlayerMove", Spec = "spec/objectives/terminal-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (TerminalHackRules.UseShouldCancel(self))
            {
                self.eventChangeState(K.s_Computer_FastLeave);
                return;
            }

            self.InteractionStillMove(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_Computer_Use.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Use.CanBeDropAttacked", Spec = "spec/objectives/terminal-hack.md#rules")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        /// <summary>
        /// The quick gadget keys (rule 26; with the default keys the mouse buttons): a quick grenade leaves with progress
        /// kept, a cloak is used without leaving. Message 23 needs the game, so only a host sends it.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Computer_Use.GadgetQuickChoice", Sha1 = "7f531a49ece26d39efb5a5a89dd66cac79559e74")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Use.GadgetQuickChoice", Spec = "spec/objectives/terminal-hack.md#rules")]
        public void GadgetQuickChoice(SPlayerController self, int i)
        {
            if (!self.PanelQuickGadget(i, pawnSelect: false, cloakQuickUse: true))
            {
                return;
            }

            self.bQuickGadgetQuery = true;
            if (self.Role == ENetRole.ROLE_Authority)
            {
                ObjectiveMessages.Broadcast(self, K.MsgInterrupted);
            }

            self.eventChangeState(K.s_Computer_Leave);
        }
    }

    /// <summary>The leave animation after a taken (or broken) hack; its end goes back to walking (rule 23).</summary>
    [Uc("SBase.SPlayerController.s_Computer_Leave")]
    public class s_Computer_Leave : UcActorState, IPlayerMove, ICanBeDropAttacked
    {
        public static readonly s_Computer_Leave Instance = new(UcMeta.s_Computer_Leave, null);

        protected s_Computer_Leave(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Computer_Leave.BeginState", Sha1 = "4cd1f91397d6550e93e18e2482648a3c340694a6")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Leave.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            TerminalHackRules.LeaveBeginState(pc);
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_COMPUTER_ENTERBACK);
            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            }
        }

        /// <summary>Any channel's end reports the use to the server; channel 0 on the input machine walks on.</summary>
        [Uc("SBase.SPlayerController.s_Computer_Leave.AnimEnd", Sha1 = "27e962d1bca47e11135b29bda9c206bac1ec5645")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Leave.AnimEnd", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            pc.InteractionPost(pc.SelectedInteraction, pc.IManager?.SelectedSubInteractions ?? 0);
            if (pc.IAmOnClient() && Channel == 0)
            {
                pc.GotoWaitingState();
            }
        }

        [Uc("SBase.SPlayerController.s_Computer_Leave.PlayerMove", Sha1 = "d1d53fcd3977692696e3bdf5ccf7a362fb68ec2b")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Leave.PlayerMove", Spec = "spec/objectives/terminal-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.InteractionStillMove(DeltaTime);

        [Uc("SBase.SPlayerController.s_Computer_Leave.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_Leave.CanBeDropAttacked", Spec = "spec/objectives/terminal-hack.md#rules")]
        public bool CanBeDropAttacked(SPlayerController self) => true;
    }

    /// <summary>The cancelled hack (rule 25): message 23 and the hack stopped on the server, walking at once.</summary>
    [Uc("SBase.SPlayerController.s_Computer_FastLeave")]
    public class s_Computer_FastLeave : UcActorState
    {
        public static readonly s_Computer_FastLeave Instance = new(UcMeta.s_Computer_FastLeave, null);

        protected s_Computer_FastLeave(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Computer_FastLeave.BeginState", Sha1 = "0c17912807d5a68ac73a6fe7d20edafc70765a4a")]
        [PortOf("uc:SBase.SPlayerController:s_Computer_FastLeave.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            TerminalHackRules.FastLeaveBeginState(pc);
            if (!pc.IAmOnClient())
            {
                return;
            }

            if (pc.SBPawn is { bIsCrouched: true } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            }

            pc.InteractionPost(pc.SelectedInteraction, pc.IManager?.SelectedSubInteractions ?? 0);
            pc.GotoWaitingState();
        }
    }
}
