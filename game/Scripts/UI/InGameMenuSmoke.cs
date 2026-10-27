using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Smoke;

namespace OpenScctGame.UI;

/// <summary>
/// <c>--svm-smoke=play-menu</c>: the In-Game Menu as a player meets it (spec/hud/in-game-menu.md). SteeE as the spy
/// with the launcher's settings (Play Level rules, three bots, the listen server on its own thread, wall clock, HUD
/// on); every key and click is a Godot input event of the player's bound keys, so it goes through the viewport,
/// <see cref="PlayInput"/> and the menu exactly as a keyboard and mouse do. Nothing is written to the session or the
/// simulation. It checks: walking forward with the key bound to BT_Up; Escape (the ShowMenu key) opens a visible
/// "In-Game Menu" with the offline buttons; the pawn's input is released while it is open and the pointer is free;
/// the match is not paused; keys are ignored for the first half second and taken while open (F10 does nothing);
/// Escape closes it, the pointer is taken again and the keys move the pawn again; F10 toggles the connected players
/// list; Options opens the options over the game and Escape comes back to the menu; Quit Session asks with the
/// server's question and Cancel comes back; Back closes; Quit Session, End session leaves for the main menu.
/// With a window and <c>SVM_PLAY_SHOTS</c> naming a folder (keep it under local/), saves a screenshot at each step.
/// </summary>
public sealed class InGameMenuSmoke : SmokeTest
{
    public const string TestName = "play-menu";

    /// <summary>The group a visible pause menu joins (docs/TESTING-PLAY.md, the inspector's PauseMenu check).</summary>
    public const string MenuGroup = "svm_pause_menu";

    /// <summary>A key no layout binds: sent first to learn whether parsed events reach the scene.</summary>
    private const Key MarkerKey = Key.F24;

    private const int TeardownFrames = 6;

    /// <summary>Speed (uu/s) over which the pawn counts as walking, under which as stopped. Test thresholds.</summary>
    private const float WalkSpeed = 60f;

    private const float StopSpeed = 15f;

    private readonly List<string> _log = [];
    private readonly List<string> _failures = [];
    private PlayRoot? _root;
    private InGameMenuProbe? _probe;
    private bool _push;
    private int _phase;
    private double _phaseStart;
    private double _now;
    private long _ticksAtOpen;
    private float _timeAtOpen;
    private float _maxSpeed;
    private bool _scoreboardBefore;
    private int _teardown = -1;
    private SmokeResult _final;
    private string _menuKey = "Escape";
    private string _upKey = "W";
    private string _playersKey = "F10";
    private readonly List<(string Ik, double At)> _releases = [];

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _now = context.ElapsedSeconds;
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            if (DisplayServer.GetName() == "headless")
            {
                // Without a window the root viewport is 64 x 64 and the menu's buttons would lie outside it; give it
                // the project's window size so the clicks land as on a screen.
                context.Tree.Root.Size = new Vector2I(1280, 720);
            }

            var settings = new PlaySettings
            {
                Map = "SteeE",
                Side = PlaySide.Spy,
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Thread,
                MapRoots = [.. context.App.Options.MapRoots],
                Bots = 3,
                Difficulty = "normal",
            };
            _root = PlayRoot.Enter(context.Tree, new PlayConfig { Settings = settings, GameRoot = context.App.Options.GameRoot });
            Begin(0);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (_phase < 100 && (_root.Session?.ServerFailure is { } failure))
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        ReleaseDue();
        try
        {
            return Run(context) ?? SmokeResult.Running;
        }
        catch (InvalidOperationException ex)
        {
            return Finish(SmokeResult.Fail(ex.Message));
        }
    }

    private SmokeResult? Run(SmokeContext context)
    {
        PlayRoot root = _root!;
        double t = _now - _phaseStart;
        PlaySession? session = root.Session;
        SBasePawn? pawn = session?.LocalPawn;
        bool headless = DisplayServer.GetName() == "headless";
        switch (_phase)
        {
            case 0:
                // Loaded, spawned and the HUD attached.
                if (!root.IsLoaded || pawn is null || root.Controls?.Bindings is null || (!headless && root.Hud is null) || t < 2.0)
                {
                    return t > 150 ? Finish(SmokeResult.Fail("no local pawn after 150 s")) : null;
                }

                InputBindings bindings = root.Controls.Bindings;
                _menuKey = HudCommands.KeyOf(bindings, "ShowMenu") ?? "Escape";
                _upKey = HudCommands.KeyOf(bindings, "BT_Up") ?? "W";
                _playersKey = HudCommands.KeyOf(bindings, "ShowConnectedPlayers") ?? "F10";
                Note($"keys: ShowMenu={_menuKey} BT_Up={_upKey} ShowConnectedPlayers={_playersKey}");
                root.Controls.Enabled = true;
                _probe = new InGameMenuProbe { MarkerKey = MarkerKey };
                root.AddChild(_probe);
                Send(new InputEventKey { Keycode = MarkerKey, PhysicalKeycode = MarkerKey, Pressed = true });
                Send(new InputEventKey { Keycode = MarkerKey, PhysicalKeycode = MarkerKey, Pressed = false });
                Begin(1);
                return null;
            case 1:
                if (_probe!.Marker == 0 && t < 0.3)
                {
                    return null;
                }

                _push = _probe.Marker == 0;
                Note(_push ? "delivery: Viewport.PushInput (parsed events did not reach the scene)" : "delivery: Input.ParseInputEvent");
                if (!headless)
                {
                    // A player clicks into the window to take the pointer.
                    Click(root.GetViewport().GetVisibleRect().Size * 0.5f);
                }

                Begin(2);
                return null;
            case 2:
                if (t < 0.3)
                {
                    return null;
                }

                if (!root.Controls!.IsCaptured)
                {
                    AssumeCaptured(root.Controls);
                    Note(headless ? "pointer: assumed captured (no window)" : "pointer: the click did not capture it (no focus); assumed captured");
                }

                Shot(context, "start");
                KeyEvent(_upKey, true);
                _maxSpeed = 0f;
                Begin(3);
                return null;
            case 3:
                _maxSpeed = MathF.Max(_maxSpeed, Speed(pawn));
                if (t < 1.5)
                {
                    return null;
                }

                Expect(session!.Input.Get("BT_Up") == 1, $"holding {_upKey} does not reach the session's BT_Up");
                Expect(_maxSpeed > WalkSpeed, $"holding {_upKey} did not move the pawn (max speed {_maxSpeed:F0} uu/s)");
                Shot(context, "walking");

                // Escape with the forward key still held.
                Tap(_menuKey);
                _ticksAtOpen = root.Stats.Ticks;
                _timeAtOpen = session.Client.World.Level.Info.TimeSeconds;
                Begin(4);
                return null;
            case 4:
                if (t < 0.15)
                {
                    return null;
                }

                if (!MenuVisible(root))
                {
                    return Finish(Report("Escape did not open a visible in-match menu (no visible node in group " + MenuGroup + ")"));
                }

                ExpectText(root, "In-Game Menu", true);
                foreach (string b in new[] { "Back", "Strategic Map", "Options", "Quit Session" })
                {
                    Expect(FindButton(root, b) is not null, $"menu button '{b}' not shown");
                }

                Expect(FindButton(root, "Return to Lobby") is null, "offline menu shows Return to Lobby (the original hides it with NET_NONE)");
                Expect(session!.Input.Get("BT_Up") == 0, $"the pawn's BT_Up is still down with the menu open ({_upKey} held)");
                if (!headless)
                {
                    Expect(OpenScctGame.Controls.Pointer.Mode == Input.MouseModeEnum.Visible, $"pointer not free with the menu open ({OpenScctGame.Controls.Pointer.Mode})");
                }

                // Escape within the first half second is ignored.
                Tap(_menuKey);
                Begin(5);
                return null;
            case 5:
                if (t < 0.25)
                {
                    return null;
                }

                Expect(MenuVisible(root), "Escape in the menu's first half second closed it (the original ignores keys for 0.5 s)");
                Shot(context, "menu-open");
                _scoreboardBefore = Scoreboard(session);
                Tap(_playersKey);
                Begin(6);
                return null;
            case 6:
                if (t < 1.2)
                {
                    return null;
                }

                Expect(Scoreboard(session) == _scoreboardBefore, $"{_playersKey} reached the HUD while the menu was open");
                Expect(Speed(pawn) < StopSpeed, $"the pawn still moves with the menu open ({Speed(pawn):F0} uu/s)");
                long ticks = root.Stats.Ticks - _ticksAtOpen;
                float seconds = session!.Client.World.Level.Info.TimeSeconds - _timeAtOpen;
                Expect(ticks > 60 && seconds > 1f, $"the match paused behind the menu ({ticks} ticks, {seconds:F2} s of level time)");
                Note(string.Create(CultureInfo.InvariantCulture, $"menu open: {ticks} client ticks, {seconds:F2} s of level time, pawn speed {Speed(pawn):F0}"));
                KeyEvent(_upKey, false);
                Tap(_menuKey);
                Begin(7);
                return null;
            case 7:
                if (t < 0.2)
                {
                    return null;
                }

                Expect(!MenuVisible(root), "Escape did not close the menu");
                Expect(root.Controls!.Enabled, "the player's keys stay off after the menu closed");
                if (!headless && CapturedBefore)
                {
                    Expect(OpenScctGame.Controls.Pointer.Mode == Input.MouseModeEnum.Captured, $"pointer not taken again after the menu closed ({OpenScctGame.Controls.Pointer.Mode})");
                }

                AssumeCapturedIfNeeded(root.Controls);
                KeyEvent(_upKey, true);
                _maxSpeed = 0f;
                Begin(8);
                return null;
            case 8:
                _maxSpeed = MathF.Max(_maxSpeed, Speed(pawn));
                if (t < 1.2)
                {
                    return null;
                }

                KeyEvent(_upKey, false);
                Expect(_maxSpeed > WalkSpeed, $"{_upKey} did not move the pawn after the menu closed (max {_maxSpeed:F0} uu/s)");
                _scoreboardBefore = Scoreboard(session);
                Tap(_playersKey);
                Begin(9);
                return null;
            case 9:
                if (t < 0.3)
                {
                    return null;
                }

                Expect(Scoreboard(session) != _scoreboardBefore, $"{_playersKey} (ShowConnectedPlayers) did not toggle the connected players list");
                Shot(context, "scoreboard");
                Tap(_playersKey);
                Begin(10);
                return null;
            case 10:
                if (t < 0.3)
                {
                    return null;
                }

                Expect(Scoreboard(session) == _scoreboardBefore, $"{_playersKey} did not toggle the list back");
                Tap(_menuKey);
                Begin(11);
                return null;
            case 11:
                if (t < 0.7)
                {
                    return null;
                }

                Expect(MenuVisible(root), "Escape did not open the menu again");
                ClickButton(root, "Options");
                Begin(12);
                return null;
            case 12:
                if (t < 0.4)
                {
                    return null;
                }

                ExpectText(root, "OPTIONS", true);
                Expect(FindButton(root, "Save") is not null && FindButton(root, "Defaults") is not null, "the options screen's Save and Defaults are not shown");
                Expect(FindButton(root, "Quit Session") is null, "the menu's buttons stay shown under the options");
                Shot(context, "options");
                Tap("Escape");
                Begin(13);
                return null;
            case 13:
                if (t < 0.3)
                {
                    return null;
                }

                ExpectText(root, "OPTIONS", false);
                Expect(MenuVisible(root) && FindButton(root, "Quit Session") is not null, "Escape in the options did not come back to the menu");
                ClickButton(root, "Quit Session");
                Begin(14);
                return null;
            case 14:
                if (t < 0.3)
                {
                    return null;
                }

                ExpectText(root, "Leaving the game now will end this session.", true);
                foreach (string b in new[] { "End session", "Cancel", "Quit Game" })
                {
                    Expect(FindButton(root, b) is not null, $"quit dialog button '{b}' not shown");
                }

                Shot(context, "quit-dialog");
                ClickButton(root, "Cancel");
                Begin(15);
                return null;
            case 15:
                if (t < 0.3)
                {
                    return null;
                }

                ExpectText(root, "Leaving the game now will end this session.", false);
                Expect(FindButton(root, "Quit Session") is not null, "Cancel did not come back to the menu");
                ClickButton(root, "Back");
                Begin(16);
                return null;
            case 16:
                if (t < 0.3)
                {
                    return null;
                }

                Expect(!MenuVisible(root), "Back did not close the menu");
                Tap(_menuKey);
                Begin(17);
                return null;
            case 17:
                if (t < 0.7)
                {
                    return null;
                }

                ClickButton(root, "Quit Session");
                Begin(18);
                return null;
            case 18:
                if (t < 0.3)
                {
                    return null;
                }

                ClickButton(root, "End session");
                Begin(19);
                return null;
            case 19:
                if (context.Tree.CurrentScene is MainMenu && !GodotObject.IsInstanceValid(root))
                {
                    Note("End session: back to the main menu, the play scene freed");
                    return Finish(Report(null));
                }

                return t > 5 ? Finish(Report($"End session did not leave for the main menu (scene {context.Tree.CurrentScene?.Name})")) : null;
        }

        return null;
    }

    // ---- checks ------------------------------------------------------------------------------------------

    private static float Speed(SBasePawn? pawn) => pawn is null ? 0f : MathF.Sqrt((pawn.Velocity.X * pawn.Velocity.X) + (pawn.Velocity.Y * pawn.Velocity.Y));

    private static bool Scoreboard(PlaySession? session) => session?.LocalController?.myHUD is SHud hud && hud.bShowConnectedPlayers;

    private static bool MenuVisible(Node root)
    {
        foreach (Node n in root.GetTree().GetNodesInGroup(MenuGroup))
        {
            if (n is CanvasItem { } c && c.IsVisibleInTree())
            {
                return true;
            }
        }

        return false;
    }

    private void ExpectText(Node root, string text, bool shown)
    {
        bool found = FindText(root.GetTree().Root, text) is not null;
        Expect(found == shown, shown ? $"text '{text}' not shown" : $"text '{text}' still shown");
    }

    /// <summary>A visible label or button whose text contains <paramref name="text"/>.</summary>
    private static Control? FindText(Node node, string text)
    {
        if (node is Label { } l && l.IsVisibleInTree() && l.Text.Contains(text, StringComparison.Ordinal))
        {
            return l;
        }

        if (node is Button { } b && b.IsVisibleInTree() && b.Text.Contains(text, StringComparison.Ordinal))
        {
            return b;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindText(child, text) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static Button? FindButton(Node root, string text) => Find(root.GetTree().Root, text);

    private static Button? Find(Node node, string text)
    {
        if (node is Button { } b && b.IsVisibleInTree() && string.Equals(b.Text, text, StringComparison.Ordinal))
        {
            return b;
        }

        foreach (Node child in node.GetChildren())
        {
            if (Find(child, text) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void Expect(bool ok, string failure)
    {
        if (!ok && !_failures.Contains(failure))
        {
            _failures.Add(failure);
            GD.Print("SVM_MENU FAIL " + failure);
        }
    }

    private void Note(string line)
    {
        _log.Add(line);
        GD.Print("SVM_MENU " + line);
    }

    private SmokeResult Report(string? fatal)
    {
        if (fatal is not null)
        {
            Expect(false, fatal);
        }

        var text = new StringBuilder();
        text.Append(string.Join("; ", _failures.Count > 0 ? _failures : _log));
        return _failures.Count == 0 ? SmokeResult.Pass(text.ToString()) : SmokeResult.Fail(text.ToString());
    }

    // ---- the player's keys and mouse ---------------------------------------------------------------------

    private bool CapturedBefore { get; set; }

    private void AssumeCaptured(PlayInput input)
    {
        // Runs without a window cannot capture the pointer; PlayInput then reads no game input. Use the test switch
        // when this build has it (docs/TESTING-PLAY.md).
        if (typeof(PlayInput).GetProperty("RequireCapture") is { } require)
        {
            require.SetValue(input, false);
        }
        else if (typeof(PlayInput).GetProperty("AssumeCaptured") is { } assume)
        {
            assume.SetValue(input, true);
        }
        else
        {
            throw new InvalidOperationException("the pointer cannot be captured here and PlayInput has no test switch for it");
        }
    }

    private void AssumeCapturedIfNeeded(PlayInput input)
    {
        if (!input.IsCaptured)
        {
            AssumeCaptured(input);
        }
    }

    private void Send(InputEvent ev)
    {
        if (_push)
        {
            _root!.GetViewport().PushInput(ev);
        }
        else
        {
            Input.ParseInputEvent(ev);
        }
    }

    private void KeyEvent(string ik, bool pressed)
    {
        InputEvent ev = GodotKeys.ToEvent(ik) ?? throw new InvalidOperationException("no Godot key for " + ik);
        switch (ev)
        {
            case InputEventKey k:
                k.Keycode = k.PhysicalKeycode;
                k.Pressed = pressed;
                break;
            case InputEventMouseButton m:
                m.Pressed = pressed;
                break;
        }

        Send(ev);
    }

    private void Tap(string ik)
    {
        KeyEvent(ik, true);
        _releases.Add((ik, _now + 0.05));
    }

    private void ReleaseDue()
    {
        for (int i = _releases.Count - 1; i >= 0; i--)
        {
            if (_now >= _releases[i].At)
            {
                KeyEvent(_releases[i].Ik, false);
                _releases.RemoveAt(i);
            }
        }
    }

    private void Click(Vector2 at)
    {
        Send(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        if (DisplayServer.GetName() != "headless")
        {
            CapturedBefore = true;
        }
    }

    private void ClickButton(Node root, string text)
    {
        if (FindButton(root, text) is not { } b)
        {
            Expect(false, $"no button '{text}' to click");
            return;
        }

        Vector2 at = b.GetGlobalRect().GetCenter();
        Note(string.Create(CultureInfo.InvariantCulture, $"click '{text}' at {at} (viewport {root.GetViewport().GetVisibleRect().Size})"));
        Send(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
    }

    private void Shot(SmokeContext context, string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        string file = System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{context.Frame:D5}-{label}.png"));
        image.SavePng(file);
        Note("shot " + file);
    }

    private void Begin(int phase)
    {
        _phase = phase;
        _phaseStart = _now;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _phase = 100;
        if (_root is { } root && GodotObject.IsInstanceValid(root))
        {
            root.Session?.Stop();
            root.QueueFree();
        }

        return SmokeResult.Running;
    }
}
