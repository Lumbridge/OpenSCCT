using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController drop attack (spec/combat/drop-attack.md rules 3, 5, 6, 11 and 12): the attacker's drop on a
// head, the zip line and knocked-out fall requests, the victim's knock-off fall and the attacker's landing. The falling
// physics raises DropAttack in the movement states; the damage handlers are in SPlayerController.Combat.cs.
public partial class SPlayerController
{
    private static readonly Name DNAME_TellServerForDropAttack = new("TellServerForDropAttack");
    private static readonly Name DNAME_TellServerToFallAttack = new("TellServerToFallAttack");
    private static readonly Name DNAME_s_DropAttack_Landing = new("s_DropAttack_Landing");
    private static readonly Name DNAME_s_FallFallDropAttacked = new("s_FallFallDropAttacked");

    /// <summary>The drop damage: 17 on pill 1 (51 after the head multiplier), nothing regenerable.</summary>
    private static void DropDamage(SBasePawn Victim, Pawn Attacker) =>
        Victim.Controller?.PawnTakeDamage(K.DropAttackDamage, Attacker, FVector.Zero, new FVector(0f, 0f, 1f),
            SDamageType.SDamage_DropAttacked, K.DropAttackPill);

    /// <summary>The "has neutralized" message when the victim survived.</summary>
    private void DropMessage(SBasePawn Victim)
    {
        if (Level.Game is { } game)
        {
            game.BroadcastLocalizedMessage(SBasePawn.GameMessagesClass, K.MsgNeutralized, PlayerReplicationInfo,
                Victim.PlayerReplicationInfo);
        }
    }

    /// <summary>
    /// The drop on a head (rule 3, authority): the victim takes the drop, the attacker lands, 2 points to the
    /// attacker, the message when the victim survived.
    /// </summary>
    [Uc("SBase.SPlayerController.performDropAttack", Sha1 = "43f237ae82a6c11147698cd301a79ccc2f6e3f95")]
    [PortOf("uc:SBase.SPlayerController:performDropAttack", Spec = "spec/combat/drop-attack.md#applying-the-drop")]
    public void performDropAttack(SBasePawn? PawnToAttack)
    {
        if (Role != ENetRole.ROLE_Authority || PawnToAttack is null || SBPawn is not { Health: > 0 } me)
        {
            return;
        }

        DropDamage(PawnToAttack, me);
        eventMirrorState(DNAME_s_DropAttack_Landing);
        (Level.Game as SGameInfo)?.AddPoint(0, this, K.AP_D_KnockOut);
        if (PawnToAttack.Health > 0)
        {
            DropMessage(PawnToAttack);
        }
    }

    /// <summary>The zip line drop asked by a remote owner (rule 5): the damage, the points and the message.</summary>
    [Uc("SBase.SPlayerController.TellServerForDropAttack", Sha1 = "d8c35015cdbcc22fa0e66a0ac29cb127eda1c2cf")]
    [PortOf("uc:SBase.SPlayerController:TellServerForDropAttack", Spec = "spec/combat/drop-attack.md#applying-the-drop")]
    [PortOf("rep:SBase.SPlayerController.TellServerForDropAttack", Spec = "spec/combat/drop-attack.md#applying-the-drop")]
    public void TellServerForDropAttack(SBasePawn PawnToAttack)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(DNAME_TellServerForDropAttack) { Actor1 = PawnToAttack });
            return;
        }

        if (SBPawn is not { } me)
        {
            return;
        }

        DropDamage(PawnToAttack, me);
        (Level.Game as SGameInfo)?.AddPoint(0, this, K.AP_D_KnockOut);
        if (PawnToAttack.Health > 0)
        {
            DropMessage(PawnToAttack);
        }
    }

    /// <summary>
    /// A falling knocked-out body lands on someone (rule 6): when that player can be dropped on, he is hit and the
    /// points go to the victim's side as in the original.
    /// </summary>
    [Uc("SBase.SPlayerController.TellServerToFallAttack", Sha1 = "e55b9c7c5146d5da2d01960c0ef8d9f36879cf0d")]
    [PortOf("uc:SBase.SPlayerController:TellServerToFallAttack", Spec = "spec/combat/drop-attack.md#applying-the-drop")]
    [PortOf("rep:SBase.SPlayerController.TellServerToFallAttack", Spec = "spec/combat/drop-attack.md#applying-the-drop")]
    public void TellServerToFallAttack(SBasePawn Other)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(DNAME_TellServerToFallAttack) { Actor1 = Other });
            return;
        }

        if (SBPawn is not { } me || Other.Controller is not SPlayerController victim || !CombatStateEvents.CanBeDropAttacked(victim))
        {
            return;
        }

        Other.m_AttackedBy = me;
        if (Other.SPawnType != me.SPawnType)
        {
            (Level.Game as SGameInfo)?.AddPoint(me is SPawnAttaque ? 0 : 1, victim, K.AP_D_KnockOut);
        }

        DropDamage(Other, me);
        if (Other.Health > 0)
        {
            DropMessage(Other);
        }
    }

    /// <summary>s_FallDropAttacked: a victim knocked off his feet; a walking pawn only hops (rule 11).</summary>
    [Uc("SBase.SPlayerController.s_FallDropAttacked")]
    public class s_FallDropAttacked : UcActorState, IPlayerMove, IMyNotifyLanded, ISwitchBinocularVision
    {
        public static readonly s_FallDropAttacked Instance = new(UcMeta.s_FallDropAttacked, null);

        protected s_FallDropAttacked(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_FallDropAttacked.BeginState", Sha1 = "ef7a3af5311ce9d9d0815a1f770e39d87e0d43fe")]
        [PortOf("uc:SBase.SPlayerController:s_FallDropAttacked.BeginState", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pc.Role == ENetRole.ROLE_Authority && pawn.m_PawnToTerminate?.Controller is SPlayerController finished &&
                !ReferenceEquals(finished, pc))
            {
                finished.eventMercCantTerminate();
            }

            if (pawn.Physics == EPhysics.PHYS_Walking)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, pawn is SPawnAttaque && pawn.WeaponLoaded == 0 ? SBasePawn.SA_JUMP_ST_0H : SBasePawn.SA_JUMP_ST_2H, false);
                return;
            }

            if (!pc.IsRemotePlayer)
            {
                pawn.WeaponLoaded = 0;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_FALL_INERTE, false);
            pc.GotoState(DNAME_s_FallFallDropAttacked);
        }

        [Uc("SBase.SPlayerController.s_FallDropAttacked.MyNotifyLanded", Sha1 = "6638c7a771d2e3a915eade3af93ff10a5a44451f")]
        [PortOf("uc:SBase.SPlayerController:s_FallDropAttacked.MyNotifyLanded", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public virtual bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            bool result = self.MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);
            if (!self.IsRemotePlayer)
            {
                self.eventChangeState(CNAME_s_FallDown, CNAME_DIE_InPlace);
            }

            return result;
        }

        /// <summary>No jump, the view turns, no acceleration.</summary>
        [Uc("SBase.SPlayerController.s_FallDropAttacked.PlayerMove", Sha1 = "cb305367e606750e537a92383f264e05f339f369")]
        [PortOf("uc:SBase.SPlayerController:s_FallDropAttacked.PlayerMove", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.eventUpdateRotation(DeltaTime, 1f);
            if (self.Pawn is { } pawn)
            {
                pawn.Acceleration = FVector.Zero;
            }

            self.SendMove(DeltaTime, FVector.Zero);
        }

        [Uc("SBase.SPlayerController.s_FallDropAttacked.SwitchBinocularVision", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_FallDropAttacked.SwitchBinocularVision", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public void SwitchBinocularVision(SPlayerController self)
        {
        }
    }

    /// <summary>
    /// s_FallFallDropAttacked: the knocked-off body falls; landing on the ground knocks it down, landing on a pawn
    /// drops on him in turn (rule 11).
    /// </summary>
    [Uc("SBase.SPlayerController.s_FallFallDropAttacked")]
    public class s_FallFallDropAttacked : s_FallDropAttacked
    {
        public static new readonly s_FallFallDropAttacked Instance = new(UcMeta.s_FallFallDropAttacked, s_FallDropAttacked.Instance);

        protected s_FallFallDropAttacked(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_FallFallDropAttacked.BeginState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_FallFallDropAttacked.BeginState", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public override void BeginState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_FallFallDropAttacked.MyNotifyLanded", Sha1 = "4e6de1152dc5a6ca3553aded2b75c3ab93bbcd13")]
        [PortOf("uc:SBase.SPlayerController:s_FallFallDropAttacked.MyNotifyLanded", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public override bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            bool result = self.MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);
            if (self.IsRemotePlayer || self.SBPawn is not { } pawn)
            {
                return result;
            }

            if (LandedOn is SBasePawn below)
            {
                self.DamagePawn_C2S(below, K.DropAttackDamage, pawn, FVector.Zero, new FVector(0f, 0f, 1f), SDamageType.SDamage_DropAttacked,
                    K.DropAttackPill);
                return result;
            }

            self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_LAND_INERTE, false);
            self.eventChangeState(CNAME_s_FallDown);
            return result;
        }
    }

    /// <summary>s_DropAttack_Landing: the attacker's landing after a drop (rule 12).</summary>
    [Uc("SBase.SPlayerController.s_DropAttack_Landing")]
    public class s_DropAttack_Landing : UcActorState, IPlayerMove, IMyFalling
    {
        public static readonly s_DropAttack_Landing Instance = new(UcMeta.s_DropAttack_Landing, null);

        protected s_DropAttack_Landing(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_DropAttack_Landing.BeginState", Sha1 = "011fb8010cef5076281c762eb49246671440570e")]
        [PortOf("uc:SBase.SPlayerController:s_DropAttack_Landing.BeginState", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IsRemotePlayer || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_DROPATTACK_LAND, true);
            if (pawn.MyWeapon is SWeaponDefense rifle)
            {
                rifle.AnimateWaiting();
                rifle.AnimatePain(0);
            }
        }

        [Uc("SBase.SPlayerController.s_DropAttack_Landing.EndState", Sha1 = "2c4c1c3f55460a5265853746b14949e496ff9c33")]
        [PortOf("uc:SBase.SPlayerController:s_DropAttack_Landing.EndState", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public override void EndState(Actor self)
        {
            if (((SPlayerController)self).SBPawn is { } pawn)
            {
                pawn.m_AttackTarget = null;
            }
        }

        /// <summary>The landing clip's end: stand up if crouched, then walking by the weapon stance.</summary>
        [Uc("SBase.SPlayerController.s_DropAttack_Landing.AnimEnd", Sha1 = "d0fc85cce3304809b9e3c86791f843a754aeeba2")]
        [PortOf("uc:SBase.SPlayerController:s_DropAttack_Landing.AnimEnd", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.IsRemotePlayer || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.bIsCrouched)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WANTS_TO_UNCROUCH, false);
            }

            bool unarmed = pawn.WeaponLoaded == 0;
            pc.CombatPorts.ChangeEnumAction(pawn, unarmed ? SBasePawn.SA_WALK_ST_0H : SBasePawn.SA_WALK_ST_2H, false);
            pc.eventChangeState(unarmed ? CNAME_s_PlayerWalkingSt0H : CNAME_s_PlayerWalkingSt2H);
        }

        [Uc("SBase.SPlayerController.s_DropAttack_Landing.PlayerMove", Sha1 = "8a1582306e2b8c2a5f01dda33b3bebc2624ac0ff")]
        [PortOf("uc:SBase.SPlayerController:s_DropAttack_Landing.PlayerMove", Spec = "spec/combat/drop-attack.md#victim-reactions-server-after-the-pipeline")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.GrabHoldStill(DeltaTime);

        public void MyFalling(SPlayerController self, float LeftTime)
        {
        }
    }
}
