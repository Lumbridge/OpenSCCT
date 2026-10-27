using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Objectives.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController panel hack states: the walk to the panel, the enter animation, the hack, the leaves.
// Written from spec/objectives/switch-hack.md (rules 9 to 17); the objective rules are in
// Objectives/SwitchHackRules.cs, the damage handlers in Combat/CombatDispatch.cs.
public partial class SPlayerController
{
    /// <summary>Walking to the panel's use spot (rule 9).</summary>
    [Uc("SBase.SPlayerController.s_Hack_GoTo")]
    public class s_Hack_GoTo : UcActorState, IPlayerMove, INotifyBump, ITopSynchro, ICanActivateLaser
    {
        public static readonly s_Hack_GoTo Instance = new(UcMeta.s_Hack_GoTo, null);

        protected s_Hack_GoTo(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Hack_GoTo.BeginState", Sha1 = "55078391729f39356d138733df6def0520226f6b")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_GoTo.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn is not SPAWNDEFENSE)
            {
                pc.HolsterForUse();
            }

            if (!SwitchHackRules.GoToBeginState(pc, out FVector facing))
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                pc.eventChangeState(K.s_PlayerWalkingSt0H);
                return;
            }

            pawn.SetInfoForVandamme(facing);
            pawn.ChangeEnumAction(SBasePawn.SA_REPOS);
        }

        [Uc("SBase.SPlayerController.s_Hack_GoTo.NotifyBump", Sha1 = "47783fd4220b19eae85035865592141d054eeeee")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_GoTo.NotifyBump", Spec = "spec/objectives/switch-hack.md#rules")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            self.SBPawn?.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
            self.GotoWaitingState();
            return true;
        }

        [Uc("SBase.SPlayerController.s_Hack_GoTo.TopSynchro", Sha1 = "1133bc71f6d05f8ed09d820865003bf84b587046")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_GoTo.TopSynchro", Spec = "spec/objectives/switch-hack.md#rules")]
        public void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient())
            {
                self.eventChangeState(K.s_Hack_Enter);
            }
        }

        [Uc("SBase.SPlayerController.s_Hack_GoTo.PlayerMove", Sha1 = "9c4038688c5550c46748c3b17481a977be86aade")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_GoTo.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.ZoneHorsCrouch();
            if (self.SBPawn is SPawnAttaque)
            {
                self.UpdateCamRotation(DeltaTime);
            }

            self.SendMove(DeltaTime, FVector.Zero);
        }

        [Uc("SBase.SPlayerController.s_Hack_GoTo.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_GoTo.CanActivateLaser", Spec = "spec/objectives/switch-hack.md#rules")]
        public bool CanActivateLaser(SPlayerController self) => false;
    }

    /// <summary>The enter animation; Interaction sub 2 tells the server the hack starts (rule 10).</summary>
    [Uc("SBase.SPlayerController.s_Hack_Enter")]
    public class s_Hack_Enter : UcActorState, IPlayerMove
    {
        public static readonly s_Hack_Enter Instance = new(UcMeta.s_Hack_Enter, null);

        protected s_Hack_Enter(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Hack_Enter.BeginState", Sha1 = "78469abb737d0dfa935f9b96adfb7ec91d4fd813")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Enter.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
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

            if (pawn is SPawnAttaque)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_HACK_ENTER);
            }

            SwitchHackRules.EnterStart(pc);
        }

        /// <summary>The enter animation ended (each machine on its own copy): the hack itself.</summary>
        [Uc("SBase.SPlayerController.s_Hack_Enter.AnimEnd", Sha1 = "209727a704f9b8bfc01811c8a42464c72f6b9197")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Enter.AnimEnd", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            if (Channel == 0)
            {
                self.GotoState(K.s_Hack_Use);
            }
        }

        [Uc("SBase.SPlayerController.s_Hack_Enter.PlayerMove", Sha1 = "8d2e204f90f947d25ce96cc06a493b9f4d440ec8")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Enter.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.InteractionStillMove(DeltaTime);
    }

    /// <summary>The hack (rules 12, 13, 16): the server's timer reports success; moving or drawing cancels.</summary>
    [Uc("SBase.SPlayerController.s_Hack_Use")]
    public class s_Hack_Use : UcActorState, IPlayerMove, ICanBeDropAttacked, ICanBeGrabbed
    {
        public static readonly s_Hack_Use Instance = new(UcMeta.s_Hack_Use, null);

        protected s_Hack_Use(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Hack_Use.BeginState", Sha1 = "819b9dc67ac10f09411406a4e97c7ca19e5a7807")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Use.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is SPAWNDEFENSE merc)
            {
                merc.bCanBeGrabbed = true;
            }

            SwitchHackRules.UseBeginState(pc);
        }

        [Uc("SBase.SPlayerController.s_Hack_Use.EndState", Sha1 = "4f39b0993705ec961bdfaf2bf5a1d9c58b420778")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Use.EndState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is SPAWNDEFENSE merc)
            {
                merc.bCanBeGrabbed = false;
            }
            else if (pc.IAmOnClient() && ObjectiveNatives.IsAlive(pc))
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_HACK_ENTERBACK);
            }
        }

        [Uc("SBase.SPlayerController.s_Hack_Use.Timer", Sha1 = "069fdbab5825b7a43ec9355daf8403b28f312cfb")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Use.Timer", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                SwitchHackRules.UseTimer(pc);
            }
        }

        [Uc("SBase.SPlayerController.s_Hack_Use.PlayerMove", Sha1 = "8ecdb26e1abddeb4e7557d7a83e3cacab803cfe7")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Use.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (SwitchHackRules.UseShouldCancel(self))
            {
                self.eventChangeState(K.s_Hack_FastLeave);
                return;
            }

            self.InteractionStillMove(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_Hack_Use.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Use.CanBeDropAttacked", Spec = "spec/objectives/switch-hack.md#rules")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Hack_Use.CanBeGrabbed", Sha1 = "9ca447bdebf24bc1fe879d251877cf611c4265e3")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Use.CanBeGrabbed", Spec = "spec/objectives/switch-hack.md#rules")]
        public bool CanBeGrabbed(SPlayerController self) => self.SBPawn is SPAWNDEFENSE;
    }

    /// <summary>
    /// The leave (rule 14): the success or failure label reports the result to the server on the input machine, the
    /// spy's back animation ends it.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Hack_Leave")]
    public class s_Hack_Leave : UcActorState, IPlayerMove
    {
        public static readonly s_Hack_Leave Instance = new(UcMeta.s_Hack_Leave, null);

        private readonly int _successPc;
        private readonly int _failedPc;

        protected s_Hack_Leave(UcStateInfo info, ActorState? super)
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

        [Uc("SBase.SPlayerController.s_Hack_Leave.BeginState", Sha1 = "1460fe19d30b987af977c5650a74af106c0b2841")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Leave.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn is SPawnAttaque)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_HACK_ENTERBACK);
            }

            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            }
        }

        [Uc("SBase.SPlayerController.s_Hack_Leave.EndState", Sha1 = "4983ca89688ac31291fcc1a2754e7612be6f11e5")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Leave.EndState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void EndState(Actor self) => ((SPlayerController)self).bCanCheckUsingButton = true;

        [Uc("SBase.SPlayerController.s_Hack_Leave@code", Sha1 = "992babfe32f63a63b95a3531ad4303e5d5a19a08")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Leave@code", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            var pc = (SPlayerController)self;
            int at = frame.Pc;
            frame.Pc = LatentFrame.NoCode;
            if (!pc.IAmOnClient() || (at != _successPc && at != _failedPc))
            {
                return;
            }

            SwitchHackRules.PostAndForget(pc, at == _successPc);
            if (pc.SBPawn is SPAWNDEFENSE)
            {
                pc.GotoWaitingState();
            }
        }

        [Uc("SBase.SPlayerController.s_Hack_Leave.AnimEnd", Sha1 = "946d6a5ccc941aad1d314bdce3031b16f27521a3")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Leave.AnimEnd", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is SPawnAttaque)
            {
                pc.GotoWaitingState();
            }
        }

        [Uc("SBase.SPlayerController.s_Hack_Leave.PlayerMove", Sha1 = "e1d55d356cbeee2e87ae773a540c1db2ce9690e7")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_Leave.PlayerMove", Spec = "spec/objectives/switch-hack.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.InteractionStillMove(DeltaTime);
    }

    /// <summary>The cancelled hack (rule 16): the server leaves s_Hacking without firing, walking at once.</summary>
    [Uc("SBase.SPlayerController.s_Hack_FastLeave")]
    public class s_Hack_FastLeave : UcActorState
    {
        public static readonly s_Hack_FastLeave Instance = new(UcMeta.s_Hack_FastLeave, null);

        protected s_Hack_FastLeave(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Hack_FastLeave.BeginState", Sha1 = "2f08be68a33ff1063637f864eaa46dcb7ab5fa5c")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_FastLeave.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IAmOnClient())
            {
                return;
            }

            if (pc.SBPawn is { bIsCrouched: true } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
            }

            SwitchHackRules.PostAndForget(pc, false);
            pc.GotoWaitingState();
        }

        [Uc("SBase.SPlayerController.s_Hack_FastLeave.EndState", Sha1 = "4983ca89688ac31291fcc1a2754e7612be6f11e5")]
        [PortOf("uc:SBase.SPlayerController:s_Hack_FastLeave.EndState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void EndState(Actor self) => ((SPlayerController)self).bCanCheckUsingButton = true;
    }
}
