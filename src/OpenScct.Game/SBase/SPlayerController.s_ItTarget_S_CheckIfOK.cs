using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ItTarget_S_CheckIfOK: the base of the climber's one-frame check before the
// co-op approach (no move, the view only). Written from spec/movement/coop.md (short ladder, climber).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ItTarget_S_CheckIfOK")]
    public class s_ItTarget_S_CheckIfOK : UcActorState, IPlayerMove
    {
        public static readonly s_ItTarget_S_CheckIfOK Instance = new(UcMeta.s_ItTarget_S_CheckIfOK, null);

        protected s_ItTarget_S_CheckIfOK(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The view turns; the pawn holds still.</summary>
        [Uc("SBase.SPlayerController.s_ItTarget_S_CheckIfOK.PlayerMove", Sha1 = "71e42bfcbe2da87f4df8a6091bcfb267038a898d")]
        [PortOf("uc:SBase.SPlayerController:s_ItTarget_S_CheckIfOK.PlayerMove", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }
}
