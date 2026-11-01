using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class AmmoStockRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrateUpgradesAnInProgressReloadToAFullResupply(bool shotgun)
    {
        var scene = new CombatScene();
        CombatEquipment.Install(scene.Level);
        var (pc, pawn) = scene.Player(false, default);
        var gun = scene.Give<SWeaponDefense>(shotgun ? new SWeaponDefense_Shotgun() : new SWeaponDefense(), pawn);
        gun.GiveTo(pawn);
        pawn.WeaponLoaded = 2;
        scene.Run(.1f);
        gun.Ammo = gun.ClipMaxAmmo + 1;
        gun.ClipAmmo = 0;
        Assert.True(gun.eventReload());
        var crate = new SAmmoStock();
        var entry = new SAmmoStockInteraction();
        entry.SetOwner(crate);
        entry.eventInitInteract(pc, 0);
        Assert.Equal(new Name("s_FullReloading"), gun.GetStateName());
        scene.Run(6f);
        Assert.Equal(gun.MaxAmmo, gun.Ammo);
        Assert.Equal(gun.ClipMaxAmmo, gun.ClipAmmo);
    }

    [Fact]
    public void RemoteMercWithoutASidearmReceivesAuthoritativeAmmo()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, default);
        var gun = scene.Give(new SWeaponDefense(), pawn);
        gun.GiveTo(pawn);
        pc.Player = new SimNetConnection();
        gun.Ammo = 0;
        gun.ClipAmmo = 0;
        new SAmmoStock().Use(pc);
        Assert.Equal(gun.MaxAmmo, gun.Ammo);
        Assert.Equal(gun.ClipMaxAmmo, gun.ClipAmmo);
        Assert.Equal(gun.MaxAmmo, gun.CarriedAmmo);
    }
}
