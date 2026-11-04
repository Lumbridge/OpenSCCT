using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Net.Discovery;
using OpenScct.Net.Scoreboards;

namespace OpenScct.Master;

internal sealed partial class AccountApi
{
    internal static bool ValidScoreboard(ResultsReport report)
    {
        if (report.Scoreboard is not { } board) return true; // Older trusted servers still report basic results.
        return board.SchemaVersion == 1 && Guid.TryParse(board.RoundId, out _) && board.Completed && !board.Partial && !board.Practice &&
            board.EndedUtc is { } ended && ended >= board.StartedUtc &&
            double.IsFinite(board.ElapsedSeconds) && board.ElapsedSeconds is >= 0 and <= 86400 &&
            board.AsOfSeconds is { } asOf && double.IsFinite(asOf) && asOf >= 0 && asOf <= board.ElapsedSeconds &&
            board.Map == report.Map && board.Mode == report.Mode && board.Winner == report.WinningTeam && board.Reason == report.WinningReason &&
            board.ObjectivesTaken == report.ObjectivesTaken && board.ObjectivesNeeded == report.ObjectivesNeeded &&
            board.Rows is { Count: <= 256 } rows && rows.Count == report.Rows.Count &&
            rows.All(p => p is not null && p.Name is { Length: <= 64 } && p.Team is 0 or 1 && p.StatsVersion == 1 &&
                p.Stats is { Count: <= 128 } stats && stats.All(s => s.Key.Length is > 0 and <= 64 && s.Value is >= 0 and <= 10000000)) &&
            rows.Select(p => p.ParticipantId).Distinct().Count() == rows.Count && RowsMatch(rows, report.Rows);
    }

    // Report rows drive awards and account membership; the rendered scoreboard must describe the same rows.
    // Names can repeat, so compare a multiset rather than keying players by their display name.
    private static bool RowsMatch(IReadOnlyList<ScoreboardPlayer> players, IReadOnlyList<ResultsReportRow> reports)
    {
        var remaining = reports.ToList();
        foreach (var player in players)
        {
            int index = remaining.FindIndex(row => row is not null && row.Name == player.Name && row.Team == player.Team &&
                row.Score == player.Score && row.Kills == player.Kills && row.Deaths == player.Deaths && row.Bot == player.Bot &&
                row.LeftEarly == player.LeftEarly && row.Feats is { } feats && feats.Count == player.Stats!.Count &&
                feats.All(stat => player.Stats.TryGetValue(stat.Key, out int value) && value == stat.Value));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        return true;
    }
}
