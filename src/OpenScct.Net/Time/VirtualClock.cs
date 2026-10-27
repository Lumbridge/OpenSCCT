using System;

namespace OpenScct.Net;

/// <summary>A clock that only moves when told to, for fast deterministic tests.</summary>
public sealed class VirtualClock : IClock
{
    public long NowMicroseconds { get; private set; }

    public void Advance(long microseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(microseconds);
        NowMicroseconds += microseconds;
    }

    public void AdvanceTo(long microseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(microseconds, NowMicroseconds);
        NowMicroseconds = microseconds;
    }

    /// <summary>Time of fixed tick <paramref name="tick"/> at <paramref name="hz"/> ticks per second, rounded down.</summary>
    public static long TickToMicroseconds(long tick, int hz) => tick * 1_000_000 / hz;
}
