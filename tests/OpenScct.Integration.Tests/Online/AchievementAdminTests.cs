using System;
using System.IO;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class AchievementAdminTests
{
    [Fact]
    public void RevocationRemovesTitleAndEquippedRewardsAndSurvivesReopen()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            long id;
            using (var store = AccountStore.Open(path))
            {
                id = store.GetOrCreate("title-revocation").Id;
                store.ApplyResults(new ResultsReport { Rows = [new() { AccountId = id, Team = 1, Kills = 750 }] }, "Test");
                Assert.Contains("kills_750", store.Earned(id).Keys);
                var appearance = store.Appearance(id);
                Assert.True(store.UpdateAppearance(id, new AppearanceUpdateRequest
                {
                    ExpectedRevision = appearance.Revision, Title = "ach_reaper", Merc = ["merc.body.ash"],
                }).Success);
                var before = store.ProgressionOf(id);
                using (AccountStore.ActingAs("admin:test"))
                    Assert.True(store.RevokeAchievement(id, "kills_750", "Correct invalid award").Ok);
                Assert.DoesNotContain("kills_750", store.Earned(id).Keys);
                Assert.DoesNotContain("ach_reaper", store.ProgressInfo(id).Titles);
                Assert.Null(store.Appearance(id).Title);
                Assert.DoesNotContain("merc.body.ash", store.Appearance(id).Merc);
                Assert.Equal(before.Xp, store.ProgressionOf(id).Xp);
                Assert.Equal(before.TotalXp, store.ProgressionOf(id).TotalXp);
                Assert.Equal(before.Rank, store.ProgressionOf(id).Rank);
                Assert.Equal(750, store.Stats(id).Kills);
                Assert.Contains(store.AuditLog(20), row => row.Action == "achievement.revoke");
            }
            using var reopened = AccountStore.Open(path);
            Assert.DoesNotContain("ach_reaper", reopened.ProgressInfo(id).Titles);
            Assert.Null(reopened.Appearance(id).Title);
            // Revocation deliberately leaves earned-progress counters intact.
            reopened.ApplyResults(new ResultsReport { Rows = [new() { AccountId = id, Team = 1 }] }, "Test");
            Assert.Contains("kills_750", reopened.Earned(id).Keys);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidRevocationsAreRefusedWithoutChanges()
    {
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("invalid-title-revocation").Id;
        Assert.False(store.RevokeAchievement(id, "kills_750", "").Ok);
        Assert.False(store.RevokeAchievement(id, "unknown", "reason").Ok);
        Assert.False(store.RevokeAchievement(id, "kills_750", "reason").Ok);
        Assert.False(store.RevokeAchievement(long.MaxValue, "kills_750", "reason").Ok);
        Assert.Empty(store.Earned(id));
    }
}
