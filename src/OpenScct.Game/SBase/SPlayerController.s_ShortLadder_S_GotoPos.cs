using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_S_GotoPos: the boost climber walks to the helper; reaching the spot or
// bumping into the helper asks the server to start the boost. Written from spec/movement/coop.md (short ladder,
// climber, rules 2 and 3).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ShortLadder_S_GotoPos")]
    public class s_ShortLadder_S_GotoPos : s_Coop_S_Goto, IPlayerMove, INotifyBump, ITopSynchro
    {
        public static new readonly s_ShortLadder_S_GotoPos Instance = new(UcMeta.s_ShortLadder_S_GotoPos, s_Coop_S_Goto.Instance);

        protected s_ShortLadder_S_GotoPos(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Bumping into the helper starts the boost; bumping anything else gives up.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_S_GotoPos.NotifyBump", Sha1 = "913f570f3554dfd7bd90f6212495730344d71040")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_GotoPos.NotifyBump", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public virtual bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.IAmOnClient())
            {
                if (ReferenceEquals(Other, self.SBPawn?.m_InteractionTarget))
                {
                    self.AskServerToStartCoopInteraction();
                }
                else
                {
                    self.GotoWaitingState();
                }
            }

            return true;
        }

        /// <summary>At the spot: the request to the server.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_S_GotoPos.TopSynchro", Sha1 = "8076b6d65e8e148073a032d8a3410070c82faed5")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_GotoPos.TopSynchro", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient())
            {
                self.AskServerToStartCoopInteraction();
            }
        }

        /// <summary>No jump; the helper no longer ready ends the approach; the crouch zones; the view.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_S_GotoPos.PlayerMove", Sha1 = "ad9260d9bf7c5499d6c8a03a85370edb5af78aaf")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_GotoPos.PlayerMove", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            if (self.SBPawn?.m_InteractionTarget is not { bReadyForShortLadder: true })
            {
                self.GotoWaitingState();
            }

            self.ZoneHorsCrouch();
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }
}
