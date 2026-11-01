using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class MercWeaponStateCleanupTests
{
    private static (CombatScene Scene, SPlayerController PC, SPAWNDEFENSE Pawn, SWeaponDefense Rifle) Merc(int kind = 0)
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, new FVector(0f, 0f, 100f));
        pc.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        SWeaponDefense rifle = kind switch
        {
            1 => new SWeaponDefense_Uzi(),
            2 => new SWeaponDefense_Shotgun(),
            _ => new SWeaponDefense(),
        };
        rifle = scene.Give(rifle, pawn);
        rifle.GiveTo(pawn);
        pawn.WeaponLoaded = 2;
        scene.Run(0.1f);
        rifle.ClipAmmo = 1;
        rifle.Ammo = rifle.MaxAmmo / 2;
        return (scene, pc, (SPAWNDEFENSE)pawn, rifle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FinishingAnAmmoCrateReloadRestoresTheReticle(int kind)
    {
        var (scene, pc, _, rifle) = Merc(kind);
        Assert.True(rifle.eventFullReload());
        Assert.True(pc.bNoReticule);

        scene.Run(8f);

        Assert.Equal(rifle.MaxAmmo, rifle.Ammo);
        Assert.Equal(rifle.ClipMaxAmmo, rifle.ClipAmmo);
        Assert.Equal(new Name("s_Selected"), rifle.GetStateName());
        Assert.False(pc.bNoReticule);
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HolsteringDuringAnAmmoCrateReloadCancelsItsOverlayAndAmmoGrant(int kind)
    {
        var (scene, pc, pawn, rifle) = Merc(kind);
        int ammo = rifle.Ammo;
        Assert.True(rifle.eventFullReload());
        scene.Run(0.1f);
        PawnWeapons.MercNotifyDrawUndrawWeapon(pawn);

        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));
        Assert.False(pc.bNoReticule);
        scene.Run(8f);
        Assert.Equal(ammo, rifle.Ammo);
        Assert.Equal(1, rifle.ClipAmmo);
        PawnWeapons.MercNotifyDrawUndrawWeapon(pawn);
        scene.Run(1f);
        Assert.Equal(new Name("s_Selected"), rifle.GetStateName());
        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmmoCrateReloadRestoresTheLaserOnCompletionOrInterruption(bool interrupt)
    {
        var (scene, pc, pawn, rifle) = Merc();
        var laser = GadgetKit.Gadget(scene, pawn, new SGadTorcheLaser(), Select: false);
        pawn.TorcheLaser = laser;
        laser.TorcheON();
        Assert.True(rifle.eventFullReload());
        Assert.False(laser.bInUse);
        Assert.True(pc.bLaserEtaitAllume);

        if (interrupt) PawnWeapons.MercNotifyDrawUndrawWeapon(pawn);
        scene.Run(3f);

        Assert.True(laser.bInUse);
        Assert.False(pc.bLaserEtaitAllume);
    }

    [Theory]
    [InlineData("s_Reloading")]
    [InlineData("s_FireGrenade")]
    public void HolsteringStopsTheInterruptedReloadOrGrenadeChannel(string state)
    {
        var (_, pc, pawn, rifle) = Merc();
        rifle.GotoState(new Name(state));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));

        PawnWeapons.MercNotifyDrawUndrawWeapon(pawn);

        Assert.Equal(0f, rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
        Assert.False(rifle.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));
        Assert.False(pc.bNoReticule);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadCleanupPreservesTheShotStartedByAHeldTrigger(bool fullReload)
    {
        var (scene, pc, _, rifle) = Merc();
        Assert.True(fullReload ? rifle.eventFullReload() : rifle.eventReload());
        pc.bFire = 1;
        for (int i = 0; i < 300 && !rifle.IsInState(new Name("s_Firing")); i++)
            scene.Run(Sim.DeltaTime);

        Assert.True(rifle.IsInState(new Name("s_Firing")));
        Assert.Equal(rifle.ClipMaxAmmo - 1, rifle.ClipAmmo);
        Assert.Equal(rifle.AFirstShooSt, rifle.ViewAnim.Sequence(SWeaponDefense.ACTIONCHANNEL));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));
        Assert.True(rifle.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL) > 0f);
    }
}
