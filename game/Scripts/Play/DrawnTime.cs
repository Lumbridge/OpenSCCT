using System;

namespace OpenScctGame.Play;

/// <summary>
/// The level time at the drawn moment between ticks (docs/RENDERING.md, frame pacing): the views draw the last two
/// client ticks blended by the clock's alpha, so everything that runs on the level time (animated materials, light
/// types, the map's emitters, the HUD's fades) is drawn at the same blend of the two ticks' Level.TimeSeconds. The
/// original advanced the level time every rendered frame; this moves it every frame too, one tick behind the
/// simulation like the rest of the picture. Godot-free.
/// </summary>
public sealed class DrawnTime
{
    private float _previous;
    private float _current;
    private bool _started;

    /// <summary>The level time after the last tick.</summary>
    public float Current => _current;

    /// <summary>The level time after the tick before it.</summary>
    public float Previous => _previous;

    /// <summary>After each client tick: its Level.TimeSeconds (a time going back, a new level, starts again from it).</summary>
    public void OnTick(float levelSeconds)
    {
        _previous = _started && levelSeconds >= _current ? _current : levelSeconds;
        _current = levelSeconds;
        _started = true;
    }

    /// <summary>The time drawn at <paramref name="alpha"/> (0: the tick before the last, 1: the last).</summary>
    public float At(float alpha) => _previous + ((_current - _previous) * Math.Clamp(alpha, 0f, 1f));
}
