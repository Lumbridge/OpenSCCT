using System;
using System.Linq;
using System.Text.Json;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    private static MatchHistoryEntry HistoryEntry(MatchRow m) => new()
    {
        Id = m.Id, RoundId = m.RoundId, Ended = DateTimeOffset.FromUnixTimeSeconds(m.Time), Map = m.Map, Server = m.ServerName,
        Mode = m.Mode, Winner = m.Winner, Reason = m.Reason, DurationSeconds = m.DurationSeconds,
        ObjectivesTaken = m.ObjectivesTaken, ObjectivesNeeded = m.ObjectivesNeeded, Counted = m.Counted,
    };

    public MatchHistoryPage History(long account, int page)
    {
        var matches = Matches(Math.Clamp(page, 0, 100_000), 20, account, lookAhead: true);
        return new(matches.Take(20).Select(HistoryEntry).ToArray(), matches.Count > 20);
    }

    public MatchHistoryDetails? HistoryDetails(long account, long id)
    {
        lock (_gate)
        {
            var found = Match(id);
            if (found is not { } match || !match.Rows.Any(r => r.AccountId == account)) return null;
            var timeline = One("SELECT events, truncated FROM match_timelines WHERE match_id = $id;",
                r => new SavedTimeline(r.GetString(0), r.GetInt64(1) != 0), ("$id", id));
            return new(HistoryEntry(match.Match), match.Rows.Select(r => new ResultsReportRow
            {
                Name = r.Name, Team = r.Team, Score = r.Score, Kills = r.Kills, Deaths = r.Deaths, Bot = r.Bot,
                Loadout = r.Loadout, Feats = r.Feats, LeftEarly = r.LeftEarly,
            }).ToArray(), timeline is null ? null : JsonSerializer.Deserialize<MatchTimelineEvent[]>(timeline.Events, MasterApi.Json),
                timeline?.Truncated ?? false)
            {
                Scoreboard = SavedScoreboard(match.Match, match.Rows),
                Demo = AvailableHistoryDemo(match.Match, match.Rows),
            };
        }
    }

    /// <summary>Bounded cleanup of auxiliary data. Scoreboards and participant rows are retained permanently.</summary>
    public void PruneMatchHistory(int days)
    {
        lock (_gate)
        {
            long cutoff = Now - Math.Clamp(days, 1, 365) * 86400L;
            InTransaction(() =>
            {
                Exec("DELETE FROM match_timelines WHERE match_id IN (SELECT t.match_id FROM match_timelines t " +
                    "JOIN matches m ON m.id = t.match_id WHERE m.time < $cutoff ORDER BY m.time LIMIT 1000);", ("$cutoff", cutoff));
                Exec("DELETE FROM demos WHERE id IN (SELECT d.id FROM demos d JOIN matches m ON m.id = d.match_id " +
                    "WHERE m.time < $cutoff AND d.expires <= $now ORDER BY m.time LIMIT 1000);", ("$cutoff", cutoff), ("$now", Now));
                return 0;
            });
        }
    }

    private sealed record SavedTimeline(string Events, bool Truncated);
}
