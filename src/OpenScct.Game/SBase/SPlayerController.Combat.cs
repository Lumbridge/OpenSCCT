using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;
using OK = OpenScct.Game.Objectives.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController damage pipeline: PawnTakeDamage and its per-state handlers, StandardReactionsToDamage
// (protection, hit zones, health, statistics, history and the kill hand-off), the client-reported damage RPC and
// the owner feedback (ClientDamage, PlayAnimDmg, PlayAnimDeath). Written from spec/combat/damage-pipeline.md and
// spec/combat/state-damage-reactions.md.
//
// State versions: a ported state implements the nested interface; states without a port use the handler the spec's
// dispatch table gives them (CombatDispatch), so the reactions hold before every state is ported.
public partial class SPlayerController
{
    private static readonly Name CNAME_s_Dead = new("s_Dead");
    private static readonly Name CNAME_GameEnded = new("GameEnded");
    private static readonly Name CNAME_s_PlayerWalkingSt2H = new("s_PlayerWalkingSt2H");
    private static readonly Name CNAME_s_PlayerWalkingCr0H = new("s_PlayerWalkingCr0H");
    private static readonly Name CNAME_s_FallDropAttacked = new("s_FallDropAttacked");
    private static readonly Name CNAME_PlayerFallingInerte = new("PlayerFallingInerte");
    private static readonly Name CNAME_s_Charge_GoBackward = new("s_Charge_GoBackward");
    private static readonly Name CNAME_s_ElbowShot_GoBackward = new("s_ElbowShot_GoBackward");
    private static readonly Name CNAME_s_FallDown = new("s_FallDown");
    private static readonly Name CNAME_s_Blast = new("s_Blast");
    private static readonly Name CNAME_s_Binoculars_Def = new("s_Binoculars_Def");
    private static readonly Name CNAME_s_TerminatedPawn_Released = new("s_TerminatedPawn_Released");
    private static readonly Name CNAME_s_GrabbingFromLedge_Begin = new("s_GrabbingFromLedge_Begin");
    private static readonly Name CNAME_s_PlaceBomb_End = new("s_PlaceBomb_End");
    private static readonly Name CNAME_s_PlayerWalkingSt0H = new("s_PlayerWalkingSt0H");
    private static readonly Name CNAME_s_Maintain_Leave = new("s_Maintain_Leave");
    private static readonly Name CNAME_DIE_Forward = new("DIE_Forward");
    private static readonly Name CNAME_DIE_Backward = new("DIE_Backward");
    private static readonly Name CNAME_DIE_Left = new("DIE_Left");
    private static readonly Name CNAME_DIE_Right = new("DIE_Right");
    private static readonly Name CNAME_DIE_InPlace = new("DIE_InPlace");
    private static readonly Name CNAME_DIE_DropAttacked = new("DIE_DropAttacked");

    private static readonly FVector DeathFallMomentum = new(0f, 0f, 1f);

    // ---- state versions ----------------------------------------------------------------------------------------

    /// <summary>A state version of <see cref="PawnTakeDamage"/>.</summary>
    public interface IPawnTakeDamage
    {
        void PawnTakeDamage(SPlayerController self, int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag, int PercentDamageRefillable);
    }

    /// <summary>A state version of <see cref="ClientDamage"/> (runs on the owner).</summary>
    public interface IClientDamage
    {
        void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy);
    }

    /// <summary>A state version of <see cref="PlayAnimDmg"/> (runs on the owner).</summary>
    public interface IPlayAnimDmg
    {
        void PlayAnimDmg(SPlayerController self, int PillTag, FVector Momentum);
    }

    /// <summary>A state version of <see cref="PlayAnimDeath"/> (runs on the owner).</summary>
    public interface IPlayAnimDeath
    {
        void PlayAnimDeath(SPlayerController self, FVector Momentum, int PawnHealth);
    }

    /// <summary>A state version of <see cref="TellClientServerWaitForFatalBlast"/> (blast states ignore it).</summary>
    public interface ITellClientServerWaitForFatalBlast
    {
        void TellClientServerWaitForFatalBlast(SPlayerController self);
    }

    /// <summary>A state version of <see cref="ExplosionHit"/> (runs on the owner).</summary>
    public interface IExplosionHit
    {
        void ExplosionHit(SPlayerController self, FVector Momentum);
    }

    /// <summary>The level's combat ports.</summary>
    protected ICombatPorts CombatPorts => CombatServices.Ports(this);

    /// <summary>The level's combat network.</summary>
    protected ICombatNet CombatNet => CombatServices.Net(this);

    private bool CombatInGameEnded => IsInState(CNAME_GameEnded);

    // ---- entry points --------------------------------------------------------------------------------------------

    /// <summary>
    /// Damage to this controller's pawn. On a client the call is sent to the server (rule 1); on the server the
    /// current state's handler runs (state-damage-reactions.md).
    /// </summary>
    [Uc("SBase.SPlayerController.PawnTakeDamage", Sha1 = "088e308b126d64a7c676591f9334c7e9ca3dfe6a")]
    [PortOf("uc:SBase.SPlayerController:PawnTakeDamage", Spec = "spec/combat/damage-pipeline.md#rules")]
    [PortOf("rep:Engine.Controller.PawnTakeDamage", Spec = "spec/combat/damage-pipeline.md#network")]
    public override void PawnTakeDamage(int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0, int PercentDamageRefillable = 0)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(CNAME_PawnTakeDamageCall)
            {
                Int1 = Damage,
                Actor1 = instigatedBy,
                Vector1 = HitLocation,
                Vector2 = Momentum,
                DamageType = DamageType,
                Int2 = PillTag,
                Int3 = PercentDamageRefillable,
            });
            return;
        }

        // Damage history and the victim's incapacitation/death effects belong to world time,
        // even when a rolling or melee attacker invokes this from an older input command.
        using var clock = WorldEventTime();

        // An OpenSCCT addition: a hub level takes no damage at all (spec/additions/hub.md).
        if (!Modes.PlayRules.Of(this).PawnsTakeDamage)
        {
            return;
        }

        // An OpenSCCT addition: a practice server's !tank takes no damage (spec/reloaded/FEATURES.md rl:net.practice).
        if (Modes.PracticeRules.Of(this)?.IgnoresDamage(this, DamageType) == true)
        {
            return;
        }

        if (StateNode is IPawnTakeDamage s)
        {
            s.PawnTakeDamage(this, Damage, instigatedBy, HitLocation, Momentum, DamageType, PillTag, PercentDamageRefillable);
            return;
        }

        RunDamageHandler(CombatDispatch.DamageHandlerFor(GetStateName()), Damage, instigatedBy, HitLocation, Momentum,
            DamageType, PillTag, PercentDamageRefillable);
    }

    private static readonly Name CNAME_PawnTakeDamageCall = new("PawnTakeDamage");

    /// <summary>
    /// Client-reported damage (reliable client to server): the server applies it to the target's controller with no
    /// validation, then counts a charge hit or a death from a ledge grab (rule 3).
    /// </summary>
    [Uc("SBase.SPlayerController.DamagePawn_C2S", Sha1 = "4d94ca474482f4f1f309546fbdc74cc83cb94736")]
    [PortOf("uc:SBase.SPlayerController:DamagePawn_C2S", Spec = "spec/combat/damage-pipeline.md#rules")]
    [PortOf("rep:SBase.SPlayerController.DamagePawn_C2S", Spec = "spec/combat/damage-pipeline.md#network")]
    public void DamagePawn_C2S(Pawn? Target, int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0, int PercentDamageRefillable = 0)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(CNAME_DamagePawn_C2S)
            {
                Target = Target,
                Int1 = Damage,
                Actor1 = instigatedBy,
                Vector1 = HitLocation,
                Vector2 = Momentum,
                DamageType = DamageType,
                Int2 = PillTag,
                Int3 = PercentDamageRefillable,
            });
            return;
        }

        (Target?.Controller as SPlayerController)?.PawnTakeDamage(Damage, instigatedBy, HitLocation, Momentum, DamageType,
            PillTag, PercentDamageRefillable);
        if (DamageType == SDamageType.SDamage_Charged && Target is not null && instigatedBy?.Controller is SPlayerController charger &&
            (SGameInfo.TeamIndexOf(Target.Controller) != SGameInfo.TeamIndexOf(charger) || IsMercDeathMatch) &&
            charger.PlayerReplicationInfo?.Stats is { } chargerStats)
        {
            chargerStats.Merc_ChargeHits += 1;
        }

        if (DamageType == SDamageType.DamTypeGrabBreak && Target is { Health: <= 0 } &&
            Target.Controller?.PlayerReplicationInfo?.Stats is { } victimStats)
        {
            victimStats.Merc_DeathFromLedgeGrab += 1;
        }
    }

    private static readonly Name CNAME_DamagePawn_C2S = new("DamagePawn_C2S");

    /// <summary>True in Merc Deathmatch (GameMode 7).</summary>
    public bool IsMercDeathMatch => GameReplicationInfo?.GameMode == EGameMode.GM_MercDeathMatch ||
        (Level.Game as SGameInfo)?.MatchInfo?.GameMode == EGameMode.GM_MercDeathMatch;

    // ---- StandardReactionsToDamage --------------------------------------------------------------------------

    /// <summary>
    /// The shared damage reactions (server, rules 10 to 31): instigator fallbacks, spawn protection, the hit marker,
    /// the hit zone and the damage number, statistics, the refill pool, the history, the owner feedback and the kill
    /// hand-off. <paramref name="PillTag"/> receives the pill the hit was resolved to.
    /// </summary>
    [Uc("SBase.SPlayerController.StandardReactionsToDamage", Sha1 = "3d3a25f936e2380b79f8cda607c8b8c1310bdd9c")]
    [PortOf("uc:SBase.SPlayerController:StandardReactionsToDamage", Spec = "spec/combat/damage-pipeline.md#standardreactionstodamage-server-only")]
    public void StandardReactionsToDamage(int Damage, int PercentDamageRefillable, Pawn? instigatedBy, FVector HitLocation,
        FVector Momentum, SDamageType DamageType, ref int PillTag)
    {
        if (SBPawn is not { } victim || victim.Health <= 0 || (DamageType == SDamageType.SFell && Damage == 0))
        {
            return;
        }

        var game = Level.Game as SGameInfo;
        SDamageVolume? volume = null;
        if (instigatedBy is null && game?.GPODamageInstigator is { } gpoInstigator)
        {
            instigatedBy = gpoInstigator;
        }

        if (instigatedBy is null && game is not null)
        {
            volume = game.FindDamageVolume(Pawn, DamageType);
            if (volume is not null)
            {
                instigatedBy = volume.TriggerController?.Pawn;
            }
        }

        Controller? killer = instigatedBy is not null ? instigatedBy.Controller : volume?.TriggerController;
        if (DamageType == SDamageType.SDTStickyShocker)
        {
            ShockInstigator = instigatedBy;
            ShockInstigatorTime = Level.TimeSeconds;
        }

        if (IsSpawnProtected(victim, DamageType))
        {
            return;
        }

        if (instigatedBy?.Controller is SPlayerController shooter && DamageType is SDamageType.SDTBase or
            SDamageType.SDTSnipeShot or SDamageType.SDamage_Jammered)
        {
            shooter.bShootedPawn = !shooter.bShootedPawn;
        }

        if (bBinocularVision)
        {
            ClientStopBinoculars();
        }

        if (Pawn is null)
        {
            return;
        }

        FVector notNormalizedMomentum = Momentum;
        int actualDamage = ResolveDamage(victim, Damage, instigatedBy, HitLocation, DamageType, ref PillTag);
        if (DamageType == SDamageType.SDamage_Explosion)
        {
            actualDamage = K.ExplosionDamage;
        }

        if (DamageType == SDamageType.SDamage_DropAttacked && instigatedBy is SBasePawn dropper &&
            dropper.SPawnType != victim.SPawnType && GameReplicationInfo is { } gri &&
            ((victim.SPawnType == PawnType.SPAWNDEFENSE && gri.bAvlb_SPY_LeathalDropTech) ||
             (victim.SPawnType == PawnType.SPAWNOFFENSE && gri.bAvlb_MERC_LeathalDropTech)))
        {
            actualDamage = victim.Health;
        }

        bool bCountForStats = killer is SPlayerController killerPc && !ReferenceEquals(killerPc, this) &&
            (SGameInfo.TeamIndexOf(killerPc) != SGameInfo.TeamIndexOf(this) || IsMercDeathMatch);
        if (bCountForStats && actualDamage > 0)
        {
            int counted = SvmMath.Min(actualDamage, victim.Health);
            if (PlayerReplicationInfo?.Stats is { } taken)
            {
                taken.DamageTaken += counted;
            }

            if (killer!.PlayerReplicationInfo?.Stats is { } given)
            {
                given.DamageGiven += counted;
            }
        }

        CountPoisonDrain();
        victim.Health -= actualDamage;
        if (DamageType == SDamageType.SDamage_Poison && bCountForStats)
        {
            PoisonInstigator = killer;
        }

        if (PoisonInstigator is not null)
        {
            PoisonLastHealth = victim.Health;
        }

        if (DamageType == SDamageType.SDamage_DropAttacked && bCountForStats)
        {
            if (killer!.PlayerReplicationInfo?.Stats is { } attacker)
            {
                attacker.DropAttacks += 1;
            }

            if (PlayerReplicationInfo?.Stats is { } dropped)
            {
                dropped.DropAttacksTaken += 1;
                if (victim.Health <= 0)
                {
                    dropped.DeathFromDropAttack += 1;
                }
            }
        }

        if (victim is SPawnAttaque spy)
        {
            spy.HealthRefill = 0f;
            spy.NbPointsRefillable += CombatRules.RefillPoints(actualDamage, PercentDamageRefillable);
        }

        for (int i = LastDamages.Length - 1; i >= 1; i--)
        {
            LastDamages[i] = LastDamages[i - 1];
        }

        LastDamages[0] = new FDamageInfos
        {
            Damage = actualDamage,
            DamageType = DamageType,
            PillTag = PillTag,
            Time = SvmMath.Trunc(Level.TimeSeconds),
        };

        ClientDamage(notNormalizedMomentum, DamageType, HitLocation, instigatedBy);
        if (victim.SPawnType == PawnType.SPAWNOFFENSE && victim.GlassEffect != 0)
        {
            if (DamageType != SDamageType.SFell)
            {
                Equipment.PawnVision.SetDesiredGlassValueOnClients(victim, 0, 1f);
            }

            Equipment.PawnVision.SetPerturbationGlassOnClients(victim);
        }

        if (victim.Health <= 0)
        {
            KillHandOff(killer, DamageType);
        }
    }

    /// <summary>Spawn protection (rule 15, spawn-protection-and-scoring.md rule 2).</summary>
    private bool IsSpawnProtected(SBasePawn victim, SDamageType DamageType)
    {
        float now = Level.TimeSeconds;
        if (DamageType == SDamageType.SDamage_Explosion && now < fSpawnProtectionEnd &&
            (victim.SPawnType == PawnType.SPAWNOFFENSE || IsMercDeathMatch))
        {
            return true;
        }

        return IsMercDeathMatch && DamageType is SDamageType.SDTBase or SDamageType.SDTBase_NoAnim or SDamageType.SDTSnipeShot &&
            now < fSpawnProtectionEnd - K.MercDmGunProtection;
    }

    /// <summary>The pill and the damage number of a hit before the explosion and drop rules (rule 20).</summary>
    private int ResolveDamage(SBasePawn victim, int Damage, Pawn? instigatedBy, FVector HitLocation, SDamageType DamageType,
        ref int PillTag)
    {
        if ((PillTag <= 0 || PillTag > K.LastPill) && DamageType != SDamageType.SDTStickyShocker)
        {
            PillTag = (int)CombatRules.ApproxPill(victim.Location, victim.Rotation, victim.CollisionHeight, HitLocation) + 1;
            if (DamageType == SDamageType.SDamage_Poison)
            {
                victim.bPoisonned = true;
            }

            return Damage;
        }

        if (DamageType == SDamageType.SDamage_Poison)
        {
            victim.bPoisonned = true;
        }

        if (DamageType == SDamageType.SDamage_Jammered)
        {
            Client_Jammed(instigatedBy);
            return 0;
        }

        if (DamageType == SDamageType.SDTSnipeShot)
        {
            if (PillTag == 1)
            {
                return victim.Health;
            }

            return PillTag > 0 ? CombatRules.ZoneScaled(Damage, victim.DamageSnipeLookupTable[PillTag - 1]) : Damage;
        }

        if (DamageType == SDamageType.SDTStickyShocker && PillTag == K.TaserPill)
        {
            if (!victim.bStickyShocking)
            {
                TimeShock = Level.TimeSeconds - 2f * RealTimeInvulSticky;
                return CombatRules.TaserDamage(K.DefaultHealth);
            }

            return 0;
        }

        return CombatRules.IsRealPill(PillTag) ? CombatRules.ZoneScaled(Damage, victim.DamageLookupTable[PillTag - 1]) : Damage;
    }

    /// <summary>The kill hand-off (rule 31): inputs cleared, Killed, then the points of the kill.</summary>
    private void KillHandOff(Controller? killer, SDamageType DamageType)
    {
        bCanTalkToGuy = false;
        bAltFire = 0;
        bFire = 0;
        TimerUSe = 0f;
        fThrowForce = 0f;
        bUsingNow = false;
        if (myHUD is { } hud)
        {
            hud.BallisticValue = 0f;
            hud.bGadgetJustSelected = false;
        }

        Level.Game?.Killed(killer, this, Pawn, DamageType);
        if (killer is null || Level.Game is not SGameInfo game)
        {
            return;
        }

        int team = SGameInfo.TeamIndexOf(killer);
        if (!ReferenceEquals(killer.PlayerReplicationInfo?.Team, Pawn?.PlayerReplicationInfo?.Team))
        {
            game.AddPoint(team, killer as PlayerController, K.AP_D_KillWithWeapon);
        }
        else if (ReferenceEquals(killer, this))
        {
            game.AddPoint(team, this, K.AP_C_Suicide);
        }
        else
        {
            game.AddPoint(team, killer as PlayerController, K.AP_C_KillFriend);
        }
    }

    /// <summary>
    /// Poison statistics (health.md rule 5): the health drained since the last check counts as damage taken and
    /// given; the instigator is dropped once the pawn is cured or dead.
    /// </summary>
    [Uc("SBase.SPlayerController.CountPoisonDrain", Sha1 = "8a770b7f4a3e874ec8db84ae24f18a253f81c8be")]
    [PortOf("uc:SBase.SPlayerController:CountPoisonDrain", Spec = "spec/combat/health.md#rules")]
    public void CountPoisonDrain()
    {
        int health = Pawn?.Health ?? 0;
        int drained = PoisonLastHealth - SvmMath.Max(health, 0);
        if (drained > 0 && PoisonInstigator is not null)
        {
            if (PlayerReplicationInfo?.Stats is { } taken)
            {
                taken.DamageTaken += drained;
            }

            if (PoisonInstigator.PlayerReplicationInfo?.Stats is { } given)
            {
                given.DamageGiven += drained;
            }
        }

        PoisonLastHealth = health;
        if (SBPawn is not { bPoisonned: true } || health <= 0)
        {
            PoisonInstigator = null;
        }
    }

    // ---- handlers ----------------------------------------------------------------------------------------------

    private void RunDamageHandler(DamageHandler handler, int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill,
        int Pct)
    {
        switch (handler)
        {
            case DamageHandler.Walking:
                DamageWalking(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.Jump:
                DamageJump(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.InteractWithObject:
                DamageInteractWithObject(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.Berserk:
                DamageBerserk(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.Charging:
                DamageCharging(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.Blast:
                DamageBlast(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.GrabBlastOnly:
                DamageGrabBlastOnly(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.GrabFromLedge:
                DamageGrabFromLedge(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.GrabbedByPlayer:
                DamageGrabbedByPlayer(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.GrabbingPlayer:
                DamageGrabbingPlayer(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.ReleaseGrabbed:
                DamageReleaseGrabbed(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.FallDown:
                DamageFallDown(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.TerminateBase:
                DamageTerminate(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.CoopSBase:
                DamageCoopSBase(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.CoopSGoto:
                DamageCoopSGoto(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.ShortLadderPBase:
                DamageShortLadderP(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.BtwBase:
                DamageBackToWall(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.BinocularsDef:
                DamageBinocularsDef(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.BinocularsAtt:
                DamageBinocularsAtt(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.UseObjectMirador:
                DamageUseObjectMirador(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.UseObjectStickyCam:
                DamageUseObjectStickyCam(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.StickyCamSmooth:
                DamageStickyCamSmooth(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.MaintainEnterUse:
                DamageMaintainEnterUse(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.MaintainLeave:
                DamageMaintainLeave(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.HackEnter:
                DamageHackEnter(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.HackUse:
                DamageHackUse(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.HackLeave:
                DamageHackLeave(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.ComputerEnter:
                DamageComputerEnter(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.ComputerUse:
                DamageComputerUse(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.ComputerLeave:
                DamageComputerLeave(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.PutFlag:
                DamagePutFlag(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.PlaceBomb:
                DamagePlaceBomb(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.PlaceBombEnd:
                DamagePlaceBombEnd(Damage, By, Hit, M, T, Pill, Pct);
                break;
            case DamageHandler.Ignored:
                break;
            default:
                GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
                break;
        }
    }

    // Common steps of the handlers (state-damage-reactions.md, Data).

    private bool CombatAlive => Pawn is { Health: > 0 };

    private bool CombatBlastOk(SDamageType T, FVector M) => T != SDamageType.SDamage_Explosion || !CombatCanBeBlasted(M);

    private bool CombatIkOk => SBPawn is not { IK_Type: IK_Type_.IKT_PIPH or IK_Type_.IKT_JCVD };

    private int CombatWeaponLoaded => SBPawn?.WeaponLoaded ?? 0;

    /// <summary>DEATH-G: the death animation and s_Dead on the ground, PlayerFallingInerte in the air.</summary>
    private void CombatDeathGrounded(FVector Momentum)
    {
        if (Pawn?.Base is not null)
        {
            PlayAnimDeath(Momentum, Pawn.Health);
            if (!CombatInGameEnded)
            {
                eventMirrorState(CNAME_s_Dead);
            }
        }
        else if (!CombatInGameEnded)
        {
            eventMirrorState(CNAME_PlayerFallingInerte);
        }
    }

    /// <summary>DEATH-D: the death animation and s_Dead without the ground test.</summary>
    private void CombatDeathDirect(FVector Momentum)
    {
        PlayAnimDeath(Momentum, Pawn?.Health ?? 0);
        eventMirrorState(CNAME_s_Dead);
    }

    /// <summary>The death animation and s_FallDropAttacked (ledge grab, terminate and coop handlers).</summary>
    private void CombatDeathFallDropAttacked(FVector Momentum)
    {
        PlayAnimDeath(Momentum, Pawn?.Health ?? 0);
        eventMirrorState(CNAME_s_FallDropAttacked);
    }

    /// <summary>ANIM: the hit animation on the owner.</summary>
    private void CombatHitAnim(int PillTag, FVector Momentum) => PlayAnimDmg(PillTag, Momentum * K.HitAnimMomentumScale);

    /// <summary>The global handler (damage-pipeline.md rules 5 to 8).</summary>
    [Uc("SBase.SPlayerController.PawnTakeDamage", Sha1 = "088e308b126d64a7c676591f9334c7e9ca3dfe6a")]
    [PortOf("uc:SBase.SPlayerController:PawnTakeDamage", Spec = "spec/combat/damage-pipeline.md#global-pawntakedamage-used-by-every-state-that-does-not-override-or-ignore-it")]
    public void GlobalPawnTakeDamage(int Damage, Pawn? instigatedBy, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0, int PercentDamageRefillable = 0)
    {
        if (!CombatAlive)
        {
            return;
        }

        StandardReactionsToDamage(Damage, PercentDamageRefillable, instigatedBy, HitLocation, Momentum, DamageType, ref PillTag);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (CombatBlastOk(DamageType, Momentum) && DamageType != SDamageType.SDTStickyBlast)
            {
                CombatDeathGrounded(Momentum);
            }
            else if (DamageType == SDamageType.SDamage_Explosion)
            {
                TellClientServerWaitForFatalBlast();
            }

            return;
        }

        switch (DamageType)
        {
            case SDamageType.SDamage_Charged:
            case SDamageType.SDamage_ElbowShock:
                return;
            case SDamageType.SDamage_DropAttacked:
                eventMirrorState(CNAME_s_FallDropAttacked);
                return;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(PillTag, false);
                break;
        }

        if (CombatIkOk && DamageType != SDamageType.SDTBase_NoAnim)
        {
            CombatHitAnim(PillTag, Momentum);
        }
    }

    [Uc("SBase.SPlayerController.s_PlayerWalking.PawnTakeDamage", Sha1 = "3c82c6140cb4ebb5bc9bc479b5a113177ab235c0")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageWalking(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (!CombatAlive)
        {
            return;
        }

        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (CombatBlastOk(T, M) && T != SDamageType.SDTStickyBlast)
            {
                CombatDeathDirect(M);
            }
            else
            {
                TellClientServerWaitForFatalBlast();
            }

            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                break;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                break;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, true);
                break;
            case SDamageType.SDamage_DropAttacked:
                Handle_DropAttackedDmg(By);
                break;
            default:
                if (T != SDamageType.SDamage_Explosion && CombatWeaponLoaded == 0 && T != SDamageType.SDTBase_NoAnim)
                {
                    CombatHitAnim(Pill, M);
                }

                break;
        }
    }

    [Uc("SBase.SPlayerController.s_PlayerJump.PawnTakeDamage", Sha1 = "19096221c28f60c0af07667743ac07dbe8be4736")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerJump.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageJump(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (!CombatAlive)
        {
            return;
        }

        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (CombatBlastOk(T, M) && T != SDamageType.SDTStickyBlast)
            {
                CombatDeathGrounded(M);
            }
            else if (T == SDamageType.SDamage_Explosion)
            {
                TellClientServerWaitForFatalBlast();
            }

            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                return;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                return;
            case SDamageType.SDamage_DropAttacked:
                eventMirrorState(CNAME_s_FallDropAttacked);
                return;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, false);
                break;
        }

        if (CombatIkOk && T != SDamageType.SDTBase_NoAnim)
        {
            CombatHitAnim(Pill, M);
        }
    }

    [Uc("SBase.SPlayerController.s_InteractWithObject.PawnTakeDamage", Sha1 = "eb7f76a6c907bb71853a7217c82de0a852c14f80")]
    [PortOf("uc:SBase.SPlayerController:s_InteractWithObject.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageInteractWithObject(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (!CombatAlive)
        {
            return;
        }

        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (CombatBlastOk(T, M))
            {
                CombatDeathDirect(M);
            }
            else
            {
                TellClientServerWaitForFatalBlast();
            }

            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                break;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                break;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, true);
                if (SBPawn is { } pawn)
                {
                    pawn.bStickyShocking = true;
                }

                break;
            case SDamageType.SDamage_DropAttacked:
                Handle_DropAttackedDmg(By);
                break;
            default:
                if (T != SDamageType.SDamage_Explosion && CombatWeaponLoaded == 0 && T != SDamageType.SDTBase_NoAnim)
                {
                    CombatHitAnim(Pill, M);
                }

                break;
        }
    }

    [Uc("SBase.SPlayerController.s_Berserk360.PawnTakeDamage", Sha1 = "32338f104a3190a7d7bd6df12f29d2743d75a297")]
    [PortOf("uc:SBase.SPlayerController:s_Berserk360.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageBerserk(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            CombatDeadDirectOrFatal(T, M);
            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                break;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                break;
            case SDamageType.SDTStickyShocker:
                eventMirrorState(CNAME_s_PlayerWalkingSt2H);
                eventCheckshock(Pill, true);
                break;
            case SDamageType.SDamage_DropAttacked:
                Handle_DropAttackedDmg(By);
                break;
        }
    }

    [Uc("SBase.SPlayerController.s_PlayerCharging.PawnTakeDamage", Sha1 = "b955405039678a1b839fd0c4bb729193d0a3342d")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageCharging(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            CombatDeadDirectOrFatal(T, M);
            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                break;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                break;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, true);
                break;
            case SDamageType.SDamage_DropAttacked:
                Handle_DropAttackedDmg(By);
                break;
        }
    }

    /// <summary>Dead: the death animation and s_Dead when not blastable, else the fatal blast.</summary>
    private void CombatDeadDirectOrFatal(SDamageType T, FVector M)
    {
        if (CombatBlastOk(T, M))
        {
            CombatDeathDirect(M);
        }
        else
        {
            TellClientServerWaitForFatalBlast();
        }
    }

    [Uc("SBase.SPlayerController.s_Blast.PawnTakeDamage", Sha1 = "fce6e0275fe5b4f520fa64437a5e2b9a9e04cb6a")]
    [Uc("SBase.SPlayerController.s_BlastFallAfterObstacle.PawnTakeDamage", Sha1 = "9705394717540fe5e552aa7ed6b80e2c59d2034a")]
    [PortOf("uc:SBase.SPlayerController:s_Blast.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    [PortOf("uc:SBase.SPlayerController:s_BlastFallAfterObstacle.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageBlast(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (!CombatAlive)
        {
            return;
        }

        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
    }

    [Uc("SBase.SPlayerController.s_GrabBaseWithBlastDamageOnly.PawnTakeDamage", Sha1 = "bb043e76f4b761a0e1985742a349d946b23ab559")]
    [PortOf("uc:SBase.SPlayerController:s_GrabBaseWithBlastDamageOnly.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageGrabBlastOnly(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (T is SDamageType.SDamage_Explosion or SDamageType.DamTypeGrabBreak)
        {
            StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        }
    }

    [Uc("SBase.SPlayerController.s_GrabFromLedgeBase.PawnTakeDamage", Sha1 = "5744d4fec777a7718eb34e07b5b2282beaec1d17")]
    [PortOf("uc:SBase.SPlayerController:s_GrabFromLedgeBase.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageGrabFromLedge(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        bool spy = SPawnType == PawnType.SPAWNOFFENSE;
        if (Pawn.Health <= 0)
        {
            if (CombatBlastOk(T, M))
            {
                CombatDeathFallDropAttacked(M);
            }
            else
            {
                TellClientServerWaitForFatalBlast();
            }

            if (spy && GetStateName() == CNAME_s_GrabbingFromLedge_Begin && SBPawn?.m_AttackTarget?.Controller is SPlayerController merc)
            {
                merc.GrabAborted();
            }

            return;
        }

        if (T == SDamageType.SDTStickyShocker)
        {
            eventCheckshock(Pill, false);
        }

        if (spy && T is SDamageType.SDamage_Charged or SDamageType.SDamage_ElbowShock or SDamageType.SDTStickyShocker or
            SDamageType.SDamage_DropAttacked)
        {
            eventMirrorState(CNAME_s_FallDropAttacked);
            if (T == SDamageType.SDamage_Charged)
            {
                Handle_ChargedDmg(M, By);
            }
            else if (T == SDamageType.SDamage_ElbowShock)
            {
                Handle_ElbowDmg(M, By);
            }
        }
    }

    [Uc("SBase.SPlayerController.s_GrabbedByPlayer.PawnTakeDamage", Sha1 = "071d18f8d6af4f2223013abd6dd5649852eeb8ac")]
    [PortOf("uc:SBase.SPlayerController:s_GrabbedByPlayer.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageGrabbedByPlayer(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        SBasePawn? spyPawn = SBPawn?.m_AttackedBy;
        if (Pawn.Health <= 0)
        {
            if (T != SDamageType.SDamage_Explosion)
            {
                (spyPawn?.Controller as SPlayerController)?.GrabServerReleaseRequest(GrabReleaseMethode_.GrabbedDamaged);
            }
            else
            {
                TellClientServerWaitForFatalBlast();
            }

            return;
        }

        if (T == SDamageType.SDTStickyShocker)
        {
            eventCheckshock(Pill, false);
            if (!ReferenceEquals(By, spyPawn) && spyPawn?.Controller is SPlayerController spy)
            {
                spy.eventCheckshock(K.ConductedShockPill, false);
            }
        }
    }

    [Uc("SBase.SPlayerController.s_GrabbingPlayer.PawnTakeDamage", Sha1 = "97ac7fbcbaf23eea1a4b2d3a00647308ce250dd4")]
    [PortOf("uc:SBase.SPlayerController:s_GrabbingPlayer.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageGrabbingPlayer(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (T is not SDamageType.SDamage_DropAttacked and not SDamageType.SDamage_Explosion)
            {
                GrabServerReleaseRequest(GrabReleaseMethode_.GrabbingDamaged);
            }
            else
            {
                TellClientServerWaitForFatalBlast();
            }
        }

        SBasePawn? merc = SBPawn?.m_AttackTarget;
        if (T == SDamageType.SDTStickyShocker)
        {
            if (!ReferenceEquals(By, merc) && merc?.Controller is SPlayerController held)
            {
                held.eventCheckshock(Pill, false);
            }

            eventCheckshock(Pill, false);
        }
        else if (T == SDamageType.SDamage_DropAttacked)
        {
            DropOnGrabber();
        }
    }

    [Uc("SBase.SPlayerController.s_ReleaseGrabbedPlayer.PawnTakeDamage", Sha1 = "cad5886dd1758add7209fcc96ae41a9a557f0530")]
    [PortOf("uc:SBase.SPlayerController:s_ReleaseGrabbedPlayer.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageReleaseGrabbed(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (!CombatAlive)
        {
            return;
        }

        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (CombatBlastOk(T, M) && T != SDamageType.SDTStickyBlast)
            {
                CombatDeathDirect(M);
            }
            else if (T == SDamageType.SDamage_Explosion)
            {
                TellClientServerWaitForFatalBlast();
            }

            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_DropAttacked:
                eventMirrorState(CNAME_s_FallDropAttacked);
                break;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, false);
                break;
        }
    }

    [Uc("SBase.SPlayerController.s_FallDown.PawnTakeDamage", Sha1 = "e661e60c80fc4fc6ced40ab878eb9eeb06673f72")]
    [PortOf("uc:SBase.SPlayerController:s_FallDown.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageFallDown(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (!CombatAlive)
        {
            return;
        }

        if (T == SDamageType.SDamage_Explosion)
        {
            StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
            if (SPawnType != PawnType.SPAWNDEFENSE || Pawn is null)
            {
                return;
            }

            if (Pawn.Health <= 0)
            {
                TellClientServerWaitForFatalBlast();
            }
            else if (CombatIkOk)
            {
                CombatHitAnim(Pill, M);
            }

            return;
        }

        if (T is SDamageType.SDTBase or SDamageType.SDTBase_NoAnim or SDamageType.SDTSnipeShot or SDamageType.Crushed)
        {
            StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        }
    }

    [Uc("SBase.SPlayerController.s_TerminatePawn_Base.PawnTakeDamage", Sha1 = "875d6907921f290eb01f0cece39699ba9213f76b")]
    [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageTerminate(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (CombatBlastOk(T, M))
            {
                CombatDeathFallDropAttacked(M);
            }
            else
            {
                TellClientServerWaitForFatalBlast();
            }

            ReleaseTerminatedSpy();
            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                ReleaseTerminatedSpy();
                break;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                ReleaseTerminatedSpy();
                break;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, true);
                ReleaseTerminatedSpy();
                break;
            case SDamageType.SDamage_DropAttacked:
                Handle_DropAttackedDmg(By);
                ReleaseTerminatedSpy();
                break;
        }
    }

    /// <summary>A living spy being finished by this merc is released (s_TerminatedPawn_Released).</summary>
    private void ReleaseTerminatedSpy()
    {
        if (SBPawn?.m_PawnToTerminate is { Health: > 0, Controller: SPlayerController victim })
        {
            victim.eventMirrorState(CNAME_s_TerminatedPawn_Released);
        }
    }

    [Uc("SBase.SPlayerController.s_Coop_S_Base.PawnTakeDamage", Sha1 = "4aab11ec3f076079a33861e4e11541ecd0fc6af0")]
    [PortOf("uc:SBase.SPlayerController:s_Coop_S_Base.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageCoopSBase(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct) =>
        CoopDamage(Damage, By, Hit, M, T, Pill, Pct, deathDirect: false, dropHandled: false);

    [Uc("SBase.SPlayerController.s_Coop_S_Goto.PawnTakeDamage", Sha1 = "58c8d72f255f1eeb7092c333d171169c90f41a6e")]
    [PortOf("uc:SBase.SPlayerController:s_Coop_S_Goto.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageCoopSGoto(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct) =>
        CoopDamage(Damage, By, Hit, M, T, Pill, Pct, deathDirect: true, dropHandled: true);

    [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.PawnTakeDamage", Sha1 = "283b6c101ce95a089808e40d652217af7d9105c0")]
    [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageShortLadderP(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct) =>
        CoopDamage(Damage, By, Hit, M, T, Pill, Pct, deathDirect: false, dropHandled: false);

    private void CoopDamage(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct, bool deathDirect,
        bool dropHandled)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (!CombatBlastOk(T, M))
            {
                TellClientServerWaitForFatalBlast();
            }
            else if (deathDirect)
            {
                CombatDeathDirect(M);
            }
            else
            {
                CombatDeathFallDropAttacked(M);
            }

            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                break;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                break;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, false);
                break;
            case SDamageType.SDamage_DropAttacked:
                if (dropHandled)
                {
                    Handle_DropAttackedDmg(By);
                }
                else
                {
                    eventMirrorState(CNAME_s_FallDropAttacked);
                }

                break;
            default:
                if (CombatWeaponLoaded == 0 && T != SDamageType.SDTBase_NoAnim)
                {
                    CombatHitAnim(Pill, M);
                }

                break;
        }
    }

    [Uc("SBase.SPlayerController.s_BTW_Base.PawnTakeDamage", Sha1 = "04e5567686983b4bf7f7b13b173552f02f661e07")]
    [PortOf("uc:SBase.SPlayerController:s_BTW_Base.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageBackToWall(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            CombatDeadDirectOrFatal(T, M);
            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                return;
            case SDamageType.SDamage_ElbowShock:
                return;
            case SDamageType.SDamage_DropAttacked:
                eventMirrorState(CNAME_s_FallDropAttacked);
                return;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, false);
                break;
        }

        if (CombatWeaponLoaded == 0 || T == SDamageType.SDTStickyShocker)
        {
            CombatHitAnim(Pill, M);
        }
    }

    [Uc("SBase.SPlayerController.s_Binoculars_Def.PawnTakeDamage", Sha1 = "449ec90bbe0733c9906cb6ac5056f117007fd0b2")]
    [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageBinocularsDef(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            CombatDeadDirectOrFatal(T, M);
            return;
        }

        if (!BinocularsSharedReaction(T, M, By, Pill) && CombatWeaponLoaded == 0 && T != SDamageType.SDTBase_NoAnim)
        {
            CombatHitAnim(Pill, M);
        }
    }

    /// <summary>The scope handlers' charge, elbow, shock and drop reactions; true when one applied.</summary>
    private bool BinocularsSharedReaction(SDamageType T, FVector M, Pawn? By, int Pill)
    {
        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                return true;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                return true;
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, true);
                return true;
            case SDamageType.SDamage_DropAttacked:
                Handle_DropAttackedDmg(By);
                return true;
            default:
                return false;
        }
    }

    [Uc("SBase.SPlayerController.s_Binoculars_Att.PawnTakeDamage", Sha1 = "759fd1a88f9b15c0fdfd0dc0e43ba08f47950f50")]
    [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageBinocularsAtt(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        bBinocularVision = false;
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            CombatDeadDirectOrFatal(T, M);
            return;
        }

        if (BinocularsSharedReaction(T, M, By, Pill) || T == SDamageType.SDamage_Explosion)
        {
            return;
        }

        eventChangeState(Pawn.bIsCrouched ? CNAME_s_PlayerWalkingCr0H : CNAME_s_PlayerWalkingSt0H);
        if (T != SDamageType.SDTBase_NoAnim)
        {
            CombatHitAnim(Pill, M);
        }
    }

    [Uc("SBase.SPlayerController.s_UseObject_Mirador.PawnTakeDamage", Sha1 = "c6df290802e3e611963514ff80521cc5c1ab46ae")]
    [PortOf("uc:SBase.SPlayerController:s_UseObject_Mirador.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageUseObjectMirador(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct) =>
        UseObjectDamage(Damage, By, Hit, M, T, Pill, Pct);

    /// <summary>The sticky cam and camera network view handler (SPlayerController.StickyCamDamage.cs).</summary>
    private void DamageUseObjectStickyCam(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct) =>
        StickyCamViewDamage(Damage, By, Hit, M, T, Pill, Pct);

    private void UseObjectDamage(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            CombatDeadDirectOrFatal(T, M);
            return;
        }

        switch (T)
        {
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, true);
                break;
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                return;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                return;
            case SDamageType.SDamage_DropAttacked:
                Handle_DropAttackedDmg(By);
                return;
        }

        eventMirrorState(CombatNatives.WaitingStateFor(this), default, true);
    }

    /// <summary>The sticky cam fly-in handler (SPlayerController.StickyCamDamage.cs).</summary>
    private void DamageStickyCamSmooth(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct) =>
        StickyCamFlyInDamage(Damage, By, Hit, M, T, Pill, Pct);

    [Uc("SBase.SPlayerController.s_Maintain_Enter.PawnTakeDamage", Sha1 = "6f24685d75a90e7f5cb4f569082eac07611801d7")]
    [Uc("SBase.SPlayerController.s_Maintain_Use.PawnTakeDamage", Sha1 = "dc37c48198c0874b765734d55a770ec361160309")]
    [PortOf("uc:SBase.SPlayerController:s_Maintain_Enter.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    [PortOf("uc:SBase.SPlayerController:s_Maintain_Use.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageMaintainEnterUse(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (CombatAlive)
        {
            eventMirrorState(CNAME_s_Maintain_Leave, OK.TriggerHack_Failed);
        }

        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    [Uc("SBase.SPlayerController.s_Maintain_Leave.PawnTakeDamage", Sha1 = "9eee9a2d932aae8c9020bbaa66c58e19969d12c7")]
    [PortOf("uc:SBase.SPlayerController:s_Maintain_Leave.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageMaintainLeave(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        GotoWaitingStateCombat();
        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    [Uc("SBase.SPlayerController.s_Hack_Leave.PawnTakeDamage", Sha1 = "4bba7447333031250a85782ec901bb7892ce693c")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Leave.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageHackLeave(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        GotoWaitingStateCombat();
        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    [Uc("SBase.SPlayerController.s_Hack_Enter.PawnTakeDamage", Sha1 = "59d3a8b15e4c620e1244c92cf3a37f5c57b37529")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Enter.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageHackEnter(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        SwitchHackRules.EnterTakeDamage(this);
        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    [Uc("SBase.SPlayerController.s_Computer_Enter.PawnTakeDamage", Sha1 = "1c3c31d459a3c06ca548f863b1bd01fd513429c0")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Enter.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageComputerEnter(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        TerminalHackRules.EnterTakeDamage(this);
        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    [Uc("SBase.SPlayerController.s_Computer_Leave.PawnTakeDamage", Sha1 = "92d37acc79f913150be203c37d95a0f4fcc4e2d9")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Leave.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageComputerLeave(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (CombatAlive)
        {
            eventMirrorState(CNAME_s_PlayerWalkingSt0H);
        }

        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    [Uc("SBase.SPlayerController.s_PutFlagInDropZone.PawnTakeDamage", Sha1 = "ff41057029808fb6dc2380acf2921f5c78aed792")]
    [PortOf("uc:SBase.SPlayerController:s_PutFlagInDropZone.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamagePutFlag(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        DiskCarryRules.PutFlagTakeDamage(this);
        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    [Uc("SBase.SPlayerController.s_PlaceBomb.PawnTakeDamage", Sha1 = "b7515acb2fdf04ee72f0889a804de2b3c4dc6430")]
    [PortOf("uc:SBase.SPlayerController:s_PlaceBomb.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamagePlaceBomb(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (CombatAlive)
        {
            eventMirrorState(CNAME_s_PlaceBomb_End);
        }

        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    /// <summary>Not ported here: the undraw action when still armed (bomb spec).</summary>
    [Uc("SBase.SPlayerController.s_PlaceBomb_End.PawnTakeDamage", Sha1 = "85b3bff6b3a34e22608c009351a45c7a56e6c89d")]
    [PortOf("uc:SBase.SPlayerController:s_PlaceBomb_End.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamagePlaceBombEnd(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        if (CombatAlive)
        {
            eventMirrorState(CNAME_s_PlayerWalkingSt0H);
        }

        GlobalPawnTakeDamage(Damage, By, Hit, M, T, Pill, Pct);
    }

    [Uc("SBase.SPlayerController.s_Hack_Use.PawnTakeDamage", Sha1 = "f2f0cec0a25b981ed6b9d6abaa106513a5e45deb")]
    [PortOf("uc:SBase.SPlayerController:s_Hack_Use.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageHackUse(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct) =>
        TerminalUseDamage(Damage, By, Hit, M, T, Pill, Pct, hack: true);

    [Uc("SBase.SPlayerController.s_Computer_Use.PawnTakeDamage", Sha1 = "fda9443ed36f965255504ae6fcb5158ddf128ff0")]
    [PortOf("uc:SBase.SPlayerController:s_Computer_Use.PawnTakeDamage", Spec = "spec/combat/state-damage-reactions.md#handler-behaviour")]
    private void DamageComputerUse(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct) =>
        TerminalUseDamage(Damage, By, Hit, M, T, Pill, Pct, hack: false);

    private void TerminalUseDamage(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct, bool hack)
    {
        // Only the terminal announces "interrupted" (message 23); the panel hack sends none.
        if (!hack)
        {
            ObjectiveMessages.Broadcast(this, OK.MsgInterrupted);
        }

        if (!CombatAlive)
        {
            return;
        }

        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            if (CombatBlastOk(T, M) && T != SDamageType.SDTStickyBlast)
            {
                CombatDeathGrounded(M);
            }
            else if (T == SDamageType.SDamage_Explosion)
            {
                TellClientServerWaitForFatalBlast();
            }

            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                return;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                return;
            case SDamageType.SDamage_DropAttacked:
                eventMirrorState(CNAME_s_FallDropAttacked);
                return;
            case SDamageType.SDTStickyShocker:
                eventMirrorState(CNAME_s_PlayerWalkingSt0H);
                if (hack)
                {
                    GotoWaitingStateCombat();
                }

                eventCheckshock(Pill, false);
                return;
        }

        if (!hack && !(CombatIkOk && T != SDamageType.SDTBase_NoAnim))
        {
            eventMirrorState(CNAME_s_PlayerWalkingSt0H);
            return;
        }

        CombatHitAnim(Pill, M);
        if (hack)
        {
            eventMirrorState(OK.s_Hack_Leave, OK.TriggerHack_Failed);
        }
        else
        {
            eventMirrorState(CNAME_s_PlayerWalkingSt0H);
        }
    }

    /// <summary>
    /// The script GotoWaitingState (the movement port owns it): the resting state through ChangeState, so a client's
    /// change reaches the server, and from the server a reliable call to the owning client.
    /// </summary>
    public void GotoWaitingStateCombat() => GotoWaitingState();

    // ---- damage reactions shared by the handlers -------------------------------------------------------------

    /// <summary>A charge or berserk hit (melee spec rule 21): the attacker is remembered and the victim knocked back.</summary>
    [Uc("SBase.SPlayerController.Handle_ChargedDmg", Sha1 = "6ecd17817a4f8aa6d6bf7ca8115160dc08dcbc8d")]
    [PortOf("uc:SBase.SPlayerController:Handle_ChargedDmg", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
    public void Handle_ChargedDmg(FVector Momentum, Pawn? instigatedBy)
    {
        if (SBPawn is { } pawn)
        {
            pawn.m_AttackedBy = instigatedBy as SBasePawn;
        }

        if (CombatAlive)
        {
            eventMirrorState(CNAME_s_Charge_GoBackward, default, false, instigatedBy);
        }
    }

    /// <summary>
    /// An elbow strike (melee spec rules 10 and 11): knock-back strength from the momentum and the controller
    /// facing; a crouched victim, or the 12th strike in 8 s (unreachable with a 10-entry history), is knocked down
    /// with the direction label of the strike, any other is knocked back.
    /// </summary>
    [Uc("SBase.SPlayerController.Handle_ElbowDmg", Sha1 = "72106229812f4674a5cc60f32e08a4da061d10cf")]
    [PortOf("uc:SBase.SPlayerController:Handle_ElbowDmg", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
    public void Handle_ElbowDmg(FVector Momentum, Pawn? instigatedBy)
    {
        fElbowDamage = Momentum | Rotation.Vector();
        var attacker = instigatedBy as SBasePawn;
        if (SBPawn is not { } victim)
        {
            return;
        }

        victim.m_AttackedBy = attacker;
        int nbElbowHit = CombatRules.RecentElbowHits(LastDamages, Level.TimeSeconds, iElbow_TimeForKO);
        var attackerPc = attacker?.Controller as SPlayerController;
        bool bStats = attackerPc is not null &&
            (SGameInfo.TeamIndexOf(attackerPc) != SGameInfo.TeamIndexOf(this) || IsMercDeathMatch);
        if (!victim.bIsCrouched && nbElbowHit < iElbow_NbHitsMaxPerTime)
        {
            if (bStats)
            {
                IncStat(attackerPc!, static s => s.ElbowStrikes++);
                IncStat(this, static s => s.ElbowStrikesTaken++);
            }

            eventMirrorState(CNAME_s_ElbowShot_GoBackward, default, false, attacker);
            return;
        }

        if (bStats)
        {
            IncStat(attackerPc!, static s => s.StrikeAttacks++);
            IncStat(this, static s => s.StrikeAttacksTaken++);
        }

        if (Level.Game is SGameInfo game)
        {
            game.BroadcastLocalizedMessage(new ClassRef("SBase.SGameMessages"), K.MsgStrikeKnockDown, PlayerReplicationInfo,
                attackerPc?.PlayerReplicationInfo);
        }

        FVector away = attacker is not null ? victim.Location - attacker.Location : FVector.Zero;
        Name label = DeathLabel(CombatPorts.ChooseDeathAnimation(victim, away));
        eventMirrorState(CNAME_s_FallDown, label);
    }

    /// <summary>A drop attack on a walking victim (drop-attack.md rule 10): knocked down in the strike's direction.</summary>
    [Uc("SBase.SPlayerController.Handle_DropAttackedDmg", Sha1 = "c0811640c03aa8600607fe3188485e525e167df4")]
    [PortOf("uc:SBase.SPlayerController:Handle_DropAttackedDmg", Spec = "spec/combat/drop-attack.md#rules")]
    public void Handle_DropAttackedDmg(Pawn? Attacker)
    {
        if (SBPawn is not { } victim)
        {
            return;
        }

        FVector away = Attacker is not null ? victim.Location - Attacker.Location : FVector.Zero;
        EDeathDirection dir = CombatPorts.ChooseDeathAnimation(victim, away);
        Name label = dir == EDeathDirection.DIE_Forward ? CNAME_DIE_DropAttacked : DeathLabel(dir);
        if (!label.IsNone)
        {
            eventMirrorState(CNAME_s_FallDown, label);
        }
    }

    /// <summary>The s_FallDown label of a death direction (no label for other results).</summary>
    public static Name DeathLabel(EDeathDirection Direction) => Direction switch
    {
        EDeathDirection.DIE_Forward => CNAME_DIE_Forward,
        EDeathDirection.DIE_Backward => CNAME_DIE_Backward,
        EDeathDirection.DIE_Left => CNAME_DIE_Left,
        EDeathDirection.DIE_Right => CNAME_DIE_Right,
        EDeathDirection.DIE_InPlace => CNAME_DIE_InPlace,
        _ => Name.None,
    };

    /// <summary>Increments a statistic of <paramref name="pc"/>'s server-side stats, when it has them.</summary>
    internal static void IncStat(Controller pc, System.Action<USPlayerStats> inc)
    {
        if (pc.PlayerReplicationInfo?.Stats is { } stats)
        {
            inc(stats);
        }
    }

    // ---- owner feedback -------------------------------------------------------------------------------------

    /// <summary>
    /// Hit feedback on the owner (rules 33 to 38): the explosion blast, the flash test, rumble, the HUD overlay and
    /// the camera shake. Runs in the owner's current state.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientDamage", Sha1 = "8b5fc3e1064dffde2961775db1883f0f1847b28c")]
    [PortOf("uc:SBase.SPlayerController:ClientDamage", Spec = "spec/combat/damage-pipeline.md#clientdamage-reliable-server-to-owner-rpc-runs-in-the-owners-current-state")]
    [PortOf("rep:SBase.SPlayerController.ClientDamage", Spec = "spec/combat/damage-pipeline.md#network")]
    public void ClientDamage(FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_ClientDamage)
            {
                Vector1 = Momentum,
                DamageType = DamageType,
                Vector2 = HitLocation,
                Actor1 = instigatedBy,
            });
            return;
        }

        if (StateNode is IClientDamage s)
        {
            s.ClientDamage(this, Momentum, DamageType, HitLocation, instigatedBy);
            return;
        }

        switch (CombatDispatch.ClientDamageFor(GetStateName()))
        {
            case ClientDamageKind.GrabBase:
                GrabBaseClientDamage(Momentum, DamageType);
                break;
            case ClientDamageKind.LaserFirst:
                LaserFirstClientDamage(Momentum, DamageType, HitLocation, instigatedBy);
                break;
            default:
                GlobalClientDamage(Momentum, DamageType, HitLocation, instigatedBy);
                break;
        }
    }

    private static readonly Name CNAME_ClientDamage = new("ClientDamage");

    /// <summary>The global ClientDamage.</summary>
    public void GlobalClientDamage(FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
    {
        if (bViewingHelp)
        {
            bViewingHelp = false;
            CombatFx.Notify(this, CombatFx.ShowHelp);
        }

        switch (DamageType)
        {
            case SDamageType.SDamage_Explosion:
                eventExplosionHit(Momentum);
                break;
            case SDamageType.SDTFlash:
                if (!FlashReachesEyes(HitLocation))
                {
                    return;
                }

                break;
            case SDamageType.SDamage_DropAttacked:
                CombatFx.Notify(this, CombatFx.Vibration_SautHaut);
                fLyingOnGroundDuration = instigatedBy is SBasePawn dropper && SBPawn is { } me && dropper.SPawnType == me.SPawnType
                    ? K.SameTypeDropLyingTime
                    : K.DefaultLyingTime;
                break;
            case SDamageType.SFell:
                CombatFx.Notify(this, CombatFx.Vibration_SautHaut);
                break;
            case SDamageType.SDamage_Charged:
            case SDamageType.SDamage_ElbowShock:
                CombatFx.Notify(this, CombatFx.Vibration_Bousculade);
                break;
            default:
                CombatFx.Notify(this, CombatFx.Vibration_CoupParDefaut);
                break;
        }

        ClientDamageHudEffect(DamageType);
        if (DamageType == SDamageType.SDTFlash)
        {
            ClientStopBinoculars();
        }

        if (DamageType != SDamageType.SDTFlash && CombatIkOk)
        {
            CombatHitCameraShake();
        }
    }

    /// <summary>The merc shake, or the unarmed spy tilt and shake, of a hit.</summary>
    private void CombatHitCameraShake()
    {
        if (SPawnType == PawnType.SPAWNDEFENSE)
        {
            m_camera?.Shake(K.MercHitShake, K.HitShakeSpeed, K.HitShakeFadeOut);
        }
        else if (CombatWeaponLoaded == 0)
        {
            m_camera?.Tilt(K.SpyHitTilt, K.HitShakeSpeed, K.HitShakeFadeOut);
            m_camera?.Shake(K.SpyHitShake, K.HitShakeSpeed, K.HitShakeFadeOut);
        }
    }

    /// <summary>The flash-bang test (rule 34): the flash is in front of the eyes and nothing in the world hides it.</summary>
    private bool FlashReachesEyes(FVector HitLocation)
    {
        FVector origin;
        FVector forward;
        if (SPawnType == PawnType.SPAWNOFFENSE || Pawn is null)
        {
            origin = Location;
            forward = Rotation.Vector();
        }
        else
        {
            origin = Pawn.Location + CombatPorts.EyePosition(Pawn);
            forward = Pawn.Rotation.Vector();
        }

        FVector d = (HitLocation - origin).SafeNormal();
        if ((d | forward) < DES_fSeuilFlashBang_Cos)
        {
            return false;
        }

        return Natives?.FastTrace(HitLocation, origin) ?? true;
    }

    /// <summary>The grab family's ClientDamage (rule 33): only explosions give feedback.</summary>
    [Uc("SBase.SPlayerController.s_GrabBase.ClientDamage", Sha1 = "29e23cd3147be8a72f4094328c390cdcfbc51332")]
    [PortOf("uc:SBase.SPlayerController:s_GrabBase.ClientDamage", Spec = "spec/combat/state-damage-reactions.md#clientdamage-overrides-owner-side")]
    private void GrabBaseClientDamage(FVector Momentum, SDamageType DamageType)
    {
        if (DamageType != SDamageType.SDamage_Explosion)
        {
            return;
        }

        eventExplosionHit(Momentum);
        CombatFx.Notify(this, CombatFx.HeadCamera, 0f);
        ClientDamageHudEffect(DamageType);
        if (CombatIkOk)
        {
            CombatHitCameraShake();
        }
    }

    /// <summary>The charge and berserk ClientDamage (rule 35): the laser back on, then the global version.</summary>
    [Uc("SBase.SPlayerController.s_Berserk360.ClientDamage", Sha1 = "54ad1e643223ff858edeb5948b5f0ebea784f150")]
    [Uc("SBase.SPlayerController.s_PlayerCharging.ClientDamage", Sha1 = "3f6b4c1fb9c47ef227d7c5f2cdd27e47f1714639")]
    [PortOf("uc:SBase.SPlayerController:s_Berserk360.ClientDamage", Spec = "spec/combat/state-damage-reactions.md#clientdamage-overrides-owner-side")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.ClientDamage", Spec = "spec/combat/state-damage-reactions.md#clientdamage-overrides-owner-side")]
    private void LaserFirstClientDamage(FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
    {
        RallumeLaserEteint();
        GlobalClientDamage(Momentum, DamageType, HitLocation, instigatedBy);
    }

    /// <summary>The HUD overlay of a damage type (native 1388; the per-type overlays belong to the HUD spec).</summary>
    [UcNative("ASPlayerController::execClientDamageHudEffect", Exe = 0x10ab0ae0)]
    [Uc("SBase.SPlayerController.ClientDamageHudEffect", Sha1 = "18b5ee79e7ac30265c00298c3c8663f4c88ba027")]
    [PortOf("nat:SBase.SPlayerController.ClientDamageHudEffect", Spec = "spec/hud/screen-effects.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Body game 0x10ab0990: nothing without myHUD; type 7 sends the goggle's mode event (1 in mode 37, 2 in 38); type 8 outside modes 36 to 38 plays Snd_FlashBangSiffle at the controller (volume 1, radius 100, pitch 1); the washes are the HUD's (CombatFx)")]
    [Provenance(ProvenanceKind.Estimated, "The native returns at once without myHUD; the port keeps going (its HUD layer draws from the notification even where no SHud actor was spawned, as in test scenes); the perturbation still needs the SHud")]
    public void ClientDamageHudEffect(SDamageType DamageType)
    {
        // The game-state parts of the native run here; the HUD (HudEffects) draws the rest from the notification.
        int mode = Goggle?.CurrentMode ?? RendMap;
        if (DamageType == SDamageType.SDTExtincteurProjection && Goggle is { } goggle)
        {
            // Foam on the visor ends motion or EMF vision: the active mode's key again turns it off.
            if (mode == 37)
            {
                goggle.SwitchMode(1);
            }
            else if (mode == 38)
            {
                goggle.SwitchMode(2);
            }
        }
        else if (DamageType == SDamageType.SDTFlash && mode is not (36 or 37 or 38))
        {
            PlaySound(Snd_FlashBangSiffle, ESoundSlot.SLOT_None, Equipment.K.FlashWhistleVolume, false, Equipment.K.FlashWhistleRadius, 1f);
        }
        else if (DamageType is not (SDamageType.SDTExtincteurProjection or SDamageType.SDTFlash or SDamageType.SDTStickyShocker) &&
                 Pawn is not null)
        {
            // Every other hit perturbs a merc's HUD for 1.5 s (reload, goggles and the scope are refused meanwhile);
            // SetHudPerturbationLength does nothing for spies.
            SetHudPerturbationLength(K.DamageHudPerturbation);
        }

        CombatFx.Notify(this, CombatFx.ClientDamageHudEffect, 0f, 0f, 0f, (int)DamageType);
    }

    /// <summary>
    /// The hit animation on the owner (rules 39 to 41): the heartbeat for the local player, then the action by
    /// momentum (global) or by pill when standing still (walking states). Runs in the owner's current state.
    /// </summary>
    [Uc("SBase.SPlayerController.PlayAnimDmg", Sha1 = "9d8325f0ab4509b82d670f1bd5c57c8be65475d4")]
    [PortOf("uc:SBase.SPlayerController:PlayAnimDmg", Spec = "spec/combat/damage-pipeline.md#rules")]
    [PortOf("rep:SBase.SPlayerController.PlayAnimDmg", Spec = "spec/combat/damage-pipeline.md#network")]
    public void PlayAnimDmg(int PillTag, FVector Momentum)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_PlayAnimDmg) { Int1 = PillTag, Vector1 = Momentum });
            return;
        }

        if (StateNode is IPlayAnimDmg s)
        {
            s.PlayAnimDmg(this, PillTag, Momentum);
            return;
        }

        switch (CombatDispatch.HitAnimFor(GetStateName()))
        {
            case HitAnimKind.None:
                return;
            case HitAnimKind.Walking:
                WalkingPlayAnimDmg(PillTag, Momentum);
                return;
            default:
                GlobalPlayAnimDmg(Momentum);
                return;
        }
    }

    private static readonly Name CNAME_PlayAnimDmg = new("PlayAnimDmg");

    /// <summary>The heartbeat of a hit and the adrenaline timer.</summary>
    private void CombatStartHeartBeat()
    {
        if (!IsRemotePlayer)
        {
            PlaySound(snd_HeartBeat, ESoundSlot.SLOT_Misc, 1f, false, K.HeartBeatRadius);
        }

        fTimeWhenHit = Level.TimeSeconds;
    }

    private void GlobalPlayAnimDmg(FVector Momentum)
    {
        CombatStartHeartBeat();
        if (SBPawn is { } pawn)
        {
            CombatPorts.ChangeEnumAction(pawn, CombatRules.GlobalHitAction(pawn.Rotation, Momentum), false);
        }
    }

    [Uc("SBase.SPlayerController.s_PlayerWalking.PlayAnimDmg", Sha1 = "48670accd8e082b1bd3e76fe6ba549cbd49a2ca8")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.PlayAnimDmg", Spec = "spec/combat/damage-pipeline.md#rules")]
    private void WalkingPlayAnimDmg(int PillTag, FVector Momentum)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (!pawn.Velocity.IsZero())
        {
            GlobalPlayAnimDmg(Momentum);
            return;
        }

        CombatStartHeartBeat();
        int action = CombatRules.StandingHitAction(PillTag, pawn.Rotation, Momentum);
        if (action != SBasePawn.SA_NONE)
        {
            CombatPorts.ChangeEnumAction(pawn, action, false);
        }
    }

    /// <summary>
    /// The death animation on the owner (rules 44 to 46): the owner's health is set first; momentum (0,0,1) is the
    /// falling death, any other the native's direction.
    /// </summary>
    [Uc("SBase.SPlayerController.PlayAnimDeath", Sha1 = "6e8befd2bc53d125b53f80abac46c1338562a10f")]
    [PortOf("uc:SBase.SPlayerController:PlayAnimDeath", Spec = "spec/combat/damage-pipeline.md#playanimdeath-reliable-server-to-owner-rpc-simulated")]
    [PortOf("rep:SBase.SPlayerController.PlayAnimDeath", Spec = "spec/combat/damage-pipeline.md#network")]
    public void PlayAnimDeath(FVector Momentum, int PawnHealth)
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_PlayAnimDeath) { Vector1 = Momentum, Int1 = PawnHealth });
            return;
        }

        if (StateNode is IPlayAnimDeath s)
        {
            s.PlayAnimDeath(this, Momentum, PawnHealth);
            return;
        }

        if (CombatDispatch.IgnoresPlayAnimDeath(GetStateName()) || SBPawn is not { } pawn)
        {
            return;
        }

        pawn.Health = PawnHealth;
        int action = Momentum == DeathFallMomentum
            ? SBasePawn.SA_DIE_FALLING
            : CombatRules.DeathAction(CombatPorts.ChooseDeathAnimation(pawn, Momentum));
        if (action != SBasePawn.SA_NONE)
        {
            CombatPorts.ChangeEnumAction(pawn, action, true);
        }
    }

    private static readonly Name CNAME_PlayAnimDeath = new("PlayAnimDeath");

    /// <summary>
    /// A death the server waits to resolve through the owner's blast (rule 47): when the owner did not start one, it
    /// dies on the ground or falls inert.
    /// </summary>
    [Uc("SBase.SPlayerController.TellClientServerWaitForFatalBlast", Sha1 = "8943647cc4dbf2393f7042e17f4e27e10b9bc01b")]
    [PortOf("uc:SBase.SPlayerController:TellClientServerWaitForFatalBlast", Spec = "spec/combat/damage-pipeline.md#rules")]
    [PortOf("rep:SBase.SPlayerController.TellClientServerWaitForFatalBlast", Spec = "spec/combat/damage-pipeline.md#network")]
    public void TellClientServerWaitForFatalBlast()
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_TellClientServerWaitForFatalBlast));
            return;
        }

        if (StateNode is ITellClientServerWaitForFatalBlast s)
        {
            s.TellClientServerWaitForFatalBlast(this);
            return;
        }

        if (CombatDispatch.IgnoresFatalBlastNotice(GetStateName()))
        {
            return;
        }

        if (Pawn?.Base is not null)
        {
            PlayAnimDeath(FVector.Zero, Pawn.Health);
            if (!CombatInGameEnded)
            {
                eventChangeState(CNAME_s_Dead);
            }
        }
        else if (!CombatInGameEnded)
        {
            eventChangeState(CNAME_PlayerFallingInerte);
        }
    }

    private static readonly Name CNAME_TellClientServerWaitForFatalBlast = new("TellClientServerWaitForFatalBlast");

    /// <summary>Leaves the binocular or scope view on the owner (the scope state leaves on its next move).</summary>
    [Uc("SBase.SPlayerController.ClientStopBinoculars", Sha1 = "b9a1743d0c5227664f55141e04bec654c6d287d2")]
    [PortOf("uc:SBase.SPlayerController:ClientStopBinoculars", Spec = "spec/equipment/merc-scope.md#rules")]
    public void ClientStopBinoculars()
    {
        if (IsRemotePlayer)
        {
            CombatNet.ClientCall(this, new CombatCall(CNAME_ClientStopBinoculars));
            return;
        }

        bBinocularVision = false;
    }

    private static readonly Name CNAME_ClientStopBinoculars = new("ClientStopBinoculars");

    // ---- blasts ---------------------------------------------------------------------------------------------

    /// <summary>
    /// CanBeBlasted for the damage handlers (the server's copy of the owner's blast decision): a state that defines
    /// it answers through eventCanBeBlasted, a state without a port through the blast spec's state table.
    /// </summary>
    public bool CombatCanBeBlasted(FVector Momentum)
    {
        if (StateNode is ICanBeBlasted)
        {
            return eventCanBeBlasted(Momentum);
        }

        return CombatDispatch.CanBeBlastedFor(GetStateName()) switch
        {
            BlastKind.Standing or BlastKind.BackToWall or BlastKind.BlastOnly => StandingCanBeBlasted(Momentum),
            BlastKind.Fence => FenceCanBeBlasted(Momentum),
            _ => false,
        };
    }

    [Uc("SBase.SPlayerController.s_PlayerWalking.CanBeBlasted", Sha1 = "50f6400c40b457eea0c57b8638e312483f57ee3c")]
    [Uc("SBase.SPlayerController.s_PlayerJump.CanBeBlasted", Sha1 = "abb6c3191b097d6ee8e2a46fb37c31627fbaada6")]
    [Uc("SBase.SPlayerController.s_PlayerRolling.CanBeBlasted", Sha1 = "96694ec59a6f675d716b61b46701fe12448921cd")]
    [Uc("SBase.SPlayerController.s_Berserk360.CanBeBlasted", Sha1 = "10686f90249ee4c5ad47dc911a202d7e1ba4934e")]
    [Uc("SBase.SPlayerController.s_BTW.CanBeBlasted", Sha1 = "f5c239c630314ebf91cded62586b0729197d04b6")]
    [Uc("SBase.SPlayerController.s_Coop_S_Base.CanBeBlasted", Sha1 = "f71e875c67dbadbde254037167fbe4400ce93675")]
    [Uc("SBase.SPlayerController.s_Blast.CanBeBlasted", Sha1 = "f4947cb1827462eb5427e0ed81e1a5ba99ec56f6")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerJump.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_Berserk360.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_BTW.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_Coop_S_Base.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_Blast.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public bool StandingCanBeBlasted(FVector Momentum) => Momentum.Size() < BlastDistMax;

    [Uc("SBase.SPlayerController.GrabFence.CanBeBlasted", Sha1 = "fa433c49e247615ce58fa235bc594b544b4907b0")]
    [Uc("SBase.SPlayerController.OnFence.CanBeBlasted", Sha1 = "87e4dbd17d8a14ebdf8bc2f373f8a1458d4ef8f6")]
    [PortOf("uc:SBase.SPlayerController:GrabFence.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:OnFence.CanBeBlasted", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public bool FenceCanBeBlasted(FVector Momentum) =>
        StandingCanBeBlasted(Momentum) && Pawn is not null && (Pawn.Rotation.Vector() | Momentum) < 0f;

    /// <summary>ExplosionHit through the current state (owner).</summary>
    public void eventExplosionHit(FVector Momentum)
    {
        if (StateNode is IExplosionHit s)
        {
            s.ExplosionHit(this, Momentum);
            return;
        }

        switch (CombatDispatch.ExplosionHitFor(GetStateName()))
        {
            case BlastKind.Standing:
                StandingExplosionHit(Momentum);
                break;
            case BlastKind.BackToWall:
                BackToWallExplosionHit(Momentum);
                break;
            case BlastKind.Fence:
                FenceExplosionHit(Momentum);
                break;
            case BlastKind.GrabBase:
                GrabBaseExplosionHit(Momentum);
                break;
            default:
                ExplosionHit(Momentum);
                break;
        }
    }

    /// <summary>The global ExplosionHit: nothing (no blast; the death resolves through the fatal blast notice).</summary>
    [Uc("SBase.SPlayerController.ExplosionHit", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public void ExplosionHit(FVector Momentum)
    {
    }

    /// <summary>The standing blast (blast spec rule 1): the blast loop, the flight velocity, then s_Blast.</summary>
    [Uc("SBase.SPlayerController.s_PlayerWalking.ExplosionHit", Sha1 = "cf84225e9aa29fa90ee61ac0885ba018f5075307")]
    [Uc("SBase.SPlayerController.s_PlayerJump.ExplosionHit", Sha1 = "6eb6aed22870ec1f97787700c7adc1626faec633")]
    [Uc("SBase.SPlayerController.s_PlayerRolling.ExplosionHit", Sha1 = "540ba89a652075177e2aa68332af0b4395074554")]
    [Uc("SBase.SPlayerController.s_Berserk360.ExplosionHit", Sha1 = "fc75a811d657e00e299c11896a0cb4b459e074ce")]
    [Uc("SBase.SPlayerController.s_Coop_S_Base.ExplosionHit", Sha1 = "ba458a8742fb9864433c16a6446fb32b003e06dc")]
    [Uc("SBase.SPlayerController.s_StandUp.ExplosionHit", Sha1 = "d38c86306a6ef9f82820ef4db960168e4f07eaac")]
    [Uc("SBase.SPlayerController.s_TerminatePawn_Base.ExplosionHit", Sha1 = "6a211c5c7b619e9ac3e545b12fcc1b167deb6f18")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalking.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerJump.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerRolling.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_Berserk360.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_Coop_S_Base.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_StandUp.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_TerminatePawn_Base.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public void StandingExplosionHit(FVector Momentum)
    {
        if (!(Momentum.Size() < BlastDistMax) || SBPawn is not { } pawn)
        {
            return;
        }

        CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_BLAST_LOOP, true);
        pawn.SetVelocityAndOrientationForBlast(CombatRules.BlastVelocity(Momentum, BlastDistMax, BlastSpeedMax, BlastSpeedMin));
        eventChangeState(CNAME_s_Blast);
    }

    [Uc("SBase.SPlayerController.s_BTW.ExplosionHit", Sha1 = "b4e16f1f7de4b02e02de0e2f5b72fd6814b55f59")]
    [PortOf("uc:SBase.SPlayerController:s_BTW.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public void BackToWallExplosionHit(FVector Momentum)
    {
        if (!(Momentum.Size() < BlastDistMax) || SBPawn is not { } pawn)
        {
            return;
        }

        CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_BTW_OUT, true);
        StandingExplosionHit(Momentum);
    }

    [Uc("SBase.SPlayerController.GrabFence.ExplosionHit", Sha1 = "515a2e4c47638b7d01ad5f1604fe26b818c3b67c")]
    [Uc("SBase.SPlayerController.OnFence.ExplosionHit", Sha1 = "e259b18b824fc15b6f0fe0fcfd6722605a602d28")]
    [PortOf("uc:SBase.SPlayerController:GrabFence.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("uc:SBase.SPlayerController:OnFence.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public void FenceExplosionHit(FVector Momentum)
    {
        if (FenceCanBeBlasted(Momentum))
        {
            StandingExplosionHit(Momentum);
        }
    }

    [Uc("SBase.SPlayerController.s_GrabBase.ExplosionHit", Sha1 = "c1280d283a60b4186a71b2b0f59cc65f3315ca2c")]
    [PortOf("uc:SBase.SPlayerController:s_GrabBase.ExplosionHit", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public void GrabBaseExplosionHit(FVector Momentum)
    {
        if (!(Momentum.Size() < BlastDistMax) || SBPawn is not { } pawn)
        {
            return;
        }

        eventChangeState(CNAME_s_Blast);
        CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_BLAST_LOOP_AFTERGRAB, true);
        pawn.SetVelocityAndOrientationForBlast(CombatRules.BlastVelocity(Momentum, BlastDistMax, BlastSpeedMax, BlastSpeedMin));
        CombatFx.Notify(this, CombatFx.HeadCamera, 0f);
    }

    /// <summary>
    /// Server side of the owner's blast flight (reliable owner to server): falling physics, the velocity sent at 100
    /// times scale, and the pawn facing back toward the blast.
    /// </summary>
    [Uc("SBase.SPlayerController.ServerSetVelocityAndOrientationForBlast", Sha1 = "bd321db165308aa25a7a9f426693bb2638e6e2f8")]
    [PortOf("uc:SBase.SPlayerController:ServerSetVelocityAndOrientationForBlast", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    [PortOf("rep:SBase.SPlayerController.ServerSetVelocityAndOrientationForBlast", Spec = "spec/combat/blast-and-fall-damage.md#rules")]
    public void ServerSetVelocityAndOrientationForBlast(FVector V)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(CNAME_ServerSetVelocityAndOrientationForBlast) { Vector1 = V });
            return;
        }

        if (SBPawn is { } pawn)
        {
            pawn.ApplyBlastVelocity(V * (1f / K.BlastVelocityWireScale));
        }
    }

    private static readonly Name CNAME_ServerSetVelocityAndOrientationForBlast = new("ServerSetVelocityAndOrientationForBlast");
}
