using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using OpenScct.Net.Scoreboards;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class MatchHistoryTests
{
    [Fact]
    public void Granted_loadout_is_snapshotted_bounded_and_kept_in_history_with_legacy_nulls()
    {
        using var store = AccountStore.Open(":memory:");
        long account = store.GetOrCreate("loadout-owner").Id;
        var kit = new RecordedLoadout { Weapon = "SBase.SWeaponDefense", Gadgets =
            ["SBase.SGadFragGrenade", "SBase.SGadGasMask", "SBase.SGadGasMask", "<invalid>"] };
        long id = store.RecordMatch(new ResultsReport { Rows = [new() { AccountId = account, Loadout = kit }] }, "Test", true);
        kit.Gadgets[0] = "SBase.SGadBomb";
        var saved = Assert.Single(store.HistoryDetails(account, id)!.Players).Loadout!;
        Assert.Equal("SBase.SWeaponDefense", saved.Weapon);
        Assert.Equal(["SBase.SGadFragGrenade", "SBase.SGadGasMask"], saved.Gadgets);
        long legacy = store.RecordMatch(new ResultsReport { Rows = [new() { AccountId = account }] }, "Old", true);
        Assert.Null(Assert.Single(store.HistoryDetails(account, legacy)!.Players).Loadout);
    }

    [Fact]
    public void Version_nine_migration_preserves_rounds_without_inventing_old_loadouts()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            long account, match;
            using (var store = AccountStore.Open(path))
            {
                account = store.GetOrCreate("legacy-loadout-owner").Id;
                match = store.RecordMatch(new ResultsReport { Rows = [new() { AccountId = account, Kills = 3 }] }, "Old", true);
            }
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "ALTER TABLE match_rows DROP COLUMN loadout; UPDATE schema_version SET version = 9;";
                command.ExecuteNonQuery();
            }
            using var upgraded = AccountStore.Open(path);
            var row = Assert.Single(upgraded.HistoryDetails(account, match)!.Players);
            Assert.Equal(3, row.Kills);
            Assert.Null(row.Loadout);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void HistoryIsAccountScopedPagedAndRetainsTimelineOrdering()
    {
        using var store = AccountStore.Open(":memory:");
        long owner = store.GetOrCreate("history-owner").Id;
        long other = store.GetOrCreate("history-other").Id;
        long id = 0;
        for (int i = 0; i < 21; i++)
            id = store.RecordMatch(new ResultsReport { Map = "Test", DurationSeconds = 20,
                Events = [new(0, "start", "Round started"), new(20, "end", "Round ended")],
                Rows = [new() { AccountId = owner, Name = "Agent", Team = 0, Score = 5 }] }, "Test server", true);
        Assert.True(store.History(owner, 0).HasMore);
        Assert.Equal(20, store.History(owner, 0).Matches.Count);
        Assert.Single(store.History(owner, 1).Matches);
        Assert.Equal(id, store.History(owner, 0).Matches[0].Id);
        Assert.Empty(store.History(other, 0).Matches);
        Assert.Null(store.HistoryDetails(other, id));
        var detail = store.HistoryDetails(owner, id)!;
        Assert.Equal("Round started", detail.Events![0].Text);
        Assert.Equal(20, detail.Events[1].Seconds);
        Assert.Equal(5, Assert.Single(detail.Players).Score);
        long old = store.RecordMatch(new ResultsReport { Rows = [new() { AccountId = owner }] }, "Old server", false);
        Assert.Null(store.HistoryDetails(owner, old)!.Events);
    }

    [Fact]
    public void ExpiryDeletesTimelinesAndDemoMetadataButKeepsScoreboardsAndCareerTotals()
    {
        var clock = new Clock();
        using var store = AccountStore.Open(":memory:", clock);
        var account = store.GetOrCreate("retention-owner");
        var report = new ResultsReport { WinningTeam = 0, Rows = [new() { AccountId = account.Id, Team = 0, Kills = 3 }],
            Events = [new(0, "start", "Round started")] };
        store.ApplyResults(report, "Test");
        long old = store.RecordMatch(report, "Test", true);
        var demo = store.CreateDemo(old, "history-test", 100, "sha", 10, "1", "1", "hash", 7);
        clock.Now += TimeSpan.FromDays(91);
        long fresh = store.RecordMatch(report, "Test", true);
        store.PruneMatchHistory(90);
        Assert.NotNull(store.Match(old));
        Assert.Null(store.HistoryDetails(account.Id, old)!.Events);
        Assert.Equal(3, Assert.Single(store.HistoryDetails(account.Id, old)!.Scoreboard!.Rows).Kills);
        Assert.Null(store.Demo(demo.Id));
        Assert.NotNull(store.HistoryDetails(account.Id, fresh));
        Assert.Equal(3, store.Progress(account.Id).Stats.Kills);
        Assert.Equal(2, store.History(account.Id, 0).Matches.Count);
    }

    [Fact]
    public async Task HttpHistoryRequiresSignInAndRejectsOtherPlayersMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        string key = path + ".pem";
        try
        {
            using var store = AccountStore.Open(path);
            var owner = store.GetOrCreate("http-owner");
            var other = store.GetOrCreate("http-other");
            string roundId = Guid.NewGuid().ToString("N");
            long id = store.RecordMatch(new ResultsReport { Scoreboard = new RoundScoreboard { RoundId = roundId },
                Rows = [new() { AccountId = owner.Id }] }, "Test", true);
            var options = MasterTests.LocalOptions();
            options.Introducer = options.Relay = options.Probe = false;
            options.Database = path;
            options.TicketKeyFile = key;
            options.Discord.ClientId = "fixture-client";
            options.Discord.ClientSecret = "fixture-secret";
            await using var master = await MasterHost.StartAsync(options, cancellationToken: ct);
            using var client = new MasterClient(master.HttpAddress);
            await Assert.ThrowsAsync<MasterException>(() => client.MatchHistoryAsync(cancellationToken: ct));
            await Assert.ThrowsAsync<MasterException>(() => client.MatchDetailsByRoundAsync(roundId, ct));
            client.SessionToken = store.CreateSession(owner.Id);
            Assert.Single((await client.MatchHistoryAsync(cancellationToken: ct)).Matches);
            Assert.Equal(id, (await client.MatchDetailsAsync(id, ct)).Match.Id);
            Assert.Equal(id, (await client.MatchDetailsByRoundAsync(roundId, ct)).Match.Id);
            Assert.Equal(roundId, (await client.MatchDetailsByRoundAsync(roundId, ct)).Scoreboard!.RoundId);
            client.SessionToken = store.CreateSession(other.Id);
            Assert.Empty((await client.MatchHistoryAsync(cancellationToken: ct)).Matches);
            await Assert.ThrowsAsync<MasterException>(() => client.MatchDetailsAsync(id, ct));
            await Assert.ThrowsAsync<MasterException>(() => client.MatchDetailsByRoundAsync(roundId, ct));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); File.Delete(key);
        }
    }

    [Fact]
    public void VersionSevenDatabaseUpgradesWithoutLosingMatches()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            long owner;
            using (var store = AccountStore.Open(path))
            {
                owner = store.GetOrCreate("upgrade-owner").Id;
                store.RecordMatch(new ResultsReport { Rows = [new() { AccountId = owner }] }, "Old server", true);
            }
            using (var db = new SqliteConnection("Data Source=" + path))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "DROP TABLE match_timelines; UPDATE schema_version SET version = 7;";
                command.ExecuteNonQuery();
            }
            using (var store = AccountStore.Open(path))
                Assert.Single(store.History(owner, 0).Matches);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
