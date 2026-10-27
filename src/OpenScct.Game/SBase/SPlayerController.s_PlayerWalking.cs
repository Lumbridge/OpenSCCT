using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalking: the root of the walking family (Mario and weapon movement). Its
// permissions say what a walking player may do. Written from spec/movement/walking.md (permission predicates).
public partial class SPlayerController
{
    /// <remarks>
    /// Not ported here: the damage and blast handlers (PawnTakeDamage, ExplosionHit, PlayAnimDmg), which belong to the
    /// combat specs.
    /// </remarks>
    [Uc("SBase.SPlayerController.s_PlayerWalking")]
    public class s_PlayerWalking : UcActorState, IPlayerTick, ICanInteract, ICanIUseBinocular, ICanUncrouchAndUndraw,
        ICanActivateLaser, ICanBeDropAttacked, ICanBeBlasted, ICanElbowShock, ICanBeElbowShocked, IDropAttack
    {
        public static readonly s_PlayerWalking Instance = new(UcMeta.s_PlayerWalking, null);

        protected s_PlayerWalking(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The walking tick is the global PlayerTick (input, then the state's PlayerMove).</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalking.PlayerTick", Sha1 = "c967a53fc98d2ac69283c17afc0228c5b68d89d7")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.PlayerTick", Spec = "spec/movement/walking.md#transition-and-resting-states")]
        [Provenance(ProvenanceKind.Estimated, "The spec does not describe this body beyond the global PlayerTick it runs")]
        public virtual void PlayerTick(PlayerController self, float DeltaTime) => ((SPlayerController)self).PlayerTick(DeltaTime);

        [Uc("SBase.SPlayerController.s_PlayerWalking.CanInteract", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanInteract", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanInteract(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalking.CanIUseBinocular", Sha1 = "10f9f7bad11963bc14aac1794a3f460939814e43")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanIUseBinocular", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanIUseBinocular(SPlayerController self) => !(self.SBPawn?.bIsRolling ?? false);

        [Uc("SBase.SPlayerController.s_PlayerWalking.CanUncrouchAndUndraw", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanUncrouchAndUndraw", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanUncrouchAndUndraw(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalking.CanActivateLaser", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanActivateLaser", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanActivateLaser(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalking.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanBeDropAttacked", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanBeDropAttacked(SPlayerController self) => true;

        /// <summary>
        /// The walking physics stood the pawn on another pawn (or the falling physics came down on one): the global
        /// performDropAttack, which ends in s_DropAttack_Landing.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerWalking.DropAttack", Sha1 = "7c03aab73cf846514c36402a5c241cfdd7bd85d4")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.DropAttack", Spec = "spec/combat/drop-attack.md#detection")]
        public virtual void DropAttack(SPlayerController self, SBasePawn PawnToAttack) =>
            MovementService.HooksOf(self).PerformDropAttack(self, PawnToAttack);

        /// <summary>A blast knocks a walking player over when its momentum is under BlastDistMax.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalking.CanBeBlasted", Sha1 = "50f6400c40b457eea0c57b8638e312483f57ee3c")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanBeBlasted", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanBeBlasted(SPlayerController self, FVector Momentum) => Momentum.Size() < self.BlastDistMax;

        /// <summary>The elbow attack is allowed unless on a wall (back to wall) or rolling.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalking.CanElbowShock", Sha1 = "2ff90e130a025c4faed5281dc18c4ff120840eae")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanElbowShock", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanElbowShock(SPlayerController self) =>
            self.SBPawn is { } pawn && pawn.BTW == BTW_.NOBTW && !pawn.bIsRolling;

        /// <summary>A walking player can be elbowed unless sticky-shocking.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalking.CanBeElbowShocked", Sha1 = "32131889b93bc14ed6ca7e7fdbde3fc292e9bcae")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanBeElbowShocked", Spec = "spec/movement/walking.md#permission-predicates")]
        public virtual bool CanBeElbowShocked(SPlayerController self) => !(self.SBPawn?.bStickyShocking ?? false);
    }
}
