using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SBombTargetObjectiveTrigger
{
    /// <summary>A bomb spot locked by an alarm (bomb-target.md rule 16); a planted charge still completes it.</summary>
    [Uc("SBase.SBombTargetObjectiveTrigger.s_TemporaryInactive")]
    public new class s_TemporaryInactive : SObjectiveTrigger.s_TemporaryInactive
    {
        public static new readonly s_TemporaryInactive Instance = new(UcMeta.s_TemporaryInactive, SObjectiveTrigger.s_TemporaryInactive.Instance);

        protected s_TemporaryInactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SBombTargetObjectiveTrigger.s_TemporaryInactive.BeginState", Sha1 = "b808e393fb4456b1f3c777745c077d3ffeeb62bb")]
        [PortOf("uc:SBase.SBombTargetObjectiveTrigger:s_TemporaryInactive.BeginState", Spec = "spec/objectives/bomb-target.md#rules")]
        public override void BeginState(Actor self) => ((SObjectiveTrigger)self).bLocked = true;

        [Uc("SBase.SBombTargetObjectiveTrigger.s_TemporaryInactive.EndState", Sha1 = "89c7da442b49b477abe85bbfad057e6fab778b96")]
        [PortOf("uc:SBase.SBombTargetObjectiveTrigger:s_TemporaryInactive.EndState", Spec = "spec/objectives/bomb-target.md#rules")]
        public override void EndState(Actor self) => ((SObjectiveTrigger)self).bLocked = false;

        /// <summary>An open objective makes the spot usable again; a taken one deactivates it.</summary>
        [Uc("SBase.SBombTargetObjectiveTrigger.s_TemporaryInactive.UnLock", Sha1 = "02d62b247156eda54b8410a2042bdc0e71122880")]
        [PortOf("uc:SBase.SBombTargetObjectiveTrigger:s_TemporaryInactive.UnLock", Spec = "spec/objectives/bomb-target.md#rules")]
        public override void UnLock(SObjectiveTrigger self)
        {
            if (self.Objective is not { ObjectiveTaken: true })
            {
                if (self.InteractionObj is not null)
                {
                    self.InteractionObj.bUsable = true;
                }

                self.GotoState(K.s_Initial);
            }
            else
            {
                self.Desactivate();
            }
        }

        [Uc("SBase.SBombTargetObjectiveTrigger.s_TemporaryInactive.Reset", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SBombTargetObjectiveTrigger:s_TemporaryInactive.Reset", Spec = "spec/objectives/bomb-target.md#rules")]
        public override void Reset(SObjectiveTrigger self)
        {
        }
    }
}
