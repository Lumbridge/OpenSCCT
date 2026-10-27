using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Net;

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
        if (_count == 0)
        {
            location = default;
            rotation = default;
            velocity = default;
            return false;
        }

        ref Snapshot oldest = ref At(0);
        if (renderTimeFixed <= oldest.Tick * NetTimeline.Fraction)
        {
            location = oldest.Location;
            rotation = oldest.Rotation;
            velocity = oldest.Velocity;
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
            location = newest.Location + newest.Velocity * seconds;
            rotation = newest.Rotation;
            velocity = newest.Velocity;
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
        location = a.Location + (b.Location - a.Location) * alpha;
        velocity = a.Velocity + (b.Velocity - a.Velocity) * alpha;
        rotation = new FRotator(
            LerpAxis(a.Rotation.Pitch, b.Rotation.Pitch, alpha),
            LerpAxis(a.Rotation.Yaw, b.Rotation.Yaw, alpha),
            LerpAxis(a.Rotation.Roll, b.Rotation.Roll, alpha));
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

    private static int LerpAxis(int from, int to, float alpha)
    {
        int delta = FRotator.NormalizeAxis(to - from);
        return FRotator.ClampAxis(from + SvmMath.Round(delta * alpha));
    }
}
