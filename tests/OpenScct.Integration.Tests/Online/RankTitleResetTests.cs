using System.Linq;
using OpenScct.Master;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class RankTitleResetTests
{
    [Fact]
    public void RankResetRelocksAndUnequipsAllLevelFinishesAndDefaultsOwnedTitle()
    {
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("cosmetic-reset").Id;
        foreach (var pistol in OpenScct.Core.Engine.Sidearms.All)
        foreach (string finish in new[] { "night_ops", "cryo" })
        {
            store.SetRank(id, 1, 55);
            string cosmetic = $"merc.{pistol.Id}.{finish}";
            Assert.Contains(cosmetic, store.Unlocked(id));
            var before = store.Appearance(id);
            Assert.True(store.UpdateAppearance(id, new AppearanceUpdateRequest
            {
                ExpectedRevision = before.Revision, Merc = [cosmetic], Title = "prestige_1",
            }).Success);
            long equippedRevision = store.Appearance(id).Revision;
            store.SetRank(id, 1, 1);
            Assert.DoesNotContain(cosmetic, store.Unlocked(id));
            var after = store.Appearance(id);
            Assert.Empty(after.Merc);
            Assert.Null(after.Title);
            Assert.True(after.Revision > equippedRevision);
            Assert.Contains(store.Titles(id), t => t.Id == "prestige_1");
            // A stale appearance editor cannot restore the old equipment.
            Assert.True(store.UpdateAppearance(id, new AppearanceUpdateRequest
            {
                ExpectedRevision = equippedRevision, Merc = [cosmetic], Title = "prestige_1",
            }).Conflict);
        }
    }

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
