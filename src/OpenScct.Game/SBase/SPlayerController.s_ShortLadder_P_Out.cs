using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_P_Out: the boost helper stands down; the end of the leave clip is
// walking. Written from spec/movement/coop.md (short ladder, helper, rule 4).
//
// The source declares it as extending s_ShortLadder_P_Base; the generated state metadata lists no super state.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ShortLadder_P_Out")]
    public class s_ShortLadder_P_Out : s_ShortLadder_P_Base
    {
        public static new readonly s_ShortLadder_P_Out Instance = new(UcMeta.s_ShortLadder_P_Out, null);

        protected s_ShortLadder_P_Out(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ShortLadder_P_Out.EndState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Out.EndState", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void EndState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_ShortLadder_P_Out.AnimEnd", Sha1 = "98b6c960c9461d28f9eb5f07c679191a8db0c9f8")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Out.AnimEnd", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient())
            {
                pc.GotoWaitingState();
            }
        }
    }
}
