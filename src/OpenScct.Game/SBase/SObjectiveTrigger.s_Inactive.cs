using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SObjectiveTrigger
{
    /// <summary>The trigger is out of play after Desactivate (no code in the base class).</summary>
    [Uc("SBase.SObjectiveTrigger.s_Inactive")]
    public class s_Inactive : UcActorState
    {
        public static readonly s_Inactive Instance = new(UcMeta.s_Inactive, null);

        protected s_Inactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }
}
