using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class EquipmentUnlockTests
{
    [Theory]
    [InlineData("Svm.Galil", 1)]
    [InlineData("Svm.Famas", 5)]
    [InlineData("Svm.AK47", 20)]
    [InlineData("Svm.M4A1S", 30)]
    [InlineData("Svm.MineHackGadget", 5)]
    [InlineData("Svm.CloneGadget", 10)]
    public void AuthorityRejectsLockedGrantsWithoutConsumingTheGrantThenAcceptsAtTheRequiredLevel(string path, int level)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        bool rifle = MercRifles.Find(path) is not null;
        var (pc, pawn) = scene.Player(!rifle, FVector.Zero);
        pc.GameReplicationInfo = new GameReplicationInfo { RiflesEnabled = true };
        pawn.PlayerReplicationInfo!.Team = rifle ? new STeamDefenseur() : new STeamAttaquant();
        pawn.PlayerReplicationInfo.SvmRank = level - 1;
        Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
        if (level > 1)
        {
            Spawn();
            Assert.Null(rifle ? pawn.MyWeapon : pawn.MyGadget);
            Assert.True(pawn.GearGrantWeapon);
            Assert.Equal(4, pawn.GearGrantGadgets);
        }
        pawn.PlayerReplicationInfo.SvmRank = level;
        Spawn();
        Assert.NotNull(rifle ? pawn.MyWeapon : pawn.MyGadget);
        void Spawn()
        {
            if (rifle) Loadouts.ServerSpawnWeapon(pawn, new ClassRef(path));
            else Loadouts.ServerSpawnInventory(pawn, new ClassRef(path));
        }
    }

    [Fact]
    public void DisabledRiflesAreRejectedEvenForAnUnrestrictedPlayer()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, FVector.Zero);
        pc.GameReplicationInfo = new GameReplicationInfo();
        pawn.PlayerReplicationInfo!.Team = new STeamDefenseur();
        pawn.PlayerReplicationInfo.SvmSidearmsUnrestricted = true;
        Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
        Loadouts.ServerSpawnWeapon(pawn, new ClassRef("Svm.M4A1S"));
        Assert.Null(pawn.MyWeapon);
        Loadouts.ServerSpawnWeapon(pawn, Loadouts.MercWeapon);
        Assert.IsType<SWeaponDefense>(pawn.MyWeapon);
    }

    [Fact]
    public void DeathmatchRespawnChoiceCannotBypassTheHostOrLevelRule()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, pawn) = scene.Player(false, FVector.Zero);
        pc.GameReplicationInfo = new GameReplicationInfo { GameMode = EGameMode.GM_MercDeathMatch, RiflesEnabled = true };
        pawn.PlayerReplicationInfo!.Team = new STeamDefenseur();
        pawn.PlayerReplicationInfo.SvmRank = 1;
        pc.RespawnWeapon = new ClassRef("Svm.M4A1S");
        Assert.Equal(Loadouts.MercWeapon, pc.SpawnWeaponFor(Loadouts.MercWeapon));
        pawn.PlayerReplicationInfo.SvmRank = 30;
        pc.RespawnWeapon = new ClassRef("Svm.M4A1S");
        Assert.Equal("Svm.M4A1S", pc.SpawnWeaponFor(Loadouts.MercWeapon).Path);
        pc.GameReplicationInfo.RiflesEnabled = false;
        Assert.Equal(Loadouts.MercWeapon, pc.SpawnWeaponFor(Loadouts.MercWeapon));
    }
}
