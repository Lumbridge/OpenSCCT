using System;
using Godot;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>Read long, non-interactive career tables without moving focus to the footer.</summary>
internal sealed partial class CareerScroll : Node
{
    private ScrollContainer _scroll = null!;
    private float _stickScroll;

    public override void _Ready() => _scroll = GetParent<ScrollContainer>();

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!_scroll.IsVisibleInTree() || _scroll.IsQueuedForDeletion() || input is not InputEventKey { Pressed: true } key) return;
        int page = Math.Max(1, (int)(_scroll.Size.Y * 0.8f));
        switch (key.Keycode)
        {
            case Key.Pagedown: _scroll.ScrollVertical += page; break;
            case Key.Pageup: _scroll.ScrollVertical -= page; break;
            case Key.Home: _scroll.ScrollVertical = 0; break;
            case Key.End: _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue; break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!_scroll.IsVisibleInTree() || _scroll.IsQueuedForDeletion() || !SettingsStore.Current.Pad.Enabled) return;
        int device = PadMenus.CurrentDevice();
        float axis = device < 0 ? 0f : Input.GetJoyAxis(device, JoyAxis.RightY);
        if (Math.Abs(axis) <= 0.25f) { _stickScroll = 0f; return; }
        _stickScroll += axis * 480f * (float)delta;
        int movement = (int)_stickScroll;
        _stickScroll -= movement;
        _scroll.ScrollVertical += movement;
    }
}
