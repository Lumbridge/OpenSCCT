using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Hoist_S_Base: the hoist climber's parent (s_Coop_S_Base as it is). Written from
// spec/movement/coop.md (hoist, climber).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_Hoist_S_Base")]
    public class s_Hoist_S_Base : s_Coop_S_Base
    {
        public static new readonly s_Hoist_S_Base Instance = new(UcMeta.s_Hoist_S_Base, s_Coop_S_Base.Instance);

        protected s_Hoist_S_Base(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }
}
