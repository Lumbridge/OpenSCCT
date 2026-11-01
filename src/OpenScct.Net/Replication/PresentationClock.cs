using System;
using OpenScct.Core;

namespace OpenScct.Net;

/// <summary>A monotonic playout clock. Buffer changes adjust speed, never rewind displayed time.</summary>
public sealed class PresentationClock
{
    private const int Window = 120;
    private const long ResyncDistance = 8 * NetTimeline.Fraction;
    private readonly int[] _lateness = new int[Window];
    private long _lastServer = -1;
    private long _lastLocal;
    private int _next;
    private int _count;
    private long _time = -1;
    private long _lastAdvance = -1;

    public long TimeFixed => _time;
    public int DelayTicks { get; private set; } = 2;
    public long Underflows { get; private set; }

    public void Observe(long serverTick, long localTick)
    {
        if (serverTick <= _lastServer) return;
        if (_lastServer >= 0)
        {
            long late = localTick - _lastLocal - (serverTick - _lastServer);
            _lateness[_next] = (int)Math.Clamp(late, 0, 6);
            _next = (_next + 1) % Window;
            _count = Math.Min(_count + 1, Window);
        }

        _lastServer = serverTick;
        _lastLocal = localTick;
    }

    public long Advance(long localTick, NetTimeline timeline, int minimumDelayTicks)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (!timeline.IsValid) return _time;
        int baseDelay = Math.Clamp(minimumDelayTicks, 2, 6);
        // The 95th percentile avoids permanently filling the buffer for one exceptional packet.
        int rank = (_count * 95 + 99) / 100;
        int jitter = 0;
        for (int value = 0; value <= 6 && rank > 0; value++)
        {
            int below = 0;
            for (int i = 0; i < _count; i++) if (_lateness[i] <= value) below++;
            if (below >= rank) { jitter = value; break; }
        }

        DelayTicks = Math.Clamp(baseDelay + jitter, 2, 6);
        long desired = timeline.RenderTimeFixed(localTick, DelayTicks);
        if (_lastAdvance < 0)
        {
            _time = desired;
        }
        else if (localTick > _lastAdvance)
        {
            long step = (localTick - _lastAdvance) * NetTimeline.Fraction;
            long error = desired - (_time + step);
            // Scene loading can stop client ticks while the server keeps running. Slewing a seconds-long gap
            // at 1/8 tick per tick would show stale poses and interaction targets for eight times that pause.
            // Recover forward discontinuities immediately; ordinary jitter still slews and time never rewinds.
            _time = error > ResyncDistance ? desired : _time + step + Math.Clamp(error, -step / 8, step / 8);
        }

        _lastAdvance = localTick;
        if (_lastServer >= 0 && _time > _lastServer * NetTimeline.Fraction) Underflows++;
        return _time;
    }

    public float DelayMs => DelayTicks * Sim.DeltaTime * 1000f;
}
