using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ZipLineSlow: sliding in the zip slow pose (stick back). Written from
// spec/movement/zip-line.md (Sliding).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ZipLineSlow")]
    public class s_ZipLineSlow : s_ZipLine
    {
        public static new readonly s_ZipLineSlow Instance = new(UcMeta.s_ZipLineSlow, s_ZipLine.Instance);

        protected s_ZipLineSlow(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The slow pose: back to normal above -500 (the end wall wins the same frame), the drop; the slide.</summary>
        [Uc("SBase.SPlayerController.s_ZipLineSlow.PlayerMove", Sha1 = "58ff8ff55910ca34b2fd3c91c0efdb4147592c14")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLineSlow.PlayerMove", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.UpdateCamRotation(DeltaTime);
            if (self.aForward > -MovementK.ZipSpeedOut)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_ZIP_SLOW2NORMAL);
                self.eventChangeState(MovementStateNames.s_ZipLineTrToNormal);
            }

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
    }
}
