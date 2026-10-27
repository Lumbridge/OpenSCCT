using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SAlarm
{
    /// <summary>Waiting (auto state): a trigger raises the alarm; an untrigger, or a stale timer, keeps it quiet.</summary>
    [Uc("SBase.SAlarm.s_Listening")]
    public class s_Listening : UcActorState, ISimulateActivated
    {
        public static readonly s_Listening Instance = new(UcMeta.s_Listening, null);

        protected s_Listening(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// Tells the instigating player (priority 2 and up), marks a gameplay object that raised it, counts it for a
        /// spy, then raises the alarm.
        /// </summary>
        [Uc("SBase.SAlarm.s_Listening.Trigger", Sha1 = "4bbd24bc4943af41ebe9a260688082fbcaae59df")]
        [PortOf("uc:SBase.SAlarm:s_Listening.Trigger", Spec = "spec/devices/alarm.md#rules")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var alarm = (SAlarm)self;
            alarm.TellInstigator(EventInstigator);
            alarm.MarkTriggerActor(Other);
            CountSpyAlarm(EventInstigator);
            alarm.SetAlarmActive();
        }

        [Uc("SBase.SAlarm.s_Listening.UnTrigger", Sha1 = "d623a9dc051102b73a2edbe32c91d40427065e49")]
        [PortOf("uc:SBase.SAlarm:s_Listening.UnTrigger", Spec = "spec/devices/alarm.md#rules")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator) => ((SAlarm)self).SetAlarmListening();

        [Uc("SBase.SAlarm.s_Listening.SimulateActivated", Sha1 = "63b08a02e69f67e732d9c299625b181d91bcec12")]
        [PortOf("uc:SBase.SAlarm:s_Listening.SimulateActivated", Spec = "spec/devices/alarm.md#rules")]
        public void SimulateActivated(SAlarm self, Actor? Other, Pawn? EventInstigator) => Trigger(self, Other, EventInstigator);

        /// <summary>A countdown left running from s_ActiveDefPresent: clears the marked device and the alarm flag.</summary>
        [Uc("SBase.SAlarm.s_Listening.Timer", Sha1 = "bf6a90e4f0e6d3d93399925ee672c6a7c5fda221")]
        [PortOf("uc:SBase.SAlarm:s_Listening.Timer", Spec = "spec/devices/alarm.md#rules")]
        public override void Timer(Actor self)
        {
            var alarm = (SAlarm)self;
            alarm.ClearTriggerActor();
            alarm.bAlarmOn = false;
        }

        [Uc("SBase.SAlarm.s_Listening.EndState", Sha1 = "eef89be80695cd42a580f243dc465d170da68aba")]
        [PortOf("uc:SBase.SAlarm:s_Listening.EndState", Spec = "spec/devices/alarm.md#rules")]
        public override void EndState(Actor self) => ((SAlarm)self).bAlarmOn = false;
    }
}
