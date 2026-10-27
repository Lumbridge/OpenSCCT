using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state PlayerFallToBackGrab: a slow spy walked off an edge with a ledge behind his feet; he
// turns in the air to catch it. Written from spec/movement/jump-fall-land.md.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.PlayerFallToBackGrab")]
    public class PlayerFallToBackGrab : PlayerFalling
    {
        public static new readonly PlayerFallToBackGrab Instance = new(UcMeta.PlayerFallToBackGrab, PlayerFalling.Instance);

        protected PlayerFallToBackGrab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The spy stops moving horizontally to turn and catch the ledge behind him.</summary>
        [Uc("SBase.SPlayerController.PlayerFallToBackGrab.BeginState", Sha1 = "3eb9adf3fcb3860d4a73a16274c19c4ba4a8fc08")]
        [PortOf("uc:SBase.SPlayerController:PlayerFallToBackGrab.BeginState", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Pawn is { } pawn)
            {
                pawn.Velocity.X = 0f;
                pawn.Velocity.Y = 0f;
            }

            base.BeginState(self);
        }
    }
}
