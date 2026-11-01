using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class PrimaryAmmoAuthorityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void AuthoritativeShotgunReloadFinishesWithTheOwnersAuthoredClip(int shells)
    {
        static (CombatScene Scene, SWeaponDefense_Shotgun Gun) Rig(bool owner, int shells)
        {
            var scene = new CombatScene(owner ? ENetMode.NM_Client : ENetMode.NM_ListenServer);
            scene.Level.SetService<ICombatNet>(new RecordingCombatNet());
            var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
            var gun = scene.Give(new SWeaponDefense_Shotgun(), pawn);
            gun.GiveTo(pawn);
            gun.ClipAmmo -= shells;
            if (owner) gun.Role = ENetRole.ROLE_AutonomousProxy;
            else pc.Player = new SimNetConnection();
            return (scene, gun);
        }
        var owner = Rig(true, shells);
        var server = Rig(false, shells);
        Assert.True(owner.Gun.eventReload());
        server.Gun.RequestCarriedReload();
        int ownerEnd = 0, serverEnd = 0;
        for (int tick = 1; tick <= 360; tick++)
        {
            owner.Scene.World.Step(); server.Scene.World.Step();
            if (ownerEnd == 0 && owner.Gun.ClipAmmo == owner.Gun.ClipMaxAmmo) ownerEnd = tick;
            if (serverEnd == 0 && server.Gun.ClipAmmo == server.Gun.ClipMaxAmmo) serverEnd = tick;
        }
        Assert.True(ownerEnd > 0 && serverEnd > 0);
        Assert.InRange(System.Math.Abs(ownerEnd - serverEnd), 0, 1);
    }

    [Fact]
    public void DeathCancelsAnAuthoritativeReload()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
        var gun = scene.Give(new SWeaponDefense(), pawn);
        gun.GiveTo(pawn);
        pc.Player = new SimNetConnection();
        gun.ClipAmmo = 1;
        gun.RequestCarriedReload();
        scene.Run(.1f);
        pawn.Health = 0;
        scene.Run(6);
        Assert.Equal(1, gun.ClipAmmo);
    }

    [Fact]
    public void DelayedLegalShotsAreNotMistakenForAnInstantFireBurst()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
        var weapon = scene.Give(new SWeaponDefense(), pawn);
        weapon.GiveTo(pawn);
        pc.Player = new SimNetConnection();
        int clip = weapon.ClipAmmo;
        for (int shot = 1; shot <= 4; shot++)
            pc.ReceiveCombatCall(new CombatCall(new Name("ServerFireWeapon"))
                { Target = weapon, Int2 = shot, Float1 = 10 + (shot - 1) * weapon.RateOfFire }, true);
        Assert.Equal(clip - 4, weapon.ClipAmmo);
    }

    [Fact]
    public void DuplicateFireTimeAndImplausiblyFastClockDoNotSpendAmmunition()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
        var weapon = scene.Give(new SWeaponDefense(), pawn);
        weapon.GiveTo(pawn);
        pc.Player = new SimNetConnection();
        int clip = weapon.ClipAmmo;
        void Request(int shot, float stamp) => pc.ReceiveCombatCall(new CombatCall(new Name("ServerFireWeapon"))
            { Target = weapon, Int2 = shot, Float1 = stamp }, true);
        Request(1, 10);
        Request(2, 10);
        Request(3, 1000);
        Assert.Equal(clip - 1, weapon.ClipAmmo);
        scene.Run(1);
        Request(4, 11);
        Assert.Equal(clip - 2, weapon.ClipAmmo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void StandardPrimaryConsumesOneRoundAndReloadsOnAuthority(int kind)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
        var weapon = scene.Give<SWeaponDefense>(kind switch
        {
            1 => new SWeaponDefense_Uzi(), 2 => new SWeaponDefense_Shotgun(), _ => new SWeaponDefense(),
        }, pawn);
        weapon.GiveTo(pawn);
        pc.Player = new SimNetConnection();
        int ammo = weapon.Ammo, clip = weapon.ClipAmmo;
        pc.ReceiveCombatCall(new CombatCall(new Name("ServerFireWeapon")) { Target = weapon, Int2 = 1 }, true);
        Assert.Equal(clip - 1, weapon.ClipAmmo);
        Assert.Equal(ammo - 1, weapon.Ammo);
        pc.ReceiveCombatCall(new CombatCall(new Name("ServerFireWeapon")) { Target = weapon, Int2 = 1 }, true);
        Assert.Equal(clip - 1, weapon.ClipAmmo);
        scene.Run(1);
        pc.ReceiveCombatCall(new CombatCall(new Name("ReloadCarriedPrimary")) { Target = weapon }, true);
        scene.Run(6);
        Assert.Equal(clip, weapon.ClipAmmo);
        Assert.Equal(ammo - 1, weapon.Ammo);
        Assert.Equal(weapon.ClipAmmo, weapon.CarriedClip);
        Assert.Equal(weapon.Ammo, weapon.CarriedAmmo);
    }
}
