using System;
using System.Collections.Generic;

namespace OpenScctGame.Options;

/// <summary>One menu step per press, with stick hysteresis and deliberate held repeat.</summary>
public sealed class PadMenuNavigation
{
    public const double RepeatDelay = 0.4;
    public const double RepeatInterval = 0.16;
    private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);
    private int _horizontal, _vertical;
    private string? _direction;
    private double _nextRepeat;

    public bool IsActive => _direction is not null || _buttons.Count > 0;

    public string? Button(string key, bool pressed, double now)
    {
        if (pressed ? !_buttons.Add(key) : !_buttons.Remove(key)) return null;
        if (key is "Enter" or "Escape") return pressed ? key : null;
        return Change(now);
    }

    public string? Axis(bool horizontal, float value, double now)
    {
        ref int direction = ref (horizontal ? ref _horizontal : ref _vertical);
        int next = value >= 0.65f ? 1 : value <= -0.65f ? -1 : Math.Abs(value) <= 0.35f ? 0 : direction;
        if (next == direction) return null;
        direction = next;
        return Change(now);
    }

    private string? Change(double now)
    {
        string? next = _buttons.Contains("Up") ? "Up" : _buttons.Contains("Down") ? "Down" :
            _buttons.Contains("Left") ? "Left" : _buttons.Contains("Right") ? "Right" :
            _vertical < 0 ? "Up" : _vertical > 0 ? "Down" : _horizontal < 0 ? "Left" : _horizontal > 0 ? "Right" : null;
        if (next == _direction) return null;
        _direction = next;
        _nextRepeat = now + RepeatDelay;
        return next;
    }

    public string? Advance(double now)
    {
        if (_direction is null || now < _nextRepeat) return null;
        // A hitch never catches up by skipping several entries in one frame.
        _nextRepeat = now + RepeatInterval;
        return _direction;
    }

    public void Reset()
    {
        _buttons.Clear();
        _horizontal = _vertical = 0;
        _direction = null;
    }
}
