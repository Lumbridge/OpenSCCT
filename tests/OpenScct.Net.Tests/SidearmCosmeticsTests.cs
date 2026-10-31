using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Net.Cosmetics;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class SidearmCosmeticsTests
{
    [Fact]
    public void FourPistolFinishesRoundTripWithoutOverwritingExistingSlots()
    {
        var ids = new[] { "merc.ar.golden", "merc.suppressed.cryo", "merc.automatic.woodland", "merc.magnum44.golden", "merc.desert_eagle.blue_tiger" };
        var look = CosmeticLook.FromIds(CosmeticSide.Merc, ids);
        Assert.NotEqual(0, look.Extra);
        Assert.Equal(look, CosmeticLook.ParseHex(look.ToHex()));
        Assert.Equal(look, look.Validate(CosmeticSide.Merc, CosmeticUnlocks.Of(ids)));
        Assert.Equal("merc.ar.golden", look[CosmeticSlot.GunAr]!.Id);
        Assert.Equal("merc.magnum44.golden", look[CosmeticSlot.Magnum44]!.Id);
        var old = new CosmeticLook(0x010203040506);
        Assert.Equal(old, CosmeticLook.ParseHex(old.ToHex()));
        Assert.Equal(0, CosmeticLook.ParseHex(old.ToHex()).Extra);
        Assert.Equal(CosmeticLook.Default, look.Validate(CosmeticSide.Merc, CosmeticUnlocks.None));
    }

    [Fact]
    public void EachPistolHasEightRewardsAndDoesNotChangePrimaryGoldRequirements()
    {
        foreach (var pistol in Sidearms.All)
        {
            Assert.Equal(8, CosmeticCatalog.For(CosmeticSide.Merc, (CosmeticSlot)(6+(int)pistol.Kind)).Count());
            Assert.Equal(6, Achievements.All.Count(a => a.Id.StartsWith("sidearm_"+pistol.Id+"_", System.StringComparison.Ordinal)));
        }
        Assert.Equal(3, Achievements.All.Single(a => a.Id == "gilded").Rewards.Count);
        Assert.True(CosmeticCatalog.All.Max(i => i.Bit) <= CosmeticCatalog.MaxBit);
    }

    [Theory]
    [InlineData(SidearmKind.Automatic, 14, false)]
    [InlineData(SidearmKind.Automatic, 15, true)]
    [InlineData(SidearmKind.Magnum44, 29, false)]
    [InlineData(SidearmKind.DesertEagle, 45, true)]
    public void GatesUseCurrentLevelRatherThanPrestigeHistory(SidearmKind kind, int level, bool expected)
    {
        Assert.Equal(expected, Sidearms.Available(kind, level, false));
        Assert.True(Sidearms.Available(kind, 1, true));
    }
}
