using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state PlayerFallingFromRoll: a roll broke off over an edge. Written from
// spec/movement/roll.md.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.PlayerFallingFromRoll")]
    public class PlayerFallingFromRoll : PlayerFalling
    {
        public static new readonly PlayerFallingFromRoll Instance = new(UcMeta.PlayerFallingFromRoll, PlayerFalling.Instance);

        protected PlayerFallingFromRoll(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The roll broke off over an edge: the spy stands up unless he wants to stay crouched.</summary>
        [Uc("SBase.SPlayerController.PlayerFallingFromRoll.BeginState", Sha1 = "34fd9ad7c2af3ee765262e27a78ebbfbb46080f2")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallingFromRoll.BeginState", Spec = "spec/movement/roll.md#falling-out-of-a-roll-playerfallingfromroll")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.bCrouchWithRoll)
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
            }

            pc.SBPawn?.ResetZones();
        }
    }
}
