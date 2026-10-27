using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_S_CheckIfOK: the boost climber picks the spot in front of the helper
// and starts the repositioning. Written from spec/movement/coop.md (short ladder, climber, rule 1).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ShortLadder_S_CheckIfOK")]
    public class s_ShortLadder_S_CheckIfOK : s_ItTarget_S_CheckIfOK
    {
        public static new readonly s_ShortLadder_S_CheckIfOK Instance =
            new(UcMeta.s_ShortLadder_S_CheckIfOK, s_ItTarget_S_CheckIfOK.Instance);

        protected s_ShortLadder_S_CheckIfOK(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// The spot 70.5 in front of the helper at the climber's own height (27.5 higher when crouched), facing the
        /// helper; the repositioning, then s_ShortLadder_S_GotoPos.
        /// </summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_S_CheckIfOK.PlayerMove", Sha1 = "90660259051cb0589b780f3e75394d455c582270")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_CheckIfOK.PlayerMove", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            if (self.SBPawn is { m_InteractionTarget: { } mate } pawn && !self.IsInState(MovementStateNames.s_ShortLadder_S_GotoPos))
            {
                mate.Rotation.GetAxes(out FVector x, out _, out _);
                FVector aim = mate.Location + x * MovementK.ShortLadderSpot;
                aim.Z = pawn.Location.Z;
                if (pawn.bIsCrouched)
                {
                    aim.Z += MovementK.ShortLadderCrouchLift;
                }

                pawn.vVandammePos = aim;
                pawn.SetInfoForVandamme(-mate.Rotation.Vector());
                pawn.ChangeEnumAction(SBasePawn.SA_SHL_S_REPOS);
                self.eventChangeState(MovementStateNames.s_ShortLadder_S_GotoPos);
            }

            self.SendMove(DeltaTime, default);
        }
    }
}
