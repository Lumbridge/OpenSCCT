using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ChangeLedgeWait: the corner turn clip from one ledge to the next. Written from
// spec/movement/ledge.md (Corner change).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ChangeLedgeWait")]
    public class s_ChangeLedgeWait : UcActorState, IPlayerMove
    {
        public static readonly s_ChangeLedgeWait Instance = new(UcMeta.s_ChangeLedgeWait, null);

        protected s_ChangeLedgeWait(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ChangeLedgeWait.BeginState", Sha1 = "4bdba3925ab789f3d17717886fbd9174f544c7e3")]
        [PortOf("uc:SBase.SPlayerController:s_ChangeLedgeWait.BeginState", Spec = "spec/movement/ledge.md#corner-change-s_changeledgewait")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCameraSmooth(SmoothDefault);

        [Uc("SBase.SPlayerController.s_ChangeLedgeWait.PlayerMove", Sha1 = "71d21c5d734a9cce1d3f727878e213cc628f4b6d")]
        [PortOf("uc:SBase.SPlayerController:s_ChangeLedgeWait.PlayerMove", Spec = "spec/movement/ledge.md#corner-change-s_changeledgewait")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.LimitedViewByCamera(DeltaTime, 0, 0);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The turn clip ended: the new ledge becomes the current one and the spy hangs from it.</summary>
        [Uc("SBase.SPlayerController.s_ChangeLedgeWait.AnimEnd", Sha1 = "17a5df65ca28839d7867cb18c8ba1316852165bc")]
        [PortOf("uc:SBase.SPlayerController:s_ChangeLedgeWait.AnimEnd", Spec = "spec/movement/ledge.md#corner-change-s_changeledgewait")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ZipP1 = pawn.vWallJumpDir;
            pawn.ZipP2 = pawn.vVandammePos;
            pawn.SendZipPToServer();
            pawn.ChangeEnumAction(SBasePawn.SA_ONLEDGE);
            pc.MovementCameraSmooth(GrabLedge.LedgeCameraSmoothValue);
            pc.eventChangeState(MovementStateNames.OnLedge);
        }
    }
}
