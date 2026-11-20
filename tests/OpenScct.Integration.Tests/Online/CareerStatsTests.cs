using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using OpenScct.Server.Online;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class CareerStatsTests
{
    [Fact]
    public void ReopenedAccountsIssueSignedWeaponProgressForTheJoiningPlayer()
    {
        var temp = Directory.CreateTempSubdirectory("OpenSCCT-weapon-progress-");
        string db = Path.Combine(temp.FullName, "accounts.db");
        string keyFile = Path.Combine(temp.FullName, "tickets.pem");
        try
        {
            string first, second;
            using (var store = AccountStore.Open(db))
            {
                var veteran = store.GetOrCreate("weapon-progress-a");
                var recruit = store.GetOrCreate("weapon-progress-b");
                store.ClaimName(veteran.Id, "Veteran", false, out _);
                store.ClaimName(recruit.Id, "Recruit", false, out _);
                first = store.CreateSession(veteran.Id);
                second = store.CreateSession(recruit.Id);
                // Before these two new primaries existed, their entire family totals belonged to the legacy guns.
                store.ApplyResults(new ResultsReport { WinningTeam = 1, Rows = [new() { AccountId = veteran.Id, Team = 1,
                    Feats = new Dictionary<string, int> { ["merc.smg.kills"] = 17, ["merc.shotgun.kills"] = 19 } }] }, "Test server");
                string[] addedGuns = ["Svm.CompactSMG", "Svm.PumpShotgun", "Svm.BoltRifle", "Svm.DesignatedMarksman"];
                var veteranFeats = new Dictionary<string, int> { ["merc.m4a1s.kills"] = 30, ["merc.galil.kills"] = 5 };
                for (int i = 0; i < addedGuns.Length; i++)
                {
                    int slot = WeaponCareerCounters.IndexOf(addedGuns[i]);
                    Assert.True(slot >= 12);
                    veteranFeats.Add(WeaponCareerCounters.Key(slot), 11 + i);
                }
                veteranFeats["merc.smg.kills"] = 11;
                veteranFeats["merc.shotgun.kills"] = 12;
                store.ApplyResults(new ResultsReport { WinningTeam = 1, Rows = [
                    new() { AccountId = veteran.Id, Team = 1, Feats = veteranFeats },
                    new() { AccountId = recruit.Id, Team = 1, Feats = new Dictionary<string, int> { ["merc.ar.kills"] = 40 } },
                ] }, "Test server");
            }
            using var reopened = AccountStore.Open(db);
            using var signer = TicketSigner.LoadOrCreate(keyFile);
            var options = MasterTests.LocalOptions();
            var api = new AccountApi(options, reopened, signer, new ServerRegistry(options), _ => IPAddress.Loopback, TimeProvider.System);
            using var key = PlayerTicket.ImportPublicKey(signer.PublicKey)!;
            var gate = new TicketGate();
            int[] Counts(string session)
            {
                var context = new DefaultHttpContext();
                context.Request.Headers.Authorization = "Bearer " + session;
                var (signed, problem) = api.Ticket(context, "weapon-test");
                Assert.Null(problem);
                return Assert.IsType<PlayerTicket>(gate.Check(signed!, key, "weapon-test", DateTimeOffset.UtcNow, out _)).WeaponKills;
            }
            int[] veteranCounts = Counts(first);
            Assert.Equal(30, veteranCounts[WeaponCareerCounters.IndexOf("Svm.M4A1S")]);
            Assert.Equal(5, veteranCounts[WeaponCareerCounters.IndexOf("Svm.Galil")]);
            Assert.Equal(11, veteranCounts[WeaponCareerCounters.IndexOf("Svm.CompactSMG")]);
            Assert.Equal(12, veteranCounts[WeaponCareerCounters.IndexOf("Svm.PumpShotgun")]);
            Assert.Equal(13, veteranCounts[WeaponCareerCounters.IndexOf("Svm.BoltRifle")]);
            Assert.Equal(14, veteranCounts[WeaponCareerCounters.IndexOf("Svm.DesignatedMarksman")]);
            Assert.Equal(17, veteranCounts[WeaponCareerCounters.IndexOf("SBase.SWeaponDefense_Uzi")]);
            Assert.Equal(19, veteranCounts[WeaponCareerCounters.IndexOf("SBase.SWeaponDefense_Shotgun")]);
            Assert.All(Counts(second), count => Assert.Equal(0, count));
        }
        finally
        {
            File.Delete(keyFile);
            File.Delete(db);
            File.Delete(db + "-wal");
            File.Delete(db + "-shm");
            temp.Delete();
        }
    }

    [Fact]
    public void JoinTicketCarriesExactWeaponKillsAndKeepsOldClassTotalsSeparate()
    {
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("weapon-career").Id;
        store.ApplyResults(new ResultsReport { WinningTeam = 1, Rows = [new() { AccountId = id, Team = 1,
            Feats = new Dictionary<string,int> { ["merc.ar.kills"] = 82, ["merc.m4a1s.kills"] = 5,
                ["merc.automatic.kills"] = 18, ["merc.shotgun.kills"] = 6 } }] }, "match");
        var counts = WeaponCareerCounters.FromFeats(store.Progress(id).Feats);
        using var signer = System.Security.Cryptography.ECDsa.Create();
        var now = DateTimeOffset.UtcNow;
        var ticket = new PlayerTicket { AccountId = id, ServerId = "arena", Name = "Player",
            Expires = now + PlayerTicket.Lifetime, WeaponKills = counts };
        Assert.True(PlayerTicket.TryVerify(ticket.Sign(signer), signer, "arena", now, out var read, out _));
        Assert.Equal(5, read!.WeaponKills[WeaponCareerCounters.IndexOf("Svm.M4A1S")]);
        Assert.Equal(0, read.WeaponKills[WeaponCareerCounters.IndexOf("Svm.AK47")]);
        Assert.Equal(0, read.WeaponKills[WeaponCareerCounters.IndexOf("SBase.SWeaponDefense")]);
        Assert.Equal(18, read.WeaponKills[WeaponCareerCounters.IndexOf("OpenScct.Sidearm_automatic")]);
        Assert.Equal(6, read.WeaponKills[WeaponCareerCounters.IndexOf("SBase.SWeaponDefense_Shotgun")]);
        var oldTicket = ticket with { WeaponKills = [] };
        Assert.True(PlayerTicket.TryVerify(oldTicket.Sign(signer), signer, "arena", now, out read, out _));
        Assert.Empty(read!.WeaponKills);
    }

    [Fact]
    public void JoiningWithAnOlderTwelveGunTicketPreservesEveryExistingWeaponIdentity()
    {
        string[] original =
        [
            "SBase.SWeaponDefense", "SBase.SWeaponDefense_Uzi", "SBase.SWeaponDefense_Shotgun",
            "Svm.M4A1S", "Svm.AK47", "Svm.Galil", "Svm.Famas", "OpenScct.Sidearm_suppressed",
            "OpenScct.Sidearm_automatic", "OpenScct.Sidearm_magnum44", "OpenScct.Sidearm_desert_eagle",
            "OpenScct.Sidearm_unsuppressed",
        ];
        int[] legacyCounts = Enumerable.Range(1, original.Length).ToArray();
        var now = DateTimeOffset.UtcNow;
        var legacy = new PlayerTicket { AccountId = 1, ServerId = "arena", Name = "Legacy",
            Expires = now + PlayerTicket.Lifetime, WeaponKills = legacyCounts };
        using var signer = System.Security.Cryptography.ECDsa.Create();
        Assert.True(PlayerTicket.TryVerify(legacy.Sign(signer), signer, "arena", now, out var ticket, out _));
        var career = new WeaponCareer(ticket!.WeaponKills);
        for (int i = 0; i < original.Length; i++)
        {
            Assert.Equal(i, WeaponCareerCounters.IndexOf(original[i]));
            Assert.Equal(legacyCounts[i], career.Kills(original[i]));
        }
        Assert.All(career.Snapshot().Skip(original.Length), count => Assert.Equal(0, count));
    }

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
