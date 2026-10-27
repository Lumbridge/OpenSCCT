using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_LadderBase: the parent of the ladder states (both teams): binoculars off, the drop
// with crouch, the floor at the bottom. Written from spec/movement/ladder.md (common ladder behaviour).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_LadderBase")]
    public class s_LadderBase : UcActorState, INotifyHitWall, ICanBeDropAttacked
    {
        public static readonly s_LadderBase Instance = new(UcMeta.s_LadderBase, null);

        protected s_LadderBase(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_LadderBase.BeginState", Sha1 = "e77ba9b207fd6559e1abea4c699527b69a90f56d")]
        [PortOf("uc:SBase.SPlayerController:s_LadderBase.BeginState", Spec = "spec/movement/ladder.md#common-ladder-behaviour-s_ladderbase-and-every-state-that-extends-it")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bBinocularVision = false;
            pc.LocalMyDesiredRotation = pc.MyDesiredRotation;
        }

        [Uc("SBase.SPlayerController.s_LadderBase.EndState", Sha1 = "f667ea40ba7adb48167fd6406616de14e386a97e")]
        [PortOf("uc:SBase.SPlayerController:s_LadderBase.EndState", Spec = "spec/movement/ladder.md#common-ladder-behaviour-s_ladderbase-and-every-state-that-extends-it")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.LocalMyDesiredRotation = pc.MyDesiredRotation;
        }

        /// <summary>
        /// Let go of the ladder: crouch latch off, the push-off jump, the merc's camera back off his head, the fall
        /// clip and the jump state.
        /// </summary>
        [Uc("SBase.SPlayerController.s_LadderBase.JumpFromLadder", Sha1 = "9fa856f25361be41ebc430ff42b1de8c568367a8")]
        [PortOf("uc:SBase.SPlayerController:s_LadderBase.JumpFromLadder", Spec = "spec/movement/ladder.md#common-ladder-behaviour-s_ladderbase-and-every-state-that-extends-it")]
        public virtual void JumpFromLadder(SPlayerController self)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.bCrouchWithRoll = false;
            pawn.MyDoJump();
            self.m_LastSpecialMoveTime = self.Level.TimeSeconds;
            if (self.SPawnType == PawnType.SPAWNDEFENSE)
            {
                self.MercLadderCameraOff();
            }

            pawn.ChangeEnumAction(SBasePawn.SA_FALL_ST_0H);
            self.eventChangeState(MovementStateNames.s_PlayerJump);
        }

        [Uc("SBase.SPlayerController.s_LadderBase.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_LadderBase.CanBeDropAttacked", Spec = "spec/movement/ladder.md#common-ladder-behaviour-s_ladderbase-and-every-state-that-extends-it")]
        public virtual bool CanBeDropAttacked(SPlayerController self) => true;

        /// <summary>The feet reached the floor at the bottom: the spy stands, the merc draws his weapon again.</summary>
        [Uc("SBase.SPlayerController.s_LadderBase.NotifyHitWall", Sha1 = "162bb9676ffbef174ca0faffa9827925aa3f3969")]
        [PortOf("uc:SBase.SPlayerController:s_LadderBase.NotifyHitWall", Spec = "spec/movement/ladder.md#common-ladder-behaviour-s_ladderbase-and-every-state-that-extends-it")]
        public virtual bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return false;
            }

            self.MovementCamera(ECamMode.ECM_Walking);
            self.bCrouchWithRoll = false;
            self.bPressedJump = false;
            if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                self.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
            }
            else
            {
                self.ResetMercCam();
                pawn.ChangeEnumAction(SBasePawn.SA_LADDER_DOWNW);
                self.LocalMyDesiredRotation = self.MyDesiredRotation;
                self.eventChangeState(MovementStateNames.s_EndLadderDrawWeapon);
            }

            return true;
        }
    }
}
