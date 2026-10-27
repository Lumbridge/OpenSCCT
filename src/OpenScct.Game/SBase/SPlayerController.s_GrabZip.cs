using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_GrabZip: the spy caught a zip line and the grab clip plays while he starts down
// it. Written from spec/movement/zip-line.md (s_GrabZip).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_GrabZip")]
    public class s_GrabZip : UcActorState, IPlayerMove, INoGE
    {
        public static readonly s_GrabZip Instance = new(UcMeta.s_GrabZip, null);

        protected s_GrabZip(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>A slow pull along the line (100, GroundSpeed 201.6); crouch drops.</summary>
        [Uc("SBase.SPlayerController.s_GrabZip.PlayerMove", Sha1 = "09abb68860e0184dfad267df9518e574b7e4aa57")]
        [PortOf("uc:SBase.SPlayerController:s_GrabZip.PlayerMove", Spec = "spec/movement/zip-line.md#s_grabzip-ignores-bump-seeplayer-hearnoise-zipline-switchbinocularvision")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            FVector accel = pawn.m_geoNormal * MovementK.ZipGrabPull;
            pawn.GroundSpeed = pawn.RunningSpeed * MovementK.ZipGrabPull / MovementK.ZipSpeedDivisor;
            self.UpdateCamRotation(DeltaTime);
            if (self.bCrouchWithRoll)
            {
                self.DropFromZip(pawn);
            }

            self.SendMove(DeltaTime, accel);
        }

        /// <summary>The grab clip ended: sliding.</summary>
        [Uc("SBase.SPlayerController.s_GrabZip.AnimEnd", Sha1 = "7825c15391a17c75bbad1e225c7a36f9d6d9a819")]
        [PortOf("uc:SBase.SPlayerController:s_GrabZip.AnimEnd", Spec = "spec/movement/zip-line.md#s_grabzip-ignores-bump-seeplayer-hearnoise-zipline-switchbinocularvision")]
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

        [Uc("SBase.SPlayerController.s_GrabZip.NoGE", Sha1 = "dec696b1291489adb391afb0bc8c5c42bf1b31e3")]
        [PortOf("uc:SBase.SPlayerController:s_GrabZip.NoGE", Spec = "spec/movement/zip-line.md#s_grabzip-ignores-bump-seeplayer-hearnoise-zipline-switchbinocularvision")]
        public virtual void NoGE(SPlayerController self)
        {
            if (self.SBPawn is { } pawn)
            {
                self.DropFromZip(pawn);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabZip.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_GrabZip.EndState", Spec = "spec/movement/zip-line.md#s_grabzip-ignores-bump-seeplayer-hearnoise-zipline-switchbinocularvision")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }
    }
}
