using System;
using System.Linq;
using System.Text.Json;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class RecentResultsTests
{
    [Fact]
    public void RecentResultsUsePlayerTeamExcludeUncountedAndLimitToNineNewest()
    {
        using var store = AccountStore.Open(":memory:");
        var account = store.GetOrCreate("recent-results");
        for (int i = 0; i < 12; i++)
            store.RecordMatch(new ResultsReport
            {
                WinningTeam = i % 3 - 1,
                Rows = [new() { AccountId = account.Id, Team = 1 }],
            }, "Test", counted: true);
        store.RecordMatch(new ResultsReport { WinningTeam = 1, Rows = [new() { AccountId = account.Id, Team = 1 }] }, "Test", counted: false);
        store.RecordMatch(new ResultsReport { WinningTeam = 1, Rows = [new() { AccountId = account.Id, Team = 1, Bot = true }] }, "Test", counted: true);
        Assert.Equal(new[] { "W", "L", "D", "W", "L", "D", "W", "L", "D" }, store.RecentResults(account.Id));
        Assert.Empty(store.RecentResults(store.GetOrCreate("no-matches").Id));
        Assert.Null(JsonSerializer.Deserialize<AccountInfo>("{}", MasterApi.Json)!.RecentResults);
    }
}
