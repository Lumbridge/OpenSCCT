using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class LoadoutStatisticsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Statistics_capture_the_granted_kit_and_ignore_duplicate_or_rejected_requests(bool spy)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, pawn) = scene.Player(spy, new FVector(0, 0, 100));
        var stats = new USPlayerStats();
        pc.PlayerReplicationInfo = new PlayerReplicationInfo { Stats = stats };
        Loadouts.UpdateInventory(pawn, EGameMode.GM_Neutralization);
        var expected = spy ? Loadouts.SpyFixedGadgets : Loadouts.MercFixedGadgets;
        Assert.Equal((spy ? Loadouts.SpyWeapon : Loadouts.MercWeapon).Path, stats.Weapon);
        Assert.Equal(4, stats.Gadgets.Length);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i].Path, stats.Gadgets[i]);
        string[] firstKit = (string[])stats.Gadgets.Clone();
        Loadouts.ServerSpawnInventory(pawn, expected[0]);
        Loadouts.ServerSpawnWeapon(pawn, spy ? Loadouts.MercWeapon : Loadouts.SpyWeapon);
        Assert.Equal(firstKit, stats.Gadgets);
        Assert.Equal((spy ? Loadouts.SpyWeapon : Loadouts.MercWeapon).Path, stats.Weapon);
        Loadouts.OpenGrant(pawn, EGameMode.GM_Neutralization);
        Assert.Empty(stats.Gadgets);
        Assert.Empty(stats.Weapon);
        Assert.Equal(4, firstKit.Length);
    }
}
