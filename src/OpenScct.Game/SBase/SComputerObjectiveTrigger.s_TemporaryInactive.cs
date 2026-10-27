using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SComputerObjectiveTrigger
{
    /// <summary>A terminal locked by an alarm (terminal-hack.md rule 29).</summary>
    [Uc("SBase.SComputerObjectiveTrigger.s_TemporaryInactive")]
    public new class s_TemporaryInactive : SObjectiveTrigger.s_TemporaryInactive, ITriggering
    {
        public static new readonly s_TemporaryInactive Instance = new(UcMeta.s_TemporaryInactive, SObjectiveTrigger.s_TemporaryInactive.Instance);

        protected s_TemporaryInactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SComputerObjectiveTrigger.s_TemporaryInactive.BeginState", Sha1 = "0a00f0fe4b60de2b92343894a67dfcfdc3643afc")]
        [PortOf("uc:SBase.SComputerObjectiveTrigger:s_TemporaryInactive.BeginState", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var t = (SComputerObjectiveTrigger)self;
            if (t.bTriggering)
            {
                t.Triggering(false);
            }

            t.bActive = false;
            t.bLocked = true;
            t.SkinState = SKIN_Locked;
        }

        /// <summary>Nothing: bLocked stays set until UnLock.</summary>
        [Uc("SBase.SComputerObjectiveTrigger.s_TemporaryInactive.EndState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SComputerObjectiveTrigger:s_TemporaryInactive.EndState", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void EndState(Actor self)
        {
        }

        /// <summary>While locked a capture request only kicks the taker out.</summary>
        [Uc("SBase.SComputerObjectiveTrigger.s_TemporaryInactive.Triggering", Sha1 = "d11f4db104e558833757d1b2582802d94660cbf9")]
        [PortOf("uc:SBase.SComputerObjectiveTrigger:s_TemporaryInactive.Triggering", Spec = "spec/objectives/terminal-hack.md#rules")]
        public void Triggering(SObjectiveTrigger self, bool Status) => ((SComputerObjectiveTrigger)self).KickTaker();

        [Uc("SBase.SComputerObjectiveTrigger.s_TemporaryInactive.Lock", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SComputerObjectiveTrigger:s_TemporaryInactive.Lock", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void Lock(SObjectiveTrigger self)
        {
        }

        [Uc("SBase.SComputerObjectiveTrigger.s_TemporaryInactive.UnLock", Sha1 = "da8909419d71a54c75a5c11689ef7e505b8118f3")]
        [PortOf("uc:SBase.SComputerObjectiveTrigger:s_TemporaryInactive.UnLock", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void UnLock(SObjectiveTrigger self)
        {
            var t = (SComputerObjectiveTrigger)self;
            if (t.Objective is not { ObjectiveTaken: true })
            {
                t.SkinState = SKIN_Idle;
                t.bActive = true;
                t.bLocked = false;
                t.GotoState(K.s_Initial);
            }
            else
            {
                t.SkinState = SKIN_Hacked;
                t.Desactivate();
            }
        }

        [Uc("SBase.SComputerObjectiveTrigger.s_TemporaryInactive.Reset", Sha1 = "3802c172e23ad38503ce818869c62369f2311863")]
        [PortOf("uc:SBase.SComputerObjectiveTrigger:s_TemporaryInactive.Reset", Spec = "spec/objectives/terminal-hack.md#rules")]
        public override void Reset(SObjectiveTrigger self)
        {
            var t = (SComputerObjectiveTrigger)self;
            t.CaptureTime = t.DominationCaptureTime;
            t.eventTriggering(false);
            t.bEMFIsOn = true;
        }
    }
}
