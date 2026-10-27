using System;

namespace OpenScct.Net;

/// <summary>
/// Client estimate of the server tick, used to pick the render time for interpolation. Every packet from the
/// server gives a sample (server tick minus local tick); the offset follows the largest recent sample (the
/// least delayed packet) and moves at most 1/16 tick per local tick, so displayed time stays smooth. Times are
/// fixed point with <see cref="Fraction"/> steps per tick.
/// </summary>
public sealed class NetTimeline
{
    public const int Fraction = 256;

    private const int Window = 64;
    private const long MaxStep = Fraction / 16;
    private const long SnapDistance = 8 * Fraction;

    private readonly long[] _samples = new long[Window];
    private int _count;
    private int _next;
    private long _offsetFixed;

    public bool IsValid { get; private set; }

    /// <summary>Estimated server tick minus local tick, fixed point.</summary>
    public long OffsetFixed => _offsetFixed;

    public void AddSample(long serverTick, long localTick)
    {
        long sample = serverTick - localTick;
        _samples[_next] = sample;
        _next = (_next + 1) % Window;
        if (_count < Window)
        {
            _count++;
        }

        if (!IsValid)
        {
            IsValid = true;
            _offsetFixed = sample * Fraction;
        }
    }

    /// <summary>Moves the offset towards the newest estimate. Call once per local tick.</summary>
    public void Advance()
    {
        if (_count == 0)
        {
            return;
        }

        long best = long.MinValue;
        for (int i = 0; i < _count; i++)
        {
            best = Math.Max(best, _samples[i]);
        }

        long target = best * Fraction;
        long diff = target - _offsetFixed;
        if (diff > SnapDistance || diff < -SnapDistance)
        {
            _offsetFixed = target;
        }
        else
        {
            _offsetFixed += Math.Clamp(diff, -MaxStep, MaxStep);
        }
    }

    /// <summary>Server time to show at <paramref name="localTick"/>, <paramref name="delayTicks"/> behind the estimate.</summary>
    public long RenderTimeFixed(long localTick, int delayTicks) =>
        localTick * Fraction + _offsetFixed - (long)delayTicks * Fraction;
}
