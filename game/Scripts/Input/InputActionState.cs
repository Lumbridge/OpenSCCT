using System.Collections.Generic;

namespace OpenScctGame.Controls;

/// <summary>Hold sources are tracked independently so releasing one key never cancels another held source.</summary>
public sealed class InputActionState
{
    private readonly HashSet<string> _walkHeld = new();
    private readonly HashSet<string> _scopeHeld = new();
    private readonly HashSet<string> _perspectiveHeld = new(System.StringComparer.OrdinalIgnoreCase);
    private bool _walkToggle;
    private bool _perspectiveToggle;
    public bool ScopeHeld => _scopeHeld.Count > 0;
    public bool SpyFirstPersonRequested => _perspectiveToggle || _perspectiveHeld.Count > 0;
    public bool Walk(string source, bool pressed, bool hold, bool pulse)
    {
        if (hold && !pulse)
        {
            if (pressed) _walkHeld.Add(source); else _walkHeld.Remove(source);
        }
        else if (pressed) _walkToggle = !_walkToggle;
        return _walkToggle || _walkHeld.Count > 0;
    }
    public bool Scope(string source, bool pressed)
    {
        if (pressed) _scopeHeld.Add(source); else _scopeHeld.Remove(source);
        return _scopeHeld.Count > 0;
    }
    public bool SpyPerspective(string source, bool pressed, bool hold, bool pulse)
    {
        if (hold && !pulse)
        {
            if (pressed) _perspectiveHeld.Add(source); else _perspectiveHeld.Remove(source);
        }
        else if (pressed) _perspectiveToggle = !_perspectiveToggle;
        return SpyFirstPersonRequested;
    }
    public void Run() { _walkToggle = false; _walkHeld.Clear(); }
    public void Clear() { Run(); _scopeHeld.Clear(); _perspectiveHeld.Clear(); _perspectiveToggle = false; }
}
