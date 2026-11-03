using System.Linq;
using OpenScct.Master;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class RankTitleResetTests
{
    [Fact]
    public void AdminResetRelocksLevelTitlesAndClearsEquippedTitle()
    {
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("title-reset").Id;
        store.SetRank(id, 1, 55);
        Assert.Equal(4, store.Titles(id).Count(t => t.Source == TitleSource.Level));
        Assert.True(store.UpdateAppearance(id, new AppearanceUpdateRequest
        {
            ExpectedRevision = store.Appearance(id).Revision, Title = "level_55",
        }).Success);
        store.SetRank(id, 1, 1);
        Assert.Equal(1, store.ProgressionOf(id).BestLevel);
        Assert.DoesNotContain(store.Titles(id), t => t.Source == TitleSource.Level);
        Assert.Contains(store.Titles(id), t => t.Id == "prestige_1");
        Assert.Null(store.Appearance(id).Title);
        foreach (int level in new[] { 10, 25, 40, 55 })
        {
            store.SetRank(id, 1, level);
            Assert.Contains(store.Titles(id), t => t.Id == "level_" + level);
            Assert.DoesNotContain(store.Titles(id), t => t.Source == TitleSource.Level && t.Value > level);
        }
    }

    [Fact]
    public void NormalPrestigeRetainsEarnedLevelTitles()
    {
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("prestige-titles").Id;
        store.SetRank(id, 0, 55);
        Assert.Equal(PrestigeResult.Done, store.Prestige(id));
        Assert.Equal(1, store.ProgressionOf(id).Rank.Level);
        Assert.Equal(4, store.Titles(id).Count(t => t.Source == TitleSource.Level));
    }
}
