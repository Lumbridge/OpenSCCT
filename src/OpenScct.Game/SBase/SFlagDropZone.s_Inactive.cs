using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SFlagDropZone
{
    /// <summary>The zone out of play (no code).</summary>
    [Uc("SBase.SFlagDropZone.s_Inactive")]
    public new class s_Inactive : SObjectiveTrigger.s_Inactive
    {
        public static new readonly s_Inactive Instance = new(UcMeta.s_Inactive, SObjectiveTrigger.s_Inactive.Instance);

        protected s_Inactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }
}
