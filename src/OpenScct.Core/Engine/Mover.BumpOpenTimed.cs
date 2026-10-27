using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    /// <summary>Opens when bumped by an accepted actor (the class default state), holds, closes.</summary>
    [Uc("Engine.Mover.BumpOpenTimed")]
    public class BumpOpenTimed : OpenTimedMover
    {
        public static new readonly BumpOpenTimed Instance = new(UcMeta.BumpOpenTimed, OpenTimedMover.Instance);

        protected BumpOpenTimed(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("Engine.Mover.BumpOpenTimed.Bump", Sha1 = "5f942f4d35545a96f292de5748e636bdd6255b8c")]
        [PortOf("uc:Engine.Mover:BumpOpenTimed.Bump", Spec = "spec/devices/mover.md#state-machines")]
        public override void Bump(Mover self, Actor Other)
        {
            if (!self.PassesBumpType(Other))
            {
                return;
            }

            self.Bump(Other);
            self.SavedTrigger = null;
            self.Instigator = Other as Pawn;

            // Instigator.Controller.WaitForMover(self) is an empty bot navigation hook in this build.
            self.GotoState(nameof(BumpOpenTimed), NAME_Open);
        }

        [Uc("Engine.Mover.BumpOpenTimed.DisableTrigger", Sha1 = "25eff69ab4a76abdb7f8301e1248b7288863a8a1")]
        [PortOf("uc:Engine.Mover:BumpOpenTimed.DisableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void DisableTrigger(Mover self) => self.Disable(NAME_Bump);

        [Uc("Engine.Mover.BumpOpenTimed.EnableTrigger", Sha1 = "0a76f15fd9b84cbc12abc250a92622c70c536027")]
        [PortOf("uc:Engine.Mover:BumpOpenTimed.EnableTrigger", Spec = "spec/devices/mover.md#state-machines")]
        public override void EnableTrigger(Mover self) => self.Enable(NAME_Bump);
    }
}
