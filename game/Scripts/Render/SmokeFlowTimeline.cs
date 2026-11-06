using System;

namespace OpenScctGame.Render;

/// <summary>A bounded visual-only clock. Rendering another camera never advances a cloud a second time.</summary>
internal sealed class SmokeFlowTimeline
{
    public const int Hertz = 30;
    public const int MaximumSteps = 2;
    private bool _initialized;
    private float _createdAt, _lastTime;
    private int _gridSize;
    private long _tick;

    public SmokeFlowStep Plan(float now, float createdAt, int gridSize, bool forceReset = false)
    {
        if (!float.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
        if (!float.IsFinite(createdAt)) throw new ArgumentOutOfRangeException(nameof(createdAt));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridSize);
        // Float server timestamps can lie a few millionths below an exact step boundary.
        long target = (long)Math.Floor(Math.Max(0, (double)now - createdAt) * Hertz + .0001);
        bool reset = !_initialized || forceReset || createdAt != _createdAt || gridSize != _gridSize ||
            now < _lastTime || target - _tick > MaximumSteps;
        long first = reset ? target : _tick;
        int steps = reset ? 0 : (int)Math.Max(0, target - _tick);
        _initialized = true; _createdAt = createdAt; _lastTime = now; _gridSize = gridSize; _tick = target;
        return new(reset, steps, createdAt + (float)first / Hertz, 1f / Hertz);
    }
}

internal readonly record struct SmokeFlowStep(bool Reset, int Steps, float StartTime, float DeltaTime);
