using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SFlag
{
    /// <summary>The disk locked by an alarm (disk-flag.md rule 31).</summary>
    [Uc("SBase.SFlag.s_TemporaryInactive")]
    public new class s_TemporaryInactive : SObjectiveTrigger.s_TemporaryInactive
    {
        public static new readonly s_TemporaryInactive Instance = new(UcMeta.s_TemporaryInactive, SObjectiveTrigger.s_TemporaryInactive.Instance);

        protected s_TemporaryInactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SFlag.s_TemporaryInactive.BeginState", Sha1 = "ddfc7c58c4e4dc48d9f8fc10e875fadb69c560a0")]
        [PortOf("uc:SBase.SFlag:s_TemporaryInactive.BeginState", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void BeginState(Actor self)
        {
            var flag = (SFlag)self;
            flag.bLocked = true;
            flag.SetTimer(0f, false);
        }

        [Uc("SBase.SFlag.s_TemporaryInactive.Lock", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SFlag:s_TemporaryInactive.Lock", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void Lock(SObjectiveTrigger self)
        {
        }

        [Uc("SBase.SFlag.s_TemporaryInactive.Reset", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SFlag:s_TemporaryInactive.Reset", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void Reset(SObjectiveTrigger self)
        {
        }

        /// <summary>An open objective comes back 1 s after the unlock; a taken one is deactivated.</summary>
        [Uc("SBase.SFlag.s_TemporaryInactive.UnLock", Sha1 = "bcd13405b4205b7ae0fb3e3c814ae2cf6fca8a53")]
        [PortOf("uc:SBase.SFlag:s_TemporaryInactive.UnLock", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void UnLock(SObjectiveTrigger self)
        {
            var flag = (SFlag)self;
            if (flag.Objective is not { ObjectiveTaken: true })
            {
                flag.SetTimer(K.UnlockReenableDelay, false);
            }
            else
            {
                flag.bLocked = false;
                flag.Desactivate();
            }

            flag.bShouldLock = false;
        }

        [Uc("SBase.SFlag.s_TemporaryInactive.Timer", Sha1 = "8e58b595a2da73848b5c9beff39710208e6d3714")]
        [PortOf("uc:SBase.SFlag:s_TemporaryInactive.Timer", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void Timer(Actor self)
        {
            var flag = (SFlag)self;
            flag.bActive = true;
            flag.bPickable = true;
            if (flag.PickupInteraction is not null)
            {
                flag.PickupInteraction.bCanPickup = true;
            }

            flag.bLocked = false;
            flag.GotoState(K.s_Initial);
        }
    }
}
