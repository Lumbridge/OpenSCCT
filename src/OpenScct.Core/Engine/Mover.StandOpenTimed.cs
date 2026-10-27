using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    /// <summary>Opens when an accepted actor starts standing on it, holds, closes.</summary>
    [Uc("Engine.Mover.StandOpenTimed")]
    public class StandOpenTimed : OpenTimedMover
    {
        public static new readonly StandOpenTimed Instance = new(UcMeta.StandOpenTimed, OpenTimedMover.Instance);

        protected StandOpenTimed(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.StandOpenTimed.Attach", Sha1 = "de9c738c1b76acb517c25d560c78f55cb4025a6e")]
        [PortOf("uc:Engine.Mover:StandOpenTimed.Attach", Spec = "spec/devices/mover.md#state-machines")]
        public override void Attach(Mover self, Actor Other)
        {
            if (!self.PassesBumpType(Other))
            {
                return;
            }

            self.SavedTrigger = null;
            self.GotoState(nameof(StandOpenTimed), NAME_Open);
        }

        [Uc("Engine.Mover.StandOpenTimed.DisableTrigger", Sha1 = "6223679f8f8c6eb6736827b064509d31cd44ed3f")]
        [PortOf("uc:Engine.Mover:StandOpenTimed.DisableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void DisableTrigger(Mover self) => self.Disable(NAME_Attach);

        [Uc("Engine.Mover.StandOpenTimed.EnableTrigger", Sha1 = "a8e07952ae49242cb9da286913f73502e82d2cf4")]
        [PortOf("uc:Engine.Mover:StandOpenTimed.EnableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void EnableTrigger(Mover self) => self.Enable(NAME_Attach);
    }
}
