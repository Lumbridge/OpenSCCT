using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state PlayerLandingSilent: a quiet crouched landing (no noise). Written from
// spec/movement/jump-fall-land.md (landing states).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.PlayerLandingSilent")]
    public class PlayerLandingSilent : PlayerLanding
    {
        public static new readonly PlayerLandingSilent Instance = new(UcMeta.PlayerLandingSilent, PlayerLanding.Instance);

        protected PlayerLandingSilent(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Only the merc's weapon landing clip: no noise.</summary>
        [Uc("SBase.SPlayerController.PlayerLandingSilent.BeginState", Sha1 = "179fc04502a534dfa369be45bf42b139dedadc84")]
        [PortOf("uc:SBase.SPlayerController:PlayerLandingSilent.BeginState", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override void BeginState(Actor self) => ((SPlayerController)self).LandingWeaponClip();
    }
}
