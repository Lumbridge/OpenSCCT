using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ZipLineSlowTR: going into the zip slow pose. Written from
// spec/movement/zip-line.md (Sliding).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ZipLineSlowTR")]
    public class s_ZipLineSlowTR : s_ZipLine
    {
        public static new readonly s_ZipLineSlowTR Instance = new(UcMeta.s_ZipLineSlowTR, s_ZipLine.Instance);

        protected s_ZipLineSlowTR(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The end wall, the drop; the slide.</summary>
        [Uc("SBase.SPlayerController.s_ZipLineSlowTR.PlayerMove", Sha1 = "cc6a6061ce8c899c95b3eb354dea28e53c5404c5")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLineSlowTR.PlayerMove", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.UpdateCamRotation(DeltaTime);
            if (self.TestZipWall())
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ZIP_WALL);
                self.eventChangeState(MovementStateNames.s_ZipWall);
            }
            else if (self.bCrouchWithRoll)
            {
                self.DropFromZip(pawn);
            }

            self.SendMove(DeltaTime, self.ZipSlide(pawn));
        }

        /// <summary>Into the slow pose loop.</summary>
        [Uc("SBase.SPlayerController.s_ZipLineSlowTR.AnimEnd", Sha1 = "d13a32472969272faecb50878b65338e682a26ce")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLineSlowTR.AnimEnd", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_ON_ZIP_END_TRSLOW);
            pc.eventChangeState(MovementStateNames.s_ZipLineSlow);
        }
    }
}
