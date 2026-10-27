using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingSt2H: standing with the weapon (the merc spawn state, the armed spy).
// Written from spec/movement/walking.md (transition and resting states).
public partial class SPlayerController
{
    /// <remarks>Not ported here: the merc view bob (CheckBob, a camera effect).</remarks>
    [Uc("SBase.SPlayerController.s_PlayerWalkingSt2H")]
    public class s_PlayerWalkingSt2H : s_PlayerWalkingWeapon, ICanUseCam, ICanBeGrabbed
    {
        public static new readonly s_PlayerWalkingSt2H Instance = new(UcMeta.s_PlayerWalkingSt2H, s_PlayerWalkingWeapon.Instance);

        protected s_PlayerWalkingSt2H(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingSt2H.BeginState", Sha1 = "6176b7b3a221fd48c80988212c15af554837ab18")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt2H.BeginState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_SecondPerson);
            if (pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H);
                if (pawn is SPAWNDEFENSE)
                {
                    pawn.bCanBeGrabbed = true;
                }
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingSt2H.EndState", Sha1 = "f6d5cc729f1a4ea49b2b7a833674f0ca2263b441")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt2H.EndState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void EndState(Actor self) => ((SPlayerController)self).EndArmedRest();

        /// <summary>The walking tick, then a spy whose fire mode was released holsters (standing).</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingSt2H.PlayerTick", Sha1 = "ff7010a7b0aa62b3d6c3db7ff9d225eb48cddb82")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt2H.PlayerTick", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            base.PlayerTick(self, DeltaTime);
            var pc = (SPlayerController)self;
            if (!pc.bCrouchWithRoll)
            {
                pc.HolsterRequest(SBasePawn.SA_WALK_ST_2Hto0H);
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingSt2H.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt2H.CanUseCam", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalkingSt2H.CanBeGrabbed", Sha1 = "9ca447bdebf24bc1fe879d251877cf611c4265e3")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingSt2H.CanBeGrabbed", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanBeGrabbed(SPlayerController self) => self.SBPawn is SPAWNDEFENSE;
    }
}
