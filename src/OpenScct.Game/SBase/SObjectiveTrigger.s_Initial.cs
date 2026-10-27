using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SObjectiveTrigger
{
    /// <summary>The trigger is in play (auto state; no code in the base class).</summary>
    [Uc("SBase.SObjectiveTrigger.s_Initial")]
    public class s_Initial : UcActorState
    {
        public static readonly s_Initial Instance = new(UcMeta.s_Initial, null);

        protected s_Initial(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }
}
