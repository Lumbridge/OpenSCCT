using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SAlarm
{
    /// <summary>An InitialState for alarms that start raised: every 2 s it tries to raise the alarm.</summary>
    [Uc("SBase.SAlarm.s_InitialyActive")]
    public class s_InitialyActive : UcActorState
    {
        public static readonly s_InitialyActive Instance = new(UcMeta.s_InitialyActive, null);

        protected s_InitialyActive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SAlarm.s_InitialyActive.BeginState", Sha1 = "42eec251b176f35757a22480d6cd753a34a638c8")]
        [PortOf("uc:SBase.SAlarm:s_InitialyActive.BeginState", Spec = "spec/devices/alarm.md#rules")]
        public override void BeginState(Actor self) => self.SetTimer(InitialyActivePeriod, true);

        [Uc("SBase.SAlarm.s_InitialyActive.Timer", Sha1 = "e1b3046777970923e6a93604263994360be9413a")]
        [PortOf("uc:SBase.SAlarm:s_InitialyActive.Timer", Spec = "spec/devices/alarm.md#rules")]
        public override void Timer(Actor self) => ((SAlarm)self).SetAlarmActive();
    }
}
