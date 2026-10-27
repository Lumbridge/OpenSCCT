namespace OpenScct.Net;

/// <summary>Counters and link estimates for one connection.</summary>
public sealed class NetStats
{
    public long InBytes { get; internal set; }

    public long OutBytes { get; internal set; }

    public long InPackets { get; internal set; }

    public long OutPackets { get; internal set; }

    /// <summary>Gaps in the sequence numbers received (packets the other side sent that never arrived).</summary>
    public long InPacketsLost { get; internal set; }

    /// <summary>Packets dropped on arrival because a newer one had already been processed (late or duplicated).</summary>
    public long InPacketsOld { get; internal set; }

    public long InMalformed { get; internal set; }

    /// <summary>Packets we sent that the other side reported missing (or that timed out).</summary>
    public long OutPacketsLost { get; internal set; }

    public long InBunches { get; internal set; }

    public long OutBunches { get; internal set; }

    public long OutReliableResends { get; internal set; }

    /// <summary>True once a round trip sample has arrived.</summary>
    public bool HasRtt { get; private set; }

    /// <summary>Smoothed round trip time in microseconds (EWMA, 1/8 weight).</summary>
    public long RttMicros { get; private set; }

    /// <summary>Smoothed absolute change between consecutive round trip samples, in microseconds.</summary>
    public long JitterMicros { get; private set; }

    /// <summary>Last raw round trip sample in microseconds.</summary>
    public long LastRttMicros { get; private set; }

    internal void AddRttSample(long micros)
    {
        if (!HasRtt)
        {
            HasRtt = true;
            RttMicros = micros;
            JitterMicros = 0;
        }
        else
        {
            long delta = micros - LastRttMicros;
            if (delta < 0)
            {
                delta = -delta;
            }

            RttMicros += (micros - RttMicros) / 8;
            JitterMicros += (delta - JitterMicros) / 8;
        }

        LastRttMicros = micros;
    }
}
