using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using OpenScct.Server.Online;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenScct.Core.Engine;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class CareerStatsTests
{
    [Fact]
    public void ServerRoundCountersSurviveReportingAndAccumulateBeyondAchievementLimits()
    {
        var stats = new USPlayerStats();
        foreach (var field in typeof(USPlayerStats).GetFields().Where(f => f.FieldType == typeof(int)))
            field.SetValue(stats, 1234);
        stats.Merc_GasMaskTimeUsed = 345.75f;
        stats.AddFeat(FeatKeys.SpyMineKills, 2);
        var pri = new PlayerReplicationInfo { Stats = stats };
        var export = typeof(OnlineServer).GetMethod("FeatsOf", BindingFlags.NonPublic | BindingFlags.Static)!;
        var feats = (Dictionary<string, int>)export.Invoke(null, [pri])!;
        Assert.Equal(2, feats[FeatKeys.SpyMineKills]);
        foreach (var counter in CareerStatCounters.All)
            Assert.Equal(counter.Read(stats), feats[counter.Key]);
        Assert.Equal(345, feats["career.merc_gasmasktimeused"]);
        using var store = AccountStore.Open(":memory:");
        var account = store.GetOrCreate("all-career-counters");
        var report = new ResultsReport { WinningTeam = 0,
            Rows = [new() { AccountId = account.Id, Team = 0, Feats = feats }] };
        report = JsonSerializer.Deserialize<ResultsReport>(JsonSerializer.Serialize(report, MasterApi.Json), MasterApi.Json)!;
        store.ApplyResults(report, "Test");
        store.ApplyResults(report, "Test");
        var progress = store.Progress(account.Id);
        foreach (var counter in CareerStatCounters.All)
            Assert.Equal(counter.Read(stats) * 2, progress.Feat(counter.Key));
        Assert.Equal(4, progress.Feat(FeatKeys.SpyMineKills));
    }

    [Fact]
    public void CareerCounterLimitsRejectUnknownAndNegativeValues()
    {
        using var store = AccountStore.Open(":memory:");
        var account = store.GetOrCreate("career-limits");
        store.ApplyResults(new ResultsReport { WinningTeam = 0, Rows = [new() { AccountId = account.Id, Team = 0,
            Feats = new Dictionary<string, int> { [CareerStatCounters.ShotsFired] = int.MaxValue,
                [CareerStatCounters.ShotsHit] = -10, [FeatKeys.SpyMineKills] = 300, ["unknown"] = 100 } }] }, "Test");
        var progress = store.Progress(account.Id);
        Assert.Equal(1000000, progress.Feat(CareerStatCounters.ShotsFired));
        Assert.Equal(0, progress.Feat(CareerStatCounters.ShotsHit));
        Assert.Equal(200, progress.Feat(FeatKeys.SpyMineKills));
        Assert.Equal(0, progress.Feat("unknown"));
    }

    [Fact]
    public void VersionEightUpgradePreservesLifetimeTotalsWithoutInventingRoleHistory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            long id;
            using (var store = AccountStore.Open(path))
            {
                id = store.GetOrCreate("legacy-role-rounds").Id;
                store.ApplyResults(new ResultsReport { WinningTeam = 0,
                    Rows = [new() { AccountId = id, Team = 0, Kills = 4 }] }, "Test");
            }
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "DROP TABLE role_round_stats; UPDATE schema_version SET version = 8;";
                command.ExecuteNonQuery();
            }
            using var upgraded = AccountStore.Open(path);
            var stats = upgraded.Stats(id);
            Assert.Equal(1, stats.Matches);
            Assert.Equal(1, stats.SpyWins);
            Assert.Equal(4, stats.Kills);
            Assert.Equal(new RoleRoundStats(), stats.SpyRounds);
            Assert.Equal(new RoleRoundStats(), stats.MercRounds);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RoleRoundsPersistWinsLossesAndIgnoreBotsAndDuplicateAccounts()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        long id;
        try
        {
            using (var store = AccountStore.Open(path))
            {
                id = store.GetOrCreate("role-rounds").Id;
                foreach (var (team, winner) in new[] { (0, 0), (0, 1), (0, 0), (1, 0) })
                    store.ApplyResults(new ResultsReport { WinningTeam = winner, Rows = [
                        new() { AccountId = id, Team = team, Feats = new Dictionary<string, int> { ["career.damagegiven"] = 1234 } }, new() { AccountId = id, Team = team },
                        new() { AccountId = id, Team = team, Bot = true }] }, "Test");
            }
            using var reopened = AccountStore.Open(path);
            var stats = reopened.Stats(id);
            Assert.Equal(4936, reopened.Progress(id).Feat("career.damagegiven"));
            Assert.Equal(new RoleRoundStats { Matches = 3, Wins = 2 }, stats.SpyRounds);
            Assert.Equal(new RoleRoundStats { Matches = 1, Wins = 0 }, stats.MercRounds);
        }
        finally { File.Delete(path); }
    }

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
            Assert.Equal(new RoleRoundStats { Matches = 1, Wins = 1 }, result.Stats.SpyRounds);
            Assert.Equal(new RoleRoundStats { Matches = 1, Wins = 1 }, result.Stats.MercRounds);
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
