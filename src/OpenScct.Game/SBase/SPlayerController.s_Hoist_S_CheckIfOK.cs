using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Hoist_S_CheckIfOK: the hoist climber picks the spot under the hanging mate and
// starts the repositioning. Written from spec/movement/coop.md (hoist, climber, rule 1).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_Hoist_S_CheckIfOK")]
    public class s_Hoist_S_CheckIfOK : s_ItTarget_S_CheckIfOK
    {
        public static new readonly s_Hoist_S_CheckIfOK Instance = new(UcMeta.s_Hoist_S_CheckIfOK, s_ItTarget_S_CheckIfOK.Instance);

        protected s_Hoist_S_CheckIfOK(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// The spot 130.7 back and 296.7 down from the mate (the climber's own height when that is more than 15 above
        /// him), facing the mate's way; the repositioning, then s_Hoist_S_GotoPos.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Hoist_S_CheckIfOK.PlayerMove", Sha1 = "b2535bedade440763076405c6055063ab675c01e")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_S_CheckIfOK.PlayerMove", Spec = "spec/movement/coop.md#hoist-climber")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            if (self.SBPawn is { m_InteractionTarget: { } mate } pawn && !self.IsInState(MovementStateNames.s_Hoist_S_GotoPos))
            {
                mate.Rotation.GetAxes(out FVector x, out _, out FVector z);
                FVector aim = mate.Location - x * MovementK.HoistOffsetX - z * MovementK.HoistOffsetZ;
                if (aim.Z - pawn.Location.Z > MovementK.HoistSpotTolerance)
                {
                    aim.Z = pawn.Location.Z;
                }

                pawn.vVandammePos = aim;
                pawn.SetInfoForVandamme(mate.Rotation.Vector());
                pawn.ChangeEnumAction(SBasePawn.SA_SHL_S_REPOS);
                self.eventChangeState(MovementStateNames.s_Hoist_S_GotoPos);
            }

            self.SendMove(DeltaTime, default);
        }
    }
}
