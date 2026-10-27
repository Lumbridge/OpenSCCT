using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state PlayerFalling: the fall after a jump clip or a walk-off. It moves and lands as
// s_PlayerJump. Written from spec/movement/jump-fall-land.md.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.PlayerFalling")]
    public class PlayerFalling : s_PlayerJump, IDropAttack
    {
        public static new readonly PlayerFalling Instance = new(UcMeta.PlayerFalling, s_PlayerJump.Instance);

        protected PlayerFalling(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The normal zones; the merc's weapon plays its fall clip.</summary>
        [Uc("SBase.SPlayerController.PlayerFalling.BeginState", Sha1 = "57a489ffc8371674e8dad616311431db4e24b5f1")]
        [PortOf("uc:SBase.SPlayerController:PlayerFalling.BeginState", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.SBPawn?.ResetZones();
            if (pc.SBPawn is SPAWNDEFENSE { WeaponLoaded: > 0 })
            {
                MovementService.HooksOf(pc).PlayWeaponClip(pc, MovementWeaponClip.Fall);
            }
        }

        /// <summary>No drop-attack target any more, then the jump's EndState (the fall damage).</summary>
        [Uc("SBase.SPlayerController.PlayerFalling.EndState", Sha1 = "af2157c22d2c7d9ef91558002befc2d12c77e678")]
        [PortOf("uc:SBase.SPlayerController:PlayerFalling.EndState", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pawn.m_AttackTarget = null;
            }

            base.EndState(self);
        }

        /// <summary>The falling physics came down on a pawn: the drop attack (combat spec).</summary>
        [Uc("SBase.SPlayerController.PlayerFalling.DropAttack", Sha1 = "0b9feeba92a8222fa265ff1436c0691913f359d7")]
        [PortOf("uc:SBase.SPlayerController:PlayerFalling.DropAttack", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public virtual void DropAttack(SPlayerController self, SBasePawn PawnToAttack) =>
            MovementService.HooksOf(self).PerformDropAttack(self, PawnToAttack);
    }
}
