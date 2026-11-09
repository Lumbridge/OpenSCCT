using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OpenScct.Server;

/// <summary>
/// Waits between real-time ticks without burning a core. On Windows it sleeps on a high-resolution waitable timer until
/// shortly before the deadline and spins only for the last <see cref="SpinMicros"/>. Elsewhere, or when the timer
/// cannot be made (Windows before 10 1803), it keeps the old wait: 1 ms sleeps, then yields for the last 2 ms.
/// Owned by one loop thread.
/// </summary>
internal sealed class HighResolutionWait : IDisposable
{
    /// <summary>The last stretch before the deadline is spun, so a slightly late timer wake does not delay the tick.</summary>
    public const long SpinMicros = 300;

    /// <summary>Longest single timer wait: the loop reads its clock and cancellation about as often as with Sleep(1).</summary>
    public const long MaxWaitMicros = 1000;

    private const long FallbackSleepAboveMicros = 2000;
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitObject0 = 0;

    private SafeWaitHandle? _timer;

    private HighResolutionWait(SafeWaitHandle? timer) => _timer = timer;

    /// <summary>True while the high-resolution timer is in use.</summary>
    public bool HighResolution => _timer is not null;

    public static HighResolutionWait Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new HighResolutionWait(null);
        }

        try
        {
            SafeWaitHandle timer = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, CreateWaitableTimerHighResolution,
                TimerAllAccess);
            if (timer.IsInvalid)
            {
                timer.Dispose();
                return new HighResolutionWait(null);
            }

            return new HighResolutionWait(timer);
        }
        catch (DllNotFoundException)
        {
            return new HighResolutionWait(null);
        }
        catch (EntryPointNotFoundException)
        {
            return new HighResolutionWait(null);
        }
    }

    /// <summary>Waits a little while the next tick is <paramref name="remainingMicros"/> away; the caller loops.</summary>
    public void Pause(long remainingMicros)
    {
        if (_timer is not null && remainingMicros > SpinMicros
            && TimerWait(_timer, Math.Min(remainingMicros - SpinMicros, MaxWaitMicros)))
        {
            return;
        }

        if (remainingMicros > FallbackSleepAboveMicros)
        {
            Thread.Sleep(1);
        }
        else
        {
            Thread.Yield();
        }
    }

    private bool TimerWait(SafeWaitHandle timer, long micros)
    {
        // Relative due times are negative, in 100 ns units.
        long due = -micros * 10;
        if (SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)
            && WaitForSingleObject(timer, Infinite) == WaitObject0)
        {
            return true;
        }

        // A timer that fails once is dropped; the old wait takes over for good.
        timer.Dispose();
        _timer = null;
        return false;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr timerAttributes, IntPtr timerName, uint flags,
        uint desiredAccess);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr completionRoutine, IntPtr completionRoutineArg, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
