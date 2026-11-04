using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScctGame.Controls;

public readonly record struct BindingInputEvent(string Key, bool Pressed);

/// <summary>Physical press ownership, independent of Godot and UI routing. A chord never turns into its plain key.</summary>
public sealed class BindingInputState
{
    private readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _owners = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> PhysicalDown => _down;
    public IEnumerable<string> ActiveKeys => _owners.Values;

    public IReadOnlyList<BindingInputEvent> Update(KeyLayout layout, string input, bool pressed,
        BindingModifiers modifiers = BindingModifiers.None, bool suppressed = false)
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
                foreach (string source in _owners.Where(p => (BindingKey.Parse(p.Value).Modifiers & ownModifier) != 0).Select(p => p.Key).ToArray())
                    Release(source, events);
            return events;
        }
        if (!pulse && !_down.Add(input)) return events;
        if (suppressed || _blocked.Contains(input))
        {
            if (!pulse) _blocked.Add(input);
            return events;
        }
        // A modifier held across focus/capture restoration stays ignored until physically released.
        foreach (string blocked in _blocked) modifiers &= ~BindingKey.ModifierOf(blocked);
        string? selected = BindingKey.Select(layout, input, modifiers);
        if (selected is null) return events;
        events.Add(new BindingInputEvent(selected, true));
        if (pulse) events.Add(new BindingInputEvent(selected, false));
        else _owners[input] = selected;
        return events;
    }

    private void Release(string input, List<BindingInputEvent> events)
    {
        if (_owners.Remove(input, out string? owner)) events.Add(new BindingInputEvent(owner, false));
    }

    /// <summary>Release actions and retain physical ownership so held inputs cannot restart when a menu closes.</summary>
    public IReadOnlyList<BindingInputEvent> Reset()
    {
        var releases = _owners.Values.Select(key => new BindingInputEvent(key, false)).ToArray();
        _owners.Clear();
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
