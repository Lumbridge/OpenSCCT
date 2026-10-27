using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_P_WaitForShortLadder: the boost helper while the climber jumps from his
// hands (set by the server); the end of the boost clip stands him up. Written from spec/movement/coop.md (short
// ladder, helper, rule 3).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ShortLadder_P_WaitForShortLadder")]
    public class s_ShortLadder_P_WaitForShortLadder : s_ShortLadder_P_Base
    {
        public static new readonly s_ShortLadder_P_WaitForShortLadder Instance =
            new(UcMeta.s_ShortLadder_P_WaitForShortLadder, s_ShortLadder_P_Base.Instance);

        private static readonly Name CoopCrAlHpEdP = new("CoopCrAlHpEdP");

        protected s_ShortLadder_P_WaitForShortLadder(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ShortLadder_P_WaitForShortLadder.PlayerMove", Sha1 = "ec314dafcc4c406baba4d062f4376c1fc40ef296")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_WaitForShortLadder.PlayerMove", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bToggleUsing = 0;
            self.bPressedJump = false;
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>A hit stops the help unless the boost clip has started.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_WaitForShortLadder.ClientDamage", Sha1 = "eb6e226627f2121baa9e3dd86c1ad66a31abe316")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_WaitForShortLadder.ClientDamage", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
        {
            if (self.SBPawn is { } pawn && pawn.PawnAnim[0].Sequence != CoopCrAlHpEdP)
            {
                base.ClientDamage(self, Momentum, DamageType, HitLocation, instigatedBy);
            }
        }

        /// <summary>The boost clip ended (controlling side): the finish, the smooth camera, standing in s_PlayerWalkingSt0H.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_WaitForShortLadder.AnimEnd", Sha1 = "2f5cd37bdb9bfde94ef913209bac89b50267c8b2")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_WaitForShortLadder.AnimEnd", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient())
            {
                base.AnimEnd(self, Channel);
                return;
            }

            if (pc.SBPawn is { } pawn && pawn.PawnAnim[0].Sequence == CoopCrAlHpEdP)
            {
                pc.bPressedJump = false;
                pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_FINISH, true);
                pc.bCrouchWithRoll = false;
                pc.MovementCameraSmooth(CameraSmoothDefault);
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H, true);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
            }
        }
    }
}
