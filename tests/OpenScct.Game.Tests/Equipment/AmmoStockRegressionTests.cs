using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class AmmoStockRegressionTests
{
    private sealed class RifleLoadout(string primary, int mask) : ILoadoutSource
    {
        public ClassRef[] Gadgets(bool spy) => [];
        public ClassRef Weapon(bool spy) => spy ? Loadouts.SpyWeapon : new(primary);
        public ClassRef Sidearm() => new(Sidearms.Get(SidearmKind.Suppressed).CarryId);
        public int Attachments(string weaponClass) => weaponClass == primary ? mask : 0;
    }

    [Theory]
    [InlineData("SBase.SWeaponDefense", 0, 20, 100)]
    [InlineData("SBase.SWeaponDefense", WeaponAttachments.ExtendedMagazine, 30, 100)]
    [InlineData("SBase.SWeaponDefense_Uzi", 0, 20, 300)]
    [InlineData("SBase.SWeaponDefense_Uzi", WeaponAttachments.ExtendedMagazine, 30, 300)]
    [InlineData("Svm.M4A1S", 0, 20, 80)]
    [InlineData("Svm.M4A1S", WeaponAttachments.ExtendedMagazine, 30, 80)]
    [InlineData("Svm.AK47", 0, 20, 90)]
    [InlineData("Svm.AK47", WeaponAttachments.ExtendedMagazine, 30, 90)]
    [InlineData("Svm.Galil", 0, 20, 105)]
    [InlineData("Svm.Galil", WeaponAttachments.ExtendedMagazine, 30, 105)]
    [InlineData("Svm.BoltRifle", WeaponAttachments.RedDot, 5, 40)]
    [InlineData("Svm.DesignatedMarksman", WeaponAttachments.ExtendedMagazine, 30, 60)]
    [InlineData("Svm.Famas", 0, 20, 100)]
    [InlineData("Svm.Famas", WeaponAttachments.ExtendedMagazine, 30, 100)]
    public void RifleKitRetainsCapacityAcrossReloadCrateWeaponSwitchAndRespawn(string weaponClass, int mask, int capacity, int reserve)
    {
        SObjective? objective = null;
        var world = new GameWorld().Load(MatchLifecycleTests.SteeEStory, map =>
        {
            MatchLifecycleTests.TwoSides(map);
            objective = map.Level.Spawn<SObjective>();
        });
        objective!.ObjectiveTaken = false;
        CombatEquipment.Install(world.Level);
        world.Level.SetService<ILoadoutSource>(new RifleLoadout(weaponClass, mask));
        var pc = world.Join("?Name=Merc?Team=1", local: true);
        world.Join("?Name=Spy?Team=0");
        world.Gri.RiflesEnabled = world.Gri.SidearmsEnabled = true;
        // Normal merc kits migrate legacy choices. Authored maps can still issue a single original primary.
        if (MercPrimarySelection.IsLegacy(weaponClass))
        {
            var team = Assert.IsType<STeamDefenseur>(world.Mercs);
            team.ClassWeapon.Clear();
            team.ClassWeapon.Add(new ClassRef(weaponClass));
        }
        pc.PlayerReplicationInfo!.SvmRank = 30;
        for (int i = 0; i < 40; i++) WeaponCareer.RecordKill(pc, weaponClass);
        world.StartNetworkMatch(pc);

        SWeaponDefense IssuedRifle()
        {
            var merc = Assert.IsAssignableFrom<SPAWNDEFENSE>(pc.Pawn);
            var gun = Assert.IsAssignableFrom<SWeaponDefense>(merc.PrimaryWeapon);
            Assert.Equal(weaponClass, WeaponAttachments.Key(gun));
            Assert.Equal(mask, gun.AttachmentMask);
            Assert.Equal(capacity, gun.ClipMaxAmmo);
            Assert.Equal(capacity, gun.ClipAmmo);
            Assert.Equal(capacity + reserve, gun.Ammo);
            return gun;
        }

        var rifle = IssuedRifle();
        var pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(pc.Pawn);
        pawn.WeaponLoaded = 2;
        world.Run(.1f);
        rifle.FireWeapon();
        Assert.Equal(capacity - 1, rifle.ClipAmmo);
        Assert.True(rifle.eventReload());
        world.Run(4f);
        Assert.Equal(capacity, rifle.ClipAmmo);
        Assert.Equal(reserve - 1, rifle.Ammo - rifle.ClipAmmo);

        Assert.True(SidearmEquipment.Select(pawn, 1, force: true));
        Assert.Equal(12, pawn.SidearmWeapon!.ClipMaxAmmo);
        Assert.True(SidearmEquipment.Select(pawn, 0, force: true));
        Assert.Same(rifle, pawn.MyWeapon);
        Assert.Equal(capacity, rifle.ClipAmmo);
        world.Run(1f);
        rifle.FireWeapon();
        Assert.Equal(capacity - 1, rifle.ClipAmmo);
        new SAmmoStock().Use(pc);
        world.Run(4f);
        Assert.Equal(capacity + reserve, rifle.Ammo);
        Assert.Equal(capacity, rifle.ClipAmmo);

        rifle.FireWeapon();
        Assert.Equal(capacity - 1, rifle.ClipAmmo);
        pc.PawnTakeDamage(1000, null, pawn.Location, default, SDamageType.SDTBase, 2, 0);
        Assert.InRange(pawn.Health, int.MinValue, 0);
        world.Game.eventRestartPlayer(pc);
        Assert.NotSame(pawn, pc.Pawn);
        Assert.NotSame(rifle, IssuedRifle());
    }

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
