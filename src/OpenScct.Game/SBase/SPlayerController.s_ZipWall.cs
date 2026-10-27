using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ZipWall: the zip line ends at a wall; the hit clip, then the fall. Written from
// spec/movement/zip-line.md (Wall at the end).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ZipWall")]
    public class s_ZipWall : UcActorState, IPlayerMove
    {
        public static readonly s_ZipWall Instance = new(UcMeta.s_ZipWall, null);

        protected s_ZipWall(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The walking camera (the slide sound stops with the sound port).</summary>
        [Uc("SBase.SPlayerController.s_ZipWall.BeginState", Sha1 = "51e77d5a67e70718d857d10b44a3447adca71159")]
        [PortOf("uc:SBase.SPlayerController:s_ZipWall.BeginState", Spec = "spec/movement/zip-line.md#wall-at-the-end-s_zipwall")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCamera(ECamMode.ECM_Walking);

        [Uc("SBase.SPlayerController.s_ZipWall.PlayerMove", Sha1 = "ffe6336527959475e8307ec9ee1911ce6628ba9c")]
        [PortOf("uc:SBase.SPlayerController:s_ZipWall.PlayerMove", Spec = "spec/movement/zip-line.md#wall-at-the-end-s_zipwall")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The hit clip ended: falling from the line.</summary>
        [Uc("SBase.SPlayerController.s_ZipWall.AnimEnd", Sha1 = "39ce35e75789757b9e09d9c04c884505335cfe83")]
        [PortOf("uc:SBase.SPlayerController:s_ZipWall.AnimEnd", Spec = "spec/movement/zip-line.md#wall-at-the-end-s_zipwall")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_ZIPFALL);
            pc.eventChangeState(MovementStateNames.s_PlayerJump);
        }

        [Uc("SBase.SPlayerController.s_ZipWall.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_ZipWall.EndState", Spec = "spec/movement/zip-line.md#wall-at-the-end-s_zipwall")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }
    }
}
