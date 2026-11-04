using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using OpenScct.Net.Scoreboards;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class ScoreboardHistoryTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ResultsReport Report(params ResultsReportRow[] rows)
    {
        DateTimeOffset ended = DateTimeOffset.UtcNow;
        return new ResultsReport
        {
            ServerId = "trusted-test", Map = "TestBox", Mode = 5, WinningTeam = 0, WinningReason = 0, DurationSeconds = 90,
            Rows = rows, Events = [new(0, "start", "Round started"), new(90, "end", "Round ended")],
            Scoreboard = new RoundScoreboard
            {
                RoundId = Guid.NewGuid().ToString("N"), Map = "TestBox", Mode = 5, Winner = 0, Reason = 0,
                StartedUtc = ended.AddSeconds(-90), EndedUtc = ended, ElapsedSeconds = 90, AsOfSeconds = 90, Completed = true,
                Rows = rows.Select((r, index) => new ScoreboardPlayer
                {
                    ParticipantId = index + 1, Name = r.Name ?? "Player", Team = r.Team, Kills = r.Kills, Deaths = r.Deaths,
                    Score = r.Score, Bot = r.Bot, LeftEarly = r.LeftEarly, Stats = r.Feats,
                }).ToArray(),
            },
        };
    }

    [Fact]
    public void Completed_scoreboard_is_idempotent_and_departed_players_get_history_without_awards()
    {
        using var store = AccountStore.Open(":memory:");
        long spy = store.GetOrCreate("complete-spy").Id, merc = store.GetOrCreate("complete-merc").Id,
            departed = store.GetOrCreate("departed-spy").Id, stranger = store.GetOrCreate("stranger").Id;
        var report = Report(new() { AccountId = spy, Team = 0, Kills = 2, Name = "Spy", Feats = new Dictionary<string, int> { ["spy.neckbreaks"] = 2 } },
            new() { AccountId = merc, Team = 1, Kills = 3, Name = "Merc" },
            new() { AccountId = departed, Team = 0, Kills = 5, Name = "Departed", LeftEarly = true });
        Assert.True(AccountApi.ValidScoreboard(report));
        Assert.True(AccountApi.Countable(report));
        long id = store.RecordReportedMatch(report, "Trusted server", counted: true);
        Assert.Equal(id, store.RecordReportedMatch(report, "Trusted server", counted: true));
        Assert.Equal(1, store.Progress(spy).Stats.Matches);
        Assert.Equal(2, store.Progress(spy).Stats.Kills);
        Assert.Equal(0, store.Progress(departed).Stats.Matches);
        Assert.Empty(store.RecentResults(departed));
        Assert.Single(store.History(departed, 0).Matches);
        var details = store.HistoryDetailsByRound(departed, report.Scoreboard!.RoundId)!;
        Assert.Equal(id, details.Match.Id);
        Assert.Equal("Trusted server", details.Scoreboard!.Server);
        Assert.True(details.Scoreboard.Rows.Single(p => p.Name == "Departed").LeftEarly);
        Assert.True(details.Players.Single(p => p.Name == "Departed").LeftEarly);
        Assert.Equal(2, details.Players.Single(p => p.Name == "Spy").Feats["spy.neckbreaks"]);
        Assert.Null(store.HistoryDetailsByRound(stranger, report.Scoreboard.RoundId));
        Assert.False(AccountApi.Countable(report with { Rows = report.Rows.Where(r => r.AccountId != spy).ToArray() }));
    }

    [Fact]
    public void Partial_or_unbounded_scoreboards_cannot_be_uploaded()
    {
        var report = Report(new ResultsReportRow { Team = 0, Name = "Player" });
        Assert.True(AccountApi.ValidScoreboard(report));
        foreach (var board in new[]
        {
            report.Scoreboard! with { Partial = true }, report.Scoreboard! with { Completed = false },
            report.Scoreboard! with { Practice = true }, report.Scoreboard! with { RoundId = "not-a-round" },
            report.Scoreboard! with { ElapsedSeconds = double.NaN }, report.Scoreboard! with { AsOfSeconds = 91 },
            report.Scoreboard! with { Rows = [report.Scoreboard!.Rows[0] with { Stats = null }] },
        }) Assert.False(AccountApi.ValidScoreboard(report with { Scoreboard = board }));
        Assert.True(AccountApi.ValidScoreboard(report with { Scoreboard = null }));
    }

    [Fact]
    public void Public_scoreboard_and_award_rows_must_match_even_with_duplicate_names()
    {
        var report = Report(new() { Name = "Same", Team = 0, Kills = 2 }, new() { Name = "Same", Team = 0, Kills = 3 });
        Assert.True(AccountApi.ValidScoreboard(report with { Rows = report.Rows.Reverse().ToArray() }));
        Assert.False(AccountApi.ValidScoreboard(report with { Rows = [report.Rows[0], report.Rows[0]] }));
        Assert.False(AccountApi.ValidScoreboard(report with { Rows = [report.Rows[0], report.Rows[1] with { LeftEarly = true }] }));
        Assert.False(AccountApi.ValidScoreboard(report with { ObjectivesTaken = 9 }));
        Assert.False(AccountApi.ValidScoreboard(report with { Rows = [report.Rows[0], report.Rows[1] with
            { Feats = new Dictionary<string, int> { ["spy.neckbreaks"] = 20 } }] }));
    }

    [Fact]
    public void Legacy_reports_without_round_identity_are_not_mistaken_for_retries()
    {
        using var store = AccountStore.Open(":memory:");
        long owner = store.GetOrCreate("legacy-reports").Id;
        var report = new ResultsReport { WinningTeam = 0, Rows = [new() { AccountId = owner, Name = "Spy", Team = 0, Kills = 2 }] };
        Assert.True(AccountApi.ValidScoreboard(report));
        long first = store.RecordReportedMatch(report, "Legacy server", true);
        long second = store.RecordReportedMatch(report, "Legacy server", true);
        Assert.NotEqual(first, second);
        Assert.Equal(2, store.Progress(owner).Stats.Matches);
        Assert.Equal(0, Assert.Single(store.HistoryDetails(owner, second)!.Scoreboard!.Rows).StatsVersion);
    }

    [Fact]
    public void Award_failure_rolls_back_the_scoreboard_so_retry_can_complete_atomically()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            using var store = AccountStore.Open(path);
            long owner = store.GetOrCreate("atomic-report").Id;
            var report = Report(new ResultsReportRow { AccountId = owner, Name = "Spy", Team = 0, Kills = 2 });
            using var db = new SqliteConnection($"Data Source={path};Pooling=False");
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_scoreboard_awards BEFORE INSERT ON stats BEGIN SELECT RAISE(FAIL, 'fixture award failure'); END;";
            command.ExecuteNonQuery();
            Assert.Throws<SqliteException>(() => store.RecordReportedMatch(report, "Test", true));
            Assert.Equal(0, store.CountMatches(0));
            Assert.Equal(0, store.Progress(owner).Stats.Matches);
            command.CommandText = "DROP TRIGGER reject_scoreboard_awards;";
            command.ExecuteNonQuery();
            long id = store.RecordReportedMatch(report, "Test", true);
            Assert.Equal(id, store.RecordReportedMatch(report, "Test", true));
            Assert.Equal(1, store.CountMatches(0));
            Assert.Equal(1, store.Progress(owner).Stats.Matches);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
        }
    }

    [Fact]
    public void Auxiliary_cleanup_advances_past_retained_scoreboards_in_bounded_batches()
    {
        var clock = new Clock();
        using var store = AccountStore.Open(":memory:", clock);
        long owner = store.GetOrCreate("long-history").Id;
        var report = new ResultsReport { Rows = [new() { AccountId = owner, Kills = 7 }], Events = [new(0, "start", "Started")] };
        long first = 0, last = 0, lastDemo = 0;
        for (int i = 0; i < 1002; i++)
        {
            last = store.RecordMatch(report, "Test", false);
            if (i == 0) first = last;
            lastDemo = store.CreateDemo(last, "demo-" + i, 1, "sha", 1, "1", "1", "1", 7).Id;
        }
        clock.Now += TimeSpan.FromDays(91);
        store.PruneMatchHistory(90);
        Assert.Null(store.HistoryDetails(owner, first)!.Events);
        Assert.NotNull(store.HistoryDetails(owner, last)!.Events);
        Assert.NotNull(store.Demo(lastDemo));
        store.PruneMatchHistory(90);
        Assert.Null(store.HistoryDetails(owner, last)!.Events);
        Assert.Null(store.Demo(lastDemo));
        Assert.Equal(7, Assert.Single(store.HistoryDetails(owner, first)!.Scoreboard!.Rows).Kills);
        Assert.Equal(1002, store.CountMatches(0));
    }

    [Fact]
    public void Version_ten_upgrade_preserves_legacy_stats_without_inventing_new_counters()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            long owner, id;
            using (var store = AccountStore.Open(path))
            {
                owner = store.GetOrCreate("legacy-scoreboard").Id;
                id = store.RecordMatch(new ResultsReport { Rows = [new() { AccountId = owner, Kills = 2,
                    Feats = new Dictionary<string, int> { ["spy.chokes"] = 4 } }] }, "Legacy", true);
            }
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "DROP INDEX matches_round; ALTER TABLE matches DROP COLUMN round_id; " +
                    "ALTER TABLE matches DROP COLUMN scoreboard; ALTER TABLE match_rows DROP COLUMN left_early; UPDATE schema_version SET version = 10;";
                command.ExecuteNonQuery();
            }
            using var upgraded = AccountStore.Open(path);
            var details = upgraded.HistoryDetails(owner, id)!;
            Assert.Null(details.Match.RoundId);
            Assert.Equal(1, details.Scoreboard!.SchemaVersion);
            var player = Assert.Single(details.Scoreboard.Rows);
            Assert.Equal(0, player.StatsVersion);
            Assert.Equal(4, player.Stats!["spy.chokes"]);
            Assert.False(player.Stats.ContainsKey("spy.neutralisations"));
            Assert.Equal(2, player.Kills);
            Assert.Equal(-1, player.Objectives);
            Assert.Equal(-2, player.Lives);
            Assert.Equal(-1, player.PingMs);
            long withObjectives = upgraded.RecordMatch(new ResultsReport { Rows = [new() { AccountId = owner,
                Feats = new Dictionary<string, int> { ["spy.objectives"] = 3 } }] }, "Legacy", true);
            Assert.Equal(3, Assert.Single(upgraded.HistoryDetails(owner, withObjectives)!.Scoreboard!.Rows).Objectives);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
        }
    }
}
