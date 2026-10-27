using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Net;

/// <summary>
/// One buffered state of a replicated actor. <see cref="Aim"/> and <see cref="View"/> are optional packed angle words
/// (yaw in the low 16 bits, pitch in the high 16, as a pawn's iDesiredYawPitch and iYawPitchFromView); each half is
/// interpolated the shorter way. Actors without them leave them 0.
/// </summary>
public struct ProxyState
{
    public FVector Location;
    public FRotator Rotation;
    public FVector Velocity;
    public int Aim;
    public int View;
}

/// <summary>
/// Client-side smoothing for a replicated actor: keeps the replicated states it received, stamped with the
/// server tick, and shows the actor where it was at a render time a little in the past, interpolating between
/// the two states around it. Past the newest state it extrapolates with the replicated velocity for a few ticks,
/// then holds. The latest replicated values live here, apart from the displayed ones on the actor.
/// <para>A <c>net.proxy=ue2</c> mode (apply replicated values directly, UE2 simulated proxy extrapolation) will
/// sit beside this one.</para>
/// </summary>
public sealed class ProxyMotion
{
    public const int Capacity = 32;

    private struct Snapshot
    {
        public long Tick;
        public FVector Location;
        public FRotator Rotation;
        public FVector Velocity;
        public int Aim;
        public int View;
    }

    private readonly Snapshot[] _ring = new Snapshot[Capacity];
    private int _start;
    private int _count;
    private Snapshot _latest;
    private bool _hasLatest;

    public int SnapshotCount => _count;

    public bool HasState => _hasLatest;

    public long NewestTick => _count > 0 ? At(_count - 1).Tick : -1;

    public FVector LatestLocation => _latest.Location;

    public FRotator LatestRotation => _latest.Rotation;

    public FVector LatestVelocity => _latest.Velocity;

    private ref Snapshot At(int i) => ref _ring[(_start + i) & (Capacity - 1)];

    /// <summary>Puts the latest replicated values back on the actor before new replicated data is applied.</summary>
    internal void BeginReceive(Actor actor)
    {
        if (_hasLatest)
        {
            actor.Location = _latest.Location;
            actor.Rotation = _latest.Rotation;
            actor.Velocity = _latest.Velocity;
        }
    }

    /// <summary>Records the actor's replicated values as the state at server tick <paramref name="tick"/>.</summary>
    internal void EndReceive(Actor actor, long tick)
    {
        _latest = new Snapshot { Tick = tick, Location = actor.Location, Rotation = actor.Rotation, Velocity = actor.Velocity };
        _hasLatest = true;
        Push(_latest);
    }

    /// <summary>The server reported no change at <paramref name="tick"/>: the latest state still holds then.</summary>
    internal void Hold(long tick)
    {
        if (!_hasLatest)
        {
            return;
        }

        Snapshot held = _latest;
        held.Tick = tick;
        Push(held);
    }

    /// <summary>
    /// Adds <paramref name="state"/> as the state at server tick <paramref name="tick"/> (a state at the newest tick
    /// replaces it; an older one is dropped). For callers that buffer states themselves (pawn proxies, whose positions
    /// arrive as calls, not properties).
    /// </summary>
    public void Push(long tick, in ProxyState state)
    {
        _latest = new Snapshot
        {
            Tick = tick, Location = state.Location, Rotation = state.Rotation, Velocity = state.Velocity, Aim = state.Aim,
            View = state.View,
        };
        _hasLatest = true;
        Push(_latest);
    }

    /// <summary>Forgets every buffered state (the next one pushed is shown at once: a snap).</summary>
    public void Reset()
    {
        _start = 0;
        _count = 0;
        _hasLatest = false;
    }

    /// <summary>The newest buffered state and its tick; false when the buffer is empty.</summary>
    public bool TryGetNewest(out long tick, out ProxyState state)
    {
        if (_count == 0)
        {
            tick = -1;
            state = default;
            return false;
        }

        ref Snapshot n = ref At(_count - 1);
        tick = n.Tick;
        state = ToState(n);
        return true;
    }

    private static ProxyState ToState(in Snapshot s) => new()
    {
        Location = s.Location, Rotation = s.Rotation, Velocity = s.Velocity, Aim = s.Aim, View = s.View,
    };

    private void Push(in Snapshot snapshot)
    {
        if (_count > 0)
        {
            ref Snapshot newest = ref At(_count - 1);
            if (snapshot.Tick < newest.Tick)
            {
                return;
            }

            if (snapshot.Tick == newest.Tick)
            {
                newest = snapshot;
                return;
            }
        }

        if (_count == Capacity)
        {
            _start = (_start + 1) & (Capacity - 1);
            _count--;
        }

        At(_count) = snapshot;
        _count++;
    }

    /// <summary>
    /// The state at <paramref name="renderTimeFixed"/> (server ticks times <see cref="NetTimeline.Fraction"/>).
    /// False when nothing has been received.
    /// </summary>
    public bool Sample(long renderTimeFixed, int maxExtrapolationTicks, out FVector location, out FRotator rotation,
        out FVector velocity)
    {
        bool ok = Sample(renderTimeFixed, maxExtrapolationTicks, out ProxyState state);
        location = state.Location;
        rotation = state.Rotation;
        velocity = state.Velocity;
        return ok;
    }

    /// <summary>
    /// The whole state at <paramref name="renderTimeFixed"/>: interpolated between the two buffered states around it,
    /// the oldest before the buffer, and past the newest extrapolated by its velocity for at most
    /// <paramref name="maxExtrapolationTicks"/> ticks (rotation and aim held). False when nothing has been received.
    /// </summary>
    public bool Sample(long renderTimeFixed, int maxExtrapolationTicks, out ProxyState state)
    {
        if (_count == 0)
        {
            state = default;
            return false;
        }

        ref Snapshot oldest = ref At(0);
        if (renderTimeFixed <= oldest.Tick * NetTimeline.Fraction)
        {
            state = ToState(oldest);
            return true;
        }

        ref Snapshot newest = ref At(_count - 1);
        long newestFixed = newest.Tick * NetTimeline.Fraction;
        if (renderTimeFixed >= newestFixed)
        {
            long ahead = renderTimeFixed - newestFixed;
            long limit = (long)maxExtrapolationTicks * NetTimeline.Fraction;
            if (ahead > limit)
            {
                ahead = limit;
            }

            float seconds = ahead * (Sim.DeltaTime / NetTimeline.Fraction);
            state = ToState(newest);
            state.Location = newest.Location + newest.Velocity * seconds;
            return true;
        }

        int i = _count - 2;
        while (i > 0 && At(i).Tick * NetTimeline.Fraction > renderTimeFixed)
        {
            i--;
        }

        ref Snapshot a = ref At(i);
        ref Snapshot b = ref At(i + 1);
        long span = (b.Tick - a.Tick) * NetTimeline.Fraction;
        float alpha = (float)(renderTimeFixed - a.Tick * NetTimeline.Fraction) / span;
        state = new ProxyState
        {
            Location = a.Location + (b.Location - a.Location) * alpha,
            Velocity = a.Velocity + (b.Velocity - a.Velocity) * alpha,
            Rotation = new FRotator(
                LerpAxis(a.Rotation.Pitch, b.Rotation.Pitch, alpha),
                LerpAxis(a.Rotation.Yaw, b.Rotation.Yaw, alpha),
                LerpAxis(a.Rotation.Roll, b.Rotation.Roll, alpha)),
            Aim = LerpPacked(a.Aim, b.Aim, alpha),
            View = LerpPacked(a.View, b.View, alpha),
        };
        return true;
    }

    /// <summary>Moves the actor to its state at <paramref name="renderTimeFixed"/>.</summary>
    public void Apply(Actor actor, long renderTimeFixed, int maxExtrapolationTicks)
    {
        if (Sample(renderTimeFixed, maxExtrapolationTicks, out FVector location, out FRotator rotation, out FVector velocity))
        {
            actor.Location = location;
            actor.Rotation = rotation;
            actor.Velocity = velocity;
            ZoneRegions.SetActorZone(actor); // proxies keep their zone on the client too (spec/devices/zone-info.md rule 2)
        }
    }

    /// <summary>Both 16-bit angle halves of a packed word (low yaw, high pitch) interpolated the shorter way.</summary>
    private static int LerpPacked(int from, int to, float alpha)
    {
        if (from == to)
        {
            return to;
        }

        int low = LerpAxis(from & 0xFFFF, to & 0xFFFF, alpha) & 0xFFFF;
        int high = LerpAxis((from >> 16) & 0xFFFF, (to >> 16) & 0xFFFF, alpha) & 0xFFFF;
        return (high << 16) | low;
    }

    private static int LerpAxis(int from, int to, float alpha)
    {
        int delta = FRotator.NormalizeAxis(to - from);
        return FRotator.ClampAxis(from + SvmMath.Round(delta * alpha));
    }
}
