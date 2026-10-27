using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SObjectiveTrigger
{
    /// <summary>Locked by an alarm (objective-trigger.md rule 18).</summary>
    [Uc("SBase.SObjectiveTrigger.s_TemporaryInactive")]
    public class s_TemporaryInactive : UcActorState, ITakeObjective, ILock, IUnLock, IReset
    {
        public static readonly s_TemporaryInactive Instance = new(UcMeta.s_TemporaryInactive, null);

        protected s_TemporaryInactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SObjectiveTrigger.s_TemporaryInactive.BeginState", Sha1 = "02593afa3e78ae77be2d9b4de3ab4db2755819b0")]
        [PortOf("uc:SBase.SObjectiveTrigger:s_TemporaryInactive.BeginState", Spec = "spec/objectives/objective-trigger.md#rules")]
        public override void BeginState(Actor self)
        {
            var t = (SObjectiveTrigger)self;
            t.bActive = false;
            t.bLocked = true;
            t.bTriggering = false;
        }

        [Uc("SBase.SObjectiveTrigger.s_TemporaryInactive.EndState", Sha1 = "89c7da442b49b477abe85bbfad057e6fab778b96")]
        [PortOf("uc:SBase.SObjectiveTrigger:s_TemporaryInactive.EndState", Spec = "spec/objectives/objective-trigger.md#rules")]
        public override void EndState(Actor self) => ((SObjectiveTrigger)self).bLocked = false;

        /// <summary>Ignored while locked, except by bomb targets, which complete normally.</summary>
        [Uc("SBase.SObjectiveTrigger.s_TemporaryInactive.TakeObjective", Sha1 = "09064327b0f993161ba9fe83f8dcacdf5b164399")]
        [PortOf("uc:SBase.SObjectiveTrigger:s_TemporaryInactive.TakeObjective", Spec = "spec/objectives/objective-trigger.md#rules")]
        public virtual void TakeObjective(SObjectiveTrigger self)
        {
            if (self is SBombTargetObjectiveTrigger)
            {
                self.TakeObjective();
            }
        }

        [Uc("SBase.SObjectiveTrigger.s_TemporaryInactive.Lock", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SObjectiveTrigger:s_TemporaryInactive.Lock", Spec = "spec/objectives/objective-trigger.md#rules")]
        public virtual void Lock(SObjectiveTrigger self)
        {
        }

        [Uc("SBase.SObjectiveTrigger.s_TemporaryInactive.UnLock", Sha1 = "0cbf16d7ab7a04eac24a43085cdaf6c8f2a3c20c")]
        [PortOf("uc:SBase.SObjectiveTrigger:s_TemporaryInactive.UnLock", Spec = "spec/objectives/objective-trigger.md#rules")]
        public virtual void UnLock(SObjectiveTrigger self)
        {
            if (self.Objective is not { ObjectiveTaken: true })
            {
                self.SetStaticMesh(self.InitialStaticMesh);
                self.bActive = true;
                self.bLocked = false;
                self.GotoState(K.s_Initial);
            }
            else
            {
                self.Desactivate();
            }
        }

        /// <summary>A reset while locked only stops the capture; the trigger stays locked.</summary>
        [Uc("SBase.SObjectiveTrigger.s_TemporaryInactive.Reset", Sha1 = "fd8a546476dc3d73668f4666a2c8b1688f625766")]
        [PortOf("uc:SBase.SObjectiveTrigger:s_TemporaryInactive.Reset", Spec = "spec/objectives/objective-trigger.md#rules")]
        public virtual void Reset(SObjectiveTrigger self)
        {
            self.eventTriggering(false);
            self.bEMFIsOn = true;
        }
    }
}
