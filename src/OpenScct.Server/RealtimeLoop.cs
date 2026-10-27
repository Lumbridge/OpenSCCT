using System;
using System.Threading;
using OpenScct.Core;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// The wall-clock loop of a server: calls a tick on a fixed 60 Hz schedule, dropping a backlog over
/// <c>maxCatchUpTicks</c> (a stall). With an <c>idle</c> check (an OpenSCCT addition), a server with nobody connected
/// ticks only <see cref="IdleHz"/> times a second and sleeps in between, so empty servers barely use the CPU; the
/// first tick that finds a connection puts it back on 60 Hz. While idle the world's clock runs slower (it advances one
/// step per tick); nobody is there to see it.
/// </summary>
public static class RealtimeLoop
{
    /// <summary>Ticks per second while idle: a joining player waits at most this long for the first answer.</summary>
    public const int IdleHz = 10;

    /// <summary>
    /// Runs <paramref name="tick"/> (given the clock's time) until <paramref name="token"/> is cancelled or
    /// <paramref name="stop"/> says so. After each tick, when <paramref name="idle"/> says the server is empty, it
    /// sleeps a tenth of a second instead of waiting for the next 60 Hz slot.
    /// </summary>
    public static void Run(IClock clock, CancellationToken token, Action<long> tick, Func<bool>? idle = null,
        Func<bool>? stop = null, int maxCatchUpTicks = 5)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(tick);
        long start = clock.NowMicroseconds;
        long ticks = 0;
        while (!token.IsCancellationRequested && !(stop?.Invoke() ?? false))
        {
            long now = clock.NowMicroseconds;
            long due = start + VirtualClock.TickToMicroseconds(ticks + 1, Sim.Hz);
            if (now < due)
            {
                if (due - now > 2000)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.Yield();
                }

                continue;
            }

            if (now - due > VirtualClock.TickToMicroseconds(maxCatchUpTicks, Sim.Hz))
            {
                start = now - VirtualClock.TickToMicroseconds(ticks + 1, Sim.Hz);
            }

            tick(now);
            ticks++;
            if (idle is not null && idle())
            {
                token.WaitHandle.WaitOne(1000 / IdleHz);
                // The next tick is due at once; the slept time is not caught up.
                start = clock.NowMicroseconds - VirtualClock.TickToMicroseconds(ticks, Sim.Hz);
            }
        }
    }
}
