using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_P_GotoDest: the boost helper walks to the spot under the ledge
// (repositioning), then offers the boost. Written from spec/movement/coop.md (short ladder, helper, rule 1).
//
// The source declares it as extending s_ShortLadder_P_Base; the generated state metadata lists no super state.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ShortLadder_P_GotoDest")]
    public class s_ShortLadder_P_GotoDest : s_ShortLadder_P_Base, INotifyBump, ITopSynchro
    {
        public static new readonly s_ShortLadder_P_GotoDest Instance = new(UcMeta.s_ShortLadder_P_GotoDest, null);

        protected s_ShortLadder_P_GotoDest(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>A bump on the way ends the repositioning and the help.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_GotoDest.NotifyBump", Sha1 = "6390d6d1da64aabfc2da1c70d09d4cf2b0a53a14")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_GotoDest.NotifyBump", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public virtual bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.IAmOnClient())
            {
                self.SBPawn?.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                self.GotoWaitingState();
            }

            return true;
        }

        /// <summary>The controlling side resends the final spot and facing.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_GotoDest.EndState", Sha1 = "251eb518022ac943f50b500eb5cfa0fd83ece294")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_GotoDest.EndState", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.vVandammePos = pawn.Location;
                pawn.SetInfoForVandamme(pawn.Rotation.Vector());
            }
        }

        /// <summary>The repositioning to vVandammePos.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_GotoDest.BeginState", Sha1 = "b79f3fc2cb05e02db6d5f768467e0376707027cc")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_GotoDest.BeginState", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void BeginState(Actor self) => ((SPlayerController)self).SBPawn?.ChangeEnumAction(SBasePawn.SA_REPOS);

        /// <summary>A spy turns the view; a merc's view keeps the pawn's yaw.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_GotoDest.PlayerMove", Sha1 = "e060d350633cd02401181622f6fd1124ca2ed2df")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_GotoDest.PlayerMove", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is SPawnAttaque)
            {
                self.UpdateCamRotation(DeltaTime);
            }
            else if (self.Pawn is { } pawn)
            {
                FRotator rot = pawn.Rotation;
                rot.Yaw &= 65535;
                self.Rotation = rot;
                self.MyDesiredRotation = rot;
            }

            self.SendMove(DeltaTime, default);
        }

        /// <summary>At the spot (controlling side): the boost offer.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_GotoDest.TopSynchro", Sha1 = "5d35e259e52933de2f5ba3a97f784f6f64969641")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_GotoDest.TopSynchro", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient())
            {
                self.eventChangeState(MovementStateNames.s_ShortLadder_P_Waiting);
            }
        }
    }
}
