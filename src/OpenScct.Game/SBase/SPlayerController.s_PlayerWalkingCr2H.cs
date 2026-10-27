using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingCr2H: crouched with the weapon. Written from
// spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    /// <remarks>Not ported here: the merc behind-view camera reset and weapon reselection (camera and weapon specs).</remarks>
    [Uc("SBase.SPlayerController.s_PlayerWalkingCr2H")]
    public class s_PlayerWalkingCr2H : s_PlayerWalkingWeapon, ICanUseCam, ICanBeGrabbed
    {
        public static new readonly s_PlayerWalkingCr2H Instance = new(UcMeta.s_PlayerWalkingCr2H, s_PlayerWalkingWeapon.Instance);

        protected s_PlayerWalkingCr2H(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingCr2H.BeginState", Sha1 = "c10158f364f58e3e2a4f9ac47638f4b09e3ac966")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr2H.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is SPAWNDEFENSE merc)
            {
                merc.bCanBeGrabbed = true;
            }
            else
            {
                pc.MovementCamera(ECamMode.ECM_SecondPersonCr);
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingCr2H.EndState", Sha1 = "4a3695eccc6022ca27e9698f922c7d44b0fa4abe")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr2H.EndState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void EndState(Actor self) => ((SPlayerController)self).EndArmedRest();

        /// <summary>The walking tick, then a spy whose fire mode was released holsters (crouched).</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingCr2H.PlayerTick", Sha1 = "2d64cbd63852214c0bad8c3bffdffd49e431bd24")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr2H.PlayerTick", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            base.PlayerTick(self, DeltaTime);
            var pc = (SPlayerController)self;
            if (pc.bCrouchWithRoll)
            {
                pc.HolsterRequest(SBasePawn.SA_WALK_CR_2Hto0H);
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingCr2H.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr2H.CanUseCam", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalkingCr2H.CanBeGrabbed", Sha1 = "9ca447bdebf24bc1fe879d251877cf611c4265e3")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingCr2H.CanBeGrabbed", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanBeGrabbed(SPlayerController self) => self.SBPawn is SPAWNDEFENSE;
    }
}
