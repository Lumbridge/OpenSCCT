using Godot;
using OpenScctGame.Hud;

namespace OpenScctGame.UI;

/// <summary>
/// The offline lobby: the spies and mercs of the coming match (the local player and the bots that fill the 2v2), the
/// local player's side and ready flag, and Start. The online lobby is the Lobby Room (<see cref="LobbyRoomScreen"/>).
/// </summary>
public partial class LobbyScreen : Control
{
    private VBoxContainer? _spies;
    private VBoxContainer? _mercs;
    private Label? _status;

    public ILobbySession? Session { get; set; }

    public Button? SpyButton { get; private set; }

    public Button? MercButton { get; private set; }

    public CheckButton? ReadyButton { get; private set; }

    public Button? StartButton { get; private set; }

    public Button? BackButton { get; private set; }

    public override void _Ready()
    {
        Theme = UiStyle.Theme;
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(UiStyle.Backdrop());
        VBoxContainer column = UiStyle.CentreColumn(this, 760f);
        column.AddChild(UiStyle.Title("LOBBY", HudK.SpyColour));
        _status = new Label { Name = "Status", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);

        var teams = new HBoxContainer { Name = "Teams" };
        teams.AddThemeConstantOverride("separation", 40);
        _spies = Team(teams, "SPIES", "Spies", UiStyle.SideColour(HudSide.Spy));
        _mercs = Team(teams, "MERCENARIES", "Mercs", UiStyle.SideColour(HudSide.Merc));
        column.AddChild(teams);

        var sides = new HBoxContainer { Name = "Sides", Alignment = BoxContainer.AlignmentMode.Center };
        SpyButton = UiStyle.MenuButton("Join spies", () => Session?.SetSide(HudSide.Spy), "JoinSpies");
        MercButton = UiStyle.MenuButton("Join mercenaries", () => Session?.SetSide(HudSide.Merc), "JoinMercs");
        SpyButton.CustomMinimumSize = MercButton.CustomMinimumSize = new Vector2(240f, 44f);
        sides.AddChild(SpyButton);
        sides.AddChild(MercButton);
        column.AddChild(sides);

        ReadyButton = new CheckButton { Name = "Ready", Text = "Ready" };
        ReadyButton.Toggled += on => Session?.SetReady(on);
        StartButton = UiStyle.MenuButton("Start match", () => Session?.Start(GetTree()), "Start");
        BackButton = UiStyle.MenuButton("Back", Back, "Back");
        column.AddChild(ReadyButton);
        column.AddChild(StartButton);
        column.AddChild(BackButton);

        if (Session is not null)
        {
            Session.Changed += Refresh;
        }

        Refresh();
    }

    public override void _ExitTree()
    {
        if (Session is not null)
        {
            Session.Changed -= Refresh;
        }
    }

    private static VBoxContainer Team(HBoxContainer parent, string title, string name, Color colour)
    {
        var box = new VBoxContainer { Name = name, CustomMinimumSize = new Vector2(320f, 180f) };
        box.AddChild(new Label { Text = title, Modulate = colour, HorizontalAlignment = HorizontalAlignment.Center });
        parent.AddChild(box);
        var list = new VBoxContainer { Name = "Players" };
        box.AddChild(list);
        return list;
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
            foreach (Node child in list.GetChildren())
            {
                list.RemoveChild(child);
                child.QueueFree();
            }
        }

        LobbyPlayer? local = null;
        foreach (LobbyPlayer p in session.Players)
        {
            string text = p.Name + (p.IsBot ? " (bot)" : "") + (p.IsBot ? "" : p.Ready ? "   ready" : "   not ready");
            var label = new Label { Text = text, Modulate = p.IsLocal ? UiStyle.SideColour(p.Side) : UiStyle.Text };
            (p.Side == HudSide.Spy ? _spies : _mercs).AddChild(label);
            if (p.IsLocal)
            {
                local = p;
            }
        }

        if (_status is not null)
        {
            _status.Text = session.Status + " - " + session.Map;
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
