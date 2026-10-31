using Godot;
using System.Linq;
using OpenScctGame.Hud;

namespace OpenScctGame.UI;

/// <summary>
/// The offline lobby: the spies and mercs of the coming match (the local player and the bots that fill the 2v2), the
/// local player's side and ready flag, and Start. The online lobby is the Lobby Room (<see cref="LobbyRoomScreen"/>).
/// </summary>
public partial class LobbyScreen : MenuPage
{
    private VBoxContainer? _spies;
    private VBoxContainer? _mercs;
    private Label? _status;
    private Label? _preparation;
    private Label? _role;

    public ILobbySession? Session { get; set; }

    public Button? SpyButton { get; private set; }

    public Button? MercButton { get; private set; }

    public CheckButton? ReadyButton { get; private set; }

    public Button? StartButton { get; private set; }

    public Button? BackButton { get; private set; }

    public override void _Ready()
    {
        BuildFrame("Lobby", 0);
        LobbyPanels.ExpandBody(Body);
        var overview = LobbyPanels.Card(Body, "MatchOverview", "Match overview");
        var pictureRow = new HBoxContainer(); overview.AddChild(pictureRow);
        var picture = new MapThumb(new Vector2(140, 120));
        picture.SetMap(MapImages.Preview(null, Session?.Map ?? ""), Session?.Map ?? "");
        pictureRow.AddChild(picture);
        _status = LobbyPanels.Text(); pictureRow.AddChild(_status);
        var columns = new LobbyColumns { Name = "Teams" }; Body.AddChild(columns);
        _spies = LobbyPanels.Card(columns, "Spies", "Spies");
        _mercs = LobbyPanels.Card(columns, "Mercs", "Mercenaries");
        var sides = new HBoxContainer(); Body.AddChild(sides);
        SpyButton = LobbyPanels.Button("Join Spies", () => Session?.SetSide(HudSide.Spy), "JoinSpies");
        MercButton = LobbyPanels.Button("Join Mercenaries", () => Session?.SetSide(HudSide.Merc), "JoinMercs");
        sides.AddChild(SpyButton); sides.AddChild(MercButton);
        var preparation = LobbyPanels.Preparation(this);
        var row = new HBoxContainer(); preparation.AddChild(row);
        _role = LobbyPanels.Text(); row.AddChild(_role);
        ReadyButton = new CheckButton { Name = "Ready", Text = "Ready" };
        ReadyButton.Toggled += on => Session?.SetReady(on); row.AddChild(ReadyButton);
        _preparation = LobbyPanels.Text(); _preparation.Modulate = UiStyle.MenuFocus; preparation.AddChild(_preparation);
        BackButton = PageButton("Leave lobby", Back, "Back");
        StartButton = PageButton("Start match", () => Session?.Start(GetTree()), "Start");
        if (Session is not null) Session.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Session is not null)
        {
            Session.Changed -= Refresh;
        }
    }

    /// <summary>Rebuilds the player lists and the button states from the session.</summary>
    public void Refresh()
    {
        if (Session is not { } session || _spies is null || _mercs is null)
        {
            return;
        }

        foreach (VBoxContainer list in new[] { _spies, _mercs })
        {
            foreach (Node child in list.GetChildren().Skip(1))
            {
                list.RemoveChild(child);
                child.QueueFree();
            }
        }

        LobbyPlayer? local = null;
        foreach (LobbyPlayer p in session.Players)
        {
            string text = p.Name + (p.IsBot ? " (bot)" : "") + (p.IsBot ? "" : p.Ready ? "   ready" : "   not ready");
            var label = LobbyPanels.Text(text + (p.IsLocal ? " (you)" : ""));
            label.Modulate = p.IsLocal ? UiStyle.SideColour(p.Side) : UiStyle.Text;
            (p.Side == HudSide.Spy ? _spies : _mercs).AddChild(label);
            if (p.IsLocal)
            {
                local = p;
            }
        }

        if (_status is not null)
        {
            _status.Text = session.Map + "\n" + session.Status;
            _role!.Text = "You: " + (local?.Side == HudSide.Spy ? "Spy" : "Mercenary");
            _preparation!.Text = session.CanStart ? "Players are ready. You can start the match." :
                local?.Ready != true ? "Select Ready when you are prepared to play." : "Waiting for players to ready up.";
            ((Label)_spies.GetChild(0)).Text = "Spies · " + session.Players.Count(p => p.Side == HudSide.Spy) + " players";
            ((Label)_mercs.GetChild(0)).Text = "Mercenaries · " + session.Players.Count(p => p.Side == HudSide.Merc) + " players";
        }

        if (ReadyButton is not null)
        {
            ReadyButton.SetPressedNoSignal(local?.Ready ?? false);
            ReadyButton.Disabled = local is null;
        }

        if (SpyButton is not null && MercButton is not null)
        {
            SpyButton.Disabled = local?.Side == HudSide.Spy;
            MercButton.Disabled = local?.Side == HudSide.Merc;
        }

        if (StartButton is not null)
        {
            StartButton.Visible = session.IsHost;
            StartButton.Disabled = !session.CanStart;
        }
    }

    private void Back()
    {
        Session?.Leave();
        FrontEnd.ShowMainMenu(GetTree());
    }
}
