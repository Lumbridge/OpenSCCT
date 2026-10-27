using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// Drives the front end as a player does, for the menu smokes (docs/TESTING-PLAY.md, docs/MENUS.md): mouse clicks on
/// buttons, check boxes, slider positions and list rows, and typing into edit boxes, all as Godot input events through
/// <see cref="Input.ParseInputEvent"/> (the same path as a real mouse and keyboard: viewport, GUI input, the controls'
/// own signals). One action at a time: after each the pilot waits <see cref="SettleFrames"/> frames so the GUI sees
/// the events and the page reacts before the next check. Without a window the root viewport is 64 x 64; the pilot gives
/// it the project's 1280 x 720 so controls lie where clicks land, as <c>InGameMenuSmoke</c> does.
/// </summary>
public sealed class MenuPilot
{
    /// <summary>Frames to wait after an action.</summary>
    public const int SettleFrames = 4;

    private readonly SceneTree _tree;
    private readonly List<string> _log = new();
    private int _wait;

    public MenuPilot(SceneTree tree)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        if (DisplayServer.GetName() == "headless")
        {
            tree.Root.Size = new Vector2I(1280, 720);
        }
    }

    /// <summary>True while the last action settles.</summary>
    public bool Busy => _wait > 0;

    /// <summary>Every action, in order (for the smoke's report).</summary>
    public IReadOnlyList<string> Log => _log;

    public int Clicks { get; private set; }

    /// <summary>Call once per frame.</summary>
    public void Update()
    {
        if (_wait > 0)
        {
            _wait--;
        }
    }

    /// <summary>The first visible control named <paramref name="name"/> under <paramref name="root"/> (the current scene by default).</summary>
    public T? Find<T>(string name, Node? root = null)
        where T : Control
    {
        root ??= _tree.CurrentScene;
        if (root is null)
        {
            return null;
        }

        foreach (Node n in root.FindChildren(name, nameof(Control), recursive: true, owned: false))
        {
            if (n is T c && c.IsVisibleInTree())
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>The first visible button whose text is <paramref name="text"/>.</summary>
    public BaseButton? FindButton(string text, Node? root = null)
    {
        root ??= _tree.CurrentScene;
        if (root is null)
        {
            return null;
        }

        foreach (Node n in root.FindChildren("*", nameof(Button), recursive: true, owned: false))
        {
            if (n is Button b && b.IsVisibleInTree() && string.Equals(b.Text, text, StringComparison.Ordinal))
            {
                return b;
            }
        }

        return null;
    }

    /// <summary>Clicks the middle of a control (a button, a check box, an edit box to focus it).</summary>
    public bool Click(Control? control, string what)
    {
        if (control is null || !control.IsVisibleInTree())
        {
            _log.Add("cannot click " + what + ": not shown");
            return false;
        }

        if (!ScrollIntoView(control))
        {
            return false;
        }

        return ClickAt(control.GetGlobalRect().GetCenter(), what);
    }

    /// <summary>
    /// Scrolls the page so <paramref name="control"/> shows, as a player scrolls to it; false when it had to scroll
    /// (click on a later frame, once the page moved).
    /// </summary>
    public bool ScrollIntoView(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        for (Node? n = control.GetParent(); n is not null; n = n.GetParent())
        {
            if (n is ScrollContainer scroll)
            {
                Rect2 view = scroll.GetGlobalRect();
                Rect2 r = control.GetGlobalRect();
                if (r.Position.Y < view.Position.Y || r.End.Y > view.End.Y)
                {
                    scroll.EnsureControlVisible(control);
                    _log.Add("scroll to " + control.Name);
                    _wait = SettleFrames;
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Clicks a horizontal slider where it shows <paramref name="value"/>: the grabber jumps to the click, its middle
    /// on the value (Godot's Slider: ratio = (x - grabber / 2) / (width - grabber)).
    /// </summary>
    public bool ClickSliderAt(HSlider? slider, double value, string what)
    {
        if (slider is null)
        {
            _log.Add("cannot click " + what + ": not shown");
            return false;
        }

        double ratio = (value - slider.MinValue) / (slider.MaxValue - slider.MinValue);
        float grabber = slider.GetThemeIcon("grabber").GetWidth();
        float width = slider.Size.X;
        return ClickSlider(slider, (grabber / 2f + ratio * (width - grabber)) / width, what);
    }

    /// <summary>Clicks a horizontal slider at <paramref name="fraction"/> of its width (the grabber jumps there).</summary>
    public bool ClickSlider(Godot.Range? slider, double fraction, string what)
    {
        if (slider is null || !slider.IsVisibleInTree())
        {
            _log.Add("cannot click " + what + ": not shown");
            return false;
        }

        if (!ScrollIntoView(slider))
        {
            return false;
        }

        Rect2 r = slider.GetGlobalRect();
        return ClickAt(new Vector2(r.Position.X + (float)(r.Size.X * Math.Clamp(fraction, 0, 1)), r.GetCenter().Y), what);
    }

    /// <summary>Clicks a row of a tree (the server browser's list).</summary>
    public bool ClickTreeItem(Tree tree, TreeItem item, string what)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(item);
        tree.ScrollToItem(item);
        Rect2 area = tree.GetItemAreaRect(item);
        Vector2 at = tree.GetGlobalRect().Position + area.Position + new Vector2(Math.Min(area.Size.X * 0.5f, 120f), area.Size.Y * 0.5f) -
            new Vector2(0f, tree.GetScroll().Y);
        return ClickAt(at, what);
    }

    /// <summary>Clicks an edit box, selects what it holds and types <paramref name="text"/> as key presses.</summary>
    public bool Type(LineEdit? edit, string text, string what)
    {
        if (edit is null || !edit.IsVisibleInTree())
        {
            _log.Add("cannot type into " + what + ": not shown");
            return false;
        }

        if (!ScrollIntoView(edit))
        {
            return false;
        }

        ClickAt(edit.GetGlobalRect().GetCenter(), what);
        Key(Godot.Key.A, ctrl: true);
        Key(Godot.Key.Backspace);
        foreach (char c in text)
        {
            var down = new InputEventKey { Pressed = true, Unicode = c, Keycode = KeyFor(c), PhysicalKeycode = KeyFor(c) };
            Input.ParseInputEvent(down);
            Input.ParseInputEvent(new InputEventKey { Pressed = false, Unicode = c, Keycode = KeyFor(c), PhysicalKeycode = KeyFor(c) });
        }

        _log.Add(string.Create(CultureInfo.InvariantCulture, $"type '{text}' into {what}"));
        _wait = SettleFrames;
        return true;
    }

    /// <summary>Presses and releases a key (Enter to submit, Escape...).</summary>
    public void Key(Key key, bool ctrl = false)
    {
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl });
        Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl });
        _wait = SettleFrames;
    }

    /// <summary>Clicks that would have landed outside the window (a page laid out wider or taller than it).</summary>
    public int OffScreen { get; private set; }

    private bool ClickAt(Vector2 at, string what)
    {
        if (!_tree.Root.GetVisibleRect().HasPoint(at))
        {
            OffScreen++;
            _log.Add(string.Create(CultureInfo.InvariantCulture, $"{what} is off the window at ({at.X:0},{at.Y:0}): a player cannot click it"));
            _wait = SettleFrames;
            return false;
        }

        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        Clicks++;
        _log.Add(string.Create(CultureInfo.InvariantCulture, $"click {what} at ({at.X:0},{at.Y:0})"));
        _wait = SettleFrames;
        return true;
    }

    private static Key KeyFor(char c) => c switch
    {
        >= 'a' and <= 'z' => (Key)((int)Godot.Key.A + (c - 'a')),
        >= 'A' and <= 'Z' => (Key)((int)Godot.Key.A + (c - 'A')),
        >= '0' and <= '9' => (Key)((int)Godot.Key.Key0 + (c - '0')),
        '.' => Godot.Key.Period,
        ':' => Godot.Key.Colon,
        ' ' => Godot.Key.Space,
        '-' => Godot.Key.Minus,
        '_' => Godot.Key.Underscore,
        _ => Godot.Key.Unknown,
    };
}
