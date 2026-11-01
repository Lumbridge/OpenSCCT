using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using OpenScct.Core.Engine;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class CareerStatsTests
{
    [Fact]
    public void StoredCareerTotalsReachTheAccountResponseWithoutAchievementCaps()
    {
        using var store = AccountStore.Open(":memory:");
        var account = store.GetOrCreate("career-stats-test");
        for (int i = 0; i < 2; i++)
            store.ApplyResults(new ResultsReport
            {
                Map = "SteeE", WinningTeam = i,
                Rows = [new() { AccountId = account.Id, Team = i, Kills = 10, Deaths = 2,
                    Feats = new Dictionary<string, int> { [FeatKeys.SpyObjectives] = 120 } }],
            }, "Test server");
        string keyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pem");
        try
        {
            using var signer = TicketSigner.LoadOrCreate(keyPath);
            var options = MasterTests.LocalOptions();
            var api = new AccountApi(options, store, signer, new ServerRegistry(options), _ => IPAddress.Loopback, TimeProvider.System);
            string json = JsonSerializer.Serialize(api.Info(account), MasterApi.Json);
            AccountInfo result = JsonSerializer.Deserialize<AccountInfo>(json, MasterApi.Json)!;
            Assert.Equal(2, result.Stats.Matches);
            Assert.Equal(20, result.Stats.Kills);
            Assert.NotNull(result.Career);
            Assert.Equal(240, result.Career.Feats[FeatKeys.SpyObjectives]);
            MapCareerStats map = Assert.Single(result.Career.Maps);
            Assert.Equal("Stee", map.Map);
            Assert.Equal(1, map.SpyWins);
            Assert.Equal(1, map.MercWins);
            var empty = api.Info(store.GetOrCreate("empty-stats-test"));
            Assert.NotNull(empty.Career);
            Assert.Empty(empty.Career.Feats);
            Assert.Empty(empty.Career.Maps);
        }
        finally { File.Delete(keyPath); }
    }
}
