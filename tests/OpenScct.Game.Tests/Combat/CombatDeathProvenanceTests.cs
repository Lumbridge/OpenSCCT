using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

public sealed class CombatDeathProvenanceTests
{
    private static (GameWorld World, SPlayerController Spy, SPlayerController Merc) Match()
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        world.Run(3f);
        foreach (var player in players)
        {
            player.Pawn!.SetPhysics(EPhysics.PHYS_None);
            player.Pawn.Health = 100;
            player.fSpawnProtectionEnd = -1f;
            player.GotoState("s_PlayerWalkingSt0H");
        }
        players[0].Pawn!.SetLocation(new FVector(2000, 0, 100));
        players[1].Pawn!.SetLocation(new FVector(100, 0, 100));
        world.Host.Calls.Clear();
        return (world, players[0], players[1]);
    }

    private static GameCall Death(GameWorld world)
    {
        var calls = world.Host.Named("ReceiveLocalizedMessage")
            .Where(c => c.Call.Class.Path == "SBase.SDeathMessage").Select(c => c.Call).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(calls[0].Int, calls[1].Int);
        Assert.Equal(default, CombatDeathContext.Current);
        return calls[0];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExplosiveKeepsItsKillCategoryAfterTheOwnersPawnIsRemoved(bool mine)
    {
        var (world, spy, merc) = Match();
        spy.Pawn!.SetLocation(new FVector(100, 0, 100));
        Pawn owner = merc.Pawn!;
        owner.SetLocation(new FVector(2000, 0, 100));
        Actor explosive = mine
            ? world.Level.SpawnActor(new SMineProx { PcOwner = merc }, new FVector(0, 0, 100), default, owner, owner)!
            : world.Level.SpawnActor(new SProjFrag(), new FVector(0, 0, 100), default, owner, owner)!;
        owner.Destroy();
        world.World.Step();
        Assert.Null(merc.Pawn);
        if (explosive is SProjFrag frag) frag.Explode(frag.Location, default);
        else explosive.eventTakeDamage(1, spy.Pawn, explosive.Location, default, SDamageType.SDTStickyShocker);
        Assert.True(spy.Pawn!.Health <= 0);
        Assert.Equal(1, merc.PlayerReplicationInfo!.Kills);
        Assert.Equal(mine ? 1 : 0, merc.PlayerReplicationInfo.Stats!.Merc_MineKills);
        Assert.Equal(mine ? 0 : 1, merc.PlayerReplicationInfo.Stats.Merc_GrenadeKills);
        Assert.Equal(mine ? 1 : 0, spy.PlayerReplicationInfo!.Stats!.Spy_DeathFromMine);
        Assert.Equal(mine ? 0 : 1, spy.PlayerReplicationInfo.Stats.Spy_DeathFromGrenade);
    }

    [Fact]
    public void AShockedMercCollisionCountsTheSpysDeathCause()
    {
        var (world, spy, merc) = Match();
        world.Game.Killed(merc, spy, spy.Pawn, SDamageType.SDTStickyBlast);
        Assert.Equal(1, spy.PlayerReplicationInfo!.Stats!.Spy_DeathFromJumpingTasedMerc);
        Assert.Equal(0, merc.PlayerReplicationInfo!.Stats!.Merc_WeaponHipfireKills);
    }

    [Fact]
    public void ABulletKillingAnAlreadyPoisonedSpyCountsAsAGunKill()
    {
        var (world, spy, merc) = Match();
        var gun = world.Level.SpawnActor(new SWeaponDefense(), merc.Pawn!.Location, default, merc.Pawn, merc.Pawn)!;
        gun.GiveTo(merc.Pawn);
        spy.PoisonInstigator = merc;
        ((SBasePawn)spy.Pawn!).bPoisonned = true;
        spy.PawnTakeDamage(500, merc.Pawn, spy.Pawn.Location, default, SDamageType.SDTBase, 1);
        Assert.Equal(1, merc.PlayerReplicationInfo!.Stats!.Merc_WeaponHipfireKills);
        Assert.Equal(0, merc.PlayerReplicationInfo.Stats.Merc_MineKills);
        Assert.Equal(1, spy.PlayerReplicationInfo!.Stats!.Spy_DeathFromWeaponHipfire);
    }

    [Theory]
    [InlineData("FAC_STM.fakebaril", CombatDeathCause.BarrelExplosion)]
    [InlineData("SM_Egouts.Egouts_GD_SM_X-Box.X_F_BonbonneSE", CombatDeathCause.GasExplosion)]
    [InlineData("Squat.squ_mer_obj_extinct", CombatDeathCause.ExplosiveExtinguisher)]
    [InlineData("Squat.sqt_bombgaz", CombatDeathCause.GasExplosion)]
    [InlineData("Squat.sqt_canetteB", CombatDeathCause.SodaExplosion)]
    [InlineData("Squat.sqt_molotov", CombatDeathCause.MolotovExplosion)]
    [InlineData("Custom.unknown_prop", CombatDeathCause.ExplosiveProp)]
    public void ShootingAnAuthoredPropReportsItsSourceEvenWithMeleeOrBulletDamage(string mesh, CombatDeathCause cause)
    {
        var (world, spy, merc) = Match();
        var prop = world.Level.SpawnActor(new SGamePlayObject
        {
            bNoDelete = false, bDamageable = true, HitPoints = 1, StaticMesh = new AssetRef(mesh),
            bCanDamageActor = true, DamageActorRange = 500, DamageActorPoints = 600,
            DamageActorType = SDamageType.SDamage_Charged,
        }, new FVector(0, 0, 100), default)!;
        prop.eventTakeDamage(1, spy.Pawn, prop.Location, default, SDamageType.SDTStickyShocker);
        GameCall death = Death(world);
        Assert.Equal(cause, CombatActivityDeath.Cause(death.Int));
        Assert.Equal(CombatDeathTrigger.Shot, CombatActivityDeath.Trigger(death.Int));
        Assert.Equal((int)SDamageType.SDamage_Charged, CombatActivityDeath.LegacySwitch(death.Int));
        Assert.Same(spy.PlayerReplicationInfo, death.Pri1);
        Assert.Same(spy.PlayerReplicationInfo, death.Object);
        Assert.Same(merc.PlayerReplicationInfo, death.Pri2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PoisonMineDetailSurvivesMineDestructionAndDelayedGenericDamageTick(bool ownerPawnDestroyed)
    {
        var (world, spy, merc) = Match();
        spy.Pawn!.SetLocation(new FVector(100, 0, 100));
        merc.Pawn!.SetLocation(new FVector(2000, 0, 100));
        var mine = world.Level.SpawnActor(new SMinePoison(), new FVector(0, 0, 100), default, merc.Pawn, merc.Pawn)!;
        mine.LaserEndLocation = new FVector(1000, 0, 100);
        mine.CheckPresence();
        var victim = (SBasePawn)spy.Pawn;
        Assert.True(victim.bPoisonned);
        Assert.True(mine.bDeleteMe);
        if (ownerPawnDestroyed)
        {
            merc.Pawn!.Destroy();
            world.World.Step();
        }
        victim.Health = 2;
        victim.bHealthDecrease = 2;
        victim.NativeTickHealth(10f);
        GameCall death = Death(world);
        Assert.Equal(CombatDeathCause.PoisonMine, CombatActivityDeath.Cause(death.Int));
        Assert.Equal((int)SDamageType.SDTBase, CombatActivityDeath.LegacySwitch(death.Int));
        Assert.Same(merc.PlayerReplicationInfo, death.Pri1);
        Assert.Same(spy.PlayerReplicationInfo, death.Pri2);
        Assert.Equal(1, merc.PlayerReplicationInfo!.Stats!.Merc_MineKills);
        Assert.Equal(1, spy.PlayerReplicationInfo!.Stats!.Spy_DeathFromMine);
        Assert.Equal(0, merc.PlayerReplicationInfo.Stats.Merc_WeaponHipfireKills);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DelayedMolotovFireRetainsTheShotAndShooterAfterTheBottleIsGone(bool disconnected, bool mineChain)
    {
        var (world, spy, merc) = Match();
        var relay = world.Level.SpawnActor(new SMagicEvent { Tag = "ignite", bNoDelete = false }, default, default)!;
        var group = new FEventGroup { Repeat = 0 };
        group.EventGroup.Add(new FSuperEvent { Event = "burn", Delay = .15f });
        relay.Groups.Add(group);
        var volume = world.Level.SpawnActor(new SDamageVolume { Tag = "burn", bNoDelete = false, bToggle = true, HitPoints = 200, Period = .1f },
            merc.Pawn!.Location, default)!;
        volume.BeginTouch(merc.Pawn);
        var bottle = world.Level.SpawnActor(new SGamePlayObject
        {
            bNoDelete = false, bDamageable = true, HitPoints = 1, StaticMesh = new AssetRef("Squat.sqt_molotov"),
            DamagedMeshes = { new FDamagedMesh { Percent = 100, Event = "ignite" } },
        }, new FVector(500, 0, 100), default)!;
        if (mineChain)
        {
            // The owner retains kill credit when a spy detonates their mine into a delayed prop hazard.
            var mine = world.Level.SpawnActor(new SMineProx(), bottle.Location, default, merc.Pawn, merc.Pawn)!;
            mine.eventTakeDamage(1, spy.Pawn, mine.Location, default, SDamageType.SDTStickyShocker);
            Assert.True(merc.Pawn!.Health > 0);
        }
        else bottle.eventTakeDamage(1, spy.Pawn, bottle.Location, default, SDamageType.SDTStickyShocker);
        Assert.Single(relay.EventQueue);
        bottle.Destroy();
        // A different player destroys a different prop before the delayed fire starts.
        var other = world.Level.SpawnActor(new SGamePlayObject
        {
            bNoDelete = false, bDamageable = true, HitPoints = 1, StaticMesh = new AssetRef("Factory.fakebaril"),
        }, new FVector(5000, 0, 100), default)!;
        other.eventTakeDamage(1, merc.Pawn, other.Location, default, SDamageType.SDTBase);
        Assert.Null(world.Game.GPODamageInstigator);
        if (disconnected) spy.PlayerReplicationInfo!.Destroy();
        world.Run(.5f);
        GameCall death = Death(world);
        Assert.Equal(CombatDeathCause.MolotovFire, CombatActivityDeath.Cause(death.Int));
        Assert.Equal(mineChain ? CombatDeathTrigger.ChainReaction : CombatDeathTrigger.Shot, CombatActivityDeath.Trigger(death.Int));
        if (disconnected) Assert.Null(death.Object);
        else
        {
            Assert.Same(spy.PlayerReplicationInfo, death.Object);
            Assert.Same(mineChain ? merc.PlayerReplicationInfo : spy.PlayerReplicationInfo, death.Pri1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrushAndWorldKillNeverAppearAsFirearmKills(bool crush)
    {
        var (world, spy, merc) = Match();
        if (crush) ((SBasePawn)merc.Pawn!).CrushedBy(spy.Pawn);
        else ((SBasePawn)merc.Pawn!).KilledBy(spy.Pawn);
        GameCall death = Death(world);
        Assert.Equal(crush ? CombatDeathCause.Crush : CombatDeathCause.Hazard, CombatActivityDeath.Cause(death.Int));
        Assert.Equal((int)SDamageType.SDTBase, CombatActivityDeath.LegacySwitch(death.Int));
    }

    [Theory]
    [InlineData(SDamageType.SFell)]
    [InlineData(SDamageType.Crushed)]
    [InlineData(SDamageType.SDamage_Poison)]
    [InlineData(SDamageType.SDamage_Explosion)]
    public void GenericVolumeDoesNotEraseItsAuthoredDamageType(SDamageType damage)
    {
        Assert.Equal(CombatDeathCause.None, CombatDeathContext.VolumeCause(new SDamageVolume { DamageType = damage }, default));
    }

    [Fact]
    public void LedgePullHasDistinctDetailFromAnOrdinaryNeckBreak()
    {
        var (world, spy, merc) = Match();
        merc.GotoState("s_FallAfterGrabbedFromLedge");
        int pill = 0;
        merc.StandardReactionsToDamage(1000, 0, spy.Pawn, merc.Pawn!.Location, default, SDamageType.DamTypeGrabBreak, ref pill);
        Assert.Equal(CombatDeathCause.LedgePull, CombatActivityDeath.Cause(Death(world).Int));
    }

    private sealed class HitPawn(SBasePawn victim, int pill) : IHitZones
    {
        public BoneTraceResult TraceBone(Actor shooter, FVector end, FVector start, bool realFire) => new()
        {
            bHit = true, Actor = victim, PillTag = pill,
            HitLocation = victim.Location, HitNormal = new FVector(-1f, 0f, 0f),
        };
    }

    [Fact]
    public void LethalTaserGadgetHitHasItsOwnCauseAndKeepsTheExistingDamage()
    {
        var (world, spy, merc) = Match();
        spy.Pawn!.Health = 40;
        var taser = world.Level.SpawnActor(new SGadTazerLauncher(), merc.Pawn!.Location, default, merc.Pawn, merc.Pawn)!;
        taser.DamagePawn(spy.Pawn, merc.Pawn, spy.Pawn.Location, merc.Pawn.Location, 0);
        GameCall death = Death(world);
        Assert.Equal(CombatDeathCause.Taser, CombatActivityDeath.Cause(death.Int));
        Assert.Equal((int)SDamageType.SDTStickyShocker, CombatActivityDeath.LegacySwitch(death.Int));
        Assert.Same(merc.PlayerReplicationInfo, death.Pri1);
        Assert.Same(spy.PlayerReplicationInfo, death.Pri2);
        Assert.Equal(-11, spy.Pawn.Health);
    }

    [Fact]
    public void ActualChargeIntoAnElectrifiedPawnCanReportShockCollision()
    {
        var (world, spy, merc) = Match();
        merc.Pawn!.Health = 30;
        merc.Pawn.Rotation = default;
        spy.Pawn!.SetLocation(merc.Pawn.Location + new FVector(100, 0, 0));
        ((SBasePawn)spy.Pawn).bStickyShocking = true;
        merc.Player = null; // Exercise the controlling-side collision callback, not the remote proxy early exit.
        merc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        merc.GotoState("s_PlayerCharging");
        SPlayerController.s_PlayerCharging.Instance.NotifyBump(merc, spy.Pawn);
        GameCall death = Death(world);
        Assert.Equal(CombatDeathCause.ShockCollision, CombatActivityDeath.Cause(death.Int));
        Assert.Equal((int)SDamageType.SDTStickyShocker, CombatActivityDeath.LegacySwitch(death.Int));
        Assert.Same(spy.PlayerReplicationInfo, death.Pri1);
        Assert.Same(merc.PlayerReplicationInfo, death.Pri2);
        Assert.Equal(-5, merc.Pawn.Health);
    }

    [Fact]
    public void OrdinaryShockerShotCannotProduceAnEliminationEvenAtOneHealth()
    {
        var (world, spy, merc) = Match();
        merc.Pawn!.Health = 1;
        world.Level.SetService<IHitZones>(new HitPawn((SBasePawn)merc.Pawn, 2));
        var shocker = world.Level.SpawnActor(new SStickyShocker(), spy.Pawn!.Location, default, spy.Pawn, spy.Pawn)!;
        shocker.GiveTo(spy.Pawn);
        shocker.TraceFireWithPos(spy.Pawn.Location, default, 0, 0, 0);
        Assert.Equal(1, merc.Pawn.Health);
        Assert.DoesNotContain(world.Host.Named("ReceiveLocalizedMessage"), c => c.Call.Class.Path == "SBase.SDeathMessage");
        Assert.Equal(default, CombatDeathContext.Current);
    }

    [Fact]
    public void ExplicitEnvironmentalSourceIsNotRelabeledAsATaser()
    {
        var (world, spy, merc) = Match();
        merc.Pawn!.Health = 40;
        using (CombatDeathContext.Push(new(CombatDeathCause.ExplosiveProp, CombatDeathTrigger.Shot, spy.PlayerReplicationInfo)))
            merc.PawnTakeDamage(0, spy.Pawn, merc.Pawn.Location, default, SDamageType.SDTStickyShocker, -10);
        GameCall death = Death(world);
        Assert.Equal(CombatDeathCause.ExplosiveProp, CombatActivityDeath.Cause(death.Int));
        Assert.Equal(CombatDeathTrigger.Shot, CombatActivityDeath.Trigger(death.Int));
    }

    [Fact]
    public void ResignationDoesNotInheritAnActiveDamageContext()
    {
        var (world, spy, merc) = Match();
        merc.PlayerReplicationInfo!.bResigned = true;
        using (CombatDeathContext.Push(new(CombatDeathCause.ProximityMine, CombatDeathTrigger.Shot, spy.PlayerReplicationInfo)))
            world.Game.BroadcastDeathMessage(merc, merc, SDamageType.Suicided);
        GameCall death = Death(world);
        Assert.Equal(24, death.Int);
        Assert.Null(death.Object);
    }

    [Theory]
    [InlineData(CombatDeathCause.Rifle, false)]
    [InlineData(CombatDeathCause.Rifle, true)]
    [InlineData(CombatDeathCause.Shotgun, false)]
    [InlineData(CombatDeathCause.Shotgun, true)]
    [InlineData(CombatDeathCause.Smg, false)]
    [InlineData(CombatDeathCause.Sniper, false)]
    [InlineData(CombatDeathCause.Sniper, true)]
    [InlineData(CombatDeathCause.Pistol, false)]
    [InlineData(CombatDeathCause.Revolver, true)]
    public void ActualWeaponTraceReportsWeaponAndLethalHeadHits(CombatDeathCause cause, bool head)
    {
        var (world, spy, merc) = Match();
        spy.Pawn!.SetLocation(new FVector(100, 0, 100));
        merc.Pawn!.SetLocation(new FVector(0, 0, 100));
        spy.Pawn.Health = 1;
        world.Level.SetService<IHitZones>(new HitPawn((SBasePawn)spy.Pawn, head ? 1 : 2));
        SWeaponDefense gun = cause switch
        {
            CombatDeathCause.Shotgun => new SWeaponDefense_Shotgun(),
            CombatDeathCause.Smg => new SWeaponDefense_Uzi(),
            CombatDeathCause.Pistol => new MercSidearm { Kind = (int)SidearmKind.Unsuppressed },
            CombatDeathCause.Revolver => new MercSidearm { Kind = (int)SidearmKind.Magnum44 },
            _ => new SWeaponDefense(),
        };
        world.Level.SpawnActor(gun, merc.Pawn.Location, default, merc.Pawn, merc.Pawn);
        gun.GiveTo(merc.Pawn);
        if (cause == CombatDeathCause.Sniper) merc.GotoState("s_Binoculars_Def");
        gun.TraceFire(0, 0, 0);
        GameCall death = Death(world);
        Assert.Equal(cause, CombatActivityDeath.Cause(death.Int));
        Assert.Equal(head, CombatActivityDeath.LegacySwitch(death.Int) == 23);
        Assert.Same(merc.PlayerReplicationInfo, death.Pri1);
        Assert.Equal(cause == CombatDeathCause.Sniper && head ? 1 : 0, merc.PlayerReplicationInfo!.Stats!.Merc_SniperHeadshots);
    }
}
