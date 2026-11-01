using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Game.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class SidearmTests
{
    [Fact]
    public void CarriedPistolDoesNotRevealTheRifleAfterScopeRaise()
    {
        var (scene, pc, pawn, primary) = Rig(SidearmKind.Suppressed);
        pc.SwitchBinocularVision();
        Assert.True(primary.bDrawWeapon);
        scene.Run(.25f);
        Assert.True(primary.bDrawWeapon);
        scene.Run(.1f);
        Assert.False(primary.bDrawWeapon);
        scene.Run(.5f);
        Assert.False(primary.bDrawWeapon);
        Assert.False(pawn.SidearmWeapon!.bDrawWeapon);
        pc.SwitchBinocularVision();
        pc.eventPlayerMove(Sim.DeltaTime);
        Assert.True(primary.bDrawWeapon);
    }

    [Fact]
    public void SlowInitialLoadingDoesNotLoseTheFirstWeaponOrAllowASecondOne()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (_, pawn) = scene.Player(false, new FVector(0, 0, 100));
        Loadouts.OpenGrant(pawn, EGameMode.GM_Neutralization);
        pawn.GearGrantUntil = pawn.Level.TimeSeconds - 1;
        Loadouts.ServerSpawnWeapon(pawn, Loadouts.MercWeapon);
        var first = Assert.IsType<SWeaponDefense>(pawn.MyWeapon);
        Assert.Same(first, pawn.PrimaryWeapon);
        Loadouts.ServerSpawnWeapon(pawn, Loadouts.MercWeapon);
        Assert.Same(first, pawn.MyWeapon);
        Assert.False(pawn.GearGrantWeapon);
    }

    [Fact]
    public void ServerRejectsInactiveRapidAndForeignWeaponShots()
    {
        var (scene, pc, pawn, _) = Rig(SidearmKind.Suppressed);
        pc.Player = new SimNetConnection();
        var pistol = pawn.SidearmWeapon!;
        void Request(SPlayerController sender) => sender.ReceiveCombatCall(
            new CombatCall(new Name("ServerFireWeapon")) { Target = pistol, Int1 = 1, Int2 = pistol.ShotCounter + 1 }, true);
        Request(pc);
        Assert.Equal(12, pistol.ClipAmmo);
        pc.SwapWeapon(); scene.Run(.6f);
        Request(pc);
        Assert.Equal(11, pistol.ClipAmmo);
        Request(pc);
        Assert.Equal(11, pistol.ClipAmmo);
        scene.Run(.3f);
        var (other, _) = scene.Player(false, new FVector(500, 0, 100));
        Request(other);
        Assert.Equal(11, pistol.ClipAmmo);
    }

    private static (CombatScene Scene, SPlayerController PC, SBasePawn Pawn, SWeaponDefense Primary) Rig(SidearmKind kind)
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
        var primary = scene.Give(new SWeaponDefense(), pawn);
        primary.GiveTo(pawn);
        pawn.GearStartAmmo = primary.Ammo;
        pawn.GearStartClipAmmo = primary.ClipAmmo;
        SidearmEquipment.Give(pawn, kind);
        pawn.WeaponLoaded = 2;
        pc.GotoState(new Name("s_PlayerWalkingSt2H"));
        scene.Run(.1f);
        return (scene, pc, pawn, primary);
    }

    [Fact]
    public void SwappingPreservesBothMagazinesAndNeedsFireRelease()
    {
        var (scene, pc, pawn, primary) = Rig(SidearmKind.Suppressed);
        Assert.Same(primary, pawn.MyWeapon);
        primary.ClipAmmo = 19;
        pc.bFire = 1;
        pc.SwapWeapon();
        Assert.Same(pawn.SidearmWeapon, pawn.MyWeapon);
        pc.eventFire(1);
        Assert.Equal(12, pawn.SidearmWeapon!.ClipAmmo);
        scene.Run(.6f);
        pc.eventFire(1);
        Assert.Equal(12, pawn.SidearmWeapon.ClipAmmo);
        pc.bFire = 0; scene.Run(.05f);
        pc.bFire = 1; pc.eventFire(1);
        Assert.Equal(11, pawn.SidearmWeapon.ClipAmmo);
        pc.bFire = 0; scene.Run(.3f); pc.SwapWeapon();
        Assert.Same(primary, pawn.MyWeapon);
        Assert.Equal(19, primary.ClipAmmo);
        Assert.Equal(11, pawn.SidearmWeapon.ClipAmmo);
    }

    [Fact]
    public void SemiAutomaticRequiresANewPressAndAutomaticRepeats()
    {
        foreach (var kind in new[] { SidearmKind.Suppressed, SidearmKind.Automatic, SidearmKind.Unsuppressed })
        {
            var (scene, pc, pawn, _) = Rig(kind);
            pc.SwapWeapon(); scene.Run(.6f);
            pc.bFire = 1; pc.eventFire(1); scene.Run(.35f); pc.eventFire(1);
            Assert.Equal(kind == SidearmKind.Automatic ? 16 : 11, pawn.SidearmWeapon!.ClipAmmo);
        }
    }

    [Fact]
    public void G18TogglesSingleAndAutoAndKeepsItsModeAcrossWeaponSwaps()
    {
        var (scene, pc, pawn, primary) = Rig(SidearmKind.Automatic);
        pc.SwapWeapon(); scene.Run(.6f);
        var pistol = pawn.SidearmWeapon!;
        Assert.Equal(SRateOfFireMode.ROF_Auto, pistol.ROFMode);
        pc.eventSwitchROF();
        Assert.Equal(SRateOfFireMode.ROF_Single, pistol.ROFMode);
        pc.bFire = 1; pc.eventFire(1); scene.Run(.35f); pc.eventFire(1);
        Assert.Equal(19, pistol.ClipAmmo);
        pc.bFire = 0; scene.Run(.1f);
        pc.SwapWeapon(); scene.Run(.6f); pc.SwapWeapon(); scene.Run(.6f);
        Assert.Equal(SRateOfFireMode.ROF_Single, pistol.ROFMode);
        Assert.Equal(SRateOfFireMode.ROF_Auto, primary.ROFMode);
        pc.eventSwitchROF();
        Assert.Equal(SRateOfFireMode.ROF_Auto, pistol.ROFMode);
        pc.bFire = 1; pc.eventFire(1); scene.Run(.35f);
        Assert.Equal(15, pistol.ClipAmmo);
    }

    [Theory]
    [InlineData(SidearmKind.Automatic)]
    [InlineData(SidearmKind.Unsuppressed)]
    public void SidearmModeRequestsRejectBurstHolsteredAndReloadingWeapons(SidearmKind kind)
    {
        var (scene, pc, pawn, _) = Rig(kind);
        var pistol = pawn.SidearmWeapon!;
        Assert.False(pistol.SetSidearmFireMode(SRateOfFireMode.ROF_Single));
        pc.SwapWeapon(); scene.Run(.6f);
        Assert.False(pistol.SetSidearmFireMode(SRateOfFireMode.ROF_Burst));
        if (kind == SidearmKind.Unsuppressed)
        {
            pc.eventSwitchROF();
            Assert.Equal(SRateOfFireMode.ROF_Single, pistol.ROFMode);
        }
        pistol.ClipAmmo--;
        Assert.True(pistol.Reload());
        Assert.False(pistol.SetSidearmFireMode(SRateOfFireMode.ROF_Single));
    }

    [Fact]
    public void SwitchingCancelsReloadWithoutRefillingAndUsedSidearmCounts()
    {
        var (scene, pc, pawn, _) = Rig(SidearmKind.Magnum44);
        pc.SwapWeapon(); scene.Run(.6f);
        var pistol = pawn.SidearmWeapon!;
        pistol.ClipAmmo = 2; pistol.Ammo = 26;
        Assert.True(pistol.Reload());
        scene.Run(.4f); pc.SwapWeapon(); scene.Run(3);
        Assert.Equal(2, pistol.ClipAmmo);
        Assert.Equal(26, pistol.Ammo);
        Assert.True(Loadouts.HasUsedKit(pawn));
    }

    [Theory]
    [InlineData(SidearmKind.Magnum44, 55)]
    [InlineData(SidearmKind.DesertEagle, 50)]
    public void HeavyDamageUsesTheAgreedBands(SidearmKind kind, int damage)
    {
        var (_, _, pawn, _) = Rig(kind);
        var weapon = pawn.SidearmWeapon!;
        Assert.Equal(damage, WeaponRules.DistanceDamage(weapon, 1000));
        Assert.True(damage * 2 >= 100);
        Assert.True(damage * 3 >= 100);
        Assert.Equal((int)(damage * .5f), WeaponRules.DistanceDamage(weapon, 3000));
    }

    [Fact]
    public void GrantsKeepSidearmsSeparateAndEnforceLevelAndOnePistolLimit()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, pawn) = scene.Player(false, new FVector(0, 0, 100));
        var team = scene.Level.SpawnActor(new STeamDefenseur(), default, default)!;
        team.iNBGadgets = 2; team.bDisallowedInv = 255;
        var pri = scene.Level.SpawnActor(new PlayerReplicationInfo(), default, default)!;
        pc.GameReplicationInfo = new GameReplicationInfo { SidearmsEnabled = true };
        pri.Team = team; pri.SvmRank = 15;
        pawn.PlayerReplicationInfo = pc.PlayerReplicationInfo = pri;
        Loadouts.OpenGrant(pawn, EGameMode.GM_Neutralization);
        Loadouts.ServerSpawnInventory(pawn, new ClassRef(Sidearms.Get(SidearmKind.DesertEagle).CarryId));
        Assert.Null(pawn.SidearmWeapon);
        Loadouts.ServerSpawnInventory(pawn, new ClassRef(Sidearms.Get(SidearmKind.Automatic).CarryId));
        Assert.Equal(2, pawn.GearGrantGadgets);
        Assert.Equal((int)SidearmKind.Automatic, pawn.SidearmWeapon!.Kind);
        Assert.False(PawnInventory.IsSelectable(pawn.MyGadget!));
        Loadouts.ServerSpawnInventory(pawn, new ClassRef(Sidearms.Get(SidearmKind.Unsuppressed).CarryId));
        Assert.Equal(2, pawn.GearGrantGadgets);
        Assert.Null(pawn.MyGadget!.MyGadget);
        Loadouts.ServerSpawnInventory(pawn, Loadouts.MercFixedGadgets[0]);
        Loadouts.ServerSpawnInventory(pawn, Loadouts.MercFixedGadgets[1]);
        Assert.Equal(0, pawn.GearGrantGadgets);
        PawnInventory.DeleteAllInventory(pawn);
        Assert.Null(pawn.SidearmWeapon);
        Assert.Null(pawn.MyWeapon);
    }

    [Fact]
    public void ASpyCannotReceiveASidearmAndBlockingStatesRefuseSwaps()
    {
        var (scene, pc, pawn, primary) = Rig(SidearmKind.Suppressed);
        pawn.bStickyShocking = true; pc.SwapWeapon();
        Assert.Same(primary, pawn.MyWeapon);
        pawn.bStickyShocking = false;
        primary.GotoState(new Name("s_ElbowShocking")); pc.SwapWeapon();
        Assert.Same(primary, pawn.MyWeapon);
        var (_, spy) = scene.Player(true, new FVector(500, 0, 100));
        Assert.False(SidearmEquipment.Available(spy, SidearmKind.Suppressed));
    }
}
