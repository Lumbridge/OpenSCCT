using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_TopPipe: over the top of a vertical pipe onto the floor, standing (a root-motion
// clip). Written from spec/movement/pipe-vertical.md (Leaving at the top).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_TopPipe")]
    public class s_TopPipe : UcActorState, IPlayerMove
    {
        public static readonly s_TopPipe Instance = new(UcMeta.s_TopPipe, null);

        protected s_TopPipe(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_TopPipe.BeginState", Sha1 = "879fcb016bbe933a23204959e6bcac045ad26b3b")]
        [PortOf("uc:SBase.SPlayerController:s_TopPipe.BeginState", Spec = "spec/movement/pipe-vertical.md#leaving-at-the-top-s_toppipe-s_toppipecr")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCamera(ECamMode.ECM_OnLedge);

        [Uc("SBase.SPlayerController.s_TopPipe.PlayerMove", Sha1 = "d75164e312c687efc106e6bee6089e0abbc7ad95")]
        [PortOf("uc:SBase.SPlayerController:s_TopPipe.PlayerMove", Spec = "spec/movement/pipe-vertical.md#leaving-at-the-top-s_toppipe-s_toppipecr")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.eventUpdateRotation(DeltaTime, 1f);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>Over the top: standing on the floor.</summary>
        [Uc("SBase.SPlayerController.s_TopPipe.AnimEnd", Sha1 = "a4dfbc9a2d032add786c406ae2d19db5bcad7e9a")]
        [PortOf("uc:SBase.SPlayerController:s_TopPipe.AnimEnd", Spec = "spec/movement/pipe-vertical.md#leaving-at-the-top-s_toppipe-s_toppipecr")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
            pc.MovementCameraSmooth(SmoothDefault);
            pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
        }
    }
}
