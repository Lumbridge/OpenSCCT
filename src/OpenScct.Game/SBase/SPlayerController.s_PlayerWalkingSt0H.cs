using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingSt0H: the spy standing unarmed (Mario walking). Written from
// spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PlayerWalkingSt0H")]
    public class s_PlayerWalkingSt0H : s_PlayerWalkingMario, ICanShortLadderHelp, ICanUseCam, ICanSwitchGoggleManually
    {
        public static new readonly s_PlayerWalkingSt0H Instance = new(UcMeta.s_PlayerWalkingSt0H, s_PlayerWalkingMario.Instance);

        protected s_PlayerWalkingSt0H(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The normal zones, the walking camera, no pending stand-and-holster, a pending quick gadget launched.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingSt0H.BeginState", Sha1 = "011f4c9ec21042c15753b166b8315a252c851199")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt0H.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.SBPawn?.ResetZones();
            pc.MovementCamera(ECamMode.ECM_Walking);
            pc.bUnCrouchAndUnDraw = 0;
            if (pc.bQuickGadgetQuery)
            {
                MovementService.HooksOf(pc).LaunchQuickGadget(pc);
            }
        }

        /// <summary>
        /// A fire-mode request draws the weapon (with the laser when asked; a carried object is thrown instead), then
        /// the walking tick.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingSt0H.PlayerTick", Sha1 = "84d57e1f5ac03e0c7ce44c0b88a660fb6419609b")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt0H.PlayerTick", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        [Provenance(ProvenanceKind.Estimated, "The draw check runs before the walking tick (the spec gives no order for this state)")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.DrawRequest(SBasePawn.SA_WALK_ST_0Hto2H);
            base.PlayerTick(self, DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingSt0H.CanShortLadderHelp", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt0H.CanShortLadderHelp", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanShortLadderHelp(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalkingSt0H.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt0H.CanUseCam", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalkingSt0H.CanSwitchGoggleManually", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt0H.CanSwitchGoggleManually", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanSwitchGoggleManually(SPlayerController self) => true;
    }
}
