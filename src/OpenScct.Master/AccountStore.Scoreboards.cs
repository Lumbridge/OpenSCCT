using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenScct.Net.Discovery;
using OpenScct.Net.Scoreboards;

namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    private void MigrateScoreboards()
    {
        var matchColumns = Query("PRAGMA table_info(matches);", r => r.GetString(1));
        if (!matchColumns.Contains("round_id")) Exec("ALTER TABLE matches ADD COLUMN round_id TEXT;");
        if (!matchColumns.Contains("scoreboard")) Exec("ALTER TABLE matches ADD COLUMN scoreboard TEXT;");
        if (!Query("PRAGMA table_info(match_rows);", r => r.GetString(1)).Contains("left_early"))
            Exec("ALTER TABLE match_rows ADD COLUMN left_early INTEGER NOT NULL DEFAULT 0;");
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS matches_round ON matches(round_id, server_id) WHERE round_id IS NOT NULL;");
    }

    private long? FindReportedRound(string server, string? roundId) => string.IsNullOrEmpty(roundId) ? null :
        One("SELECT id FROM matches WHERE server_id = $server AND round_id = $round;", r => (long?)r.GetInt64(0),
            ("$server", server), ("$round", roundId));

    /// <summary>Stores a trusted report and awards it atomically; a repeated stable round is never awarded twice.</summary>
    public long RecordReportedMatch(ResultsReport report, string serverName, bool counted)
    {
        lock (_gate)
        {
            return InTransaction(() =>
            {
                if (FindReportedRound(report.ServerId, report.Scoreboard?.RoundId) is { } existing) return existing;
                long id = RecordMatch(report, serverName, counted);
                if (counted) ApplyResults(report, serverName);
                return id;
            });
        }
    }

    public MatchHistoryDetails? HistoryDetailsByRound(long account, string roundId)
    {
        if (!Guid.TryParse(roundId, out var parsed)) return null;
        lock (_gate)
        {
            long? id = One("SELECT m.id FROM matches m WHERE m.round_id = $round AND EXISTS " +
                "(SELECT 1 FROM match_rows r WHERE r.match_id = m.id AND r.account_id = $a) ORDER BY m.id DESC LIMIT 1;",
                r => (long?)r.GetInt64(0), ("$round", parsed.ToString("N")), ("$a", account));
            return id is { } match ? HistoryDetails(account, match) : null;
        }
    }

    private RoundScoreboard SavedScoreboard(MatchRow match, IReadOnlyList<MatchPlayerRow> rows)
    {
        string? json = One("SELECT scoreboard FROM matches WHERE id = $id;", r => r.IsDBNull(0) ? null : r.GetString(0), ("$id", match.Id));
        if (json is not null && JsonSerializer.Deserialize<RoundScoreboard>(json, MasterApi.Json) is { } board)
            return board with { MatchId = match.Id };
        var ended = DateTimeOffset.FromUnixTimeSeconds(match.Time);
        return new RoundScoreboard
        {
            RoundId = match.RoundId ?? string.Empty, MatchId = match.Id,
            Map = match.Map, Server = match.ServerName, Mode = match.Mode, StartedUtc = ended.AddSeconds(-match.DurationSeconds),
            EndedUtc = ended, ElapsedSeconds = match.DurationSeconds, AsOfSeconds = match.DurationSeconds,
            Completed = true, Winner = match.Winner, Reason = match.Reason,
            ObjectivesTaken = match.ObjectivesTaken, ObjectivesNeeded = match.ObjectivesNeeded,
            Rows = rows.Select((r, index) => new ScoreboardPlayer
            {
                ParticipantId = index + 1, Name = r.Name, Team = r.Team, Score = r.Score, Kills = r.Kills, Deaths = r.Deaths,
                Objectives = r.Feats.TryGetValue("spy.objectives", out int objectives) ? objectives : -1,
                Lives = -2, PingMs = -1, Bot = r.Bot, LeftEarly = r.LeftEarly, StatsVersion = 0, Stats = r.Feats,
            }).ToArray(),
        };
    }

    private DemoEntry? AvailableHistoryDemo(MatchRow match, IReadOnlyList<MatchPlayerRow> players)
    {
        if (DemoOfMatch(match.Id) is not { State: DemoState.Ready } demo || demo.Expires <= Now) return null;
        return new DemoEntry
        {
            Id = demo.Id, MatchId = match.Id, RoundId = match.RoundId, Map = match.Map, Mode = match.Mode, ServerName = match.ServerName,
            Time = DateTimeOffset.FromUnixTimeSeconds(match.Time), DurationSeconds = demo.DurationSeconds > 0 ? demo.DurationSeconds : match.DurationSeconds,
            WinningTeam = match.Winner, Size = demo.Size, Sha256 = demo.Sha256, GameVersion = demo.GameVersion,
            NetVersion = demo.NetVersion, ContentHash = demo.ContentHash, Expires = DateTimeOffset.FromUnixTimeSeconds(demo.Expires),
            Players = players.Select(p => new DemoPlayer { Name = p.Name, Team = p.Team, Score = p.Score, Kills = p.Kills, Deaths = p.Deaths, Bot = p.Bot }).ToArray(),
        };
    }
}
