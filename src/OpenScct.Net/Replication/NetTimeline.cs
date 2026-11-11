using System;

namespace OpenScct.Net;

/// <summary>
/// Client estimate of the server tick, used to pick the render time for interpolation. Every packet from the
/// server gives a sample (server tick minus local tick); the offset follows the largest recent sample (the
/// least delayed packet) and moves forward at most 1/16 tick per local tick, so displayed time stays smooth. When
/// the server itself falls behind, the offset follows the newest packets back. Times are fixed point with
/// <see cref="Fraction"/> steps per tick.
/// </summary>
public sealed class NetTimeline
{
    public const int Fraction = 256;

    private const int Window = 64;
    private const long MaxStep = Fraction / 16;
    private const long SnapDistance = 8 * Fraction;
    private const int Recent = 8;
    private const long FellBehind = 2;

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

        // A server that stalls or ticks below its rate (a listen server on a busy machine) falls behind for good:
        // every newer packet then gives a smaller sample, and the largest of the whole window would keep the
        // estimate ahead of the data for a second and more. When even the best of the newest packets is two ticks
        // behind, the server's time moved, not one packet's delay.
        if (_count >= Recent)
        {
            long recent = long.MinValue;
            for (int i = 1; i <= Recent; i++)
            {
                recent = Math.Max(recent, _samples[(_next - i + Window) % Window]);
            }

            if (best - recent >= FellBehind)
            {
                best = recent;
            }
        }

        long target = best * Fraction;
        long diff = target - _offsetFixed;
        if (diff > SnapDistance || diff < -SnapDistance)
        {
            _offsetFixed = target;
        }
        else
        {
            // Back towards a server that fell behind in proportion to the distance: at 1/16 tick per tick the
            // estimate cannot follow a server running a few ticks per second slow.
            _offsetFixed += Math.Clamp(diff, -Math.Max(MaxStep, -diff / 8), MaxStep);
        }
    }

    /// <summary>Server time to show at <paramref name="localTick"/>, <paramref name="delayTicks"/> behind the estimate.</summary>
    public long RenderTimeFixed(long localTick, int delayTicks) =>
        localTick * Fraction + _offsetFixed - (long)delayTicks * Fraction;
}
