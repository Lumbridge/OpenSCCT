using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

public sealed class PrecisionRifleTests
{
    private static (CombatScene Scene, SPlayerController PC, MercRifle Rifle) Equip(string path, bool aiming)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, merc) = scene.Player(false, new FVector(0, 0, 100));
        var rifle = Assert.IsAssignableFrom<MercRifle>(DeviceClasses.Spawn(merc, new ClassRef(path), merc,
            default, merc.Location, merc.Rotation));
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        scene.Run(1);
        pc.GotoState(new Name("s_PlayerWalkingSt2H"));
        rifle.SetAiming(pc, aiming);
        Assert.Equal(aiming, rifle.AimRequested);
        return (scene, pc, rifle);
    }

    [Theory]
    [InlineData("Svm.BoltRifle", 70, 100)]
    [InlineData("Svm.DesignatedMarksman", 35, 60)]
    public void FiredShotsDeliverTheSpecifiedDamageToSpyHitZonesAtBothRanges(string weapon, int body, int head)
    {
        foreach (float range in new[] { 300f, 6000f })
        foreach (bool aiming in new[] { false, true })
        foreach (var (height, lateral, expected) in new[] { (.87f, 0f, head), (.27f, 0f, body), (-.56f, 9f, body) })
        {
            var (scene, pc, rifle) = Equip(weapon, aiming);
            var (targetPc, target) = scene.Player(true, new FVector(range, 0, 100));
            // Center the selected pill on the muzzle ray and remove spread so this measures the impact itself.
            rifle.MinDispersion = rifle.MaxDispersion = 0;
            var (start, end) = rifle.AimRay(pc.MyDesiredRotation);
            FVector point = start + (end - start).SafeNormal() * range;
            target.SetLocation(point - new FVector(0, lateral, target.CollisionHeight * height));
            pc.bFire = 1;
            pc.eventFire(1f);
            Assert.Same(rifle, target.LastBulletWeapon);
            Assert.Equal(expected, targetPc.LastDamages[0].Damage);
            Assert.Equal(100 - expected, target.Health);
            Assert.Equal(expected == head, targetPc.LastDamages[0].PillTag == 1);
        }
    }

    [Theory]
    [InlineData("Svm.BoltRifle", 1.05f, false)]
    [InlineData("Svm.BoltRifle", 1.05f, true)]
    [InlineData("Svm.DesignatedMarksman", .27f, false)]
    [InlineData("Svm.DesignatedMarksman", .27f, true)]
    public void PrecisionFireRequiresAReleaseAndRespectsTheMinimumInterval(string weapon, float interval, bool aiming)
    {
        var (scene, pc, rifle) = Equip(weapon, aiming);
        Assert.Equal(interval, rifle.ShotInterval, 4);
        rifle.SwitchROF();
        rifle.ServerSetROF(SRateOfFireMode.ROF_Auto);
        Assert.Equal(SRateOfFireMode.ROF_Single, rifle.ROFMode);
        int full = rifle.ClipAmmo;
        void Press()
        {
            pc.bFire = 1;
            pc.eventFire(1f);
        }
        void Release()
        {
            pc.bFire = 0;
            scene.World.Step();
        }

        Press();
        Assert.Equal(full - 1, rifle.ClipAmmo);
        Assert.Equal(rifle.AFirstShooSt, rifle.ViewAnim.Sequence(SWeaponDefense.ACTIONCHANNEL));
        if (rifle is BoltRifle)
        {
            Assert.Equal(1f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
            Assert.Equal(interval, rifle.ViewAnim.Remaining(SWeaponDefense.ACTIONCHANNEL), 4);
        }
        int intervalTicks = (int)MathF.Ceiling(interval * Sim.Hz);
        for (int tick = 0; tick < intervalTicks * 2; tick++)
        {
            scene.World.Step();
            Press();
        }
        Assert.Equal(full - 1, rifle.ClipAmmo);
        Release();
        Press();
        Assert.Equal(full - 2, rifle.ClipAmmo);
        Release();
        scene.World.Step(intervalTicks - 3);
        Press();
        Assert.Equal(full - 2, rifle.ClipAmmo);
        Release();
        scene.World.Step(2);
        Press();
        Assert.Equal(full - 3, rifle.ClipAmmo);
    }

    [Theory]
    [InlineData("Svm.CompactSMG", 2.05f)]
    [InlineData("Svm.BoltRifle", 3.1f)]
    [InlineData("Svm.DesignatedMarksman", 2.7f)]
    public void MagazineRefillsWhenTheFittedReloadClipFinishes(string weapon, float seconds)
    {
        var (scene, pc, rifle) = Equip(weapon, false);
        int full = rifle.ClipAmmo;
        pc.bFire = 1;
        pc.eventFire(1f);
        pc.bFire = 0;
        scene.Run(rifle.ShotInterval + Sim.DeltaTime);
        Assert.Equal(full - 1, rifle.ClipAmmo);

        Assert.True(rifle.Reload());
        Assert.Equal(seconds, rifle.ViewAnim.Remaining(SWeaponDefense.ACTIONCHANNEL), 4);
        scene.Run(seconds - 2 * Sim.DeltaTime);
        Assert.Equal(full - 1, rifle.ClipAmmo);
        scene.World.Step(3);
        Assert.Equal(full, rifle.ClipAmmo);
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
    }

    [Theory]
    [InlineData("Svm.BoltRifle", 1.05f)]
    [InlineData("Svm.DesignatedMarksman", .27f)]
    public void RemoteShooterCannotExceedTheSameCycleTime(string weapon, float interval)
    {
        var (scene, pc, rifle) = Equip(weapon, false);
        pc.Player = new SimNetConnection();
        int magazine = rifle.ClipAmmo;
        void Request(int sequence) => pc.ReceiveCombatCall(new CombatCall(new Name("ServerFireWeapon"))
            { Target = rifle, Int2 = sequence, Float1 = scene.Time }, FromOwner: true);
        Request(1);
        Assert.Equal(magazine - 1, rifle.ClipAmmo);
        scene.Run(interval / 2);
        Request(2);
        Assert.Equal(magazine - 1, rifle.ClipAmmo);
        scene.Run(interval / 2 + Sim.DeltaTime);
        Request(3);
        Assert.Equal(magazine - 2, rifle.ClipAmmo);
    }
}
