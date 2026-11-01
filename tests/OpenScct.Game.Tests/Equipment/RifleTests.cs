using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Equipment;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The merc rifle's trigger, rate of fire, clip and reload (spec/equipment/merc-rifle.md).</summary>
public sealed class RifleTests
{
    private static readonly Name Selected = new("s_Selected");
    private static readonly Name Firing = new("s_Firing");
    private static readonly Name Reloading = new("s_Reloading");
    private static readonly Name FullReloading = new("s_FullReloading");

    private static (CombatScene Scene, SPlayerController PC, SWeaponDefense Rifle) Merc()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), merc);
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        scene.Run(0.1f);
        return (scene, pc, rifle);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LandingDipSurvivesWeaponTicksAndRecoversOnlyAfterTheClip(bool high, bool moving)
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc();
        pc.Pawn!.SetPhysics(EPhysics.PHYS_None);
        pc.Pawn.Velocity = moving ? new FVector(100f, 0f, 0f) : FVector.Zero;
        rifle.AnimateFall();
        if (high) rifle.AnimateLandHigh(); else rifle.AnimateLand();
        Name landing = high ? ViewModelClips.FirstLandStHi2 : ViewModelClips.FirstLandStFd2;
        float seconds = high ? 20f / 15f : 10f / 15f;
        Assert.Equal(seconds, rifle.ViewAnim.Remaining(SWeaponDefense.MVTCHANNEL), 5);

        scene.Run(0.1f);
        Assert.Equal(landing, rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.MVTCHANNEL));
        Assert.InRange(rifle.ViewAnim.Remaining(SWeaponDefense.MVTCHANNEL), seconds - 0.12f, seconds - 0.08f);
        scene.Run(seconds - 0.2f);
        Assert.Equal(landing, rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
        scene.Run(0.2f);
        Assert.Equal(moving ? ViewModelClips.FirstWalkStFd2 : ViewModelClips.FirstWaitStFd2,
            rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
    }

    [Fact]
    public void TheRifleStartsIdleWithAFullClip()
    {
        (_, _, SWeaponDefense rifle) = Merc();
        Assert.Equal(Selected, rifle.GetStateName());
        Assert.Equal(25, rifle.ClipAmmo);
        Assert.Equal(125, rifle.Ammo);
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.ROFMode);
    }

    [Fact]
    public void AutoFireShootsEveryRateOfFireWhileHeldThenStops()
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc();
        pc.bFire = 1;
        rifle.eventFire(1f);
        Assert.Equal(Firing, rifle.GetStateName());
        Assert.Equal(24, rifle.ClipAmmo);
        Assert.Equal(124, rifle.Ammo);
        scene.Run(0.45f);
        Assert.Equal(22, rifle.ClipAmmo);
        pc.bFire = 0;
        scene.Run(0.25f);
        Assert.Equal(Selected, rifle.GetStateName());
        Assert.Equal(22, rifle.ClipAmmo);
    }

    [Fact]
    public void BurstFiresThreeShotsAndNeedsANewPress()
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc();
        rifle.SwitchROF();
        Assert.Equal(SRateOfFireMode.ROF_Burst, rifle.ROFMode);
        pc.bFire = 1;
        rifle.eventFire(1f);
        scene.Run(1f);
        Assert.Equal(22, rifle.ClipAmmo);
        Assert.Equal(Selected, rifle.GetStateName());
    }

    [Fact]
    public void FireIsIgnoredWhileFiring()
    {
        (_, _, SWeaponDefense rifle) = Merc();
        rifle.SwitchROF();
        rifle.SwitchROF();
        rifle.ROF_Single(true);
        rifle.eventFire(1f);
        rifle.eventFire(1f);
        Assert.Equal(24, rifle.ClipAmmo);
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.PreviousROFMode);
    }

    [Fact]
    public void ReloadFillsTheClipFromTheReserveAtTheEndOfTheClip()
    {
        (CombatScene scene, _, SWeaponDefense rifle) = Merc();
        rifle.ClipAmmo = 10;
        rifle.Ammo = 110;
        Assert.True(rifle.eventReload());
        Assert.Equal(Reloading, rifle.GetStateName());
        rifle.eventFire(1f);
        Assert.Equal(10, rifle.ClipAmmo);
        scene.Run(2.2f);
        Assert.Equal(25, rifle.ClipAmmo);
        Assert.Equal(110, rifle.Ammo);
        Assert.Equal(Selected, rifle.GetStateName());
    }

    [Fact]
    public void AFullClipDoesNotReload()
    {
        (_, _, SWeaponDefense rifle) = Merc();
        Assert.False(rifle.eventReload());
        Assert.Equal(Selected, rifle.GetStateName());
    }

    [Fact]
    public void AnEmptyAutoClipReloadsByItself()
    {
        (CombatScene scene, _, SWeaponDefense rifle) = Merc();
        rifle.ClipAmmo = 0;
        rifle.Ammo = 50;
        scene.Run(0.1f);
        Assert.Equal(Reloading, rifle.GetStateName());
    }

    [Fact]
    public void TheAmmoCrateRefillsReserveAndClip()
    {
        (CombatScene scene, _, SWeaponDefense rifle) = Merc();
        rifle.ClipAmmo = 3;
        rifle.Ammo = 40;
        Assert.True(rifle.eventFullReload());
        Assert.Equal(FullReloading, rifle.GetStateName());
        scene.Run(2.2f);
        Assert.Equal(125, rifle.Ammo);
        Assert.Equal(25, rifle.ClipAmmo);
    }

    [Fact]
    public void SpreadDecaysAfterTheBurst()
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc();
        pc.bFire = 1;
        rifle.eventFire(1f);
        scene.Run(0.65f);
        pc.bFire = 0;
        Assert.True(rifle.BulletInARow >= 3);
        scene.Run(1f);
        Assert.Equal(0, rifle.BulletInARow);
        Assert.Equal(0.15f, rifle.GetAccuracy(), 4);
    }
}
