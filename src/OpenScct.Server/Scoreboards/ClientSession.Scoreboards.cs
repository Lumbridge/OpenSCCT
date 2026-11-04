using System;
using OpenScct.Net.Scoreboards;

namespace OpenScct.Server;

public sealed partial class ClientSession
{
    private readonly ScoreboardWire.Receiver _scoreboardReceiver = new();
    public RoundScoreboard? Scoreboard { get; private set; }
    public int ScoreboardLocalParticipantId { get; private set; } = -1;
    public event Action<RoundScoreboard>? ScoreboardReceived;
    /// <summary>A lobby's previous round. Receiving it never emits ScoreboardReceived or archives a played game.</summary>
    public RoundScoreboard? HistoricalScoreboard { get; private set; }
    public event Action<RoundScoreboard>? HistoricalScoreboardReceived;

    private bool ReceiveScoreboard(string? line)
    {
        if (!_scoreboardReceiver.Receive(line, out var board, out int local, out bool historical)) return false;
        if (board is null) return true;
        if (historical)
        {
            if (HistoricalScoreboard is { } old && old.RoundId == board.RoundId && old.Sequence >= board.Sequence) return true;
            HistoricalScoreboard = board;
            HistoricalScoreboardReceived?.Invoke(board);
        }
        else
        {
            if (Scoreboard is { } old && old.RoundId == board.RoundId && old.Sequence >= board.Sequence) return true;
            Scoreboard = board;
            ScoreboardLocalParticipantId = local;
            ScoreboardReceived?.Invoke(board);
        }
        return true;
    }
}
