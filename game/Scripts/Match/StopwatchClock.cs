using System.Diagnostics;
using OpenScct.Net;

namespace OpenScctGame.Match;

/// <summary>Monotonic wall clock shared by the listen server thread and the client ticks on the main thread.</summary>
public sealed class StopwatchClock : IClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public long NowMicroseconds => (long)Stopwatch.GetElapsedTime(_start).TotalMicroseconds;
}
