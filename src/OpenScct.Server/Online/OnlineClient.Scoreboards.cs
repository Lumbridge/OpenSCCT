using OpenScct.Net.Scoreboards;

namespace OpenScct.Server.Online;

public sealed partial class OnlineClient
{
    private void OnScoreboard(RoundScoreboard scoreboard)
    {
        if (scoreboard.EndedUtc is null) return;
        LastScoreboard = scoreboard;
        LastScoreboardLocalParticipantId = Session?.ScoreboardLocalParticipantId ?? -1;
        if (Results?.Scoreboard?.RoundId == scoreboard.RoundId)
            Results = Results with { Scoreboard = scoreboard };
        Changed?.Invoke();
    }

    private void OnHistoricalScoreboard(RoundScoreboard scoreboard)
    {
        if (LastScoreboard?.RoundId != scoreboard.RoundId) LastScoreboardLocalParticipantId = -1;
        LastScoreboard = scoreboard;
        Changed?.Invoke();
    }
}
