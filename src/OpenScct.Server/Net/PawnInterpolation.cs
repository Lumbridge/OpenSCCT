using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// Enhanced proxy pawns (<see cref="PawnProxyMode.Interpolate"/>, rl:net.clientsync; docs/NETCODE.md "Proxy pawn
/// interpolation"): each other player's pawn is shown from a buffer of received states (<see cref="ProxyMotion"/>) at the
/// client's render time, about two server send intervals in the past, in place of the Stock dead reckoning of
/// <see cref="PawnProxyReceiver.Tick"/>.
/// <para>
/// The server keeps the Stock sample schedule: it sends a position sample only when its own dead reckoning from the
/// last one fails (spec/native/net-proxy.md rule 2). So every packet from the server that brings no sample for a pawn
/// says that prediction still holds, and the client buffers one state per new packet tick: the sample when one came,
/// else the prediction from the newest state (gravity while falling, as the server predicts), each with the latest
/// received Rotation and aim words (the server pushes those every pass for this mode). The pawn tick interpolates
/// position, velocity (the animation's input), rotation and aim between the two states around the render time, and
/// extrapolates by velocity for at most <see cref="NetDriverSettings.MaxExtrapolationTicks"/> when the buffer runs dry.
/// </para>
/// <para>A snap (the buffer reset to the newest state) happens only on a teleport (a sample more than six collision
/// radii from where the buffer had the pawn) and on the first sample. Root motion and follow-parent run the stock
/// physics (rule 12) while the buffer holds the pawn where it is shown, so leaving them is seamless; other physics
/// changes ride the buffer (rule 11's snap would pop the pawn forward by the delay, as Physics arrives undelayed).</para>
/// </summary>
[Provenance(ProvenanceKind.Estimated, "OpenSCCT addition (rl:net.clientsync, Enhanced): snapshot interpolation of proxy pawns over the decoded sample schedule")]
public sealed class PawnInterpolation
{
    private sealed class Track
    {
        public readonly ProxyMotion Motion = new();

        /// <summary>A sample arrived since the last push.</summary>
        public bool HasSample;
        public long SampleTick;
        public FVector SampleLocation;
        public FVector SampleVelocity;

        /// <summary>The latest received aim words (iDesiredYawPitch, iYawPitchFromView).</summary>
        public int Aim;
        public int View;

        /// <summary>The displayed aim words kept across a received bunch.</summary>
        public int ShownAim;
        public int ShownView;
    }

    private readonly Dictionary<SBasePawn, Track> _tracks = new(ReferenceEqualityComparer.Instance);
    private readonly List<SBasePawn> _gone = new();
    private long _newestTick = -1;

    public PawnInterpolation(int maxExtrapolationTicks)
    {
        MaxExtrapolationTicks = Math.Max(0, maxExtrapolationTicks);
    }

    /// <summary>Ticks a pawn may be extrapolated past its newest state.</summary>
    public int MaxExtrapolationTicks { get; }

    /// <summary>The server time shown this tick (server ticks times <see cref="NetTimeline.Fraction"/>), or -1.</summary>
    public long RenderTimeFixed { get; set; } = -1;

    /// <summary>Pawns with a buffer.</summary>
    public int Count => _tracks.Count;

    private Track Get(SBasePawn pawn)
    {
        if (!_tracks.TryGetValue(pawn, out Track? t))
        {
            t = new Track { Aim = pawn.iDesiredYawPitch, View = pawn.iYawPitchFromView };
            _tracks[pawn] = t;
        }

        return t;
    }

    /// <summary>A position sample for <paramref name="pawn"/> sent at server tick <paramref name="tick"/>.</summary>
    internal void OnSample(SBasePawn pawn, long tick, FVector location, FVector velocity)
    {
        Track t = Get(pawn);
        t.HasSample = true;
        t.SampleTick = tick;
        t.SampleLocation = location;
        t.SampleVelocity = velocity;
    }

    /// <summary>
    /// Before a bunch of <paramref name="pawn"/>: the latest received aim words go back on the pawn, so after the bunch
    /// the pawn holds the received ones whether or not the bunch had them (as <see cref="ProxyMotion"/> does for
    /// replicated movement).
    /// </summary>
    internal void BeginReceive(SBasePawn pawn)
    {
        Track t = Get(pawn);
        t.ShownAim = pawn.iDesiredYawPitch;
        t.ShownView = pawn.iYawPitchFromView;
        pawn.iDesiredYawPitch = t.Aim;
        pawn.iYawPitchFromView = t.View;
    }

    /// <summary>After the bunch: the pawn's aim words become the latest received ones and the shown ones come back.</summary>
    internal void EndReceive(SBasePawn pawn)
    {
        Track t = Get(pawn);
        t.Aim = pawn.iDesiredYawPitch;
        t.View = pawn.iYawPitchFromView;
        pawn.iDesiredYawPitch = t.ShownAim;
        pawn.iYawPitchFromView = t.ShownView;
    }

    /// <summary>
    /// After the client received this tick's packets (<paramref name="newestTick"/>: the newest server tick seen):
    /// buffers each pawn's state at that tick.
    /// </summary>
    public void AfterReceive(long newestTick)
    {
        _newestTick = Math.Max(_newestTick, newestTick);
        foreach (KeyValuePair<SBasePawn, Track> kv in _tracks)
        {
            SBasePawn pawn = kv.Key;
            Track t = kv.Value;
            if (pawn.bDeleteMe || pawn.Role != ENetRole.ROLE_SimulatedProxy)
            {
                _gone.Add(pawn);
                continue;
            }

            if (pawn.Physics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_FollowParent)
            {
                // Rule 12: the stock physics moves it; Follow keeps the buffer on it.
                t.HasSample = false;
                continue;
            }

            Buffer(pawn, t);
        }

        foreach (SBasePawn pawn in _gone)
        {
            _tracks.Remove(pawn);
        }

        _gone.Clear();
    }

    private void Buffer(SBasePawn pawn, Track t)
    {
        bool hasNewest = t.Motion.TryGetNewest(out long newestTick, out ProxyState newest);
        long tick = _newestTick;
        if (hasNewest)
        {
            tick = Math.Max(tick, newestTick);
        }

        if (t.HasSample)
        {
            tick = Math.Max(tick, t.SampleTick);
        }
        else if (!hasNewest || tick <= newestTick)
        {
            return;
        }

        bool falling = pawn.Physics == EPhysics.PHYS_Falling;
        ProxyState state;
        bool snap = !hasNewest;
        if (t.HasSample)
        {
            // A sample from an older packet (a reliable one that waited) is carried to the newest tick.
            state = Predict(t.SampleLocation, t.SampleVelocity, tick - t.SampleTick, falling);
            if (hasNewest)
            {
                ProxyState expected = Predict(newest.Location, newest.Velocity, tick - newestTick, falling);
                snap = (state.Location - expected.Location).SizeSquared() > Sq(PawnProxyReceiver.SnapRadii * pawn.CollisionRadius);
            }
        }
        else
        {
            state = Predict(newest.Location, newest.Velocity, tick - newestTick, falling);
        }

        t.HasSample = false;
        state.Rotation = pawn.ReceivedRotation;
        state.Aim = t.Aim;
        state.View = t.View;
        if (snap)
        {
            Snap(t, tick, state);
        }
        else
        {
            t.Motion.Push(tick, state);
        }
    }

    /// <summary>
    /// Resets the buffer to <paramref name="state"/> at <paramref name="tick"/>. When the render time is behind it, a
    /// state backed off along the velocity is put at the render time too, so the pawn moves on from the snap at once
    /// instead of waiting there for the delay.
    /// </summary>
    private void Snap(Track t, long tick, in ProxyState state)
    {
        t.Motion.Reset();
        long renderTick = RenderTimeFixed >= 0 ? RenderTimeFixed / NetTimeline.Fraction : tick;
        if (renderTick < tick)
        {
            ProxyState back = state;
            back.Location = state.Location - state.Velocity * ((tick - renderTick) * Sim.DeltaTime);
            t.Motion.Push(renderTick, back);
        }

        t.Motion.Push(tick, state);
    }

    /// <summary>
    /// Rule 12: in root motion or following a parent the buffer holds where the stock physics put the pawn, at the render
    /// time, so when the pawn leaves that physics the buffer carries on from where it is shown (the forced sample of the
    /// physics change then arrives as an ordinary state, or as a teleport).
    /// </summary>
    internal void Follow(SBasePawn pawn)
    {
        long tick = RenderTimeFixed >= 0 ? RenderTimeFixed / NetTimeline.Fraction : _newestTick;
        if (!_tracks.TryGetValue(pawn, out Track? t) || tick < 0)
        {
            return;
        }

        t.Motion.Reset();
        t.Motion.Push(tick, new ProxyState
        {
            Location = pawn.Location, Rotation = pawn.Rotation, Velocity = pawn.Velocity, Aim = t.Aim, View = t.View,
        });
    }

    /// <summary>The pawn's state at the render time; false before its first sample or before the render time is known.</summary>
    internal bool TrySample(SBasePawn pawn, out ProxyState state)
    {
        if (RenderTimeFixed < 0 || !_tracks.TryGetValue(pawn, out Track? t))
        {
            state = default;
            return false;
        }

        return t.Motion.Sample(RenderTimeFixed, MaxExtrapolationTicks, out state);
    }

    /// <summary>The server's own dead reckoning (net-proxy.md rule 2): velocity, and gravity while falling.</summary>
    private static ProxyState Predict(FVector location, FVector velocity, long ticks, bool falling)
    {
        float dt = ticks * Sim.DeltaTime;
        FVector l = location + velocity * dt;
        FVector v = velocity;
        if (falling)
        {
            l.Z -= PawnProxySender.PredictionGravity * 0.5f * dt * dt;
            v.Z -= PawnProxySender.PredictionGravity * dt;
        }

        return new ProxyState { Location = l, Velocity = v };
    }

    private static float Sq(float x) => x * x;
}
