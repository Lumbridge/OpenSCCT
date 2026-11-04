using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Net.Scoreboards;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class ScoreboardArchiveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "openscct-scoreboard-archive-" + Guid.NewGuid().ToString("N"));
    private ScoreboardArchive Archive => new(_directory);

    private static ArchivedScoreboard Entry(bool completed = false) => new()
    {
        PlayedLocally = true, LocalParticipantId = 7, LeftEarly = !completed, Master = "https://master.test/", AccountId = 17,
        Scoreboard = new RoundScoreboard
        {
            RoundId = Guid.NewGuid().ToString("N"), StartedUtc = DateTimeOffset.UtcNow.AddSeconds(-90),
            EndedUtc = DateTimeOffset.UtcNow, ElapsedSeconds = 90, AsOfSeconds = completed ? 90 : 60,
            Completed = completed, Partial = !completed, Sequence = 3,
            Rows = [new() { ParticipantId = 7, Name = "Agent", Team = 0,
                Stats = new Dictionary<string, int> { ["spy.neutralisations"] = completed ? 4 : 2 } }],
        },
    };

    [Fact]
    public void Partial_upgrades_once_to_final_and_keeps_local_participation_and_departure()
    {
        var archive = Archive;
        var partial = Entry();
        archive.Save(partial);
        var final = partial with
        {
            PlayedLocally = false, LocalParticipantId = -1, LeftEarly = false,
            Scoreboard = partial.Scoreboard with
            {
                Completed = true, Partial = false, MatchId = 10, ElapsedSeconds = 120, AsOfSeconds = 120,
                Rows = [partial.Scoreboard.Rows[0] with { Stats = new Dictionary<string, int> { ["spy.neutralisations"] = 5 } }],
            },
        };
        archive.Save(final);
        archive.Save(final);
        var stored = Assert.Single(Archive.ReadAll());
        Assert.Single(Directory.GetFiles(_directory, "*.json"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.True(stored.Scoreboard.Completed);
        Assert.False(stored.Scoreboard.Partial);
        Assert.Equal(7, stored.LocalParticipantId);
        Assert.True(stored.PlayedLocally);
        Assert.True(stored.LeftEarly);
        Assert.True(Assert.Single(stored.Scoreboard.Rows).LeftEarly);
        Assert.Equal(5, stored.Scoreboard.Rows[0].Stats!["spy.neutralisations"]);
        Assert.Equal(10, stored.Scoreboard.MatchId);
    }

    [Fact]
    public void Completed_results_cannot_be_downgraded_by_a_later_partial_snapshot()
    {
        var archive = Archive;
        var final = Entry(completed: true);
        archive.Save(final);
        archive.Save(final with { Scoreboard = final.Scoreboard with
        {
            Completed = false, Partial = true, Sequence = 999, ElapsedSeconds = 500, AsOfSeconds = 470,
            Rows = [final.Scoreboard.Rows[0] with { Stats = null }],
        } });
        var stored = Assert.Single(archive.ReadAll());
        Assert.True(stored.Scoreboard.Completed);
        Assert.Equal(90, stored.Scoreboard.ElapsedSeconds);
        Assert.Equal(4, stored.Scoreboard.Rows[0].Stats!["spy.neutralisations"]);
    }

    [Fact]
    public void Adding_a_cloud_id_does_not_replace_a_newer_sample_with_older_counters()
    {
        var newer = Entry();
        var older = newer with { Scoreboard = newer.Scoreboard with
        {
            MatchId = 3, ElapsedSeconds = 40, AsOfSeconds = 10,
            Rows = [newer.Scoreboard.Rows[0] with { Stats = null }],
        } };
        var merged = ScoreboardArchive.Merge(newer, older);
        Assert.Equal(90, merged.Scoreboard.ElapsedSeconds);
        Assert.NotNull(merged.Scoreboard.Rows[0].Stats);
        Assert.Equal(3, merged.Scoreboard.MatchId);
        var imported = newer with { LocalParticipantId = -1 };
        Assert.Equal(7, ScoreboardArchive.Merge(imported, newer).LocalParticipantId);
    }

    [Fact]
    public void Failed_replacement_leaves_the_previous_complete_file_readable()
    {
        var archive = Archive;
        var entry = Entry(completed: true);
        archive.Save(entry);
        Assert.Throws<InvalidDataException>(() => archive.Save(entry with { ReplayPath = new string('x', 4 * 1024 * 1024) }));
        Assert.Throws<InvalidDataException>(() => archive.Save(entry with { Scoreboard = entry.Scoreboard with { Rows = null! } }));
        var stored = Assert.Single(Archive.ReadAll());
        Assert.Equal(entry.Scoreboard.RoundId, stored.Scoreboard.RoundId);
        Assert.Null(stored.ReplayPath);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Malformed_unsupported_and_oversized_files_are_isolated_from_valid_history()
    {
        var archive = Archive;
        var entry = Entry(completed: true);
        archive.Save(entry);
        File.WriteAllText(Path.Combine(_directory, "malformed.json"), "{");
        File.WriteAllText(Path.Combine(_directory, "version.json"), JsonSerializer.Serialize(entry with { Version = 999 }));
        File.WriteAllText(Path.Combine(_directory, "null-master.json"), JsonSerializer.Serialize(entry with { Master = null! }));
        File.WriteAllText(Path.Combine(_directory, "large.json"), new string('x', 4 * 1024 * 1024 + 1));
        var problems = new List<string>();
        Assert.Equal(entry.Scoreboard.RoundId, Assert.Single(archive.ReadAll(problems.Add)).Scoreboard.RoundId);
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void Cloud_only_records_are_scoped_to_the_fetching_account_and_master()
    {
        var cloud = Entry(completed: true) with { PlayedLocally = false };
        Assert.True(ScoreboardArchive.VisibleTo(cloud, "HTTPS://MASTER.TEST", 17));
        Assert.False(ScoreboardArchive.VisibleTo(cloud, "https://master.test", 18));
        Assert.False(ScoreboardArchive.VisibleTo(cloud, "https://other.test", 17));
        Assert.False(ScoreboardArchive.VisibleTo(cloud, "https://master.test", 0));
        Assert.True(ScoreboardArchive.VisibleTo(cloud with { PlayedLocally = true }, "https://other.test", 0));
        Assert.True(ScoreboardArchive.VisibleTo(cloud with { ImportedReplay = true }, "https://other.test", 0));
        Assert.NotEqual(ScoreboardArchive.NormaliseMaster("https://master.test/First"),
            ScoreboardArchive.NormaliseMaster("https://master.test/first"));
        Assert.NotEqual(ScoreboardArchive.Identity(cloud with { Scoreboard = cloud.Scoreboard with { RoundId = "", MatchId = 5 } }),
            ScoreboardArchive.Identity(cloud with { Master = "https://other.test", Scoreboard = cloud.Scoreboard with { RoundId = "", MatchId = 5 } }));
    }

    [Fact]
    public void Conflicting_master_references_cannot_overwrite_a_saved_round()
    {
        var archive = Archive;
        var entry = Entry(completed: true);
        archive.Save(entry);
        Assert.Throws<ArgumentException>(() => archive.Save(entry with { Master = "https://other.test", AccountId = 19 }));
        var stored = Assert.Single(archive.ReadAll());
        Assert.Equal(entry.Master, stored.Master);
        Assert.Equal(17, stored.AccountId);
    }

    [Fact]
    public void Scoreboard_persists_after_its_optional_replay_is_deleted()
    {
        var archive = Archive;
        Directory.CreateDirectory(_directory);
        string replay = Path.Combine(_directory, "round.scctdemo");
        File.WriteAllBytes(replay, [1, 2]);
        var entry = Entry(completed: true) with { ReplayPath = replay };
        archive.Save(entry);
        File.Delete(replay);
        Assert.Equal(entry.Scoreboard.RoundId, Assert.Single(Archive.ReadAll()).Scoreboard.RoundId);
    }

    [Fact]
    public void Imported_replays_remain_local_without_claiming_the_user_played_them()
    {
        var archive = Archive;
        var imported = Entry(completed: true) with { PlayedLocally = false, LocalParticipantId = -1, ImportedReplay = true };
        archive.Save(imported);
        var cloud = imported with { ImportedReplay = false };
        var stored = archive.Save(cloud);
        Assert.True(stored.ImportedReplay);
        Assert.False(stored.PlayedLocally);
        Assert.Equal(-1, stored.LocalParticipantId);
        Assert.True(ScoreboardArchive.VisibleTo(stored, "https://other.test", 0));
    }

    [Fact]
    public void Fetching_the_same_round_with_another_account_keeps_each_accounts_cached_access()
    {
        var archive = Archive;
        var first = Entry(completed: true) with { PlayedLocally = false, LocalParticipantId = -1 };
        archive.Save(first);
        archive.Save(first with { AccountId = 18 });
        var cached = Assert.Single(Archive.ReadAll());
        Assert.Equal(new long[] { 17, 18 }, cached.FetchedAccounts);
        Assert.True(ScoreboardArchive.VisibleTo(cached, "https://master.test", 17));
        Assert.True(ScoreboardArchive.CanQuery(cached, "https://master.test", 18));
        Assert.False(ScoreboardArchive.CanQuery(cached, "https://master.test", 19));
        Assert.False(ScoreboardArchive.VisibleTo(cached, "https://other.test", 17));
        var backToFirst = archive.Save(first);
        Assert.Equal(new long[] { 17, 18 }, backToFirst.FetchedAccounts);
        Assert.True(ScoreboardArchive.CanQuery(backToFirst, "https://master.test", 18));
        Assert.False(ScoreboardArchive.CanQuery(backToFirst with { Master = "" }, "", 17));
        Assert.Throws<InvalidDataException>(() => archive.Save(first with { FetchedAccounts = [17, 17] }));
        Assert.Throws<InvalidDataException>(() => archive.Save(first with { FetchedAccounts = [0] }));
        Assert.Throws<InvalidDataException>(() => archive.Save(first with { FetchedAccounts = Enumerable.Range(1, 257).Select(i => (long)i).ToArray() }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
