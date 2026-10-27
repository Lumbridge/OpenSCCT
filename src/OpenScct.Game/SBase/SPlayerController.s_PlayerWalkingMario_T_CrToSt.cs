using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingMario_T_CrToSt: the spy standing up unarmed (Mario walking goes on
// during the clip). Written from spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_CrToSt")]
    public class s_PlayerWalkingMario_T_CrToSt : s_PlayerWalkingMario
    {
        public static new readonly s_PlayerWalkingMario_T_CrToSt Instance =
            new(UcMeta.s_PlayerWalkingMario_T_CrToSt, s_PlayerWalkingMario.Instance);

        protected s_PlayerWalkingMario_T_CrToSt(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_CrToSt.BeginState", Sha1 = "9301b72453d3362068f4b6560746b4a5fda85dd4")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_CrToSt.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self) => ((SPlayerController)self).MovementCamera(ECamMode.ECM_Walking);

        /// <summary>The stand-up clip ended: standing unarmed.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_CrToSt.AnimEnd", Sha1 = "febaab8806134c60fa7d475c4db1025808edf22b")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_CrToSt.AnimEnd", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
            pc.eventChangeState(Movement.MovementStateNames.s_PlayerWalkingSt0H);
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingMario_T_CrToSt.CanInteract", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario_T_CrToSt.CanInteract", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanInteract(SPlayerController self) => false;
    }
}
