using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScctGame.Play.Demo;

/// <summary>
/// The playback bar of a demo (docs/DEMOS.md, "Watching"): play/pause, speed, time, a seek bar with the round and
/// death markers, the players to watch and the camera mode, plus the keys:
/// <list type="bullet">
/// <item>Space pause, Up/Down speed, Left/Right 5 s back/on (Shift: 30 s), click the bar to jump.</item>
/// <item>C first person / third person / free camera, [ and ] or the mouse wheel the previous/next player,
/// 1 to 9 a player.</item>
/// <item>Hold the right mouse button to look round (third person and free camera); WASD fly, Q/E down/up, Shift
/// faster (free camera).</item>
/// <item>H hides the bar, Escape leaves.</item>
/// </list>
/// </summary>
public partial class DemoHud : CanvasLayer
{
    private const double HideAfterSeconds = 3.0;
    private const double SeekBudgetMs = 30.0;

    private static readonly double[] Speeds = [0.25, 0.5, 1.0, 2.0, 4.0];

    private readonly DemoPlayer _player;
    private PanelContainer? _bar;
    private Label? _time;
    private Label? _speed;
    private Label? _watching;
    private Label? _players;
    private Label? _seeking;
    private DemoSeekBar? _seekBar;
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
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _watching = new Label { Position = new Vector2(16f, 12f) };
        _watching.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
        root.AddChild(_watching);

        _players = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _players.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _players.Position = new Vector2(-260f, 12f);
        _players.Size = new Vector2(244f, 300f);
        _players.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
        root.AddChild(_players);

        _seeking = new Label { Text = "Seeking...", Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        _seeking.SetAnchorsPreset(Control.LayoutPreset.Center);
        _seeking.AddThemeFontSizeOverride("font_size", 28);
        root.AddChild(_seeking);

        _bar = new PanelContainer();
        _bar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _bar.OffsetTop = -64f;
        _bar.OffsetLeft = 16f;
        _bar.OffsetRight = -16f;
        _bar.OffsetBottom = -12f;
        var style = new StyleBoxFlat { BgColor = new Color(0.05f, 0.07f, 0.06f, 0.82f) };
        style.SetCornerRadiusAll(6);
        style.ContentMarginLeft = style.ContentMarginRight = 12f;
        style.ContentMarginTop = style.ContentMarginBottom = 6f;
        _bar.AddThemeStyleboxOverride("panel", style);
        root.AddChild(_bar);

        var column = new VBoxContainer();
        _bar.AddChild(column);
        _seekBar = new DemoSeekBar(_player) { CustomMinimumSize = new Vector2(0f, 18f) };
        _seekBar.Seek += Seek;
        column.AddChild(_seekBar);

        var row = new HBoxContainer();
        column.AddChild(row);
        var play = new Button { Text = "Play / Pause", FocusMode = Control.FocusModeEnum.None };
        play.Pressed += TogglePause;
        row.AddChild(play);
        var slower = new Button { Text = "-", FocusMode = Control.FocusModeEnum.None };
        slower.Pressed += () => StepSpeed(-1);
        row.AddChild(slower);
        _speed = new Label();
        row.AddChild(_speed);
        var faster = new Button { Text = "+", FocusMode = Control.FocusModeEnum.None };
        faster.Pressed += () => StepSpeed(1);
        row.AddChild(faster);
        _time = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center };
        row.AddChild(_time);
        var camera = new Button { Text = "Camera (C)", FocusMode = Control.FocusModeEnum.None };
        camera.Pressed += _player.CycleMode;
        row.AddChild(camera);
        var leave = new Button { Text = "Leave", FocusMode = Control.FocusModeEnum.None };
        leave.Pressed += Leave;
        row.AddChild(leave);

        OpenScctGame.Controls.Pointer.Mode = Input.MouseModeEnum.Visible;
    }

    public override void _Input(InputEvent @event)
    {
        _idle = 0;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                _looking = right.Pressed;
                OpenScctGame.Controls.Pointer.Mode = _looking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                Handled();
                return;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _player.CycleTarget(-1);
                Handled();
                return;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _player.CycleTarget(1);
                Handled();
                return;
            case InputEventMouseMotion motion when _looking:
                _player.Turn(motion.Relative.X, motion.Relative.Y);
                Handled();
                return;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (OnKey(key))
                {
                    Handled();
                }

                return;
        }
    }

    private bool OnKey(InputEventKey key)
    {
        uint seconds = key.ShiftPressed ? 30u : 5u;
        uint step = seconds * (uint)Math.Max(1, _player.Header.TickRate);
        switch (key.PhysicalKeycode)
        {
            case Key.Space:
                TogglePause();
                return true;
            case Key.Up:
                StepSpeed(1);
                return true;
            case Key.Down:
                StepSpeed(-1);
                return true;
            case Key.Left:
                Seek(_player.CurrentTick > step ? _player.CurrentTick - step : 0);
                return true;
            case Key.Right:
                Seek(_player.CurrentTick + step);
                return true;
            case Key.C:
                _player.CycleMode();
                return true;
            case Key.Bracketleft:
                _player.CycleTarget(-1);
                return true;
            case Key.Bracketright:
                _player.CycleTarget(1);
                return true;
            case Key.H:
                _hidden = !_hidden;
                return true;
            case Key.Escape:
                Leave();
                return true;
            case >= Key.Key1 and <= Key.Key9:
                _player.WatchIndex((int)(key.PhysicalKeycode - Key.Key1));
                return true;
            default:
                return false;
        }
    }

    private void Handled() => GetViewport().SetInputAsHandled();

    public override void _Process(double delta)
    {
        _idle += delta;
        if (_player.IsSeeking)
        {
            long start = Stopwatch.GetTimestamp();
            while (_player.FastForward(30) && Stopwatch.GetElapsedTime(start).TotalMilliseconds < SeekBudgetMs)
            {
            }
        }

        if (_player.Mode == DemoCameraMode.Free)
        {
            float forward = Axis(Key.W, Key.S);
            float right = Axis(Key.D, Key.A);
            float up = Axis(Key.E, Key.Q);
            _player.Fly(forward, right, up, Input.IsPhysicalKeyPressed(Key.Shift), (float)delta);
        }

        // Paused, nothing ticks: the camera still moves.
        if (_player.Clock.Paused && _player.Session is { } session)
        {
            session.SetViewNow(_player.ViewFor(session.View));
        }

        UpdateTexts();
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(negative) ? 1f : 0f);

    private void UpdateTexts()
    {
        bool show = !_hidden && (_idle < HideAfterSeconds || _player.Clock.Paused || _player.IsSeeking);
        if (_bar is not null)
        {
            _bar.Visible = show;
        }

        if (_seeking is not null)
        {
            _seeking.Visible = _player.IsSeeking;
        }

        int rate = Math.Max(1, _player.Header.TickRate);
        if (_time is not null)
        {
            string state = _player.AtEnd ? "  (end)" : _player.Clock.Paused ? "  (paused)" : string.Empty;
            _time.Text = Clock(_player.CurrentTick - _player.FirstTick, rate) + " / " + Clock(_player.LastTick - _player.FirstTick, rate) + state;
        }

        if (_speed is not null)
        {
            _speed.Text = string.Create(CultureInfo.InvariantCulture, $" {_player.Clock.Speed:0.##}x ");
        }

        if (_watching is not null)
        {
            string mode = _player.Mode switch
            {
                DemoCameraMode.FirstPerson => "First person",
                DemoCameraMode.ThirdPerson => "Third person",
                _ => "Free camera",
            };
            string who = _player.Mode != DemoCameraMode.Free && _player.Target?.PlayerReplicationInfo is { } pri ? pri.PlayerName + "  -  " : string.Empty;
            _watching.Text = $"DEMO  {_player.Header.Map}  {who}{mode}";
            _watching.Visible = show;
        }

        if (_players is not null)
        {
            var text = new StringBuilder();
            for (int i = 0; i < _player.Targets.Count; i++)
            {
                SBasePawn pawn = _player.Targets[i];
                string marker = ReferenceEquals(pawn, _player.Target) ? "> " : "  ";
                string team = pawn.PlayerReplicationInfo?.Team?.TeamIndex == 1 ? "Merc" : "Spy";
                text.Append(marker).Append(i + 1).Append("  ").Append(pawn.PlayerReplicationInfo?.PlayerName).Append("  (").Append(team).Append(")\n");
            }

            _players.Text = text.ToString();
            _players.Visible = show;
        }

        _seekBar?.QueueRedraw();
    }

    private static string Clock(uint ticks, int rate)
    {
        int seconds = (int)(ticks / (uint)rate);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    private void TogglePause()
    {
        if (_player.AtEnd)
        {
            return;
        }

        _player.Clock.Paused = !_player.Clock.Paused;
    }

    private void StepSpeed(int step)
    {
        int index = Array.IndexOf(Speeds, _player.Clock.Speed);
        index = Math.Clamp((index < 0 ? 2 : index) + step, 0, Speeds.Length - 1);
        _player.Clock.Speed = Speeds[index];
    }

    /// <summary>Jumps to <paramref name="tick"/>: forward by fast-forwarding, back by starting again from the beginning.</summary>
    private void Seek(uint tick)
    {
        tick = Math.Clamp(tick, _player.FirstTick, _player.LastTick);
        if (tick >= _player.CurrentTick)
        {
            _player.SeekTarget = tick;
            return;
        }

        DemoLibrary.Enter(GetTree(), _player.Restart(tick));
    }

    private void Leave()
    {
        OpenScctGame.Controls.Pointer.Mode = Input.MouseModeEnum.Visible;
        UI.FrontEnd.ShowMainMenu(GetTree());
    }
}

/// <summary>The seek bar: the played part, the markers (rounds, deaths) and the click to jump.</summary>
public partial class DemoSeekBar : Control
{
    private readonly DemoPlayer _player;

    public DemoSeekBar(DemoPlayer player)
    {
        _player = player;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>Raised with the server tick the user clicked.</summary>
    public event Action<uint>? Seek;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click && Size.X > 0f)
        {
            float t = Math.Clamp(click.Position.X / Size.X, 0f, 1f);
            uint length = _player.LastTick - _player.FirstTick;
            uint tick = _player.FirstTick + (uint)(length * t);

            // A click on a marker jumps a few seconds before it.
            foreach (DemoMarker marker in _player.Recording.Index.Markers)
            {
                if (Math.Abs(X(marker.Tick) - click.Position.X) <= 4f)
                {
                    uint lead = 3u * (uint)Math.Max(1, _player.Header.TickRate);
                    tick = marker.Tick > _player.FirstTick + lead ? marker.Tick - lead : _player.FirstTick;
                    break;
                }
            }

            Seek?.Invoke(tick);
            AcceptEvent();
        }
    }

    private float X(uint tick)
    {
        uint length = Math.Max(1u, _player.LastTick - _player.FirstTick);
        return Size.X * Math.Clamp((tick - _player.FirstTick) / (float)length, 0f, 1f);
    }

    public override void _Draw()
    {
        float h = Size.Y;
        DrawRect(new Rect2(0f, h * 0.35f, Size.X, h * 0.3f), new Color(1f, 1f, 1f, 0.18f));
        DrawRect(new Rect2(0f, h * 0.35f, X(_player.CurrentTick), h * 0.3f), new Color(0.55f, 0.85f, 0.45f, 0.9f));
        if (_player.IsSeeking)
        {
            DrawRect(new Rect2(X(_player.SeekTarget) - 1f, 0f, 2f, h), new Color(1f, 1f, 1f, 0.7f));
        }

        foreach (DemoMarker marker in _player.Recording.Index.Markers)
        {
            Color color = marker.Kind switch
            {
                DemoMarkerKind.Kill => new Color(0.95f, 0.3f, 0.25f),
                DemoMarkerKind.RoundStart or DemoMarkerKind.RoundEnd => new Color(1f, 0.85f, 0.3f),
                _ => new Color(0.6f, 0.75f, 1f),
            };
            DrawRect(new Rect2(X(marker.Tick) - 1f, 0f, 2f, h), color);
        }
    }
}
