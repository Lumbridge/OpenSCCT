using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController melee (spec/combat/melee-elbow-charge-berserk.md): the elbow strike search and its server
// check, the charge target lock and the post-charge slow-down (the merc's Use choice between charge, berserk and
// elbow is UseEdgeWeapon in SPlayerController.s_PlayerWalkingWeapon.cs). The melee states are in
// SPlayerController.CombatMeleeStates.cs; the victims' reactions (Handle_ElbowDmg, Handle_ChargedDmg) are in
// SPlayerController.Combat.cs.
public partial class SPlayerController
{
    private static readonly Name MNAME_AskServerToElbowAttack = new("AskServerToElbowAttack");
    private static readonly Name MNAME_ForceRotateTo = new("ForceRotateTo");
    private static readonly Name MNAME_Client_Vibration_Charge_Obstacle = new("Client_Vibration_Charge_Obstacle");
    private static readonly Name MNAME_s_ElbowShooting = new("s_ElbowShooting");
    private static readonly Name MNAME_EnemyIsStanding = new("EnemyIsStanding");
    private static readonly Name MNAME_EnemyIsCrouched = new("EnemyIsCrouched");
    private static readonly Name MNAME_s_Firing = new("s_Firing");
    private static readonly Name MNAME_AtakStAlFd2 = new("AtakStAlFd2");
    private static readonly Name MNAME_AtakStAlFd0 = new("AtakStAlFd0");
    private static readonly Name MNAME_AtakStAlDn0 = new("AtakStAlDn0");
    private static readonly AssetRef MSND_RifleButt = new("Sound_Persos.divers.Coup_crosse1");
    private static readonly AssetRef MSND_Elbow = new("Sound_Persos.divers.Coup_coude1");

    /// <summary>The rifle butt sound when the weapon is drawn, else the elbow sound.</summary>
    private AssetRef MeleeSound => SBPawn is { WeaponLoaded: not 0 } ? MSND_RifleButt : MSND_Elbow;

    /// <summary>
    /// The merc's input scale after a charge (rule 3): half speed for 2 s, back to full over 0.5 s (the walking scale
    /// of 0.9 times this).
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.PlayerMove", Spec = "spec/combat/melee-elbow-charge-berserk.md#who-presses-use-and-what-happens")]
    public float PostChargeSpeedFactor()
    {
        float t = Level.TimeSeconds - fLastChargingTime;
        return t < K.PostChargeSlowTime ? K.PostChargeSlowFactor : SvmMath.Min(K.PostChargeSlowFactor + (t - K.PostChargeSlowTime), 1f);
    }

    // ---- elbow ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The owner's elbow (rules 4 to 6): refused by option, state, weapon, stance, shock, the spy cooldown, an
    /// offered interaction or an attack clip already playing; the first pawn in reach and in front with a clear box
    /// path is asked for on the server; with no target a blocked roll plays the whiff.
    /// </summary>
    [Uc("SBase.SPlayerController.FindPawnToElbowShotAndGoForIt", Sha1 = "a0435b8fb77601f40dab3633f739226568518f3c")]
    [PortOf("uc:SBase.SPlayerController:FindPawnToElbowShotAndGoForIt", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
    public bool FindPawnToElbowShotAndGoForIt(bool bCantRollCuzColl)
    {
        if (SBPawn is not { } me)
        {
            return false;
        }

        bool spy = me is SPawnAttaque;
        GameReplicationInfo? gri = GameReplicationInfo;
        if (gri is not null && !(spy ? gri.bAvlb_SPY_Elbow : gri.bAvlb_MERC_Elbow))
        {
            return false;
        }

        float now = Level.TimeSeconds;
        if (!eventCanElbowShock() || me.MyWeapon is { } w && (w.IsInState(CNAME_s_Reloading) || w.IsInState(MNAME_s_Firing)) ||
            (me.bIsCrouched && !me.CanUnCrouch()) || me.bStickyShocking ||
            (spy && now - fLastElbowAttackTime < fTimeBetweenElbowAttack) || (IManager?.HasUsableInteractions() ?? false))
        {
            return false;
        }

        if (CombatPorts.IsAnimating(me, K.AttackChannel))
        {
            Name clip = CombatPorts.AnimSequence(me, K.AttackChannel);
            if (clip == MNAME_AtakStAlFd2 || clip == MNAME_AtakStAlFd0 || clip == MNAME_AtakStAlDn0)
            {
                if (spy && me.WeaponLoaded == 0)
                {
                    bFireMode = 0;
                }

                return false;
            }
        }

        bool found = false;
        FVector facing = me.Rotation.Vector().SafeNormal();
        float cone = spy ? K.ElbowSpyCone : K.ElbowMercCone;
        ICollisionWorld? world = TraceHitZones.CollisionOf(this);
        foreach (Pawn p in XLevel.DynamicActors<Pawn>())
        {
            if (p is not SBasePawn m || ReferenceEquals(m, me) || (spy && m is not SPAWNDEFENSE))
            {
                continue;
            }

            if (!(FVector.Dist(m.Location, me.Location) < K.ElbowReach) || m.bStickyShocking ||
                !(now - m.iLastWakeTime > K.ElbowWakeProtection) || !(((m.Location - me.Location).SafeNormal() | facing) > cone))
            {
                continue;
            }

            found = true;
            var extent = new FVector(K.ElbowBox.X, K.ElbowBox.Y, me.bIsCrouched ? me.CrouchHeight : K.ElbowBox.Z);
            if (world is not null && world.LineCheck(out FCheckResult hit, me, m.Location, me.Location, extent, ETraceFlags.TRACE_AllColliding) &&
                !ReferenceEquals(hit.Actor, m))
            {
                continue;
            }

            AskServerToElbowAttack(m, 0, me, FVector.Zero, m.Location - me.Location, SDamageType.SDamage_ElbowShock);
            fLastElbowAttackTime = now;
            return true;
        }

        if ((found || !spy) && bCantRollCuzColl)
        {
            CombatPorts.ChangeEnumAction(me, SBasePawn.SA_ELBOW_ATTACK_MISSED, true);
            if (!spy)
            {
                me.MyWeapon?.GotoState(CNAME_s_ElbowShocking);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// The server's elbow (rule 7): the target's state must allow it and be within 350 uu; then the 0-damage
    /// elbow hit, 2 points, the attacker turned toward the target and the strike state with the stance label.
    /// </summary>
    [Uc("SBase.SPlayerController.AskServerToElbowAttack", Sha1 = "41c2ea31b2c723e0fe50feed8dab2838bd6fb120")]
    [PortOf("uc:SBase.SPlayerController:AskServerToElbowAttack", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
    [PortOf("rep:SBase.SPlayerController.AskServerToElbowAttack", Spec = "spec/combat/melee-elbow-charge-berserk.md#network")]
    public void AskServerToElbowAttack(SBasePawn Target, int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(MNAME_AskServerToElbowAttack)
            {
                Actor1 = Target,
                Int1 = Damage,
                Actor2 = instigatedBy,
                Vector1 = HitLocation,
                Vector2 = Momentum,
                DamageType = DamageType,
            });
            return;
        }

        if (SBPawn is not { Health: > 0 } me || Target.Controller is not SPlayerController victim ||
            !CombatStateEvents.CanBeElbowShocked(victim) || !(FVector.Dist(Target.Location, me.Location) < K.ElbowServerReach))
        {
            return;
        }

        Target.m_AttackedBy = me;
        victim.PawnTakeDamage(Damage, instigatedBy, HitLocation, Momentum, DamageType, 0, K.MeleeRefillable);
        (Level.Game as SGameInfo)?.AddPoint(0, this, K.AP_D_KnockOut);
        FVector dir = (Target.Location - me.Location).SafeNormal();
        me.Rotation.GetAxes(out FVector x, out _, out _);
        ForceRotateTo(dir.Rotation(), (dir ^ x).Z < 0f ? 1 : -1, K.ForceRotateStep);
        Name label = me is SPawnAttaque && Target.bIsCrouched ? MNAME_EnemyIsCrouched : MNAME_EnemyIsStanding;
        eventMirrorState(MNAME_s_ElbowShooting, label);
    }

    /// <summary>
    /// The owner turns toward a target (reliable server to owner): the target rotation, the side and the step the
    /// native rotation code consumes.
    /// </summary>
    [Uc("SBase.SPlayerController.ForceRotateTo", Sha1 = "6071631e61fc98d60c7721a6389d1a4fc02a1a83")]
    [PortOf("uc:SBase.SPlayerController:ForceRotateTo", Spec = "spec/combat/melee-elbow-charge-berserk.md#network")]
    public void ForceRotateTo(FRotator Target, int Side, int Step)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(MNAME_ForceRotateTo) { Rotation = Target, Int1 = Side, Int2 = Step });
            return;
        }

        FRTarget = Target;
        FRSide = Side;
        FRStep = Step;
        FRStepTime = Level.TimeSeconds;
    }

    // ---- charge ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The charge target (rule 15): the current target is kept; else the nearest visible living pawn of the other
    /// side within 1500 uu and the 0.8 cone of the aim.
    /// </summary>
    [Uc("SBase.SPlayerController.FindTargetToCharge", Sha1 = "2f00e3c5388b2bc61fde7b88bc117c557b8d2e2c")]
    [PortOf("uc:SBase.SPlayerController:FindTargetToCharge", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
    public bool FindTargetToCharge()
    {
        if (TargetPawn is not null)
        {
            return true;
        }

        if (SBPawn is not { } me)
        {
            return false;
        }

        ICollisionWorld? world = TraceHitZones.CollisionOf(this);
        FVector aim = MyDesiredRotation.Vector();
        SBasePawn? best = null;
        float bestDist = float.MaxValue;
        foreach (Pawn p in XLevel.DynamicActors<Pawn>())
        {
            if (p is not SBasePawn t || ReferenceEquals(t, me) || t.SPawnType == me.SPawnType || t.Health <= 0)
            {
                continue;
            }

            FVector d = t.Location - new FVector(0f, 0f, K.ChargeTargetDrop) - me.Location;
            float dist = d.Size();
            if (!((aim | d.SafeNormal()) > K.ChargeTargetCone) || !(dist < K.ChargeTargetRange) || !(dist < bestDist))
            {
                continue;
            }

            if (world is not null && !world.FastTrace(t.Location, me.Location + CombatPorts.EyePosition(me)))
            {
                continue;
            }

            best = t;
            bestDist = dist;
        }

        TargetPawn = best;
        return best is not null;
    }

    /// <summary>The charge ending (rule 18): first the 0.4 s run-out, then armed walking.</summary>
    [Uc("SBase.SPlayerController.s_PlayerCharging.FinDeCharge", Sha1 = "2366a6c31ee6a8d1152f164440939e26f62b1cb3")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.FinDeCharge", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
    public void FinDeCharge()
    {
        if (!bFinDeChargeEnCours)
        {
            bFinDeChargeEnCours = true;
            SetTimer(K.ChargeSecondPhase, false);
            SBPawn?.MyWeapon?.GotoState(MNAME_s_Charging_End);
            if (SBPawn is { } pawn)
            {
                CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_RUN_ST_2H, false);
            }

            return;
        }

        eventChangeState(CNAME_s_PlayerWalkingSt2H);
    }

    private static readonly Name MNAME_s_Charging_End = new("s_Charging_End");

    /// <summary>The obstacle rumble of a berserk hit (reliable server to owner).</summary>
    [Uc("SBase.SPlayerController.Client_Vibration_Charge_Obstacle", Sha1 = "5bce48fd0f37127e8f92f5e0be9188aa947a0989")]
    [PortOf("uc:SBase.SPlayerController:Client_Vibration_Charge_Obstacle", Spec = "spec/combat/melee-elbow-charge-berserk.md#network")]
    public void Client_Vibration_Charge_Obstacle()
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(MNAME_Client_Vibration_Charge_Obstacle));
            return;
        }

        CombatFx.Notify(this, CombatFx.Vibration_Charge);
    }

    /// <summary>The 35 hp charge hit (sent by the charging owner, rule 19).</summary>
    private void ChargeDamage(SBasePawn Victim, SBasePawn From, SDamageType Type) =>
        DamagePawn_C2S(Victim, (int)iChargeDamage, From, Victim.Location, Victim.Location - From.Location, Type, 0, K.MeleeRefillable);

    /// <summary>Turns the charger toward a pawn he hit (rule 19, as rule 7).</summary>
    private void TurnToward(SBasePawn me, Actor other)
    {
        FVector dir = (other.Location - me.Location).SafeNormal();
        me.Rotation.GetAxes(out FVector x, out _, out _);
        ForceRotateTo(dir.Rotation(), (dir ^ x).Z < 0f ? 1 : -1, K.ForceRotateStep);
    }
}
