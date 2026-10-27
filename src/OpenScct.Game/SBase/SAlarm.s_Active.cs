using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SAlarm
{
    /// <summary>
    /// Raised: the mercs were told; further triggers re-tell them and restart the countdown (at most once per game
    /// second); the timer or an untrigger stands it down.
    /// </summary>
    [Uc("SBase.SAlarm.s_Active")]
    public class s_Active : UcActorState, ISimulateActivated
    {
        public static readonly s_Active Instance = new(UcMeta.s_Active, null);

        protected s_Active(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SAlarm.s_Active.SimulateActivated", Sha1 = "bfc2755a3aea113b1f25d3b362312f5e48b95fcf")]
        [PortOf("uc:SBase.SAlarm:s_Active.SimulateActivated", Spec = "spec/devices/alarm.md#rules")]
        public virtual void SimulateActivated(SAlarm self, Actor? Other, Pawn? EventInstigator)
        {
            self.MarkTriggerActor(Other);
            self.RepeatAlarm(MsgAlarm);
        }

        /// <summary>The alarm is on; the mercs get the alarm line, and for priorities 0 and 1 the spies get theirs.</summary>
        [Uc("SBase.SAlarm.s_Active.BeginState", Sha1 = "34f2158dc8b117c50f0049b95cd449df9e18571f")]
        [PortOf("uc:SBase.SAlarm:s_Active.BeginState", Spec = "spec/devices/alarm.md#rules")]
        public override void BeginState(Actor self) => Raise((SAlarm)self, MsgAlarm);

        /// <summary>bAlarmOn, the merc line <paramref name="mercSwitch"/>, and the spy line for priorities 0 and 1.</summary>
        protected static void Raise(SAlarm alarm, int mercSwitch)
        {
            alarm.bAlarmOn = true;
            alarm.Broadcast(MercTeam, mercSwitch);
            if (alarm.MessagePriority < InstigatorMessagePriority)
            {
                alarm.Broadcast(SpyTeam, MsgAlarmSpy);
            }
        }

        [Uc("SBase.SAlarm.s_Active.EndState", Sha1 = "bf6a90e4f0e6d3d93399925ee672c6a7c5fda221")]
        [PortOf("uc:SBase.SAlarm:s_Active.EndState", Spec = "spec/devices/alarm.md#rules")]
        public override void EndState(Actor self)
        {
            var alarm = (SAlarm)self;
            alarm.ClearTriggerActor();
            alarm.bAlarmOn = false;
        }

        /// <summary>Duration is over: undo everything and listen again.</summary>
        [Uc("SBase.SAlarm.s_Active.Timer", Sha1 = "d623a9dc051102b73a2edbe32c91d40427065e49")]
        [PortOf("uc:SBase.SAlarm:s_Active.Timer", Spec = "spec/devices/alarm.md#rules")]
        public override void Timer(Actor self) => ((SAlarm)self).SetAlarmListening();

        [Uc("SBase.SAlarm.s_Active.Trigger", Sha1 = "912ba3e09a8d8ba57a667813546eca2fb5a6ccdb")]
        [PortOf("uc:SBase.SAlarm:s_Active.Trigger", Spec = "spec/devices/alarm.md#rules")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var alarm = (SAlarm)self;
            alarm.TellInstigator(EventInstigator);
            alarm.MarkTriggerActor(Other);
            alarm.RepeatAlarm(MsgAlarm);
        }

        /// <summary>Stood down by an event: the mercs get the alarm line again (now off), then everything is undone.</summary>
        [Uc("SBase.SAlarm.s_Active.UnTrigger", Sha1 = "ccc9f7f3c78c5907dfe27d992f3b5b129d8767d4")]
        [PortOf("uc:SBase.SAlarm:s_Active.UnTrigger", Spec = "spec/devices/alarm.md#rules")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var alarm = (SAlarm)self;
            alarm.bAlarmOn = false;
            alarm.Broadcast(MercTeam, MsgAlarm);
            alarm.SetAlarmListening();
            alarm.ClearTriggerActor();
        }
    }
}
