using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Coop_S_Base: the parent of the co-op climber's states. Written from
// spec/movement/coop.md (s_Coop_S_Base) and spec/combat/drop-attack.md rule 4.
public partial class SPlayerController
{
    /// <remarks>
    /// The damage and blast handlers (PawnTakeDamage, CanBeBlasted, ExplosionHit) are the combat port's (by state
    /// name); the ignored inputs come from the state metadata.
    /// </remarks>
    [Uc("SBase.SPlayerController.s_Coop_S_Base")]
    public class s_Coop_S_Base : UcActorState, IDropAttack, ICanSwitchGoggleManually, ICanActivateLaser
    {
        public static readonly s_Coop_S_Base Instance = new(UcMeta.s_Coop_S_Base, null);

        protected s_Coop_S_Base(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Coop_S_Base.DropAttack", Sha1 = "436227bae99213d424a67ac3ae21e10a88c1619e")]
        [PortOf("uc:SBase.SPlayerController:s_Coop_S_Base.DropAttack", Spec = "spec/combat/drop-attack.md#detection")]
        public virtual void DropAttack(SPlayerController self, SBasePawn PawnToAttack) => performDropAttack(self, PawnToAttack);

        /// <summary>
        /// The climber came down on a pawn: on the server, a living climber deals the 17 drop-attack damage (pill tag
        /// 1), without the attacker's landing state. Not ported here: the score type 16 and the message broadcast
        /// (scoring spec).
        /// </summary>
        [Uc("SBase.SPlayerController.s_Coop_S_Base.performDropAttack", Sha1 = "374d5bc681518bb607b140a1034e218a8e4de16e")]
        [PortOf("uc:SBase.SPlayerController:s_Coop_S_Base.performDropAttack", Spec = "spec/combat/drop-attack.md#applying-the-drop")]
        public virtual void performDropAttack(SPlayerController self, SBasePawn? PawnToAttack)
        {
            if (self.Role != ENetRole.ROLE_Authority || PawnToAttack is null || self.SBPawn is not { Health: > 0 } pawn)
            {
                return;
            }

            PawnToAttack.Controller?.PawnTakeDamage(MovementK.RollKnockDamage, pawn, default, new FVector(0f, 0f, 1f),
                SDamageType.SDamage_DropAttacked, CoopDropPillTag);
        }

        [Provenance(ProvenanceKind.Decoded, "s_Coop_S_Base.performDropAttack pill tag 1")]
        private const int CoopDropPillTag = 1;

        [Uc("SBase.SPlayerController.s_Coop_S_Base.CanSwitchGoggleManually", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Coop_S_Base.CanSwitchGoggleManually", Spec = "spec/movement/coop.md#hoist-climber")]
        public virtual bool CanSwitchGoggleManually(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_Coop_S_Base.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Coop_S_Base.CanActivateLaser", Spec = "spec/movement/coop.md#hoist-climber")]
        public virtual bool CanActivateLaser(SPlayerController self) => false;
    }
}
