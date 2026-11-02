using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScctGame.UI;

namespace OpenScctGame.Play.Demo;

/// <summary>Responsive playback controls, player and camera selection, and an event timeline for demos.</summary>
public partial class DemoHud : CanvasLayer
{
    private const double HideAfterSeconds = 4.0;
    private const double SeekBudgetMs = 30.0;
    private static readonly double[] Speeds = [0.25, 0.5, 1.0, 2.0, 4.0];

    private readonly DemoPlayer _player;
    private Control? _root;
    private PanelContainer? _bar;
    private Label? _time;
    private Label? _state;
    private Label? _watching;
    private Label? _seeking;
    private Label? _preview;
    private Label? _hint;
    private Label? _help;
    private Button? _play;
    private Button? _helpButton;
    private OptionButton? _speed;
    private OptionButton? _players;
    private OptionButton? _camera;
    private Button? _previousPlayer;
    private Button? _nextPlayer;
    private DemoSeekBar? _seekBar;
    private string _roster = string.Empty;
    private double _idle;
    private bool _hidden;
    private bool _looking;

    public DemoHud(DemoPlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        Name = "DemoHud";
        Layer = 90;
    }

    public override void _Ready()
    {
        _root = new Control
        {
            Name = "DemoCanvas", Theme = UiStyle.MenuTheme, MouseFilter = Control.MouseFilterEnum.Ignore,
            OversamplingWithScale = Control.OversamplingWithScaleEnum.Enabled,
        };
        AddChild(_root);
        _bar = new PanelContainer
        {
            Name = "PlaybackBar", AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetBottom = -16f, OffsetTop = -16f, GrowVertical = Control.GrowDirection.Begin,
        };
        var panel = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.045f, 0.035f, 0.96f), BorderColor = UiStyle.MenuFocus with { A = 0.35f },
            ContentMarginLeft = 16f, ContentMarginRight = 16f, ContentMarginTop = 12f, ContentMarginBottom = 12f,
        };
        panel.SetBorderWidthAll(1);
        panel.SetCornerRadiusAll(6);
        _bar.AddThemeStyleboxOverride("panel", panel);
        _root.AddChild(_bar);
        var column = new VBoxContainer { Name = "PlaybackContent" };
        column.AddThemeConstantOverride("separation", 7);
        _bar.AddChild(column);

        var titleRow = new HBoxContainer { Name = "PlaybackHeading" };
        titleRow.AddThemeConstantOverride("separation", 14);
        column.AddChild(titleRow);
        string mode = DemoPlayer.ModeKey(_player.Header.GameOptions) switch
        {
            "DiskHunt" => "Disk Hunt", "MercDeathMatch" => "Merc Deathmatch", var name => name,
        };
        var title = Text("DemoTitle", "DEMO  /  " + _player.Header.Map + "  /  " + mode, 15);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.ClipText = true;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.TooltipText = _player.Header.Map + " · " + _player.Header.ServerName;
        titleRow.AddChild(title);
        _state = Text("PlaybackState", "PLAYING", 13);
        _state.Modulate = UiStyle.MenuFocus;
        titleRow.AddChild(_state);
        _watching = Text("Watching", string.Empty, 15);
        _watching.ClipText = true;
        _watching.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_watching);

        _seekBar = new DemoSeekBar(_player) { Name = "Timeline", CustomMinimumSize = new Vector2(0f, 30f) };
        _seekBar.Seek += Seek;
        _seekBar.PreviewChanged += text => { if (_preview is not null) _preview.Text = text; };
        column.AddChild(_seekBar);
        var times = new HBoxContainer { Name = "TimelineInfo" };
        times.AddThemeConstantOverride("separation", 16);
        column.AddChild(times);
        _time = Text("PlaybackTime", string.Empty, 14);
        times.AddChild(_time);
        _preview = Text("TimelinePreview", DemoSeekBar.DefaultPreview, 13);
        _preview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _preview.HorizontalAlignment = HorizontalAlignment.Right;
        _preview.ClipText = true;
        _preview.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _preview.Modulate = UiStyle.Dim;
        times.AddChild(_preview);

        var transport = Flow(column, "TransportControls");
        AddButton(transport, "Restart", "Restart", Restart, "Restart from the beginning (Home)");
        AddButton(transport, "BackFive", "−5 s", () => Skip(-5), "Back 5 seconds (Left); Shift + Left: 30 seconds");
        _play = AddButton(transport, "PlayPause", "Pause", TogglePause, "Play or pause (Space)", 86f);
        AddButton(transport, "ForwardFive", "+5 s", () => Skip(5), "Forward 5 seconds (Right); Shift + Right: 30 seconds");
        _speed = Choice("PlaybackSpeed", 102f, "Playback speed (Up / Down)");
        foreach (double speed in Speeds) _speed.AddItem(string.Create(CultureInfo.InvariantCulture, $"{speed:0.##}×"));
        _speed.ItemSelected += index => _player.Clock.Speed = Speeds[(int)index];
        transport.AddChild(_speed);
        _helpButton = AddButton(transport, "ControlsHelp", "Controls", ToggleHelp, "Show camera and playback shortcuts (?)");
        _helpButton.ToggleMode = true;
        AddButton(transport, "HideControls", "Hide", () => _hidden = true, "Hide all demo controls; press H to restore");
        AddButton(transport, "LeaveDemo", "Back to demos", Leave, "Return to your demo library (Escape)");

        var view = Flow(column, "ViewControls");
        _previousPlayer = AddButton(view, "PreviousPlayer", "‹", () => _player.CycleTarget(-1), "Previous player ([ or mouse wheel)", 36f);
        _players = Choice("WatchPlayer", 224f, "Choose a player to watch (1–9, [ / ] or mouse wheel)");
        _players.ItemSelected += index => { if (_players is { } picker) WatchPlayerId(picker.GetItemId((int)index)); };
        view.AddChild(_players);
        _nextPlayer = AddButton(view, "NextPlayer", "›", () => _player.CycleTarget(1), "Next player (] or mouse wheel)", 36f);
        _camera = Choice("CameraMode", 176f, "Camera perspective (C)");
        _camera.AddItem("First person");
        _camera.AddItem("Third person");
        _camera.AddItem("Free camera");
        _camera.ItemSelected += index => SelectCamera((DemoCameraMode)index);
        view.AddChild(_camera);
        var legend = new HBoxContainer { Name = "TimelineLegend", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        legend.AddThemeConstantOverride("separation", 10);
        view.AddChild(legend);
        foreach ((string text, Color color) in new[] { ("• Round", DemoSeekBar.RoundColor), ("• Death", DemoSeekBar.KillColor), ("• Event", DemoSeekBar.EventColor) })
        {
            Label item = Text("Legend", text, 12);
            item.Modulate = color;
            legend.AddChild(item);
        }

        _hint = Text("CameraHint", string.Empty, 13);
        _hint.Modulate = UiStyle.Dim;
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_hint);
        _help = Text("PlaybackHelp",
            "Space  Play / pause     ← / →  Seek 5 s (Shift: 30 s)     ↑ / ↓  Speed     Home  Restart\n" +
            "C  Camera     [ / ] or wheel  Change player     1–9  Select player     H  Hide / show controls\n" +
            "Hold right mouse to look around. Free camera: WASD move, Q / E down / up, Shift move faster.\n" +
            "Click or drag to seek; event markers include a 3 s lead-in. Backward seeks reload from the start.\n" +
            "First person shows the player's view without their weapon, HUD or vision effects.     Esc  Back to demos", 13);
        _help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _help.Visible = false;
        column.AddChild(_help);

        _seeking = Text("SeekingStatus", "Seeking…", 22);
        _seeking.HorizontalAlignment = HorizontalAlignment.Center;
        _seeking.AnchorLeft = 0.2f;
        _seeking.AnchorRight = 0.8f;
        _seeking.AnchorTop = _seeking.AnchorBottom = 0.4f;
        _seeking.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _seeking.AddThemeConstantOverride("shadow_outline_size", 6);
        _root.AddChild(_seeking);
        FitCanvas();
        GetViewport().SizeChanged += FitCanvas;
        OpenScctGame.Controls.Pointer.Mode = Input.MouseModeEnum.Visible;
        UpdateTexts();
    }

    private static Label Text(string name, string text, int size)
    {
        var label = new Label { Name = name, Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static HFlowContainer Flow(Node parent, string name)
    {
        var flow = new HFlowContainer { Name = name };
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        parent.AddChild(flow);
        return flow;
    }

    private static Button AddButton(Node parent, string name, string text, Action action, string tooltip, float width = 0f)
    {
        var button = new Button
        {
            Name = name, Text = text, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(width, 34f),
        };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private static OptionButton Choice(string name, float width, string tooltip)
    {
        var choice = new OptionButton
        {
            Name = name, CustomMinimumSize = new Vector2(width, 34f), FitToLongestItem = false,
            ClipText = true, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None,
        };
        choice.AddThemeFontSizeOverride("font_size", 14);
        return choice;
    }

    private void FitCanvas()
    {
        if (_root is null || _bar is null) return;
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float scale = Math.Max(1f, Math.Min(viewport.X / 1280f, viewport.Y / 720f));
        if (_root.Scale.X != scale)
        {
            _root.Scale = new Vector2(scale, scale);
            _root.OversamplingWithScale = Control.OversamplingWithScaleEnum.Disabled;
            _root.OversamplingWithScale = Control.OversamplingWithScaleEnum.Enabled;
        }
        _root.Size = viewport / scale;
        float width = Math.Min(1200f, _root.Size.X - 32f);
        _bar.OffsetLeft = -width / 2f;
        _bar.OffsetRight = width / 2f;
    }

    private bool PopupOpen => _players?.GetPopup().Visible == true || _speed?.GetPopup().Visible == true || _camera?.GetPopup().Visible == true;

    private bool OverControls => _bar is { Visible: true } && _bar.GetGlobalRect().HasPoint(_bar.GetGlobalMousePosition());

    public override void _Input(InputEvent @event)
    {
        _idle = 0;
        if (PopupOpen) return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right when _looking || !OverControls:
                _looking = right.Pressed;
                OpenScctGame.Controls.Pointer.Mode = _looking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                Handled();
                return;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when !OverControls:
                _player.CycleTarget(-1);
                Handled();
                return;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when !OverControls:
                _player.CycleTarget(1);
                Handled();
                return;
            case InputEventMouseMotion motion when _looking:
                _player.Turn(motion.Relative.X, motion.Relative.Y);
                Handled();
                return;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (OnKey(key)) Handled();
                return;
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMWindowFocusOut || !_looking) return;
        _looking = false;
        OpenScctGame.Controls.Pointer.Mode = Input.MouseModeEnum.Visible;
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= FitCanvas;
        if (_looking) OpenScctGame.Controls.Pointer.Mode = Input.MouseModeEnum.Visible;
    }

    private bool OnKey(InputEventKey key)
    {
        Key code = key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode;
        switch (code)
        {
            case Key.Space: TogglePause(); return true;
            case Key.Up: StepSpeed(1); return true;
            case Key.Down: StepSpeed(-1); return true;
            case Key.Left: Skip(key.ShiftPressed ? -30 : -5); return true;
            case Key.Right: Skip(key.ShiftPressed ? 30 : 5); return true;
            case Key.Home: Restart(); return true;
            case Key.End: Seek(_player.LastTick); return true;
            case Key.C: _player.CycleMode(); return true;
            case Key.Bracketleft: _player.CycleTarget(-1); return true;
            case Key.Bracketright: _player.CycleTarget(1); return true;
            case Key.H: _hidden = !_hidden; return true;
            case Key.Slash when key.ShiftPressed: ToggleHelp(); return true;
            case Key.Escape:
                if (_help is { Visible: true }) ToggleHelp();
                else Leave();
                return true;
            case >= Key.Key1 and <= Key.Key9:
                _player.WatchIndex((int)(code - Key.Key1));
                return true;
            default: return false;
        }
    }

    private void Handled() => GetViewport().SetInputAsHandled();

    public override void _Process(double delta)
    {
        _idle += delta;
        if (_player.IsSeeking)
        {
            long start = Stopwatch.GetTimestamp();
            while (_player.FastForward(30) && Stopwatch.GetElapsedTime(start).TotalMilliseconds < SeekBudgetMs) { }
        }
        if (_player.Mode == DemoCameraMode.Free && !PopupOpen && GetWindow().HasFocus())
            _player.Fly(Axis(Key.W, Key.S), Axis(Key.D, Key.A), Axis(Key.E, Key.Q), Input.IsPhysicalKeyPressed(Key.Shift), (float)delta);
        if (_player.Clock.Paused && _player.Session is { } session) session.SetViewNow(_player.ViewFor(session.View));
        UpdateTexts();
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);

    private void UpdateTexts()
    {
        bool show = !_hidden && (_idle < HideAfterSeconds || _player.Clock.Paused || _player.IsSeeking || OverControls || PopupOpen || _help is { Visible: true } || _seekBar is { Dragging: true });
        if (_bar is not null) _bar.Visible = show;
        int rate = Math.Max(1, _player.Header.TickRate);
        uint elapsed = Math.Clamp(_player.CurrentTick, _player.FirstTick, _player.LastTick) - _player.FirstTick;
        if (_time is not null) _time.Text = Clock(elapsed, rate) + " / " + Clock(_player.LastTick - _player.FirstTick, rate);
        if (_seeking is not null)
        {
            _seeking.Visible = _player.IsSeeking;
            _seeking.Text = "Seeking to " + Clock(Math.Clamp(_player.SeekTarget, _player.FirstTick, _player.LastTick) - _player.FirstTick, rate) + "…";
        }
        if (_state is not null) _state.Text = _player.IsSeeking ? "SEEKING" : _player.AtEnd ? "FINISHED" : _player.Clock.Paused ? "PAUSED" : "PLAYING";
        if (_play is not null) _play.Text = _player.AtEnd ? "Replay" : _player.Clock.Paused ? "Play" : "Pause";
        if (_speed is not null && !_speed.GetPopup().Visible) _speed.Select(Array.IndexOf(Speeds, _player.Clock.Speed));
        if (_camera is not null && !_camera.GetPopup().Visible) _camera.Select((int)_player.Mode);
        if (_watching is not null)
        {
            string who = _player.Mode == DemoCameraMode.Free ? "Free camera" : _player.Target?.PlayerReplicationInfo?.PlayerName ?? "Waiting for a player";
            _watching.Text = "WATCHING  " + who;
            _watching.TooltipText = who;
        }
        if (_hint is not null) _hint.Text = _player.Mode switch
        {
            DemoCameraMode.Free => "Hold right mouse to look · WASD fly · Q / E down / up · Shift faster",
            DemoCameraMode.ThirdPerson => "Hold right mouse to orbit · C change camera · [ / ] change player · H hide controls",
            _ => "Following the player's view · C change camera · [ / ] change player · H hide controls",
        };
        UpdatePlayers();
        _seekBar?.QueueRedraw();
        // A panel anchored at the bottom must shrink again after help closes or flow rows unwrap on resize.
        if (_bar is not null) _bar.OffsetTop = -16f - _bar.GetCombinedMinimumSize().Y;
    }

    private void UpdatePlayers()
    {
        if (_players is null || _players.GetPopup().Visible) return;
        var roster = new StringBuilder();
        foreach (SBasePawn pawn in _player.Targets)
            roster.Append(pawn.PlayerReplicationInfo?.PlayerID).Append(':').Append(pawn.PlayerReplicationInfo?.PlayerName).Append(':').Append(pawn.PlayerReplicationInfo?.Team?.TeamIndex).Append('\n');
        string signature = roster.ToString();
        if (signature != _roster || _players.ItemCount == 0)
        {
            _roster = signature;
            _players.Clear();
            for (int i = 0; i < _player.Targets.Count; i++)
            {
                SBasePawn pawn = _player.Targets[i];
                string team = pawn.PlayerReplicationInfo?.Team?.TeamIndex == 1 ? "Merc" : "Spy";
                _players.AddItem($"{i + 1}  {pawn.PlayerReplicationInfo?.PlayerName} · {team}", pawn.PlayerReplicationInfo?.PlayerID ?? i);
            }
            if (_players.ItemCount == 0) _players.AddItem("No players available");
        }
        _players.Disabled = _player.Targets.Count == 0;
        if (_previousPlayer is not null) _previousPlayer.Disabled = _player.Targets.Count < 2;
        if (_nextPlayer is not null) _nextPlayer.Disabled = _player.Targets.Count < 2;
        for (int i = 0; i < _player.Targets.Count; i++)
            if (ReferenceEquals(_player.Targets[i], _player.Target)) { _players.Select(i); break; }
    }

    internal static string Clock(uint ticks, int rate)
    {
        uint seconds = ticks / (uint)Math.Max(1, rate);
        return seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    private void ToggleHelp()
    {
        if (_help is null) return;
        _help.Visible = !_help.Visible;
        _helpButton?.SetPressedNoSignal(_help.Visible);
        _hidden = false;
    }

    private void SelectCamera(DemoCameraMode mode)
    {
        // Cycling preserves the free camera's initial position behind the watched player.
        while (_player.Mode != mode) _player.CycleMode();
    }

    private void WatchPlayerId(int id)
    {
        // A pawn may die or join while the popup is open; keep the selected identity instead of its old row index.
        for (int i = 0; i < _player.Targets.Count; i++)
            if (_player.Targets[i].PlayerReplicationInfo?.PlayerID == id) { _player.WatchIndex(i); break; }
    }

    private void TogglePause()
    {
        if (_player.AtEnd) { Restart(); return; }
        _player.Clock.Paused = !_player.Clock.Paused;
    }

    private void Restart()
    {
        DemoPlayer restart = _player.Restart(_player.FirstTick);
        if (_player.AtEnd) restart.Clock.Paused = false;
        DemoLibrary.Enter(GetTree(), restart);
    }

    private void StepSpeed(int step)
    {
        int index = Array.IndexOf(Speeds, _player.Clock.Speed);
        _player.Clock.Speed = Speeds[Math.Clamp((index < 0 ? 2 : index) + step, 0, Speeds.Length - 1)];
    }

    private void Skip(int seconds) => Seek((uint)Math.Clamp((long)_player.CurrentTick + (long)seconds * Math.Max(1, _player.Header.TickRate), _player.FirstTick, _player.LastTick));

    private void Seek(uint tick)
    {
        tick = Math.Clamp(tick, _player.FirstTick, _player.LastTick);
        if (tick >= _player.CurrentTick) _player.SeekTarget = tick;
        else DemoLibrary.Enter(GetTree(), _player.Restart(tick));
    }

    private void Leave()
    {
        OpenScctGame.Controls.Pointer.Mode = Input.MouseModeEnum.Visible;
        FrontEnd.ShowDemos(GetTree());
    }
}

/// <summary>Seek by click or drag, with event previews and a three-second lead-in when selecting a marker.</summary>
public partial class DemoSeekBar : Control
{
    internal const string DefaultPreview = "Click or drag to seek · select a marker to watch the event";
    internal static readonly Color RoundColor = new(1f, 0.8f, 0.32f);
    internal static readonly Color KillColor = new(1f, 0.42f, 0.38f);
    internal static readonly Color EventColor = new(0.48f, 0.74f, 1f);
    private readonly DemoPlayer _player;
    private float _hoverX;
    private bool _hovering;

    public DemoSeekBar(DemoPlayer player)
    {
        _player = player;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += () =>
        {
            _hovering = false;
            if (!Dragging) PreviewChanged?.Invoke(DefaultPreview);
            QueueRedraw();
        };
    }

    public event Action<uint>? Seek;
    public event Action<string>? PreviewChanged;
    public bool Dragging { get; private set; }

    public override void _Notification(int what)
    {
        if (what != NotificationWMWindowFocusOut) return;
        Dragging = false;
        _hovering = false;
        PreviewChanged?.Invoke(DefaultPreview);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (Size.X <= 0f) return;
        if (@event is InputEventMouseMotion motion)
        {
            Preview(motion.Position.X);
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            Preview(click.Position.X);
            if (click.Pressed) Dragging = true;
            else if (Dragging)
            {
                Dragging = false;
                Seek?.Invoke(TickAt(_hoverX, out _));
            }
            AcceptEvent();
        }
    }

    private void Preview(float x)
    {
        _hovering = true;
        _hoverX = Math.Clamp(x, 0f, Size.X);
        uint tick = TickAt(_hoverX, out DemoMarker? marker);
        string when = DemoHud.Clock((marker?.Tick ?? tick) - _player.FirstTick, _player.Header.TickRate);
        string description = marker is { } m ? $"{when} · {MarkerName(m.Kind)}{(string.IsNullOrWhiteSpace(m.Text) ? string.Empty : ": " + m.Text)} · 3 s lead-in" : "Seek to " + when;
        TooltipText = description;
        PreviewChanged?.Invoke(description);
        QueueRedraw();
    }

    private uint TickAt(float x, out DemoMarker? closest)
    {
        closest = null;
        float distance = 6f;
        foreach (DemoMarker marker in _player.Recording.Index.Markers)
        {
            if (marker.Tick < _player.FirstTick || marker.Tick > _player.LastTick) continue;
            float delta = Math.Abs(X(marker.Tick) - x);
            if (delta > distance) continue;
            distance = delta;
            closest = marker;
        }
        if (closest is { } selected)
        {
            uint lead = 3u * (uint)Math.Max(1, _player.Header.TickRate);
            return selected.Tick - _player.FirstTick > lead ? selected.Tick - lead : _player.FirstTick;
        }
        return _player.FirstTick + (uint)((_player.LastTick - _player.FirstTick) * Math.Clamp(x / Size.X, 0f, 1f));
    }

    private static string MarkerName(DemoMarkerKind kind) => kind switch
    {
        DemoMarkerKind.RoundStart => "Round start",
        DemoMarkerKind.RoundEnd => "Round end",
        DemoMarkerKind.Kill => "Death",
        DemoMarkerKind.Objective => "Objective",
        DemoMarkerKind.MatchEnd => "Match end",
        _ => "Event",
    };

    private float X(uint tick) => Size.X * (Math.Clamp(tick, _player.FirstTick, _player.LastTick) - _player.FirstTick) / Math.Max(1f, _player.LastTick - _player.FirstTick);

    public override void _Draw()
    {
        float middle = Size.Y / 2f;
        DrawRect(new Rect2(0f, middle - 3f, Size.X, 6f), new Color(1f, 1f, 1f, 0.16f));
        DrawRect(new Rect2(0f, middle - 3f, X(_player.CurrentTick), 6f), UiStyle.MenuFocus);
        foreach (DemoMarker marker in _player.Recording.Index.Markers)
        {
            if (marker.Tick < _player.FirstTick || marker.Tick > _player.LastTick) continue;
            Color color = marker.Kind switch
            {
                DemoMarkerKind.Kill => KillColor,
                DemoMarkerKind.RoundStart or DemoMarkerKind.RoundEnd => RoundColor,
                _ => EventColor,
            };
            DrawRect(new Rect2(X(marker.Tick) - 1.5f, middle - 10f, 3f, 20f), color);
        }
        if (_player.IsSeeking) DrawLine(new Vector2(X(_player.SeekTarget), 1f), new Vector2(X(_player.SeekTarget), Size.Y - 1f), Colors.White, 2f);
        DrawCircle(new Vector2(X(_player.CurrentTick), middle), 5f, UiStyle.MenuFocus);
        if (_hovering || Dragging)
        {
            DrawLine(new Vector2(_hoverX, 1f), new Vector2(_hoverX, Size.Y - 1f), Colors.White with { A = 0.7f }, 1f);
            DrawCircle(new Vector2(_hoverX, middle), 5f, Colors.White with { A = 0.9f }, filled: false, width: 1f);
        }
    }
}
