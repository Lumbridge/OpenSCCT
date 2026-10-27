using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_GrabLadder: the grab clip at the foot of a ladder (both teams). Written from
// spec/movement/ladder.md (grab).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_GrabLadder")]
    public class s_GrabLadder : UcActorState, IPlayerMove
    {
        public static readonly s_GrabLadder Instance = new(UcMeta.s_GrabLadder, null);

        protected s_GrabLadder(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>A merc on the controlling side takes the head camera and puts his weapon away.</summary>
        [Uc("SBase.SPlayerController.s_GrabLadder.BeginState", Sha1 = "856cd71c3fe6a1bb4c9a6eb5b9614cfbcd91c6bf")]
        [PortOf("uc:SBase.SPlayerController:s_GrabLadder.BeginState", Spec = "spec/movement/ladder.md#grab-s_grabladder")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && pc.SPawnType == PawnType.SPAWNDEFENSE)
            {
                pc.MercLadderCamera();
            }
        }

        /// <summary>The grab clip ended: the hold (spy on the right hand, merc on the left), climbing.</summary>
        [Uc("SBase.SPlayerController.s_GrabLadder.AnimEnd", Sha1 = "5d00fe25c5b56fcc7bce2a381463c6f9632e7cda")]
        [PortOf("uc:SBase.SPlayerController:s_GrabLadder.AnimEnd", Spec = "spec/movement/ladder.md#grab-s_grabladder")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(pawn.SPawnType == PawnType.SPAWNOFFENSE ? SBasePawn.SA_ON_LADDER : SBasePawn.SA_ON_LADDERW);
            pc.MovementCamera(ECamMode.ECM_OnLadder);
            pc.eventChangeState(MovementStateNames.s_OnLadder);
        }

        [Uc("SBase.SPlayerController.s_GrabLadder.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_GrabLadder.EndState", Spec = "spec/movement/ladder.md#grab-s_grabladder")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        /// <summary>Standing up, no crouch latch; the spy's free view, the merc's head view; the approach move (0, 20, 0).</summary>
        [Uc("SBase.SPlayerController.s_GrabLadder.PlayerMove", Sha1 = "0ad30f2c754e304f3bbc6e2b4d94904b2dda813c")]
        [PortOf("uc:SBase.SPlayerController:s_GrabLadder.PlayerMove", Spec = "spec/movement/ladder.md#grab-s_grabladder")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.ZoneHorsCrouch();
            self.bCrouchWithRoll = false;
            if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
            {
                self.eventUpdateRotation(DeltaTime, 1f);
            }
            else
            {
                self.bFirstPersonWithPawn = true;
                self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            }

            self.SendMove(DeltaTime, MovementK.LadderApproachAccel);
        }
    }
}
