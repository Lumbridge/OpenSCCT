using System;
using System.Globalization;
using OpenScct.Core;

namespace OpenScct.Server;

/// <summary>
/// How long a host's last ticks took (an OpenSCCT diagnostic): the host's thread adds each tick, any thread may read.
/// It keeps the last <see cref="Capacity"/> ticks (about 17 s), so each reader sums up its own window.
/// </summary>
public sealed class TickTimes
{
    public const int Capacity = 1024;

    /// <summary>A tick's slot: a tick that takes longer puts the server behind its clock.</summary>
    public const double SlotMs = 1000.0 / Sim.Hz;

    private readonly object _gate = new();
    private readonly float[] _tickMs = new float[Capacity];
    private readonly float[] _flushMs = new float[Capacity];
    private long _count;

    // Wall-clock time for this diagnostic only: nothing read here goes back into the simulation.
#pragma warning disable RS0030
    /// <summary>A mark to measure from with <see cref="MillisecondsSince"/>.</summary>
    public static long Mark() => System.Diagnostics.Stopwatch.GetTimestamp();

    public static double MillisecondsSince(long mark) => System.Diagnostics.Stopwatch.GetElapsedTime(mark).TotalMilliseconds;
#pragma warning restore RS0030

    /// <summary>Ticks added so far.</summary>
    public long Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>One tick: its whole time and the part spent in the replication pass and flush (0 on a tick without one).</summary>
    public void Add(double tickMs, double flushMs)
    {
        lock (_gate)
        {
            int at = (int)(_count % Capacity);
            _tickMs[at] = (float)tickMs;
            _flushMs[at] = (float)flushMs;
            _count++;
        }
    }

    /// <summary>The last <paramref name="ticks"/> ticks (fewer when fewer are kept).</summary>
    public TickSummary Recent(int ticks)
    {
        float[] times;
        double flush = 0;
        lock (_gate)
        {
            int n = (int)Math.Min(Math.Clamp(ticks, 0, Capacity), _count);
            times = new float[n];
            for (int i = 0; i < n; i++)
            {
                int at = (int)((_count - 1 - i) % Capacity);
                times[i] = _tickMs[at];
                flush += _flushMs[at];
            }
        }

        if (times.Length == 0)
        {
            return default;
        }

        Array.Sort(times);
        double sum = 0;
        int slow = 0;
        foreach (float ms in times)
        {
            sum += ms;
            if (ms > SlotMs)
            {
                slow++;
            }
        }

        return new TickSummary(times.Length, sum / times.Length, times[times.Length / 2],
            times[Math.Min(times.Length - 1, (int)(times.Length * 0.99))], times[^1], slow, flush / times.Length);
    }
}

/// <summary>
/// A window of server ticks, in milliseconds: <paramref name="Slow"/> counts the ticks longer than their slot
/// (<see cref="TickTimes.SlotMs"/>), <paramref name="FlushMeanMs"/> is the replication and flush share of the mean.
/// </summary>
public readonly record struct TickSummary(int Ticks, double MeanMs, double P50Ms, double P99Ms, double MaxMs, int Slow, double FlushMeanMs)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Ticks} ticks, mean {MeanMs:F2} ms (replication {FlushMeanMs:F2}), p50 {P50Ms:F2}, p99 {P99Ms:F2}, max {MaxMs:F2}, {Slow} over the {TickTimes.SlotMs:F1} ms slot");
}
