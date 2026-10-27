using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The rifle, recoil and throw arithmetic (spec/equipment/merc-rifle.md, grenade-throw.md).</summary>
public sealed class WeaponRulesTests
{
    private static SWeaponDefense Rifle() => new();

    [Theory]
    [InlineData(0f, 25)]
    [InlineData(150f, 25)]
    [InlineData(299.9f, 25)]
    [InlineData(300f, 25)]
    [InlineData(1200f, 14)]
    [InlineData(1500f, 12)]
    [InlineData(1800f, 9)]
    [InlineData(5000f, 9)]
    public void DistanceDamageFollowsTheThreeBands(float distance, int expected)
    {
        Assert.Equal(expected, WeaponRules.DistanceDamage(Rifle(), distance));
    }

    [Fact]
    public void DistanceDamageFallsLinearlyBetweenBands()
    {
        int previous = int.MaxValue;
        for (float d = 300f; d <= 1800f; d += 50f)
        {
            int dmg = WeaponRules.DistanceDamage(Rifle(), d);
            Assert.True(dmg <= previous);
            previous = dmg;
        }
    }

    [Theory]
    [InlineData(0f, 0, 0.15f)]
    [InlineData(0f, 4, 0.55f)]
    [InlineData(240f, 0, 0.55f)]
    [InlineData(600f, 0, 0.7f)]
    [InlineData(0f, 10, 0.7f)]
    public void SpreadGrowsWithSpeedAndBurstUpToTheCap(float speed, int bullets, float expected)
    {
        Assert.Equal(expected, WeaponRules.Accuracy(speed, 0.15f, 0.7f, bullets), 4);
    }

    [Fact]
    public void RateOfFireGivesTheShotsOfAPull()
    {
        Assert.Equal(1, WeaponRules.BulletsForMode(SRateOfFireMode.ROF_Single, 25));
        Assert.Equal(3, WeaponRules.BulletsForMode(SRateOfFireMode.ROF_Burst, 25));
        Assert.Equal(2, WeaponRules.BulletsForMode(SRateOfFireMode.ROF_Burst, 2));
        Assert.Equal(25, WeaponRules.BulletsForMode(SRateOfFireMode.ROF_Auto, 25));
    }

    [Fact]
    public void TheModeKeyCyclesAutoBurstAndNeverReachesSingle()
    {
        Assert.Equal(SRateOfFireMode.ROF_Burst, WeaponRules.NextMode(SRateOfFireMode.ROF_Auto));
        Assert.Equal(SRateOfFireMode.ROF_Auto, WeaponRules.NextMode(SRateOfFireMode.ROF_Burst));
        Assert.Equal(SRateOfFireMode.ROF_Burst, WeaponRules.NextMode(SRateOfFireMode.ROF_Single));
        Assert.Equal("Full Auto", WeaponRules.ModeName(SRateOfFireMode.ROF_Auto));
    }

    [Fact]
    public void RecoilPitchKicksUpBy66To230AndClamps()
    {
        Assert.Equal(66, WeaponRules.KickPitch(0, 2f, 0f));
        Assert.Equal(230, WeaponRules.KickPitch(0, 2f, 0.999f));
        Assert.Equal(13000, WeaponRules.KickPitch(12990, 2f, 0.5f));
    }

    [Fact]
    public void RecoilYawStaysInsideTheDocumentedRanges()
    {
        Assert.InRange(WeaponRules.CameraYawKick(4f, 0.999f, 0.999f), 0, 449);
        Assert.InRange(WeaponRules.CameraYawKick(4f, 0f, 0.999f), -449, 0);
        Assert.InRange(WeaponRules.AimYawKick(4f, 0.999f, 0.999f), 0, 296);
        Assert.InRange(WeaponRules.AimYawKick(4f, 0.999f, 0f), -296, 0);
    }

    [Theory]
    [InlineData(0f, 450)]
    [InlineData(0.5f, 1012)]
    [InlineData(1f, 1800)]
    public void ThrowForceForA900Grenade(float value, int expected)
    {
        Assert.Equal(expected, WeaponRules.ThrowForce(value, 900f));
    }

    [Fact]
    public void ThrowGaugeTapIsFullHoldRampsHolsteredIsWeakest()
    {
        Assert.Equal(1f, WeaponRules.ThrowValue(0.1f, true, K.ThrowTapWindow, K.ThrowRamp));
        Assert.Equal(0f, WeaponRules.ThrowValue(0.25f, true, K.ThrowTapWindow, K.ThrowRamp), 4);
        Assert.Equal(0.5f, WeaponRules.ThrowValue(0.75f, true, K.ThrowTapWindow, K.ThrowRamp), 4);
        Assert.Equal(1f, WeaponRules.ThrowValue(2f, true, K.ThrowTapWindow, K.ThrowRamp));
        Assert.Equal(0f, WeaponRules.ThrowValue(1f, false, K.ThrowTapWindow, K.ThrowRamp));
    }

    [Fact]
    public void OneShotClipsEndAtTheLastFrameStart()
    {
        Assert.Equal(2f, K.FirstReloStFd2.Seconds(1f), 4);
        Assert.Equal(6f / 24f, K.FirstSnipStBg2.Seconds(K.SnipeClipRate), 4);
    }
}
