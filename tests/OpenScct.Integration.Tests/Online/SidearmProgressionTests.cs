using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Master;
using OpenScct.Net.Cosmetics;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class SidearmProgressionTests
{
    [Fact]
    public void SuppressedFinishesFollowItsNewUnlockLevel()
    {
        using var store=AccountStore.Open(":memory:");
        long account=store.GetOrCreate("sidearm-unlocks").Id;
        store.SetRank(account,0,11);
        Assert.Contains("merc.unsuppressed.cryo",store.Unlocked(account));
        Assert.DoesNotContain("merc.suppressed.night_ops",store.Unlocked(account));
        store.SetRank(account,0,30);
        Assert.Contains("merc.suppressed.night_ops",store.Unlocked(account));
        Assert.DoesNotContain("merc.suppressed.cryo",store.Unlocked(account));
        store.SetRank(account,0,35);
        Assert.Contains("merc.suppressed.cryo",store.Unlocked(account));
    }

    [Fact]
    public void PrestigeRelocksLevelFinishesAndPistolsUntilTheirLevelsAreReachedAgain()
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
        Assert.Equal(55,store.ProgressionOf(account).BestLevel);
        foreach(var pistol in Sidearms.All)
        {
            Assert.Equal(pistol.Level == 1, Sidearms.Available(pistol.Kind,store.ProgressionOf(account).Rank.Level,false));
            Assert.DoesNotContain("merc."+pistol.Id+".night_ops",store.Unlocked(account));
            Assert.DoesNotContain("merc."+pistol.Id+".cryo",store.Unlocked(account));
            store.SetRank(account,1,pistol.Level + 5);
            Assert.Contains("merc."+pistol.Id+".night_ops",store.Unlocked(account));
            Assert.DoesNotContain("merc."+pistol.Id+".cryo",store.Unlocked(account));
            store.SetRank(account,1,pistol.Level + 10);
            Assert.Contains("merc."+pistol.Id+".cryo",store.Unlocked(account));
            store.SetRank(account,1,1);
        }
        store.SetRank(account,1,55);
        Assert.Equal(unlocked.Order(),store.Unlocked(account).Order());
        Assert.Equal(PrestigeResult.Done,store.Prestige(account));
        Assert.DoesNotContain("merc.desert_eagle.cryo",store.Unlocked(account));
    }
}
