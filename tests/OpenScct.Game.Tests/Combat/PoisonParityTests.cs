using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>Decoded poison behavior from combat/health.md and natives2-damage.md rule 8.</summary>
public sealed class PoisonParityTests
{
    private static (GameWorld World, SPlayerController Spy, SPlayerController Merc) Match()
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        CombatEquipment.Install(world.Level);
        world.Run(3f);
        foreach (SPlayerController player in players)
        {
            player.Pawn!.SetPhysics(EPhysics.PHYS_None);
            player.Pawn.Velocity = default;
            player.fSpawnProtectionEnd = -1f;
            player.GotoState("s_PlayerWalkingSt0H");
        }
        return (world, players[0], players[1]);
    }

    private static SPawnAttaque Poison(SPlayerController victim, SPlayerController source)
    {
        var pawn = (SPawnAttaque)victim.Pawn!;
        victim.PawnTakeDamage(0, source.Pawn, pawn.Location, default, SDamageType.SDamage_Poison);
        pawn.PoisonnedBy = (SBasePawn)source.Pawn!;
        return pawn;
    }

    [Fact]
    public void PoisonHitStartsTheStatusWithoutImmediateDamageOrRefillPoints()
    {
        var (_, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);

        Assert.True(pawn.bPoisonned);
        Assert.Equal(100, pawn.Health);
        Assert.Equal(6f, pawn.fPoisonned_HealthLostPerSecond);
        Assert.Equal(0, pawn.NbPointsRefillable);
        Assert.Same(merc, spy.PoisonInstigator);
        Assert.Equal(100, spy.PoisonLastHealth);
        Assert.Equal(0, spy.PlayerReplicationInfo!.Stats!.DamageTaken);
    }

    [Theory]
    [InlineData(4, 97)]
    [InlineData(8, 94)]
    [InlineData(40, 70)]
    [InlineData(80, 40)]
    public void PoisonDrainsSixHealthPerSecondWithNativeIntegerTruncation(int eighthSeconds, int health)
    {
        var (_, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);
        // Eighths give exact binary steps, independent of floating-point accumulation at a particular frame rate.
        for (int i = 0; i < eighthSeconds; i++) pawn.NativeTickHealth(0.125f);

        Assert.Equal(health, pawn.Health);
        spy.CountPoisonDrain();
        Assert.Equal(100 - health, spy.PlayerReplicationInfo!.Stats!.DamageTaken);
        Assert.Equal(100 - health, merc.PlayerReplicationInfo!.Stats!.DamageGiven);
        spy.CountPoisonDrain();
        Assert.Equal(100 - health, merc.PlayerReplicationInfo.Stats.DamageGiven);
    }

    [Fact]
    public void TheRunningWorldTicksPoisonAtSixHealthPerSecond()
    {
        var (world, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);
        world.Run(5f);

        // Native float subtraction and truncation can put the displayed integer one below the ideal value.
        Assert.InRange(pawn.Health, 69, 70);
        Assert.True(pawn.bPoisonned);
        spy.CountPoisonDrain();
        Assert.Equal(100 - pawn.Health, merc.PlayerReplicationInfo!.Stats!.DamageGiven);
    }

    [Fact]
    public void PoisonDoesNotRegenerateOrConsumeTheSpysExistingRefillPool()
    {
        var (_, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);
        pawn.NbPointsRefillable = 30;
        pawn.HealthRefill = 10f;
        for (int i = 0; i < 8; i++) pawn.NativeTickHealth(0.125f);

        Assert.Equal(94, pawn.Health);
        Assert.Equal(30, pawn.NbPointsRefillable);
        Assert.Equal(10f, pawn.HealthRefill);
    }

    [Fact]
    public void ASecondPoisonHitDoesNotStackDrainOrResetItsFractionalHealth()
    {
        var (_, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);
        pawn.NativeTickHealth(0.125f);
        Assert.Equal(99, pawn.Health);
        Poison(spy, merc);
        pawn.NativeTickHealth(0.125f);

        Assert.Equal(98, pawn.Health);
        Assert.Equal(98.5f, pawn.bHealthDecrease);
        spy.CountPoisonDrain();
        Assert.Equal(2, merc.PlayerReplicationInfo!.Stats!.DamageGiven);
    }

    [Fact]
    public void BulletDamageResynchronizesPoisonWithoutHealingTheHitOrDoubleCountingDamage()
    {
        var (_, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);
        pawn.NativeTickHealth(0.25f);
        Assert.Equal(98, pawn.Health);
        spy.PawnTakeDamage(25, merc.Pawn, pawn.Location, default, SDamageType.SDTBase, 6, 70);
        Assert.Equal(73, pawn.Health);

        // The native uses this tick to synchronize after a separate damage hit, then drains on the next tick.
        pawn.NativeTickHealth(0.125f);
        Assert.Equal(73, pawn.Health);
        pawn.NativeTickHealth(0.125f);
        Assert.Equal(72, pawn.Health);
        Assert.Equal(17, pawn.NbPointsRefillable);
        spy.CountPoisonDrain();
        Assert.Equal(28, spy.PlayerReplicationInfo!.Stats!.DamageTaken);
        Assert.Equal(28, merc.PlayerReplicationInfo!.Stats!.DamageGiven);
    }

    [Fact]
    public void MedkitCuresPoisonCreditsItsDrainAndAllowsANewFullHealthInfection()
    {
        var (world, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);
        pawn.NativeTickHealth(5f);
        pawn.NbPointsRefillable = 15;
        var kit = world.Level.SpawnActor(new SInfirmary { bNoDelete = false }, default, default)!;
        kit.Use(spy);

        Assert.Equal(100, pawn.Health);
        Assert.False(pawn.bPoisonned);
        Assert.Equal(0, pawn.NbPointsRefillable);
        Assert.Equal(30, merc.PlayerReplicationInfo!.Stats!.DamageGiven);
        Assert.Equal(1, spy.PlayerReplicationInfo!.Stats!.MedkitsUsed);
        world.World.Step();
        spy.CountPoisonDrain();
        Assert.Null(spy.PoisonInstigator);
        Poison(spy, merc);
        pawn.NativeTickHealth(1f);
        Assert.Equal(94, pawn.Health);
    }

    [Fact]
    public void TeamHealCuresPoisonAndPreservesExistingRefillPoints()
    {
        var (world, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);
        pawn.NativeTickHealth(5f);
        pawn.NbPointsRefillable = 15;
        var healer = world.Level.Spawn<SPlayerController>()!;
        var healerPawn = world.Level.Spawn<SPawnAttaque>(pawn.Location - new FVector(120f, 0f, 0f))!;
        healer.Pawn = healer.SBPawn = healerPawn;
        healerPawn.Controller = healer;
        healer.GotoState("s_PlayerWalkingSt0H");
        pawn.Rotation = new FRotator(0, 32768, 0);
        var heal = world.Level.Spawn<SHealMateInteraction>(owner: healer)!;
        InteractionMenuRules.InteractionInit(healer, heal, 0);

        Assert.Equal(100, pawn.Health);
        Assert.False(pawn.bPoisonned);
        Assert.Equal(15, pawn.NbPointsRefillable);
        Assert.Equal(30, merc.PlayerReplicationInfo!.Stats!.DamageGiven);
        pawn.NativeTickHealth(1f);
        Assert.Equal(100, pawn.Health);
    }

    [Fact]
    public void PoisonDeathUsesTheOriginalLethalDamageAndCreditsExactlyTheVictimsHealth()
    {
        var (world, spy, merc) = Match();
        SPawnAttaque pawn = Poison(spy, merc);
        // int(1.75) is still alive; int(1.0) is still alive; the next 0.75-point drain kills.
        for (int i = 0; i < 132; i++) pawn.NativeTickHealth(0.125f);
        Assert.Equal(1, pawn.Health);
        Assert.True(pawn.bPoisonned);
        pawn.NativeTickHealth(0.125f);

        Assert.False(pawn.bPoisonned);
        Assert.True(pawn.Health <= 0);
        // NativeTick sends 1500 SDTBase with pill 3; the spy's 40% zone table records 600 actual damage.
        Assert.Equal(600, spy.LastDamages[0].Damage);
        Assert.Contains(world.Host.Named("ReceiveLocalizedMessage"), call =>
            call.Call.Class.Path == "SBase.SDeathMessage" &&
            CombatActivityDeath.LegacySwitch(call.Call.Int) == (int)SDamageType.SDTBase);
        Assert.Equal(3, spy.LastDamages[0].PillTag);
        Assert.Equal(1, spy.PlayerReplicationInfo!.Deaths);
        Assert.Equal(1, merc.PlayerReplicationInfo!.Kills);
        Assert.Equal(100, spy.PlayerReplicationInfo.Stats!.DamageTaken);
        Assert.Equal(100, merc.PlayerReplicationInfo.Stats!.DamageGiven);
        Assert.Null(spy.PoisonInstigator);
    }

    [Theory]
    [InlineData(ENetRole.ROLE_AutonomousProxy)]
    [InlineData(ENetRole.ROLE_SimulatedProxy)]
    public void ReplicatedClientPawnsNeverApplyPoisonDamageLocally(ENetRole role)
    {
        var pawn = new SPawnAttaque { Role = role, Health = 100, bHealthDecrease = 100, bPoisonned = true };
        pawn.NativeTickHealth(30f);

        Assert.Equal(100, pawn.Health);
        Assert.True(pawn.bPoisonned);
        Assert.Equal(100f, pawn.bHealthDecrease);
    }
}
