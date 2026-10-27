using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ZipLineTrToNormal: back from the speed or slow pose to the normal one. Written
// from spec/movement/zip-line.md (Sliding).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ZipLineTrToNormal")]
    public class s_ZipLineTrToNormal : s_ZipLine
    {
        public static new readonly s_ZipLineTrToNormal Instance = new(UcMeta.s_ZipLineTrToNormal, s_ZipLine.Instance);

        protected s_ZipLineTrToNormal(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The end wall, the drop; the slide.</summary>
        [Uc("SBase.SPlayerController.s_ZipLineTrToNormal.PlayerMove", Sha1 = "6915f68ab8860f2e20aa983ea7af1070a167f933")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLineTrToNormal.PlayerMove", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
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

        /// <summary>Back in the normal pose.</summary>
        [Uc("SBase.SPlayerController.s_ZipLineTrToNormal.AnimEnd", Sha1 = "32cbed79172e8118d5c7e19b6d489ef5f0968662")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLineTrToNormal.AnimEnd", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_ON_ZIP);
            pc.eventChangeState(MovementStateNames.s_ZipLine);
        }
    }
}
