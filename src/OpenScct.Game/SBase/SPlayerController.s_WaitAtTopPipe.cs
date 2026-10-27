using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_WaitAtTopPipe: the spy is repositioned to the top of a flush pipe before
// climbing down onto it. Written from spec/movement/pipe-vertical.md (Getting on from the top).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_WaitAtTopPipe")]
    public class s_WaitAtTopPipe : UcActorState, IPlayerMove, ITopSynchro, INotifyBump
    {
        public static readonly s_WaitAtTopPipe Instance = new(UcMeta.s_WaitAtTopPipe, null);

        protected s_WaitAtTopPipe(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_WaitAtTopPipe.PlayerMove", Sha1 = "5cdabbcfbe9d52a24d57774a2a2999c68719daf4")]
        [PortOf("uc:SBase.SPlayerController:s_WaitAtTopPipe.PlayerMove", Spec = "spec/movement/pipe-vertical.md#getting-on-from-the-top-s_waitattoppipe-s_downtoppipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>At the top attach point: the climb down onto the pipe from the side the spy came from.</summary>
        [Uc("SBase.SPlayerController.s_WaitAtTopPipe.TopSynchro", Sha1 = "8e97550350260e54e991b0e8d6af8b4365cd6041")]
        [PortOf("uc:SBase.SPlayerController:s_WaitAtTopPipe.TopSynchro", Spec = "spec/movement/pipe-vertical.md#getting-on-from-the-top-s_waitattoppipe-s_downtoppipe")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(pawn.bIsLeftPart ? SBasePawn.SA_DOWN_TOPPIPE2 : SBasePawn.SA_DOWN_TOPPIPE);
            self.eventChangeState(MovementStateNames.s_downTopPipe);
        }

        /// <summary>Someone is in the way: the repositioning stops and the spy rests.</summary>
        [Uc("SBase.SPlayerController.s_WaitAtTopPipe.NotifyBump", Sha1 = "47783fd4220b19eae85035865592141d054eeeee")]
        [PortOf("uc:SBase.SPlayerController:s_WaitAtTopPipe.NotifyBump", Spec = "spec/movement/pipe-vertical.md#getting-on-from-the-top-s_waitattoppipe-s_downtoppipe")]
        public virtual bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_EXIT_REPOS);
                self.GotoWaitingState();
            }

            return false;
        }
    }
}
