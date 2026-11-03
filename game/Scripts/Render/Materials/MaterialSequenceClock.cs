using System;

namespace OpenScctGame.Render.Materials;

public readonly record struct MaterialSequenceItem(float Time, bool Fade);
public readonly record struct MaterialSequenceSample(int Previous, int Current, float Blend);

/// <summary>MaterialSequence.PreSetMaterial (editor 0x110c9a40): elapsed scene time, show/fade, loop and pause.</summary>
public sealed class MaterialSequenceClock(MaterialSequenceItem[] items, bool loop = true, bool paused = false)
{
    private float _lastTime;
    private float _currentTime;
    private MaterialSequenceSample _sample;
    private readonly bool _initialPaused = paused;
    public bool Paused { get; private set; } = paused;

    public MaterialSequenceSample Update(float seconds)
    {
        if (Paused || items.Length == 0 || !float.IsFinite(seconds)) return _sample;
        if (seconds < _lastTime) _currentTime = 0f;
        else if (_lastTime > 0f) _currentTime += seconds - _lastTime;
        _lastTime = seconds;
        float total = 0f;
        foreach (MaterialSequenceItem item in items) total += Math.Max(0f, item.Time);
        if (loop && total > 0f && _currentTime > total) _currentTime %= total;
        float start = 0f;
        for (int i = 0; i < items.Length; i++)
        {
            float duration = Math.Max(0f, items[i].Time), end = start + duration;
            if (_currentTime >= start && (_currentTime < end || !loop && i == items.Length - 1))
            {
                bool fade = items[i].Fade && duration > 0f && _currentTime < end;
                // Native uses a ConstantColor alpha byte for the Combiner's interpolation mask.
                float blend = fade ? Math.Clamp(MathF.Floor((_currentTime - start) / duration * 255f + 0.5f), 0f, 255f) / 255f : 1f;
                _sample = new(fade ? (i + items.Length - 1) % items.Length : i, i, blend);
                break;
            }
            start = end;
        }
        return _sample;
    }

    public void Reset() { _currentTime = _lastTime = 0f; Paused = _initialPaused; _sample = default; }
    public void Trigger(int action)
    {
        if (action == 1) { _currentTime = _lastTime = 0f; }
        else if (action == 2) Paused = !Paused;
        else if (action == 3) Paused = true;
    }
}
