using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Hoist_S_GotoPos: the hoist climber walks to the spot under the hanging mate; the
// first TopSynchro asks the server to start the climb. Written from spec/movement/coop.md (hoist, climber, rule 2).
//
// The source declares it as extending s_Coop_S_Goto; the generated state metadata lists no super state, so the state
// chain follows the metadata while the C# class keeps s_Coop_S_Goto's behaviour.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_Hoist_S_GotoPos")]
    public class s_Hoist_S_GotoPos : s_Coop_S_Goto, IPlayerMove, INotifyBump, ITopSynchro
    {
        public static new readonly s_Hoist_S_GotoPos Instance = new(UcMeta.s_Hoist_S_GotoPos, null);

        protected s_Hoist_S_GotoPos(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Hoist_S_GotoPos.BeginState", Sha1 = "9336a90bd258c7a98bd7173655a42499c5265d4a")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_GotoPos.BeginState", Spec = "spec/movement/coop.md#hoist-climber")]
        public override void BeginState(Actor self)
        {
            ((SPlayerController)self).bAlreadyReceivedEventTopSynchro = false;
            base.BeginState(self);
        }

        /// <summary>Bumping anything but the mate gives up the approach (the mate's bumps are left to TopSynchro).</summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_GotoPos.NotifyBump", Sha1 = "4db67b4dffb55c1cd93cc4c74d15f56d471739c3")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_GotoPos.NotifyBump", Spec = "spec/movement/coop.md#hoist-climber")]
        public virtual bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.IAmOnClient() && !ReferenceEquals(Other, self.SBPawn?.m_InteractionTarget))
            {
                self.GotoWaitingState();
            }

            return true;
        }

        /// <summary>At the spot, once: the request to the server.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_GotoPos.TopSynchro", Sha1 = "16dc1f3c0fa203066e7ba4aedc284b80990b7028")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_GotoPos.TopSynchro", Spec = "spec/movement/coop.md#hoist-climber")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient() && !self.bAlreadyReceivedEventTopSynchro)
            {
                self.bAlreadyReceivedEventTopSynchro = true;
                self.AskServerToStartCoopInteraction();
            }
        }

        /// <summary>No jump; the mate no longer ready ends the approach; the crouch zones; the view.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_GotoPos.PlayerMove", Sha1 = "5a5e57697976367e8ba02394a9608784f046b82b")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_GotoPos.PlayerMove", Spec = "spec/movement/coop.md#hoist-climber")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            if (self.SBPawn?.m_InteractionTarget is not { bReadyForHoistClimb: true })
            {
                self.GotoWaitingState();
            }

            self.ZoneHorsCrouch();
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }
}
