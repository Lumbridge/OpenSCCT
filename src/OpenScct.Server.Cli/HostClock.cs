using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenScct.Net;

namespace OpenScct.Server.Cli;

/// <summary>Monotonic wall clock for the real-time host loop.</summary>
internal sealed class StopwatchClock : IClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public long NowMicroseconds => (long)Stopwatch.GetElapsedTime(_start).TotalMicroseconds;
}

/// <summary>
/// Asks Windows for 1 ms timer resolution for this process while the server runs, so the host loop can sleep
/// between ticks instead of waking up to 15 ms late. Does nothing elsewhere.
/// </summary>
internal sealed class TimerResolution : IDisposable
{
    private readonly bool _active;

    private TimerResolution(bool active)
    {
        _active = active;
    }

    public static TimerResolution Request() =>
        new(OperatingSystem.IsWindows() && timeBeginPeriod(1) == 0);

    public void Dispose()
    {
        if (_active)
        {
            _ = timeEndPeriod(1);
        }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);
}
