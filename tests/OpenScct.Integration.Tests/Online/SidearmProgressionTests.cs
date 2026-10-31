using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Master;
using OpenScct.Net.Cosmetics;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class SidearmProgressionTests
{
    [Fact]
    public void LevelFinishesRemainOwnedThroughPrestigeWhileWeaponAccessResets()
    {
        using var store=AccountStore.Open(":memory:");
        long account=store.GetOrCreate("sidearm-test").Id;
        store.SetRank(account,0,55);
        var unlocked=store.Unlocked(account);
        foreach(var pistol in Sidearms.All)
        {
            Assert.Contains("merc."+pistol.Id+".night_ops",unlocked);
            Assert.Contains("merc."+pistol.Id+".cryo",unlocked);
            Assert.DoesNotContain("merc."+pistol.Id+".golden",unlocked);
        }
        Assert.Equal(PrestigeResult.Done,store.Prestige(account));
        Assert.Equal(1,store.ProgressionOf(account).Rank.Level);
        Assert.False(Sidearms.Available(SidearmKind.DesertEagle,store.ProgressionOf(account).Rank.Level,false));
        Assert.Equal(unlocked.Order(),store.Unlocked(account).Order());
    }
}
