using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SFlag
{
    /// <summary>After Desactivate (disk-flag.md rule 32).</summary>
    [Uc("SBase.SFlag.s_Inactive")]
    public new class s_Inactive : SObjectiveTrigger.s_Inactive
    {
        public static new readonly s_Inactive Instance = new(UcMeta.s_Inactive, SObjectiveTrigger.s_Inactive.Instance);

        protected s_Inactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>StoppedMoving (which re-enters s_Initial), then no collision.</summary>
        [Uc("SBase.SFlag.s_Inactive.BeginState", Sha1 = "6d279075577ed5c04eaf26effae35debca12c331")]
        [PortOf("uc:SBase.SFlag:s_Inactive.BeginState", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void BeginState(Actor self)
        {
            var flag = (SFlag)self;
            flag.eventStoppedMoving();
            flag.SetCollision(false, false, false);
        }

        [Uc("SBase.SFlag.s_Inactive.Timer", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SFlag:s_Inactive.Timer", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void Timer(Actor self)
        {
        }
    }
}
