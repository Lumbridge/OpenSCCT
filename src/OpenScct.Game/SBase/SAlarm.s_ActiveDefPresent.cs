using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SAlarm
{
    /// <summary>
    /// Raised while a merc stands in the alarm zone: the mercs are told (switch 40) but nothing is triggered or
    /// locked; a later trigger with the zone empty raises it for real.
    /// </summary>
    [Uc("SBase.SAlarm.s_ActiveDefPresent")]
    public class s_ActiveDefPresent : s_Active
    {
        public static new readonly s_ActiveDefPresent Instance = new(UcMeta.s_ActiveDefPresent, s_Active.Instance);

        protected s_ActiveDefPresent(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SAlarm.s_ActiveDefPresent.BeginState", Sha1 = "9efd32c34f18e2574bf28cfa2903aba8835249c6")]
        [PortOf("uc:SBase.SAlarm:s_ActiveDefPresent.BeginState", Spec = "spec/devices/alarm.md#rules")]
        public override void BeginState(Actor self) => Raise((SAlarm)self, MsgAlarmDefPresent);

        [Uc("SBase.SAlarm.s_ActiveDefPresent.EndState", Sha1 = "bf6a90e4f0e6d3d93399925ee672c6a7c5fda221")]
        [PortOf("uc:SBase.SAlarm:s_ActiveDefPresent.EndState", Spec = "spec/devices/alarm.md#rules")]
        public override void EndState(Actor self) => base.EndState(self);

        /// <summary>Duration is over: back to listening without the undo (nothing was done).</summary>
        [Uc("SBase.SAlarm.s_ActiveDefPresent.Timer", Sha1 = "c2b3868789076b71a734cb4f7fba6f2deb08d4fe")]
        [PortOf("uc:SBase.SAlarm:s_ActiveDefPresent.Timer", Spec = "spec/devices/alarm.md#rules")]
        public override void Timer(Actor self) => self.GotoState(nameof(s_Listening));

        [Uc("SBase.SAlarm.s_ActiveDefPresent.SimulateActivated", Sha1 = "287ebe3badf862319a4edee5cad6a1da4b23c990")]
        [PortOf("uc:SBase.SAlarm:s_ActiveDefPresent.SimulateActivated", Spec = "spec/devices/alarm.md#rules")]
        public override void SimulateActivated(SAlarm self, Actor? Other, Pawn? EventInstigator)
        {
            self.MarkTriggerActor(Other);
            self.RepeatAlarm(MsgAlarmDefPresent);
            if (!self.IsDefenseInAlarmZone())
            {
                self.SetAlarmActive();
            }
        }

        [Uc("SBase.SAlarm.s_ActiveDefPresent.Trigger", Sha1 = "3b1d9c99a0dcb65b4665ef80d5e08f5df93e528c")]
        [PortOf("uc:SBase.SAlarm:s_ActiveDefPresent.Trigger", Spec = "spec/devices/alarm.md#rules")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var alarm = (SAlarm)self;
            alarm.TellInstigator(EventInstigator);
            alarm.MarkTriggerActor(Other);
            alarm.RepeatAlarm(MsgAlarmDefPresent);
            if (!alarm.IsDefenseInAlarmZone())
            {
                alarm.SetAlarmActive();
            }
        }

        [Uc("SBase.SAlarm.s_ActiveDefPresent.UnTrigger", Sha1 = "3ac9883629358e19d68206ca68ca3344dc2b52b1")]
        [PortOf("uc:SBase.SAlarm:s_ActiveDefPresent.UnTrigger", Spec = "spec/devices/alarm.md#rules")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var alarm = (SAlarm)self;
            alarm.bAlarmOn = false;
            alarm.Broadcast(MercTeam, MsgAlarmDefPresent);
            alarm.ClearTriggerActor();
            alarm.GotoState(nameof(s_Listening));
        }
    }
}
