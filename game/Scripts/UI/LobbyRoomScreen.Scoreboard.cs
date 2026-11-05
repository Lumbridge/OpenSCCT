using Godot;

namespace OpenScctGame.UI;

public partial class LobbyRoomScreen
{
    private VBoxContainer? _lastGame;
    private Label? _lastGameSummary;

    private void BuildLastGame(Container parent)
    {
        _lastGame = new VBoxContainer { Name = "LastGame", Visible = false };
        parent.AddChild(_lastGame);
        _lastGame.AddChild(new HSeparator());
        _lastGameSummary = LobbyPanels.Text();
        _lastGame.AddChild(_lastGameSummary);
        _lastGame.AddChild(LobbyPanels.Button("View scoreboard", () =>
        {
            if (Session?.LastScoreboard is { } board)
                RoundScoreboardDialog.Open(this, board, Session.LastScoreboardLocalParticipantId, maps: _mapCatalog);
        }, "LastGameScoreboard"));
    }

    private void RefreshLastGame()
    {
        if (_lastGame is null || _lastGameSummary is null) return;
        var board = Session?.LastScoreboard;
        _lastGame.Visible = board is not null;
        if (board is null) return;
        string outcome = board.Partial ? "Incomplete" : board.Winner == 0 ? "Spies win" : board.Winner == 1 ? "Mercenaries win" : "Round stopped";
        _lastGameSummary.Text = "Last game · " + MapTitle(board.Map) + "\n" + outcome + (board.Practice ? " · Practice" : "");
    }
}
