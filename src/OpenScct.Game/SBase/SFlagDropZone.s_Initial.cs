using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SFlagDropZone
{
    /// <summary>The zone in play (auto state, no code).</summary>
    [Uc("SBase.SFlagDropZone.s_Initial")]
    public new class s_Initial : SObjectiveTrigger.s_Initial
    {
        public static new readonly s_Initial Instance = new(UcMeta.s_Initial, SObjectiveTrigger.s_Initial.Instance);

        protected s_Initial(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }
}
