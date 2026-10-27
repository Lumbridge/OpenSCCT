using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerRolling: the spy's roll from a crouch (and the parent of the dive roll):
// its direction fixed when it starts and turned toward at the Mario turn rate, ending crouched. Written from
// spec/movement/roll.md.
public partial class SPlayerController
{
    /// <remarks>Not ported here: CanBeBlasted and ExplosionHit while rolling (combat spec).</remarks>
    [Uc("SBase.SPlayerController.s_PlayerRolling")]
    public class s_PlayerRolling : s_PlayerWalkingMario, IMyFalling, IMyNotifyLanded, IDropAttack
    {
        public static new readonly s_PlayerRolling Instance = new(UcMeta.s_PlayerRolling, s_PlayerWalkingMario.Instance);

        protected s_PlayerRolling(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerRolling.BeginState", Sha1 = "efa052f6cca6b149a12105ed610ca71c05e0d340")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.BeginState", Spec = "spec/movement/roll.md#rolling-s_playerrolling-also-the-parent-of-s_playerrollingup")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bRemindDrawWeapon = false;
            pc.MovementCamera(ECamMode.ECM_WalkingCr);
        }

        /// <summary>
        /// A broken roll falls; fire mode asks to draw at the end; the roll vector (controller axes) drives the Mario
        /// turn and the speed, with no dead zone. There is no UpdateRotation here: the controller yaw stays where the
        /// roll began, so neither the mouse nor the keys turn the roll.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerRolling.PlayerMove", Sha1 = "eb8b7cf5ad50558c02eddc7d37b2307dad9372fe")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.PlayerMove", Spec = "spec/movement/roll.md#rolling-s_playerrolling-also-the-parent-of-s_playerrollingup")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if (!pawn.bIsRolling)
            {
                self.eventChangeState(MovementStateNames.PlayerFallingFromRoll);
                return;
            }

            if (self.bFireMode == 1 && !pawn.bStickyShocking)
            {
                self.bRemindDrawWeapon = true;
                MovementService.HooksOf(self).SwitchLaser(self, true);
            }

            FVector newAccel = self.UpdatePawnRotationMario(DeltaTime, MovementMath.Int(self.aForwardRoulade), MovementMath.Int(self.aStrafeRoulade));
            pawn.GroundSpeed = GroundSpeedFor(pawn, newAccel);
            self.SendMove(DeltaTime, newAccel);
        }

        /// <summary>The roll clip ended (roll channel): the crouched resting state, or the draw when asked.</summary>
        [Uc("SBase.SPlayerController.s_PlayerRolling.AnimEnd", Sha1 = "6df337c195ccb8e8de10cf5cc854b8187e3fb91a")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.AnimEnd", Spec = "spec/movement/roll.md#rolling-s_playerrolling-also-the-parent-of-s_playerrollingup")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != MovementK.DoubleJumpChannel || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.m_LastRollingTime = pc.Level.TimeSeconds;
            if (pc.CeilingAbove(pawn))
            {
                pc.bRemindDrawWeapon = false;
                if (pawn.WeaponLoaded != 0)
                {
                    MovementService.HooksOf(pc).SetWeaponLoaded(pawn, 0);
                }
            }

            pc.EndRollCrouched(pawn);
        }

        /// <summary>Leaving the floor: the fall action only; the broken roll is seen at the next move.</summary>
        [Uc("SBase.SPlayerController.s_PlayerRolling.MyFalling", Sha1 = "210e05f0b34ae73a69c383c99435b314b13507d7")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.MyFalling", Spec = "spec/movement/roll.md#rolling-s_playerrolling-also-the-parent-of-s_playerrollingup")]
        public virtual void MyFalling(SPlayerController self, float LeftTime)
        {
            if (self.IAmOnClient())
            {
                self.SBPawn?.ChangeEnumAction(SBasePawn.SA_FALL_ST_0H);
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerRolling.MyNotifyLanded", Sha1 = "0a431fdb48350cd604f13ae06747282d14d2d155")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.MyNotifyLanded", Spec = "spec/movement/roll.md#rolling-s_playerrolling-also-the-parent-of-s_playerrollingup")]
        public virtual bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            self.MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);
            if (self.RemoteRole != ENetRole.ROLE_AutonomousProxy)
            {
                self.SBPawn?.ChangeEnumAction(SBasePawn.SA_ROLL_LAND);
            }

            return true;
        }

        /// <summary>The rolling spy ran into a pawn: the knock (server side).</summary>
        [Uc("SBase.SPlayerController.s_PlayerRolling.DropAttack", Sha1 = "f9f16e8f0dcfb4d8e1700611d5e7e050ac2c5686")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.DropAttack", Spec = "spec/movement/roll.md#rolling-s_playerrolling-also-the-parent-of-s_playerrollingup")]
        public override void DropAttack(SPlayerController self, SBasePawn PawnToAttack) => performDropAttack(self, PawnToAttack);

        /// <summary>
        /// The roll knock: on the server, a living spy deals 17 drop-attack damage (pill tag 1) to the pawn. Not ported
        /// here: the score type 16 and the SGameMessages switch 16 broadcast (scoring spec).
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerRolling.performDropAttack", Sha1 = "807849b9c6bc5bcaba411b69514bb374f57d1cf2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.performDropAttack", Spec = "spec/movement/roll.md#rolling-s_playerrolling-also-the-parent-of-s_playerrollingup")]
        public virtual void performDropAttack(SPlayerController self, SBasePawn? PawnToAttack)
        {
            if (self.Role != ENetRole.ROLE_Authority || PawnToAttack is null || self.SBPawn is not { Health: > 0 } pawn)
            {
                return;
            }

            PawnToAttack.Controller?.PawnTakeDamage(MovementK.RollKnockDamage, pawn, PawnToAttack.Location, pawn.Velocity,
                SDamageType.SDamage_DropAttacked, RollKnockPillTag);
        }

        [Provenance(ProvenanceKind.Decoded, "s_PlayerRolling.performDropAttack pill tag 1")]
        private const int RollKnockPillTag = 1;
    }

    /// <summary>The end of a roll that stays crouched: the draw when asked, else the crouched resting state for the hands.</summary>
    [Provenance(ProvenanceKind.Decoded, "roll.md rolling rule 3")]
    public void EndRollCrouched(SBasePawn pawn)
    {
        if (pawn.WeaponLoaded == 0)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
            if (bRemindDrawWeapon)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0Hto2H);
                eventChangeState(MovementStateNames.s_PlayerWalkingMario_T_0HTo2H);
            }
            else
            {
                eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
            }
        }
        else
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_2H);
            eventChangeState(MovementStateNames.s_PlayerWalkingCr2H);
        }
    }
}
