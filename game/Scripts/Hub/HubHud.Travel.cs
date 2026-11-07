using Godot;

namespace OpenScctGame.Hub;

public partial class HubHud
{
    private Control? _travel;
    private Button? _travelButton;

    private void ReadyTravel()
    {
        var button = _travelButton = new Button { Name = "HubTravelButton", Text = "Travel", Position = new Vector2(16, 156), CustomMinimumSize = new Vector2(144, 36) };
        button.Pressed += OpenTravel;
        AddChild(button);
        if (Client is { } client) client.Play.TravelMenuOpened += OpenTravel;
    }

    private void OpenTravel()
    {
        if (_travel is not null || _armoury is not null || _laptop is not null || Typing || Client is not { } client) return;
        if (_playerList is not null) TogglePlayerList(client);
        CloseEmoteMenu();
        var overlay = new Control { Name = "HubTravel", ZIndex = 100, MouseFilter = Control.MouseFilterEnum.Stop };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _travel = overlay;
        AddChild(overlay);
        var shade = new ColorRect { Color = new Color(0.01f, 0.025f, 0.045f, 0.93f) };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(shade);
        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(centre);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) };
        column.AddThemeConstantOverride("separation", 10);
        centre.AddChild(column);
        column.AddChild(new Label { Text = "TRAVEL THE HUB", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(new Label
        {
            Text = client.Play.Running || client.Play.InRound || client.Queue.PadId is not null
                ? "Travelling cancels your current activity and leaves any match queue."
                : "Choose a district. All destinations are available.",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        foreach (var (id, title) in client.Play.Destinations)
        {
            string caption = client.Play.PersonalBests.TryGetValue(id, out int best) ? title + "   ·   Best " + OpenScct.Server.Hub.HubScores.Time(best) : title;
            var destination = new Button { Name = "Travel_" + id, Text = caption, CustomMinimumSize = new Vector2(600, 42) };
            destination.Pressed += () => { client.Travel(id); CloseTravel(); };
            column.AddChild(destination);
        }
        if (client.Play.Destinations.Count == 0) column.AddChild(new Label { Text = "Travel destinations are not available on this hub." });
        var close = new Button { Text = $"Back  [{KeyHint("ShowMenu")}]", CustomMinimumSize = new Vector2(600, 36) };
        close.Pressed += CloseTravel;
        column.AddChild(close);
        close.GrabFocus();
        if (Round?.Controls is { } input) { input.ReleasePointer(); input.Enabled = false; }
    }

    private void CloseTravel()
    {
        _travel?.QueueFree();
        _travel = null;
        if (Round?.Controls is { } input && !Typing) input.ResumePointer();
    }

    public override void _Input(InputEvent @event)
    {
        if (!HubModalActive || !@event.IsPressed() || @event is InputEventKey { Echo: true }) return;
        string? command = HubBound(@event, "ShowMenu") ? "ShowMenu" : HubCommandOf(@event);
        if (command is not null && CloseOverlayCommand(command)) GetViewport().SetInputAsHandled();
    }
}
