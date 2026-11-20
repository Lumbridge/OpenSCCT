using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>The damage numbers and the server damage path (spec/combat/damage-pipeline.md, hit-zones-and-weapons.md).</summary>
public sealed class DamageTests
{
    [Theory]
    [InlineData(25, 300f, 75)]
    [InlineData(25, 100f, 25)]
    [InlineData(25, 40f, 10)]
    [InlineData(25, 20f, 5)]
    [InlineData(17, 300f, 51)]
    [InlineData(17, 150f, 25)]
    public void ZoneScalingTruncatesThePercentProduct(int damage, float percent, int expected)
    {
        Assert.Equal(expected, CombatRules.ZoneScaled(damage, percent));
    }

    [Fact]
    public void TaserDamageIs51OfTheDefaultHealth()
    {
        Assert.Equal(51, CombatRules.TaserDamage(K.DefaultHealth));
    }

    [Fact]
    public void RefillPointsArePercentOfTheActualDamage()
    {
        Assert.Equal(35, CombatRules.RefillPoints(35, 100));
        Assert.Equal(17, CombatRules.RefillPoints(25, 70));
    }

    [Theory]
    [InlineData(1500f, 600f, 1500f, 100, 100f)]
    [InlineData(1050f, 600f, 1500f, 100, 50f)]
    [InlineData(1000f, 450f, 1000f, 100, 100f)]
    public void FallDamageIsLinearInTheHeight(float h, float min, float max, int health, float expected)
    {
        Assert.Equal(expected, CombatRules.FallDamage(h, min, max, health), 3);
    }

    [Fact]
    public void ApproxPillUsesTheHeightBandsAndTheRightAxis()
    {
        var at = new FVector(0f, 0f, 0f);
        Assert.Equal(EDamageLocation.P_Head, CombatRules.ApproxPill(at, default, 80f, new FVector(10f, 0f, 70f)));
        Assert.Equal(EDamageLocation.P_ChestR, CombatRules.ApproxPill(at, default, 80f, new FVector(0f, 10f, 20f)));
        Assert.Equal(EDamageLocation.P_ChestL, CombatRules.ApproxPill(at, default, 80f, new FVector(0f, -10f, 20f)));
        Assert.Equal(EDamageLocation.P_LegL, CombatRules.ApproxPill(at, default, 80f, new FVector(0f, -10f, -20f)));
    }

    [Fact]
    public void BlastSpeedRunsFrom800To350OverTheMomentumLength()
    {
        FVector near = CombatRules.BlastVelocity(new FVector(1f, 0f, 0f), 1000f, 800f, 350f);
        FVector far = CombatRules.BlastVelocity(new FVector(999f, 0f, 0f), 1000f, 800f, 350f);
        Assert.Equal(800f, near.Size(), 0);
        Assert.Equal(350f, far.Size(), 0);
        Assert.True(near.Z > 0f);
    }

    [Theory]
    [InlineData(true, 21, 5)]
    [InlineData(false, 17, 6)]
    public void M4TorsoShotsAtTenMetresDefeatAFullHealthSpy(bool beforeRebalance, int perHit, int shots)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (spyPc, spy) = scene.Player(true, new FVector(1000, 0, 100));
        var (_, merc) = scene.Player(false, new FVector(0, 0, 100));
        var rifle = scene.Give(new OpenScct.Game.Svm.M4A1S(), merc);
        rifle.GiveTo(merc);
        if (beforeRebalance) rifle.BaseDamage = 17;
        scene.Run(1);
        Assert.Equal(100, spy.Health);
        int damage = OpenScct.Game.Equipment.WeaponRules.DistanceDamage(rifle, FVector.Dist(merc.Location, spy.Location));
        for (int i = 0; i < shots; i++)
        {
            Assert.True(spy.Health > 0);
            // Pill 2 spans spine to pelvis and uses the normal torso multiplier.
            spyPc.PawnTakeDamage(damage, merc, spy.Location, default, rifle.MyDamageType, 2, rifle.PercentDamageRefillable);
            Assert.Equal(perHit, spyPc.LastDamages[0].Damage);
        }
        Assert.True(spy.Health <= 0);
    }

    [Fact]
    public void RifleHitOnTheHeadPillDealsTripleDamageToASpy()
    {
        var scene = new CombatScene();
        (SPlayerController spyPc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        (_, SBasePawn merc) = scene.Player(false, new FVector(300f, 0f, 100f));
        scene.Run(1f);
        int before = spy.Health;
        spyPc.PawnTakeDamage(25, merc, spy.Location, new FVector(-1f, 0f, 0f), SDamageType.SDTBase, 1, 70);
        Assert.Equal(before - 75, spy.Health);
    }

    [Theory]
    [InlineData(typeof(M4A1S), 1000f, 17)]
    [InlineData(typeof(M4A1S), 6000f, 13)]
    [InlineData(typeof(Famas), 1000f, 17)]
    [InlineData(typeof(Famas), 6000f, 15)]
    [InlineData(typeof(Galil), 1000f, 18)]
    [InlineData(typeof(Galil), 6000f, 16)]
    public void RecoilRecoveryRifleHitAppliesTheWeaponRangeFalloffOnTheServer(System.Type type, float range, int damage)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, merc) = scene.Player(false, new FVector(0, 0, 100));
        var rifle = scene.Give((MercRifle)System.Activator.CreateInstance(type)!, merc);
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        scene.Run(1);
        pc.GotoState(new Name("s_PlayerWalkingSt2H"));
        var (spyPc, spy) = scene.Player(true, new FVector(range, 0, 100));
        rifle.MinDispersion = rifle.MaxDispersion = 0;
        var (start, end) = rifle.AimRay(pc.MyDesiredRotation);
        spy.SetLocation(start + (end - start).SafeNormal() * range - new FVector(0, 0, spy.CollisionHeight * .27f));
        Assert.Equal(14, rifle.BaseDamage);
        pc.bFire = 1;
        pc.eventFire(1f);
        Assert.Same(rifle, spy.LastBulletWeapon);
        Assert.Equal(damage, spyPc.LastDamages[0].Damage);
        Assert.Equal(100 - damage, spy.Health);
    }

    [Fact]
    public void ScopeShotOnTheHeadKillsAndElsewhereUsesTheSnipeTable()
    {
        var scene = new CombatScene();
        (SPlayerController spyPc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        (_, SBasePawn merc) = scene.Player(false, new FVector(800f, 0f, 100f));
        scene.Run(1f);
        int before = spy.Health;
        spyPc.PawnTakeDamage(17, merc, spy.Location, new FVector(-1f, 0f, 0f), SDamageType.SDTSnipeShot, 2, 70);
        Assert.Equal(before - 51, spy.Health);
        spyPc.PawnTakeDamage(17, merc, spy.Location, new FVector(-1f, 0f, 0f), SDamageType.SDTSnipeShot, 1, 70);
        Assert.True(spy.Health <= 0);
    }

    [Fact]
    public void AHitIsRecordedInTheDamageHistoryWithItsPill()
    {
        var scene = new CombatScene();
        (SPlayerController spyPc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        (_, SBasePawn merc) = scene.Player(false, new FVector(300f, 0f, 100f));
        scene.Run(1f);
        spyPc.PawnTakeDamage(25, merc, spy.Location, new FVector(-1f, 0f, 0f), SDamageType.SDTBase, 6, 70);
        Assert.Equal(25, spyPc.LastDamages[0].Damage);
        Assert.Equal(6, spyPc.LastDamages[0].PillTag);
        Assert.Equal(SDamageType.SDTBase, spyPc.LastDamages[0].DamageType);
    }
}
