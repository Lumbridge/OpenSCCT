using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScctGame.Controls;

public readonly record struct BindingInputEvent(string Key, bool Pressed);

/// <summary>
/// Physical press ownership, independent of Godot and UI routing. A chord never turns into its plain key. A double
/// tap (<c>Double+H</c>) is extra: the second quick press of an input raises it after the input's ordinary binding.
/// </summary>
public sealed class BindingInputState
{
    /// <summary>The longest time between two presses of one input that still counts as a double tap.</summary>
    public const long DoubleTapMsec = 300;

    private readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _owners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _doubleOwners = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastTap;
    private long _lastTapAt;
    public IReadOnlySet<string> PhysicalDown => _down;
    public IEnumerable<string> ActiveKeys => _owners.Values.Concat(_doubleOwners.Values);

    /// <param name="nowMsec">The time of the event for double taps; the system's tick count when omitted.</param>
    public IReadOnlyList<BindingInputEvent> Update(KeyLayout layout, string input, bool pressed,
        BindingModifiers modifiers = BindingModifiers.None, bool suppressed = false, long? nowMsec = null)
    {
        var events = new List<BindingInputEvent>();
        BindingModifiers ownModifier = BindingKey.ModifierOf(input);
        bool pulse = new BindingKey(input).IsPulse;
        if (!pressed)
        {
            _down.Remove(input);
            _blocked.Remove(input);
            Release(input, events);
            if (ownModifier != BindingModifiers.None)
                foreach (Dictionary<string, string> owners in new[] { _owners, _doubleOwners })
                    foreach (string source in owners.Where(p => (BindingKey.Parse(p.Value).Modifiers & ownModifier) != 0).Select(p => p.Key).ToArray())
                        if (owners.Remove(source, out string? owner)) events.Add(new BindingInputEvent(owner, false));
            return events;
        }
        if (!pulse && !_down.Add(input)) return events;
        if (suppressed || _blocked.Contains(input))
        {
            if (!pulse) _blocked.Add(input);
            _lastTap = null;
            return events;
        }
        // The wheel neither makes nor interrupts a double tap. A third quick press starts a new count.
        bool second = false;
        if (!pulse)
        {
            long now = nowMsec ?? Environment.TickCount64;
            second = string.Equals(_lastTap, input, StringComparison.OrdinalIgnoreCase) && now - _lastTapAt <= DoubleTapMsec;
            _lastTap = second ? null : input;
            _lastTapAt = now;
        }
        // A modifier held across focus/capture restoration stays ignored until physically released.
        foreach (string blocked in _blocked) modifiers &= ~BindingKey.ModifierOf(blocked);
        if (BindingKey.Select(layout, input, modifiers) is { } selected)
        {
            events.Add(new BindingInputEvent(selected, true));
            if (pulse) events.Add(new BindingInputEvent(selected, false));
            else _owners[input] = selected;
        }
        if (second && BindingKey.SelectDouble(layout, input, modifiers) is { } doubled)
        {
            events.Add(new BindingInputEvent(doubled, true));
            _doubleOwners[input] = doubled;
        }
        return events;
    }

    private void Release(string input, List<BindingInputEvent> events)
    {
        if (_owners.Remove(input, out string? owner)) events.Add(new BindingInputEvent(owner, false));
        if (_doubleOwners.Remove(input, out string? doubled)) events.Add(new BindingInputEvent(doubled, false));
    }

    /// <summary>Release actions and retain physical ownership so held inputs cannot restart when a menu closes.</summary>
    public IReadOnlyList<BindingInputEvent> Reset()
    {
        var releases = ActiveKeys.Select(key => new BindingInputEvent(key, false)).ToArray();
        _owners.Clear();
        _doubleOwners.Clear();
        _lastTap = null;
        _blocked.UnionWith(_down);
        return releases;
    }

    /// <summary>Drop stale physical state after focus was lost and releases could not reach the application.</summary>
    public void Reconcile(IReadOnlySet<string> physicallyDown)
    {
        _down.RemoveWhere(key => !physicallyDown.Contains(key));
        _blocked.RemoveWhere(key => !physicallyDown.Contains(key));
    }

    public void BlockHeld(IEnumerable<string> physicallyDown)
    {
        _down.UnionWith(physicallyDown);
        _blocked.UnionWith(physicallyDown);
    }
}
