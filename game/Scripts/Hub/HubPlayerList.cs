using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Server.Hub;
using OpenScctGame.Hud;
using OpenScctGame.UI;

namespace OpenScctGame.Hub;

/// <summary>
/// The hub's player list (P, an OpenSCCT addition): every player in the hub by side, signed in or a guest, with his
/// ping, and for the others a voice volume, a voice mute and a chat mute. Volume and mute are this game's own (the
/// visit's <see cref="HubVoice"/>, kept by name for the visit); nothing is sent to the hub but the voice relay's
/// muted set. Built in code like the game's other overlays; the rows refresh as the hub's <c>hub who</c> lines come in.
/// </summary>
public partial class HubPlayerList : Control
{
    private readonly Dictionary<int, Row> _rows = new();
    private VBoxContainer? _spies;
    private VBoxContainer? _mercs;
    private double _refresh;

    public HubClient? Client { get; set; }

    public HubVoice? Voice { get; set; }

    /// <summary>Close (or P again) was pressed.</summary>
    public event Action? Closed;

    private sealed class Row
    {
        public required HBoxContainer Line { get; init; }

        public required Label Name { get; init; }

        public required Label Ping { get; init; }

        public int Team { get; set; } = -1;
    }

    public override void _Ready()
    {
        Name = "HubPlayerList";
        Theme = UiStyle.Theme;
        // The offsets too: SetAnchorsPreset alone keeps the control's zero size, which left the list a sliver in the
        // top left corner with no room for its rows.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1f, AnchorBottom = 1f });

        var panel = new PanelContainer { AnchorLeft = 0.1f, AnchorRight = 0.9f, AnchorTop = 0.1f, AnchorBottom = 0.9f };
        AddChild(panel);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        panel.AddChild(outer);

        var head = new HBoxContainer();
        head.AddChild(new Label { Text = "Players in the hub", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var close = new Button { Text = "Close (P)" };
        close.Pressed += () => Closed?.Invoke();
        head.AddChild(close);
        outer.AddChild(head);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        outer.AddChild(scroll);
        var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24);
        scroll.AddChild(columns);
        _spies = Column(columns, "SPIES", HudK.SpyColour);
        _mercs = Column(columns, "MERCS", HudK.MercColour);
        Refresh();
    }

    private static VBoxContainer Column(HBoxContainer parent, string title, Color colour)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        var label = new Label { Text = title };
        label.AddThemeColorOverride("font_color", colour);
        column.AddChild(label);
        parent.AddChild(column);
        return column;
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh <= 0)
        {
            _refresh = 1.0;
            Refresh();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.P or Key.Escape)
        {
            Closed?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Refresh()
    {
        if (Client is not { } client || _spies is null || _mercs is null)
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (HubRosterEntry p in client.Roster)
        {
            seen.Add(p.PlayerId);
            if (!_rows.TryGetValue(p.PlayerId, out Row? row))
            {
                row = MakeRow(client, p);
                _rows[p.PlayerId] = row;
            }

            if (row.Team != p.Team)
            {
                row.Line.GetParent()?.RemoveChild(row.Line);
                (p.Team == 1 ? _mercs : _spies).AddChild(row.Line);
                row.Team = p.Team;
            }

            bool me = p.PlayerId == client.PlayerId;
            row.Name.Text = p.Name + (p.SignedIn ? "  ✓" : "  (guest)") + (me ? "  (you)" : string.Empty);
            row.Ping.Text = string.Create(CultureInfo.InvariantCulture, $"{p.PingMs} ms");
        }

        foreach (int id in new List<int>(_rows.Keys))
        {
            if (!seen.Contains(id))
            {
                _rows[id].Line.QueueFree();
                _rows.Remove(id);
            }
        }
    }

    private Row MakeRow(HubClient client, HubRosterEntry p)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 6);
        var name = new Label { CustomMinimumSize = new Vector2(160f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "✓ signed in" };
        var ping = new Label { CustomMinimumSize = new Vector2(56f, 0f) };
        line.AddChild(name);
        line.AddChild(ping);
        if (p.PlayerId != client.PlayerId)
        {
            string player = p.Name;
            if (Voice is { } voice)
            {
                var volume = new HSlider
                {
                    MinValue = 0, MaxValue = 200, Step = 5, Value = voice.VolumeOf(player) * 100f,
                    CustomMinimumSize = new Vector2(90f, 20f), TooltipText = "This player's voice volume",
                };
                var volumeText = new Label { CustomMinimumSize = new Vector2(44f, 0f) };
                void Apply(double v)
                {
                    voice.SetVolume(player, (float)v / 100f);
                    volumeText.Text = string.Create(CultureInfo.InvariantCulture, $"{v:0}%");
                }

                // A click on the track moves the grabber without value_changed (Godot's Slider blocks it on press):
                // the value is taken when the press ends too (as the lobby's rows do).
                volume.ValueChanged += Apply;
                volume.DragEnded += _ => Apply(volume.Value);
                volumeText.Text = string.Create(CultureInfo.InvariantCulture, $"{volume.Value:0}%");
                var mute = new CheckBox { Text = "Mute voice", ButtonPressed = voice.IsMuted(player) };
                mute.Toggled += on => voice.SetMuted(player, on);
                line.AddChild(volume);
                line.AddChild(volumeText);
                line.AddChild(mute);
            }

            var chatMute = new CheckBox { Text = "Mute chat", ButtonPressed = client.IsChatMuted(player) };
            chatMute.Toggled += on => client.MuteChat(player, on);
            line.AddChild(chatMute);
        }

        return new Row { Line = line, Name = name, Ping = ping };
    }
}
