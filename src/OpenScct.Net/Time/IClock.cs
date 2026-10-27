namespace OpenScct.Net;

/// <summary>
/// Monotonic host clock in microseconds. The network layer never reads system time itself: hosts pass a real
/// clock, tests pass a <see cref="VirtualClock"/>.
/// </summary>
public interface IClock
{
    long NowMicroseconds { get; }
}
