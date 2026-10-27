using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingCr0H: the spy crouched unarmed (the spawn state, vents and crouch
// gaps). Written from spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PlayerWalkingCr0H")]
    public class s_PlayerWalkingCr0H : s_PlayerWalkingMario, ICanShortLadderHelp, ICanUseCam, ICanSwitchGoggleManually
    {
        public static new readonly s_PlayerWalkingCr0H Instance = new(UcMeta.s_PlayerWalkingCr0H, s_PlayerWalkingMario.Instance);

        protected s_PlayerWalkingCr0H(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingCr0H.BeginState", Sha1 = "2b7e43824c3a3fc4a0c2c1562291b14d31d59258")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr0H.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.SBPawn?.ResetZones();
            pc.MovementCamera(ECamMode.ECM_WalkingCr);
        }

        /// <summary>
        /// A fire-mode request draws the weapon only when the low-ceiling probe is clear (else the request is
        /// dropped), then the walking tick.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingCr0H.PlayerTick", Sha1 = "3bdb9102678716a261d92191faef5978160cc83a")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr0H.PlayerTick", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        [Provenance(ProvenanceKind.Estimated, "The draw check runs before the walking tick (the spec gives no order for this state)")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if (pc.bForceFireMode && pc.SBPawn is { } pawn && pc.CeilingAbove(pawn))
            {
                pc.bFireMode = 0;
                pc.bForceFireMode = false;
            }
            else
            {
                pc.DrawRequest(SBasePawn.SA_WALK_CR_0Hto2H);
            }

            base.PlayerTick(self, DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingCr0H.CanShortLadderHelp", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr0H.CanShortLadderHelp", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanShortLadderHelp(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalkingCr0H.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr0H.CanUseCam", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalkingCr0H.CanSwitchGoggleManually", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr0H.CanSwitchGoggleManually", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanSwitchGoggleManually(SPlayerController self) => true;
    }
}
