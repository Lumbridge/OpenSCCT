using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Net.Scoreboards;
using OpenScct.Net.Cosmetics;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Passive scoreboard input and centered solo, 2v2 and 24-player presentation, with isolated snapshots.</summary>
public sealed class ScoreboardSmoke : SmokeTest
{
    public const string TestName = "scoreboard";
    public override string Name => TestName;
    private readonly List<string> _problems = [];
    private static readonly Vector2I[] Sizes = [new(800, 600), new(1280, 720), new(1920, 1080), new(3840, 2160)];
    private CanvasLayer? _layer;
    private ScoreboardOverlay? _overlay;
    private PlayInput? _input;
    private HudCommands? _commands;
    private BindingInput? _ownedBinding;
    private KeyLayout? _savedLayout;
    private int _step, _frames;
    private float _fullRosterHeight;
    private const int Cases = 6;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_layer is null)
        {
            Setup(context);
            InputChecks(context);
            Present(context);
            return SmokeResult.Running;
        }
        if (++_frames < 12) return SmokeResult.Running;
        _frames = 0;
        var view = _overlay!.View!;
        int scenario = _step % Cases;
        int players = scenario == 4 ? 1 : scenario == 5 ? 4 : 24;
        Vector2 screen = context.Tree.Root.GetVisibleRect().Size;
        Rect2 rect = view.GetGlobalRect();
        Expect(rect.Position.X >= 0 && rect.Position.Y >= 0 && rect.End.X <= screen.X + 1 && rect.End.Y <= screen.Y + 1,
            $"{players}-player scoreboard fits {screen}: {rect}");
        Rect2 panel = ((Control)view.GetParent()).GetGlobalRect();
        Expect(panel.Position.Y >= 0 && panel.End.Y <= screen.Y + 1, $"scoreboard frame fits {screen}: {panel}");
        Expect(Mathf.Abs(panel.GetCenter().X - screen.X / 2) <= 2 && Mathf.Abs(panel.GetCenter().Y - screen.Y / 2) <= 2,
            $"{players}-player frame is centered at {screen}: {panel}");
        if (screen.X > 1280)
            Expect(panel.Size.X <= 1200 * Mathf.Max(1, screen.Y / 1080) + 2,
                $"{players}-player frame has a bounded desktop width at {screen}: {panel}");
        if (scenario == 0) _fullRosterHeight = panel.Size.Y;
        if (scenario >= 4)
            Expect(panel.Size.Y < _fullRosterHeight * .6f,
                $"{players}-player frame shrinks naturally from the full roster at {screen}: {panel.Size.Y} vs {_fullRosterHeight}");
        string[] labels = view.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text).ToArray();
        Expect(labels.Count(t => t.StartsWith("Spy player ") || t.StartsWith("Merc player ")) == players,
            $"all {players} players have rows at {screen}");
        if (players == 24)
            Expect(labels.Any(t => t.StartsWith("Spy player 12")) && labels.Any(t => t.StartsWith("Merc player 12")), "all 24 players have rows");
        if (view.Scoreboard!.Rows.Any(p => p.Team == 0))
            Expect(labels.Contains("Cam sleep") || labels.Contains("Cam KO"), "spy rows have their own columns");
        if (view.Scoreboard.Rows.Any(p => p.Team == 1))
            Expect(labels.Contains("Damage") && labels.Contains("Mines"), "merc rows have their own columns");
        if (_step % Cases == 1) Expect(labels.Contains("Pending"), "unreleased counters display Pending");
        if (_step % Cases == 2) Expect(labels.Contains("—"), "legacy counters display unavailable");
        if (_step % Cases == 3) Expect(labels.Contains("3 earlier participants are included in final details") && !labels.Any(t => t.StartsWith("Earlier player")), "departed rows never displace the 24 active participants");
        MenuPage.Shot(view, $"scoreboard-{Sizes[_step / Cases].X}x{Sizes[_step / Cases].Y}-{_step % Cases}");
        if (++_step == Sizes.Length * Cases)
        {
            Cleanup();
            return _problems.Count == 0 ? SmokeResult.Pass("Centered solo/2v2/24-player layouts, delayed/legacy states, F10 and keyboard/mouse/controller holds passed.")
                : SmokeResult.Fail(string.Join("; ", _problems));
        }
        Present(context);
        return SmokeResult.Running;
    }

    private void Setup(SmokeContext context)
    {
        var binding = BindingInput.Current;
        if (binding is null) context.Tree.Root.AddChild(_ownedBinding = new BindingInput());
        _savedLayout = BindingInput.Current!.Layout;
        _layer = new CanvasLayer { Name = "ScoreboardSmoke", Layer = 100 };
        context.Tree.Root.AddChild(_layer);
        var layout = DefaultKeyLayout.Create();
        layout.Bind("F20", "HoldScoreboard");
        layout.Bind("MouseX1", "HoldScoreboard");
        layout.Bind("Ctrl+F21", "HoldScoreboard");
        _input = new PlayInput { Target = new LocalInput(), CapturePointer = false, RequireCapture = false,
            Bindings = InputBindings.FromLayout(layout, PlaySide.Spy) };
        _layer.AddChild(_input);
        _input.SetProcess(false);
        _overlay = new ScoreboardOverlay();
        _layer.AddChild(_overlay);
        _commands = new HudCommands { PlayInput = _input, Scoreboard = _overlay };
        _layer.AddChild(_commands);
        context.Tree.Root.GuiReleaseFocus();
    }

    private void InputChecks(SmokeContext context)
    {
        void Send(InputEvent ev)
        {
            context.Tree.Root.EmitSignal(Window.SignalName.WindowInput, ev);
            context.Tree.Root.PushInput(ev);
        }
        void Key(Key key, bool pressed, bool ctrl = false) => Send(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, CtrlPressed = ctrl });
        void Mouse(bool pressed) => Send(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1, Pressed = pressed });
        var pointer = Pointer.Mode;
        _input!.Target!.Set("bFire", true);
        Key(Godot.Key.F10, true); Key(Godot.Key.F10, false);
        Expect(_overlay!.IsOpen && _input.Enabled && _input.Target.Get("bFire") == 1 && pointer == Pointer.Mode, "toggle opens without changing gameplay or pointer capture");
        Key(Godot.Key.F10, true); Key(Godot.Key.F10, false);
        Expect(!_overlay.IsOpen, "second toggle closes");
        Key(Godot.Key.F20, true); Mouse(true); Key(Godot.Key.F20, false);
        Expect(_overlay.IsOpen, "mouse hold survives keyboard release");
        Mouse(false); Expect(!_overlay.IsOpen, "last hold release closes");
        Key(Godot.Key.F20, true); Key(Godot.Key.F10, true); Key(Godot.Key.F10, false); Key(Godot.Key.F20, false);
        Expect(_overlay.IsOpen, "toggle pins a held scoreboard");
        _overlay.Clear();
        Key(Godot.Key.Ctrl, true, true); Key(Godot.Key.F21, true, true);
        Expect(_overlay.IsOpen, "chord opens hold");
        Key(Godot.Key.Ctrl, false); Expect(!_overlay.IsOpen, "modifier release closes chord hold");
        Key(Godot.Key.F21, false);
        Key(Godot.Key.F20, true); BindingInput.Current!.ResetAll();
        Expect(!_overlay.IsOpen, "focus/capture reset releases held scoreboard");
        Key(Godot.Key.F20, false);
        _commands!.Blocked = () => true;
        Key(Godot.Key.F10, true); Key(Godot.Key.F10, false); Expect(!_overlay.IsOpen, "chat/menu blocks opening");
        _commands.Blocked = null;
        var pad = new GamepadInput { PlayInput = _input, Settings = new GameSettings() };
        pad.Settings.Pad.SpyButtons = new Dictionary<string, string> { ["A"] = "Action:scoreboard_hold", ["X"] = "Action:players" };
        pad.UpdateButtons(_input, false, new HashSet<string> { "A" }, 0);
        Expect(_overlay.IsOpen, "controller hold works without keyboard assignment");
        pad.UpdateButtons(_input, false, new HashSet<string>(), 0);
        Expect(!_overlay.IsOpen, "controller release closes");
        pad.UpdateButtons(_input, false, new HashSet<string> { "X" }, 0);
        pad.UpdateButtons(_input, false, new HashSet<string>(), 0);
        Expect(_overlay.IsOpen, "controller toggle works independently");
        _input.ClearHeld(_input.Target);
        Expect(!_overlay.IsOpen, "input reset clears toggle and holds");
        _overlay.Toggle();
        _input.Bindings = InputBindings.FromLayout(_input.Bindings!.Layout.Clone(), PlaySide.Spy);
        Expect(!_overlay.IsOpen, "rebinding clears the open board");
        pad.Free();
    }

    private void Present(SmokeContext context)
    {
        Vector2I size = Sizes[_step / Cases];
        if (DisplayServer.GetName() != "headless") { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); DisplayServer.WindowSetSize(size); }
        context.Tree.Root.Size = size;
        _overlay!.Clear();
        _overlay.Toggle();
        var board = Sample();
        if (_step % Cases is 1 or 2) board = board with { AsOfSeconds = null, Rows = board.Rows.Select(p => p with { Stats = null, StatsVersion = _step % Cases == 2 ? 0 : 1 }).ToArray() };
        if (_step % Cases == 3) board = board with { Rows = board.Rows.Select(p => p with { LeftEarly = false }).Concat(
            Enumerable.Range(24, 3).Select(i => board.Rows[0] with { ParticipantId = i, Name = "Earlier player " + i, LeftEarly = true })).ToArray() };
        if (_step % Cases == 4) board = board with { Rows = [board.Rows[12] with { Rank = new PlayerRank(10, 55, 0, 0).Pack() }] };
        if (_step % Cases == 5) board = board with { Rows = board.Rows.Where(p => p.ParticipantId is 0 or 1 or 12 or 13).ToArray() };
        _overlay.View!.Scoreboard = board;
        _overlay.View.LocalParticipantId = _step % Cases == 4 ? 12 : _step % 2 == 0 ? 0 : 12;
        _overlay.View.Refresh();
    }

    public static RoundScoreboard Sample() => new()
    {
        RoundId = "scoreboard-smoke", Map = "Warehouse · scoreboard preview", ElapsedSeconds = 120, AsOfSeconds = 90,
        Rows = Enumerable.Range(0, 24).Select(i => new ScoreboardPlayer
        {
            ParticipantId = i, Team = i < 12 ? 0 : 1, Name = (i < 12 ? "Spy player " : "Merc player ") + (i % 12 + 1) + " with a long name",
            Lives = 3, Kills = i, Deaths = i / 2, Objectives = i % 3, PingMs = 48, Bot = i == 4, LeftEarly = i == 22,
            Rank = i == 4 ? 0 : new PlayerRank(i % 11, 55, 0, 0).Pack(),
            Stats = FeatKeys.All.ToDictionary(k => k, _ => 123),
        }).ToArray(),
    };

    private void Expect(bool pass, string message) { if (!pass) _problems.Add(message); }
    private void Cleanup()
    {
        _overlay?.Clear();
        if (_savedLayout is not null) BindingInput.SetLayout(_savedLayout);
        _layer?.QueueFree();
        _ownedBinding?.QueueFree();
    }
}
