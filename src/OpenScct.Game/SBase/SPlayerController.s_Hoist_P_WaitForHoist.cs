using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Hoist_P_WaitForHoist: the hanging helper while the climber jumps (set by the
// server; the climber's s_Hoist_S_Jump.EndState returns him to OnLedge). Written from spec/movement/coop.md (hoist,
// helper, rule 2).
//
// The source declares it as extending s_Hoist_P_Base; the generated state metadata lists no super state.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_Hoist_P_WaitForHoist")]
    public class s_Hoist_P_WaitForHoist : s_Hoist_P_Base
    {
        public static new readonly s_Hoist_P_WaitForHoist Instance = new(UcMeta.s_Hoist_P_WaitForHoist, null);

        protected s_Hoist_P_WaitForHoist(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Hoist_P_WaitForHoist.PerformCoopMvt", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_WaitForHoist.PerformCoopMvt", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override void PerformCoopMvt(SPlayerController self)
        {
        }

        /// <summary>Held still, the view turning.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_WaitForHoist.PlayerMove", Sha1 = "532098517a931fe9b56f4aba227e535a5a57295e")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_WaitForHoist.PlayerMove", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bToggleUsing = 0;
            self.bPressedJump = false;
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }
}
