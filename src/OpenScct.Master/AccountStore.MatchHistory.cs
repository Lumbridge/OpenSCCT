using System;
using System.Linq;
using System.Text.Json;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    private static MatchHistoryEntry HistoryEntry(MatchRow m) => new()
    {
        Id = m.Id, Ended = DateTimeOffset.FromUnixTimeSeconds(m.Time), Map = m.Map, Server = m.ServerName,
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
            }).ToArray(), timeline is null ? null : JsonSerializer.Deserialize<MatchTimelineEvent[]>(timeline.Events, MasterApi.Json),
                timeline?.Truncated ?? false);
        }
    }

    /// <summary>Bounded cleanup of detailed records; career aggregates remain intact.</summary>
    public void PruneMatchHistory(int days)
    {
        lock (_gate)
        {
            long cutoff = Now - Math.Clamp(days, 1, 365) * 86400L;
            InTransaction(() =>
            {
                const string expired = "SELECT id FROM matches WHERE time < $cutoff ORDER BY time LIMIT 1000";
                foreach (string table in new[] { "match_timelines", "match_rows", "demos" })
                    Exec($"DELETE FROM {table} WHERE match_id IN ({expired});", ("$cutoff", cutoff));
                Exec($"DELETE FROM matches WHERE id IN ({expired});", ("$cutoff", cutoff));
                return 0;
            });
        }
    }

    private sealed record SavedTimeline(string Events, bool Truncated);
}
