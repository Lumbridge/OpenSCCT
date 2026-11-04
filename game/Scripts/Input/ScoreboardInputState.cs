using System;
using System.Collections.Generic;

namespace OpenScctGame.Controls;

/// <summary>The passive scoreboard's independent toggle and held input sources.</summary>
public sealed class ScoreboardInputState
{
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    public bool Toggled { get; private set; }
    public bool Visible => Toggled || _held.Count != 0;
    public void Toggle() => Toggled = !Toggled;
    public bool Hold(string source, bool pressed) => pressed ? _held.Add(source) : _held.Remove(source);
    public void Clear() { Toggled = false; _held.Clear(); }
}
