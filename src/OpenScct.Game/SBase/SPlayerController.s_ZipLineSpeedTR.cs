using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ZipLineSpeedTR: tucking into the zip speed pose. Written from
// spec/movement/zip-line.md (Sliding).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ZipLineSpeedTR")]
    public class s_ZipLineSpeedTR : s_ZipLine
    {
        public static new readonly s_ZipLineSpeedTR Instance = new(UcMeta.s_ZipLineSpeedTR, s_ZipLine.Instance);

        protected s_ZipLineSpeedTR(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The tuck: no drop without room to leave the speed pose; the end wall; the slide.</summary>
        [Uc("SBase.SPlayerController.s_ZipLineSpeedTR.PlayerMove", Sha1 = "fc02ab43ec5367b348a58fe6e7b84e94393049e5")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLineSpeedTR.PlayerMove", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
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
            else if (self.bCrouchWithRoll)
            {
                self.DropFromZip(pawn);
            }

            self.SendMove(DeltaTime, self.ZipSlide(pawn));
        }

        /// <summary>The tuck ended: the speed pose loop.</summary>
        [Uc("SBase.SPlayerController.s_ZipLineSpeedTR.AnimEnd", Sha1 = "e2a90f47565fb008ebad93e2444490bd2b0131b5")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLineSpeedTR.AnimEnd", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_ON_ZIP_END_TRSPEED);
            pc.eventChangeState(MovementStateNames.s_ZipLineSpeed);
        }
    }
}
