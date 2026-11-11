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
    /// <param name="skipped">
    /// Told how many whole ticks a stall's dropped backlog held, before the next tick runs (the idle slowdown is
    /// not reported: nobody is connected).
    /// </param>
    public static void Run(IClock clock, CancellationToken token, Action<long> tick, Func<bool>? idle = null,
        Func<bool>? stop = null, int maxCatchUpTicks = 5, Action<long>? skipped = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(tick);
        long start = clock.NowMicroseconds;
        long ticks = 0;
        long skippedRemainder = 0;
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
                long restart = now - VirtualClock.TickToMicroseconds(ticks + 1, Sim.Hz);
                long whole = SkippedTicks(restart - start, ref skippedRemainder);
                start = restart;
                if (whole > 0)
                {
                    skipped?.Invoke(whole);
                }
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

    /// <summary>
    /// The whole ticks in a schedule moved later by <paramref name="shiftMicros"/> (a dropped backlog); the part of a
    /// tick left over stays in <paramref name="remainderMicros"/> for the next drop.
    /// </summary>
    public static long SkippedTicks(long shiftMicros, ref long remainderMicros)
    {
        remainderMicros += Math.Max(0, shiftMicros);
        long whole = remainderMicros * Sim.Hz / 1_000_000;
        remainderMicros -= VirtualClock.TickToMicroseconds(whole, Sim.Hz);
        return whole;
    }
}
