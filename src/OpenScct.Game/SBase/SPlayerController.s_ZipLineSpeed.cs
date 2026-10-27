using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ZipLineSpeed: sliding in the zip speed pose (stick forward). Written from
// spec/movement/zip-line.md (Sliding).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ZipLineSpeed")]
    public class s_ZipLineSpeed : s_ZipLine
    {
        public static new readonly s_ZipLineSpeed Instance = new(UcMeta.s_ZipLineSpeed, s_ZipLine.Instance);

        protected s_ZipLineSpeed(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The speed pose: the end wall, back to normal below 500 with room, the drop; the slide.</summary>
        [Uc("SBase.SPlayerController.s_ZipLineSpeed.PlayerMove", Sha1 = "ee3cfd6a0b32d8686923d2c97f2f3a25e9807db4")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLineSpeed.PlayerMove", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.UpdateCamRotation(DeltaTime);
            if (!pawn.CanUnZipSpeed())
            {
                self.bCrouchWithRoll = false;
            }

            if (self.TestZipWall())
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ZIP_WALL);
                self.eventChangeState(MovementStateNames.s_ZipWall);
            }
            else if (self.aForward < MovementK.ZipSpeedOut && pawn.CanUnZipSpeed())
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_ZIP_NORMAL);
                self.eventChangeState(MovementStateNames.s_ZipLineTrToNormal);
            }
            else if (self.bCrouchWithRoll)
            {
                self.DropFromZip(pawn);
            }

            self.SendMove(DeltaTime, self.ZipSlide(pawn));
        }
    }
}
