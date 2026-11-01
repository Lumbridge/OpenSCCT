using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;

namespace OpenScct.Server;

/// <summary>Explicit pawn transforms and channel phases. Reliable play descriptors never block newer snapshots.</summary>
public sealed class PawnStateReplication : INetworkPawnPose, IPawnAnimationTransitions
{
    public const int History = 128;
    public const int TransitionCapacity = 512;
    private const int PhaseRecoveryTicks = 4;
    private const string Snapshot = "SvmPawnSnapshot";
    private const string Animation = "SvmPawnAnimation";
    private const string Presentation = "SvmPawnPresentation";
    private readonly NetDriver _driver;
    private readonly MercShockStream _shock;
    private readonly Dictionary<SBasePawn, Track> _tracks = new(ReferenceEqualityComparer.Instance);
    private readonly List<SBasePawn> _gone = new();
    private readonly Dictionary<ActorChannel, Viewer> _viewers = new(ReferenceEqualityComparer.Instance);
    private readonly List<ActorChannel> _closed = new();
    private readonly Frame _discard = new();
    private long _sourceTick = -1;

    public PawnStateReplication(NetDriver driver)
    {
        _driver = driver;
        _shock = MercShockStream.For(driver);
        driver.World!.Level.SetService(this);
        if (!driver.IsServer) driver.World.Level.SetService<INetworkPawnPose>(this);
        else driver.World.Level.SetService<IPawnAnimationTransitions>(this);
    }

    public long RenderTimeFixed { get; set; } = -1;
    public long SnapshotsSent { get; private set; }
    public long SnapshotsReceived { get; private set; }
    public long LatestSnapshotTick { get; private set; } = -1;
    internal long SourceTick => _sourceTick;
    public long AnimationRecords { get; private set; }
    public long SnapshotPayloadBytes { get; private set; }
    public long AnimationPayloadBytes { get; private set; }
    public long PresentationPayloadBytes { get; private set; }
    public long PresentationRecordsSent { get; private set; }
    public long PresentationRecordsReceived { get; private set; }
    public long ShockStatusRecordsSent => _shock.RecordsSent;
    public long StateGaps { get; private set; }
    public long MissingDescriptors { get; private set; }
    public long Resets { get; private set; }
    public long Underflows { get; private set; }
    public long[] RevisionChanges { get; } = new long[PawnAnimation.ChannelCount];
    public long BudgetDeferrals { get; private set; }
    public long ReliableDeferrals { get; private set; }
    public long HistoryResyncs { get; private set; }
    /// <summary>Diagnostic notification after the reliable receiver has deduplicated a transition.</summary>
    public event Action<SBasePawn, int, NetworkAnimChannel>? TransitionReceived;

    public long NewestTick(SBasePawn pawn) => _tracks.TryGetValue(pawn, out Track? t) ? t.Newest : -1;

    internal bool TryGetShockSample(SBasePawn pawn, long time, out MercShockSample sample)
    {
        sample = default;
        if (!_tracks.TryGetValue(pawn, out Track? track) || !track.Ready || track.Newest < 0 || track.ShockLocalReset != pawn.NetworkResetId) return false;
        Frame? frame = null;
        foreach (Frame candidate in track.Frames)
            if (candidate.Tick >= 0 && candidate.Epoch == track.Epoch && candidate.Tick * NetTimeline.Fraction <= time &&
                (frame is null || candidate.Tick > frame.Tick)) frame = candidate;
        frame ??= track.Frames[Slot(track.Newest)];
        sample = frame.Shock;
        return true;
    }

    /// <summary>Whether transform samples bracket display time, independent of animation phase delivery.</summary>
    public bool IsMotionBuffered(SBasePawn pawn, long time) => _tracks.TryGetValue(pawn, out Track? track) &&
        track.Motion.OldestTick >= 0 && track.Motion.OldestTick * NetTimeline.Fraction <= time &&
        track.Motion.NewestTick * NetTimeline.Fraction >= time;

    public bool TryGetMotionInterval(SBasePawn pawn, long fromTime, long toTime, out long firstTick, out long lastTick)
    {
        firstTick = lastTick = -1;
        return _tracks.TryGetValue(pawn, out Track? track) && track.Motion.TryGetInterval(fromTime, toTime, out firstTick, out lastTick);
    }

    /// <summary>Exact recorded motion for diagnostics that compare rendered placement with authoritative history.</summary>
    public bool TryGetRecordedMotion(SBasePawn pawn, long tick, out ProxyState motion, out FVector pivot, out EPhysics physics)
    {
        if (_tracks.TryGetValue(pawn, out Track? track) && track.Frames[Slot(tick)] is { } frame &&
            frame.Tick == tick && frame.Epoch == track.Epoch)
        {
            motion = frame.Motion; pivot = frame.PrePivot; physics = frame.Physics; return true;
        }
        motion = default; pivot = default; physics = default; return false;
    }

    /// <summary>Whether display time has resolved descriptors and tick samples, rather than an underflow estimate.</summary>
    public bool IsPoseBuffered(SBasePawn pawn, long time)
    {
        if (!_tracks.TryGetValue(pawn, out Track? track) || !track.Ready) return false;
        if (track.StateSequence < track.Frames[Slot(track.Newest)].StateSequence) return false;
        for (int i = 0; i < track.Channels.Length; i++)
            if (!track.Channels[i].Buffered(time, i is 0 or 4 or 5)) return false;
        return true;
    }

    public long OldestTick(SBasePawn pawn)
    {
        if (!_tracks.TryGetValue(pawn, out Track? t)) return -1;
        long oldest = long.MaxValue;
        foreach (Frame frame in t.Frames) if (frame.Tick >= 0) oldest = Math.Min(oldest, frame.Tick);
        return oldest == long.MaxValue ? -1 : oldest;
    }

    /// <summary>Pin authored changes to the host tick, even while movement temporarily uses a command clock.</summary>
    public void BeginServerTick() => _sourceTick = _driver.World!.TickCount + 1;

    /// <summary>Advance presentation and release the bounded histories of destroyed or irrelevant client pawns.</summary>
    public void AfterReceive(long renderTimeFixed)
    {
        RenderTimeFixed = renderTimeFixed;
        PruneDeleted();
    }

    private void PruneDeleted()
    {
        foreach (var kv in _tracks) if (kv.Key.bDeleteMe) _gone.Add(kv.Key);
        foreach (SBasePawn pawn in _gone)
        {
            pawn.StickyShockSfx(false);
            _tracks.Remove(pawn);
        }
        _gone.Clear();
    }

    public static RepExtension CreateExtension()
    {
        var ext = new RepExtension();
        ext.Functions.Add(new(Snapshot, false, false, "pawn-snapshot-21", ReceiveSnapshot));
        ext.Functions.Add(new(Animation, true, false, "pawn-animation-5", ReceiveAnimation));
        ext.Functions.Add(new(Presentation, true, false, "pawn-presentation-6", ReceivePresentation));
        ext.Functions.Add(MercShockStream.Extension());
        return ext;
    }

    private sealed class Frame
    {
        public long Tick = -1;
        public uint Epoch;
        public ProxyState Motion;
        public EPhysics Physics;
        public byte Zone, Weapon, Ik, Btw, Pipe;
        public int Health;
        public bool Crouched, Lying, Sleeping, Hidden;
        public bool ReadyBoost, ReadyHoist;
        public bool Grabbable, Terminable, StickyShocking;
        public MercShockSample Shock;
        public bool StateChanged;
        public uint StateSequence;
        public uint BaseCode, TargetCode, AttackerCode, UsedCode, InteractionCode, ReleasedCode, TerminateCode;
        public NetworkPawnAttachments Attachments;
        public float Radius, Height;
        public FVector PrePivot;
        public Actor? Base, UsedObject;
        public SBasePawn? Target, Attacker, Interaction, Released, Terminate;
        public PawnRootMotion.State Root;
        public readonly NetworkAnimChannel[] Pose = new NetworkAnimChannel[PawnAnimation.ChannelCount];
        public readonly ushort[] Revisions = new ushort[PawnAnimation.ChannelCount];
    }

    private sealed class Track
    {
        // Visual lifecycle is independent of bStickySkinned: drying/phospho expiry can restore skins while
        // the seven electric emitters must survive until the sampled shock ends. Keep this across history resets.
        public bool ElectricFxActive;
        public uint Epoch;
        public uint ShockLocalReset;
        public long Newest = -1;
        public bool Ready;
        public readonly ProxyMotion Motion = new();
        public readonly Frame[] Frames = new Frame[History];
        public readonly ChannelHistory[] Channels = new ChannelHistory[PawnAnimation.ChannelCount];
        public readonly Transition[]? Transitions;
        public long TransitionSequence;
        public uint StateSequence;
        public readonly NetworkAnimChannel[] LastPose = new NetworkAnimChannel[PawnAnimation.ChannelCount];
        public readonly ushort[] Revisions = new ushort[PawnAnimation.ChannelCount];
        public Track(bool server)
        {
            if (server) Transitions = new Transition[TransitionCapacity];
            for (int i = 0; i < History; i++) Frames[i] = new Frame();
            for (int i = 0; i < Channels.Length; i++) Channels[i] = new ChannelHistory();
        }
        public void Reset(uint epoch)
        {
            Epoch = epoch; Newest = -1; Ready = false; Motion.Reset();
            foreach (Frame f in Frames) f.Tick = -1;
            foreach (ChannelHistory c in Channels) c.Reset();
            TransitionSequence = 0; StateSequence = 0; Array.Clear(Revisions); Array.Clear(LastPose);
        }
    }

    private sealed class Viewer
    {
        public uint Epoch;
        public uint WireEpoch;
        public long SentTick = -1, Checkpoint = -1, SnapshotTick = -1;
        public int Channel;
        public bool Baseline;
        public long TransitionSequence;
        public long StateTick = -1;
        public bool StateBaseline;
        public EPhysics LastPhysics;
        public byte LastZone;
        public readonly ushort[] Revisions = new ushort[PawnAnimation.ChannelCount];
    }

    private struct Transition
    {
        public long Sequence, Tick;
        public int Channel;
        public ushort Revision;
        public NetworkAnimChannel State;
        public bool Authored;
    }

    private struct Phase
    {
        public long Tick;
        public ushort Revision;
        public float Frame, Rate, Alpha, Tween;
        public bool Snapshot;
    }

    private sealed class ChannelHistory
    {
        private readonly NetworkAnimChannel[] _descriptors = new NetworkAnimChannel[History];
        private readonly ushort[] _versions = new ushort[History];
        private readonly bool[] _valid = new bool[History];
        private readonly long[] _descriptorTicks = new long[History];
        private readonly Phase[] _phases = new Phase[History];
        private int _count;
        public void Reset() { _count = 0; Array.Clear(_valid); }
        public void Describe(ushort revision, in NetworkAnimChannel state, long tick)
        {
            int i = revision % History;
            _descriptors[i] = state; _versions[i] = revision; _valid[i] = true;
            _descriptorTicks[i] = tick;
            Push(new Phase { Tick = tick, Revision = revision, Frame = state.Frame, Rate = state.Rate, Alpha = state.Alpha, Tween = state.TweenWeight });
        }
        public Name SequenceAt(long time)
        {
            Name name = default;
            long latest = -1;
            ushort revision = 0;
            for (int i = 0; i < History; i++)
                if (_valid[i] && (_descriptorTicks[i] > latest || (_descriptorTicks[i] == latest &&
                    (short)(_versions[i] - revision) > 0)) && _descriptorTicks[i] * NetTimeline.Fraction <= time)
                {
                    latest = _descriptorTicks[i]; revision = _versions[i]; name = _descriptors[i].Sequence;
                }
            return name;
        }
        public bool HasDescriptor(ushort revision) => revision == 0 ||
            (_valid[revision % History] && _versions[revision % History] == revision);
        public void Push(in Phase phase)
        {
            int i = _count;
            while (i > 0 && _phases[i - 1].Tick > phase.Tick) i--;
            if (i > 0 && _phases[i - 1].Tick == phase.Tick)
            {
                Phase prior = _phases[i - 1];
                // Snapshots describe the completed tick. A delayed intermediate descriptor at that same tick
                // must not overwrite its final phase, and duplicate/older descriptors cannot restart it.
                if ((prior.Snapshot && !phase.Snapshot) || (!phase.Snapshot &&
                    (short)(phase.Revision - prior.Revision) <= 0)) return;
                _phases[i - 1] = phase; return;
            }
            if (_count == History)
            {
                if (i == 0) return;
                Array.Copy(_phases, 1, _phases, 0, History - 1);
                _count--; i--;
            }
            if (i < _count) Array.Copy(_phases, i, _phases, i + 1, _count - i);
            _phases[i] = phase; _count++;
        }
        private bool Resolve(in Phase phase, out NetworkAnimChannel state)
        {
            // Revision zero is an explicit invisible channel sample, independent of descriptor delivery.
            if (phase.Revision == 0) { state = default; return true; }
            int slot = phase.Revision % History;
            state = _descriptors[slot];
            if (!_valid[slot] || _versions[slot] != phase.Revision) return false;
            state.Frame = phase.Frame; state.Rate = phase.Rate; state.Alpha = phase.Alpha; state.TweenWeight = phase.Tween;
            return true;
        }
        public bool Buffered(long time, bool retainPhase)
        {
            long tick = time / NetTimeline.Fraction;
            long end = tick + (time % NetTimeline.Fraction == 0 ? 0 : 1);
            int before = -1;
            for (int i = 0; i < _count && _phases[i].Tick <= tick; i++) before = i;
            if (before < 0 || !Resolve(_phases[before], out NetworkAnimChannel state)) return false;
            if (state.Sequence.IsNone || (!retainPhase && state.Alpha <= 0)) return true;
            if (_phases[before].Tick != tick) return false;
            if (end == tick) return true;
            return before + 1 < _count && _phases[before + 1].Tick == end && Resolve(_phases[before + 1], out _);
        }
        public bool Sample(long time, float speed, SBasePawn pawn, out NetworkAnimChannel state)
        {
            state = default;
            int before = -1;
            for (int i = 0; i < _count && _phases[i].Tick * NetTimeline.Fraction <= time; i++)
                if (Resolve(_phases[i], out _)) before = i;
            if (before < 0) return false;
            Phase a = _phases[before];
            Resolve(a, out state);
            for (int j = before + 1; j < _count; j++)
            {
                Phase b = _phases[j];
                if (!Resolve(b, out NetworkAnimChannel next)) continue;
                if (a.Revision == b.Revision)
                {
                    float u = (time - a.Tick * NetTimeline.Fraction) / (float)((b.Tick - a.Tick) * NetTimeline.Fraction);
                    state = NetworkAnimChannel.Interpolate(state, next, Math.Clamp(u, 0f, 1f));
                    return true;
                }
                break;
            }
            float seconds = Math.Clamp((time - a.Tick * NetTimeline.Fraction) * (Sim.DeltaTime / NetTimeline.Fraction), 0f, 0.1f);
            PawnClip? clip = state.Sequence.IsNone ? null : PawnActions.Clip(pawn, state.Sequence);
            state = state.Extrapolate(seconds, speed, clip is { Frames: > 1 } ? 1f - 1f / clip.Frames : 0f);
            return true;
        }
    }

    private Track Get(SBasePawn pawn)
    {
        // A client's actor channel destroys its copy on relevancy loss. Retire that lifetime's bounded rings.
        if (!_driver.IsServer)
        {
            foreach (var kv in _tracks) if (kv.Key.bDeleteMe) _gone.Add(kv.Key);
            foreach (SBasePawn gone in _gone) _tracks.Remove(gone);
            _gone.Clear();
        }
        if (!_tracks.TryGetValue(pawn, out Track? track)) _tracks.Add(pawn, track = new Track(_driver.IsServer));
        return track;
    }

    public void Authored(SBasePawn pawn, int channel, in NetworkAnimChannel state)
    {
        if (!_driver.IsServer || _driver.Settings.PawnProxies != PawnProxyMode.Snapshots || pawn.bDeleteMe) return;
        Track t = Get(pawn);
        if (t.Epoch != pawn.NetworkResetId) t.Reset(pawn.NetworkResetId);
        AppendTransition(t, channel, state, _sourceTick >= 0 ? _sourceTick : pawn.XLevel.TickCount, authored: true);
    }

    private void AppendTransition(Track t, int channel, NetworkAnimChannel state, long tick, bool authored = false)
    {
        if (channel is >= 2 and <= 13) state.Serial = 0;
        ushort revision = (ushort)(t.Revisions[channel] + 1);
        if (revision == 0) revision = 1;
        t.Revisions[channel] = revision; t.LastPose[channel] = state; RevisionChanges[channel]++;
        long sequence = ++t.TransitionSequence;
        t.Transitions![(int)(sequence & (TransitionCapacity - 1))] = new Transition
        {
            Sequence = sequence, Tick = tick, Channel = channel, Revision = revision, State = state, Authored = authored,
        };
    }

    /// <summary>Record every world tick, including ticks between replication passes.</summary>
    public void Record()
    {
        _shock.Record();
        if (!_driver.IsServer || _driver.Settings.PawnProxies != PawnProxyMode.Snapshots) return;
        foreach (SBasePawn pawn in _driver.World!.Level.DynamicActors<SBasePawn>())
        {
            if (pawn.bDeleteMe) continue;
            Track t = Get(pawn);
            uint epoch = pawn.NetworkResetId;
            if (t.Newest >= 0 && (pawn.Location - t.Frames[Slot(t.Newest)].Motion.Location).SizeSquared() > 40000f)
            {
                pawn.ResetNetworkHistory(); epoch = pawn.NetworkResetId;
            }
            if (t.Epoch != epoch) t.Reset(epoch);
            long tick = pawn.XLevel.TickCount;
            Frame f = t.Frames[Slot(tick)];
            for (int i = 0; i < f.Pose.Length; i++)
            {
                f.Pose[i] = NetworkAnimChannel.Capture(pawn.PawnAnim[i]);
                // The locomotion blender copies its channels each tick. A copy serial is not a new authored play.
                if (i is >= 2 and <= 13) f.Pose[i].Serial = 0;
                // The journal catches intermediate plays; this comparison also catches direct channel-field
                // changes made by authored state code outside the animation API.
                bool becameVisible = i is >= 2 and <= 13 && i is not (4 or 5) &&
                    t.LastPose[i].Alpha <= 0f && f.Pose[i].Alpha > 0f;
                if (t.Revisions[i] == 0 || !f.Pose[i].SamePlay(t.LastPose[i]) || becameVisible)
                    AppendTransition(t, i, f.Pose[i], tick);
                t.LastPose[i] = f.Pose[i];
                f.Revisions[i] = t.Revisions[i];
            }
            Capture(pawn, f);
            f.StateChanged = t.Newest < 0 || !SameBody(f, t.Frames[Slot(t.Newest)]);
            f.StateSequence = f.StateChanged ? ++t.StateSequence : t.StateSequence;
            f.Tick = tick; f.Epoch = epoch; t.Newest = tick;
        }
        PruneDeleted();
        foreach (var kv in _viewers) if (kv.Key.Closing || kv.Key.Connection.State == EConnectionState.USOCK_Closed) _closed.Add(kv.Key);
        foreach (ActorChannel channel in _closed) _viewers.Remove(channel);
        _closed.Clear();
    }

    public void Send(SBasePawn pawn, NetConnection connection, int snapshotInterval = 2)
    {
        pawn.bCompressedPosition = true;
        if (!_tracks.TryGetValue(pawn, out Track? t) || t.Newest < 0 || connection.FindActorChannel(pawn) is not { OpenAcked: true, Closing: false } channel) return;
        if (!_viewers.TryGetValue(channel, out Viewer? viewer)) _viewers.Add(channel, viewer = new Viewer());
        bool exhausted = viewer.WireEpoch != 0 && viewer.Epoch == t.Epoch &&
            ((viewer.Baseline && viewer.SentTick + 1 < t.Newest - History + 1) ||
            viewer.TransitionSequence < t.TransitionSequence - TransitionCapacity ||
            viewer.StateTick + 1 < t.Newest - History + 1);
        if (viewer.Epoch != t.Epoch || viewer.WireEpoch == 0 || exhausted)
        {
            viewer.Epoch = t.Epoch; viewer.SentTick = t.Newest - 1; viewer.Checkpoint = -1;
            viewer.WireEpoch++; viewer.SnapshotTick = -1;
            if (exhausted) HistoryResyncs++;
            viewer.Channel = 0; Array.Clear(viewer.Revisions);
            viewer.Baseline = true; viewer.TransitionSequence = t.TransitionSequence;
            viewer.StateTick = t.Newest - 1; viewer.StateBaseline = true;
        }
        // Reserve alternating passes for a pending descriptor. At a low configured rate, a snapshot can otherwise
        // consume every refill forever and leave a newly visible pawn without its first clip.
        if ((t.Newest & 1) != 0) SendTransitions(t, viewer, channel, connection, 1);
        bool checkpoint = t.Newest - viewer.Checkpoint >= 30;
        bool reliableCheckpoint = checkpoint && channel.NumOutRec < 96;
        // Phase checkpoints reference descriptors already sent reliably. Repeating all clip names and parameters
        // every half second would starve movement on the very links this buffer is intended to support.
        Frame current = t.Frames[Slot(t.Newest)];
        bool changedMode = current.Physics != viewer.LastPhysics || current.Zone != viewer.LastZone;
        if ((t.Newest - viewer.SnapshotTick >= snapshotInterval || changedMode) && connection.IsNetReady)
        {
            Frame newest = t.Frames[Slot(t.Newest)];
            FOutBunch snapshot = channel.BeginFunction(channel.Layout!.FindFunction(Snapshot)!, reliableCheckpoint);
            WriteFrame(snapshot.Writer, newest, connection, viewer.WireEpoch, t, pawn);
            if (channel.SendFunction(snapshot) >= 0)
            {
                viewer.SnapshotTick = t.Newest; SnapshotsSent++;
                SnapshotPayloadBytes += (snapshot.Writer.NumBits + 7) / 8;
                viewer.LastPhysics = current.Physics; viewer.LastZone = current.Zone;
                if (reliableCheckpoint) viewer.Checkpoint = t.Newest;
            }
        }
        SendTransitions(t, viewer, channel, connection, int.MaxValue);
        SendPresentation(t, viewer, channel, connection, pawn);
    }

    private void SendPresentation(Track track, Viewer viewer, ActorChannel channel, NetConnection connection, SBasePawn pawn)
    {
        for (long tick = viewer.StateTick + 1; tick <= track.Newest; tick++)
        {
            Frame frame = track.Frames[Slot(tick)];
            if (frame.Tick != tick) { viewer.Epoch = uint.MaxValue; return; }
            if (viewer.StateBaseline || frame.StateChanged)
            {
                if (!connection.IsNetReady || channel.NumOutRec >= 96) return;
                FOutBunch bunch = channel.BeginFunction(channel.Layout!.FindFunction(Presentation)!, true);
                bunch.Writer.WriteIntPacked(viewer.WireEpoch); bunch.Writer.WriteIntPacked((uint)tick);
                WriteBody(bunch.Writer, frame, connection, pawn);
                if (channel.SendFunction(bunch) < 0) return;
                PresentationRecordsSent++;
                PresentationPayloadBytes += (bunch.Writer.NumBits + 7) / 8;
                viewer.StateBaseline = false;
            }
            viewer.StateTick = tick;
        }
    }

    private static bool SameBody(Frame a, Frame b) =>
        a.Physics == b.Physics && a.Zone == b.Zone && a.Crouched == b.Crouched && a.Lying == b.Lying && a.Sleeping == b.Sleeping && a.Hidden == b.Hidden &&
        a.ReadyBoost == b.ReadyBoost && a.ReadyHoist == b.ReadyHoist && a.Grabbable == b.Grabbable && a.Terminable == b.Terminable &&
        a.StickyShocking == b.StickyShocking && a.Shock.SameStatus(b.Shock) && a.Health == b.Health && a.Radius == b.Radius && a.Height == b.Height && a.PrePivot == b.PrePivot &&
        a.Weapon == b.Weapon && a.Ik == b.Ik && a.Btw == b.Btw && a.Pipe == b.Pipe && a.Attachments == b.Attachments &&
        ReferenceEquals(a.Base, b.Base) && ReferenceEquals(a.Target, b.Target) && ReferenceEquals(a.Attacker, b.Attacker) &&
        ReferenceEquals(a.UsedObject, b.UsedObject) && ReferenceEquals(a.Interaction, b.Interaction) &&
        ReferenceEquals(a.Released, b.Released) && ReferenceEquals(a.Terminate, b.Terminate) &&
        a.Root.LockType == b.Root.LockType && (a.Root.LockType == 0 ||
            (a.Root.Serial == b.Root.Serial && a.Root.LockYaw == b.Root.LockYaw && a.Root.LockedRoot == b.Root.LockedRoot));

    private void SendTransitions(Track t, Viewer viewer, ActorChannel channel, NetConnection connection, int limit)
    {
        // The first baseline is pinned to a recorded frame while it is spread across budgeted sends.
        // Journal entries that follow it remain ordered even when several plays share a tick/channel.
        if (viewer.Baseline)
        {
            long tick = viewer.SentTick + 1;
            Frame frame = t.Frames[Slot(tick)];
            if (frame.Tick != tick)
            {
                viewer.Epoch = uint.MaxValue; return;
            }
            for (int i = viewer.Channel; i < frame.Pose.Length; i++)
            {
                if (viewer.Revisions[i] == frame.Revisions[i]) continue;
                if (i is >= 2 and <= 13 && i is not (4 or 5) && frame.Pose[i].Alpha <= 0f && !frame.Pose[i].Sequence.IsNone)
                    continue;
                if (viewer.Revisions[i] == 0 && frame.Pose[i].Sequence.IsNone)
                {
                    // A baseline's empty channels are already represented by the snapshot visibility mask.
                    viewer.Revisions[i] = frame.Revisions[i];
                    continue;
                }
                if (!connection.IsNetReady || channel.NumOutRec >= 96)
                {
                    if (!connection.IsNetReady) BudgetDeferrals++; else ReliableDeferrals++;
                    viewer.Channel = i; return;
                }
                FOutBunch b = channel.BeginFunction(channel.Layout!.FindFunction(Animation)!, true);
                b.Writer.WriteIntPacked(viewer.WireEpoch); b.Writer.WriteIntPacked((uint)tick);
                b.Writer.WriteByte((byte)i); b.Writer.WriteIntPacked(frame.Revisions[i]);
                WriteChannel(b.Writer, frame.Pose[i]);
                if (channel.SendFunction(b) < 0) { viewer.Channel = i; return; }
                viewer.Revisions[i] = frame.Revisions[i]; AnimationRecords++;
                AnimationPayloadBytes += (b.Writer.NumBits + 7) / 8;
                if (--limit == 0) { viewer.Channel = i + 1; return; }
            }
            viewer.Channel = 0; viewer.SentTick = tick; viewer.Baseline = false;
        }
        for (long sequence = viewer.TransitionSequence + 1; sequence <= t.TransitionSequence; sequence++)
        {
            Transition transition = t.Transitions![(int)(sequence & (TransitionCapacity - 1))];
            int i = transition.Channel;
            if (!transition.Authored && i is >= 2 and <= 13 && i is not (4 or 5) && transition.State.Alpha <= 0f && !transition.State.Sequence.IsNone)
            {
                viewer.TransitionSequence = sequence; continue;
            }
            if (!connection.IsNetReady || channel.NumOutRec >= 96)
            {
                if (!connection.IsNetReady) BudgetDeferrals++; else ReliableDeferrals++;
                return;
            }
            FOutBunch b = channel.BeginFunction(channel.Layout!.FindFunction(Animation)!, true);
            b.Writer.WriteIntPacked(viewer.WireEpoch); b.Writer.WriteIntPacked((uint)transition.Tick);
            b.Writer.WriteByte((byte)i); b.Writer.WriteIntPacked(transition.Revision);
            WriteChannel(b.Writer, transition.State);
            if (channel.SendFunction(b) < 0) return;
            viewer.Revisions[i] = transition.Revision; viewer.TransitionSequence = sequence; AnimationRecords++;
            AnimationPayloadBytes += (b.Writer.NumBits + 7) / 8;
            if (--limit == 0) return;
        }
    }

    private void Capture(SBasePawn pawn, Frame f)
    {
        f.Shock = _shock.Capture(pawn);
        f.Motion = new ProxyState { Location = pawn.Location, Rotation = pawn.Rotation, Velocity = pawn.Velocity, Aim = pawn.iDesiredYawPitch, View = pawn.iYawPitchFromView };
        f.Physics = pawn.Physics; f.Zone = pawn.ZoneConfig; f.Radius = pawn.CollisionRadius; f.Height = pawn.CollisionHeight;
        f.PrePivot = pawn.PrePivot; f.Crouched = pawn.bIsCrouched; f.Lying = pawn.bLyingOnGround; f.Sleeping = pawn.bSleeping; f.Hidden = pawn.bHidden; f.Health = pawn.Health;
        f.ReadyBoost = pawn.bReadyForShortLadder; f.ReadyHoist = pawn.bReadyForHoistClimb;
        f.Grabbable = pawn.bCanBeGrabbed; f.Terminable = pawn.bCanBeTerminated; f.StickyShocking = pawn.bStickyShocking;
        f.Weapon = pawn.WeaponLoaded; f.Ik = (byte)pawn.IK_Type; f.Btw = (byte)pawn.BTW; f.Pipe = (byte)pawn.PipHFire;
        f.Attachments = NetworkPawnAttachments.Capture(pawn);
        f.Base = pawn.Base; f.Target = pawn.m_AttackTarget; f.Attacker = pawn.m_AttackedBy;
        f.UsedObject = pawn.UsedObject; f.Interaction = pawn.m_InteractionTarget;
        f.Released = pawn.m_PawnNoMoreGrabbed; f.Terminate = pawn.m_PawnToTerminate;
        f.Root = pawn.NetworkRootMotion.Capture();
    }

    private static void ReceiveAnimation(Actor actor, FBitReader r, NetConnection connection)
    {
        uint epoch = r.ReadIntPacked(); long tick = r.ReadIntPacked(); int channel = r.ReadByte(); uint version = r.ReadIntPacked();
        if (version > ushort.MaxValue) { connection.Close("Malformed animation revision"); return; }
        ushort revision = (ushort)version;
        NetworkAnimChannel state = ReadChannel(r);
        if (r.IsError || actor is not SBasePawn pawn || channel >= PawnAnimation.ChannelCount || connection.Driver.World?.Level.GetService<PawnStateReplication>() is not { } stream) return;
        Track t = stream.Get(pawn);
        if (epoch < t.Epoch) return;
        if (epoch > t.Epoch) { t.Reset(epoch); pawn.ResetNetworkHistory(); stream.Resets++; }
        t.Channels[channel].Describe(revision, state, tick); stream.AnimationRecords++;
        stream.TransitionReceived?.Invoke(pawn, channel, state);
    }

    private static void ReceivePresentation(Actor actor, FBitReader r, NetConnection connection)
    {
        if (actor is not SBasePawn pawn || connection.Driver.World?.Level.GetService<PawnStateReplication>() is not { } stream) return;
        uint epoch = r.ReadIntPacked(); long tick = r.ReadIntPacked();
        Track track = stream.Get(pawn);
        bool accept = epoch >= track.Epoch && (epoch > track.Epoch || tick > track.Newest - History);
        if (epoch > track.Epoch) { track.Reset(epoch); pawn.ResetNetworkHistory(); stream.Resets++; }
        Frame frame = accept ? track.Frames[Slot(tick)] : stream._discard;
        if (frame.Tick != tick) Array.Clear(frame.Revisions);
        ReadBody(r, frame, connection, pawn, tick);
        if (!accept || r.IsError) return;
        frame.Tick = tick; frame.Epoch = epoch;
        if (tick >= track.Newest) track.ShockLocalReset = pawn.NetworkResetId;
        track.StateSequence = Math.Max(track.StateSequence, frame.StateSequence);
        track.Motion.Recover(tick, MeshAnchorMotion(frame));
        stream.PresentationRecordsReceived++;
    }

    private static void ReceiveSnapshot(Actor actor, FBitReader r, NetConnection connection)
    {
        if (actor is not SBasePawn pawn || connection.Driver.World?.Level.GetService<PawnStateReplication>() is not { } stream) return;
        uint epoch = r.ReadIntPacked(); long tick = r.ReadIntPacked();
        Track t = stream.Get(pawn);
        // Always consume a stale call: other calls may follow in the same actor bunch.
        Frame frame = t.Frames[Slot(tick)];
        bool accept = epoch >= t.Epoch && (epoch > t.Epoch || tick > t.Newest);
        if (!accept) { ReadFrame(r, stream._discard, connection, pawn); return; }
        if (epoch > t.Epoch) { t.Reset(epoch); pawn.ResetNetworkHistory(); stream.Resets++; }
        ReadFrame(r, frame, connection, pawn, t, tick);
        if (r.IsError) return;
        frame.Tick = tick; frame.Epoch = epoch;
        // Gameplay readers such as the disk-carrier radar use the last server velocity, not the delayed
        // presentation velocity. Keep this contract shared with the legacy proxy sample receiver.
        pawn.ReceivedVelocity = frame.Motion.Velocity;
        t.ShockLocalReset = pawn.NetworkResetId;
        if (frame.StateSequence > t.StateSequence) stream.StateGaps++;
        for (int i = 0; i < frame.Pose.Length; i++)
        {
            NetworkAnimChannel c = frame.Pose[i];
            t.Channels[i].Push(new Phase { Tick = tick, Revision = frame.Revisions[i], Frame = c.Frame, Rate = c.Rate, Alpha = c.Alpha, Tween = c.TweenWeight, Snapshot = true });
        }
        t.Motion.Recover(tick, MeshAnchorMotion(frame)); t.Newest = tick; stream.SnapshotsReceived++;
        stream.LatestSnapshotTick = Math.Max(stream.LatestSnapshotTick, tick);
    }

    public bool Tick(SBasePawn pawn, float deltaTime)
    {
        if (!_tracks.TryGetValue(pawn, out Track? t) || t.Newest < 0 || RenderTimeFixed < 0)
        {
            pawn.NetworkPoseReady = false;
            return false;
        }
        Frame? f = null;
        for (int i = 0; i < History; i++)
        {
            Frame candidate = t.Frames[i];
            if (candidate.Tick >= 0 && candidate.Tick * NetTimeline.Fraction <= RenderTimeFixed && (f is null || candidate.Tick > f.Tick)) f = candidate;
        }
        f ??= t.Frames[Slot(t.Newest)];
        if (!t.Ready)
        {
            bool complete = true;
            Frame baseline = t.Frames[Slot(t.Newest)];
            for (int i = 0; i < baseline.Revisions.Length; i++) complete &= t.Channels[i].HasDescriptor(baseline.Revisions[i]);
            t.Ready = complete;
        }
        pawn.NetworkPoseReady = t.Ready;
        t.Motion.Sample(RenderTimeFixed, 6, out ProxyState motion);
        // Interpolate the mesh anchor, then reconstruct the cylinder centre using the displayed stance.
        // Interpolating centre height while switching PrePivot discretely sinks/floats crouch transitions.
        motion.Location -= f.PrePivot;
        if (RenderTimeFixed > t.Newest * NetTimeline.Fraction) Underflows++;
        bool wasReplaying = pawn.XLevel.World.IsReplaying;
        pawn.XLevel.World.IsReplaying = true;
        try { ApplyPresentation(pawn, deltaTime, t, f, motion, RenderTimeFixed); }
        finally { pawn.XLevel.World.IsReplaying = wasReplaying; }
        // Snapshot proxies consume authored poses instead of replaying SA_DMG_ELECTRIC actions. Keep their
        // persistent visual side effects on the same sampled timeline, without replaying gameplay or notifies.
        if (!wasReplaying && !pawn.bDeleteMe && t.ElectricFxActive != pawn.bStickyShocking)
        {
            if (pawn.bStickyShocking) pawn.BeginStickySkin();
            else pawn.RestoreSkins();
            pawn.StickyShockSfx(pawn.bStickyShocking);
            t.ElectricFxActive = pawn.bStickyShocking;
        }
        return true;
    }

    private void ApplyPresentation(SBasePawn pawn, float deltaTime, Track t, Frame f, in ProxyState motion, long time,
        bool presentationOnly = true)
    {
        ResolveReferences(f, _driver);
        pawn.Physics = f.Physics; pawn.bIsCrouched = f.Crouched; pawn.bLyingOnGround = f.Lying; pawn.Health = f.Health;
        pawn.bSleeping = f.Sleeping; pawn.bHidden = f.Hidden;
        pawn.bReadyForShortLadder = f.ReadyBoost; pawn.bReadyForHoistClimb = f.ReadyHoist;
        pawn.bCanBeGrabbed = f.Grabbable; pawn.bCanBeTerminated = f.Terminable; pawn.bStickyShocking = f.StickyShocking;
        pawn.ZoneConfig = pawn.ZoneConfigForClient = f.Zone; pawn.PrePivot = f.PrePivot;
        MovementNatives.SetCollisionSize(pawn, f.Radius, f.Height);
        MovementNatives.FarMoveActor(pawn, motion.Location); pawn.Rotation = motion.Rotation;
        pawn.Velocity = motion.Velocity; pawn.Acceleration = motion.Velocity.SafeNormal();
        pawn.iDesiredYawPitch = motion.Aim; pawn.iYawPitchFromView = motion.View;
        pawn.WeaponLoaded = f.Weapon; pawn.IK_Type = (IK_Type_)f.Ik;
        pawn.NetworkAttachments = f.Attachments;
        pawn.BTW = (BTW_)f.Btw; pawn.PipHFire = (PipHFire_)f.Pipe;
        pawn.SetBase(f.Base, bNotifyActor: false); pawn.m_AttackTarget = f.Target; pawn.m_AttackedBy = f.Attacker;
        pawn.UsedObject = f.UsedObject; pawn.m_InteractionTarget = f.Interaction;
        pawn.m_PawnNoMoreGrabbed = f.Released; pawn.m_PawnToTerminate = f.Terminate;
        pawn.NetworkRootMotion.Restore(f.Root); pawn.iLockRootMotionType = f.Root.LockType;
        pawn.PawnAnim.Aim.Update(deltaTime);
        PawnMovementAnimation.TurnHead(pawn, deltaTime);
        for (int i = 0; i < PawnAnimation.ChannelCount; i++)
        {
            // Motion reports zero velocity after the extrapolation limit. Keep the last authoritative speed for
            // phase prediction, otherwise a speed-scaled clip jumps backward when the held body stops reporting it.
            if (t.Channels[i].Sample(time, f.Motion.Velocity.Size(), pawn, out NetworkAnimChannel state)) state.Apply(pawn, i, presentationOnly);
            else MissingDescriptors++;
        }
        Name forward = t.Channels[5].SequenceAt(time), run = t.Channels[4].SequenceAt(time);
        if (!forward.IsNone) pawn.ForwardAnim = forward;
        if (!run.IsNone) pawn.RunAnim = run;
        pawn.PawnAnim.Owner = pawn; pawn.OldRotYaw = pawn.Rotation.Yaw;
        ZoneRegions.SetActorZone(pawn);
    }

    private static ProxyState MeshAnchorMotion(Frame frame)
    {
        ProxyState motion = frame.Motion;
        motion.Location += frame.PrePivot;
        return motion;
    }

    private static int Slot(long tick) => (int)(tick & (History - 1));
    private static void Vec(FBitWriter w, FVector v) { Scalar(w, v.X); Scalar(w, v.Y); Scalar(w, v.Z); }
    private static FVector Vec(FBitReader r) => new(Scalar(r), Scalar(r), Scalar(r));
    private static void Signed(FBitWriter w, int value) => w.WriteIntPacked((uint)((value << 1) ^ (value >> 31)));
    private static int Signed(FBitReader r) { uint value = r.ReadIntPacked(); return (int)(value >> 1) ^ -(int)(value & 1); }
    private static void Half(FBitWriter w, float f) => w.WriteUInt16(BitConverter.HalfToUInt16Bits((System.Half)f));
    private static float Half(FBitReader r) => (float)BitConverter.UInt16BitsToHalf(r.ReadUInt16());

    private static void DefaultShape(SBasePawn pawn, byte zone, out float radius, out float height, out FVector pivot)
    {
        (radius, height, pivot) = NetworkPawnShape.Defaults(pawn, zone);
    }

    private static void WriteReference(FBitWriter writer, Actor? actor, NetDriver driver)
    {
        if (actor is null || actor.bDeleteMe) { writer.WriteIntPacked(0); return; }
        int index = actor.bStatic || actor.bNoDelete ? driver.MapBinding.IndexOf(actor) : -1;
        writer.WriteIntPacked(index >= 0 ? (uint)(index * 2 + 1) : (uint)actor.ActorId * 2u + 2u);
    }

    private static Actor? ResolveReference(uint code, NetDriver driver)
    {
        if (code == 0) return null;
        Actor? actor = (code & 1) != 0 ? driver.MapBinding.Get((int)((code - 1) / 2)) : driver.FindActorByNetGuid((code - 2) / 2);
        return actor is { bDeleteMe: false } ? actor : null;
    }

    private static void ResolveReferences(Frame frame, NetDriver driver)
    {
        frame.Base = ResolveReference(frame.BaseCode, driver);
        frame.Target = ResolveReference(frame.TargetCode, driver) as SBasePawn;
        frame.Attacker = ResolveReference(frame.AttackerCode, driver) as SBasePawn;
        frame.UsedObject = ResolveReference(frame.UsedCode, driver);
        frame.Interaction = ResolveReference(frame.InteractionCode, driver) as SBasePawn;
        frame.Released = ResolveReference(frame.ReleasedCode, driver) as SBasePawn;
        frame.Terminate = ResolveReference(frame.TerminateCode, driver) as SBasePawn;
    }

    private static void WriteBody(FBitWriter w, Frame f, NetConnection map, SBasePawn pawn)
    {
        w.WriteIntPacked(f.StateSequence);
        w.WritePackedVector(f.Motion.Location); w.WritePackedVector(f.Motion.Velocity); w.WriteCompressedRotator(f.Motion.Rotation);
        AimWord(w, f.Motion.Aim, f.Motion.Rotation.Yaw);
        w.WriteBit(f.Motion.View == f.Motion.Aim);
        if (f.Motion.View != f.Motion.Aim) AimWord(w, f.Motion.View, f.Motion.Rotation.Yaw);
        SmallByte(w, (byte)f.Physics, 5); SmallByte(w, f.Zone, 2);
        DefaultShape(pawn, f.Zone, out float radius, out float height, out FVector prePivot);
        bool authoredShape = f.Radius != radius || f.Height != height || f.PrePivot != prePivot;
        w.WriteBit(authoredShape);
        if (authoredShape)
        {
            Half(w, f.Radius); Half(w, f.Height);
            uint pivot = (f.PrePivot.X != 0 ? 1u : 0u) | (f.PrePivot.Y != 0 ? 2u : 0u) | (f.PrePivot.Z != 0 ? 4u : 0u);
            w.WriteBits(pivot, 3);
            if ((pivot & 1) != 0) w.WriteFloat(f.PrePivot.X);
            if ((pivot & 2) != 0) w.WriteFloat(f.PrePivot.Y);
            if ((pivot & 4) != 0) w.WriteFloat(f.PrePivot.Z);
        }
        w.WriteBit(f.Crouched); w.WriteBit(f.Lying); w.WriteBit(f.Sleeping); w.WriteBit(f.Hidden);
        w.WriteBit(f.ReadyBoost); w.WriteBit(f.ReadyHoist);
        w.WriteBit(f.Grabbable); w.WriteBit(f.Terminable); w.WriteBit(f.StickyShocking);
        f.Shock.Write(w);
        w.WriteBit(f.Health != 100);
        if (f.Health != 100) w.WriteIntPacked((uint)((f.Health << 1) ^ (f.Health >> 31)));
        SmallByte(w, f.Weapon, 2); SmallByte(w, f.Ik, 3); SmallByte(w, f.Btw, 4); SmallByte(w, f.Pipe, 3);
        SmallByte(w, f.Attachments.ActiveSlot, 1);
        w.WriteBit(f.Attachments.WeaponInHand.HasValue);
        if (f.Attachments.WeaponInHand is { } inHand) w.WriteBit(inHand);
        uint references = (f.Base is null ? 0u : 1u) | (f.Target is null ? 0u : 2u) | (f.Attacker is null ? 0u : 4u) |
            (f.UsedObject is null ? 0u : 8u) | (f.Interaction is null ? 0u : 16u) | (f.Released is null ? 0u : 32u) |
            (f.Terminate is null ? 0u : 64u);
        w.WriteBits(references, 7);
        if ((references & 1) != 0) WriteReference(w, f.Base, map.Driver);
        if ((references & 2) != 0) WriteReference(w, f.Target, map.Driver);
        if ((references & 4) != 0) WriteReference(w, f.Attacker, map.Driver);
        if ((references & 8) != 0) WriteReference(w, f.UsedObject, map.Driver);
        if ((references & 16) != 0) WriteReference(w, f.Interaction, map.Driver);
        if ((references & 32) != 0) WriteReference(w, f.Released, map.Driver);
        if ((references & 64) != 0) WriteReference(w, f.Terminate, map.Driver);
        SmallByte(w, (byte)f.Root.LockType, 2);
        if (f.Root.LockType != 0)
        {
            Vec(w, f.Root.LockedRoot); Vec(w, f.Root.LastRoot); Scalar(w, f.Root.LastYaw);
            Signed(w, f.Root.LockYaw); Signed(w, f.Root.Carried); Signed(w, f.Root.Serial);
        }
    }

    private static void WriteFrame(FBitWriter w, Frame f, NetConnection map, uint epoch, Track track, SBasePawn pawn)
    {
        w.WriteIntPacked(epoch); w.WriteIntPacked((uint)f.Tick);
        WriteBody(w, f, map, pawn);
        int recovered = 0;
        for (int offset = 1; offset <= PhaseRecoveryTicks; offset++)
        {
            Frame prior = track.Frames[Slot(f.Tick - offset)];
            if (prior.Tick != f.Tick - offset || prior.Epoch != f.Epoch) break;
            recovered++;
        }
        w.WriteBits((uint)recovered, 3);
        uint active = 0;
        for (int i = 0; i < f.Pose.Length; i++)
            // The walk and run channels share the locomotion cycle even while one has zero weight. Retain their
            // phase for crossfades and traversal handoffs; other invisible channels need only their descriptors.
            if (!f.Pose[i].Sequence.IsNone && (i is 0 or 4 or 5 || f.Pose[i].Alpha > 0f)) active |= 1u << i;
        for (int offset = 1; offset <= PhaseRecoveryTicks; offset++)
        {
            Frame prior = track.Frames[Slot(f.Tick - offset)];
            if (prior.Tick != f.Tick - offset || prior.Epoch != f.Epoch) continue;
            for (int i = 0; i < prior.Pose.Length; i++)
                if (!prior.Pose[i].Sequence.IsNone && prior.Pose[i].Alpha > 0f && prior.Revisions[i] != f.Revisions[i])
                    active |= 1u << i;
        }
        w.WriteIntPacked(active);
        for (int i = 0; i < f.Pose.Length; i++)
        {
            if ((active & (1u << i)) == 0) continue;
            NetworkAnimChannel c = f.Pose[i];
            w.WriteIntPacked(f.Revisions[i]);
            bool sharedCycle = i != 0 && (active & 1) != 0 &&
                Math.Abs(c.Frame - f.Pose[0].Frame) < 0.00001f &&
                BitConverter.HalfToUInt16Bits((System.Half)c.Rate) == BitConverter.HalfToUInt16Bits((System.Half)f.Pose[0].Rate);
            if (i != 0) w.WriteBit(sharedCycle);
            if (!sharedCycle) { w.WriteQuantizedFloat(c.Frame, -1f, 1f, 16); Half(w, c.Rate); }
            Unit(w, c.Alpha); Unit(w, c.TweenWeight);
            WritePhaseRecovery(w, track, f, i);
        }
    }

    private static void WritePhaseRecovery(FBitWriter w, Track track, Frame current, int channel)
    {
        NetworkAnimChannel c = current.Pose[channel];
        uint changed = 0;
        for (int offset = 1; offset <= PhaseRecoveryTicks; offset++)
        {
            Frame prior = track.Frames[Slot(current.Tick - offset)];
            if (prior.Tick != current.Tick - offset || prior.Epoch != current.Epoch) continue;
            NetworkAnimChannel p = prior.Pose[channel];
            float rate = c.Rate < 0 ? Math.Max(0.3f, -c.Rate * current.Motion.Velocity.Size()) : c.Rate;
            float expected = c.Frame - offset * Sim.DeltaTime * (c.Frame < 0 ? c.TweenRate : rate);
            float error = Math.Abs(expected - p.Frame);
            if (c.Frame < 0 || p.Frame < 0 || prior.Revisions[channel] != current.Revisions[channel] || error > 0.0005f ||
                Math.Abs(c.Rate - p.Rate) > 0.0005f ||
                Math.Abs(c.Alpha - p.Alpha) > 0.0005f || Math.Abs(c.TweenWeight - p.TweenWeight) > 0.0005f)
                changed |= 1u << (offset - 1);
        }
        w.WriteBit(changed != 0);
        if (changed == 0) return;
        w.WriteBits(changed, PhaseRecoveryTicks);
        float nearerFrame = c.Frame;
        for (int offset = 1; offset <= PhaseRecoveryTicks; offset++)
        {
            Frame prior = track.Frames[Slot(current.Tick - offset)];
            NetworkAnimChannel p = prior.Pose[channel];
            if ((changed & (1u << (offset - 1))) == 0)
            { nearerFrame = p.Frame; continue; }
            bool revised = prior.Revisions[channel] != current.Revisions[channel];
            w.WriteBit(revised); if (revised) w.WriteUInt16(prior.Revisions[channel]);
            // Burst input processing often holds several adjacent host ticks at the same phase. Preserve the
            // samples while encoding that hold, or their common linear step, once rather than four full floats.
            float rate = c.Rate < 0 ? Math.Max(0.3f, -c.Rate * current.Motion.Velocity.Size()) : c.Rate;
            float stepped = nearerFrame - Sim.DeltaTime * (nearerFrame < 0 ? c.TweenRate : rate);
            uint phase = Math.Abs(p.Frame - c.Frame) < 0.00005f ? 0u :
                Math.Abs(p.Frame - nearerFrame) < 0.00005f ? 1u :
                c.Frame >= 0 && nearerFrame >= 0 && Math.Abs(p.Frame - stepped) < 0.00005f ? 2u : 3u;
            w.WriteBits(phase, 2);
            if (phase == 3) w.WriteQuantizedFloat(p.Frame, -1, 1, 16);
            nearerFrame = p.Frame;
            bool rateChanged = Math.Abs(p.Rate - c.Rate) > 0.0005f;
            w.WriteBit(rateChanged); if (rateChanged) Half(w, p.Rate);
            bool alphaChanged = Math.Abs(p.Alpha - c.Alpha) > 0.0005f;
            w.WriteBit(alphaChanged); if (alphaChanged) Unit(w, p.Alpha);
            bool tweenChanged = Math.Abs(p.TweenWeight - c.TweenWeight) > 0.0005f;
            w.WriteBit(tweenChanged); if (tweenChanged) Unit(w, p.TweenWeight);
        }
    }

    private static void ReadBody(FBitReader r, Frame f, NetConnection map, SBasePawn pawn, long tick)
    {
        f.StateSequence = r.ReadIntPacked();
        f.Motion = new ProxyState { Location = r.ReadPackedVector(), Velocity = r.ReadPackedVector(), Rotation = r.ReadCompressedRotator() };
        f.Motion.Aim = AimWord(r, f.Motion.Rotation.Yaw);
        f.Motion.View = r.ReadBit() ? f.Motion.Aim : AimWord(r, f.Motion.Rotation.Yaw);
        f.Physics = (EPhysics)SmallByte(r, 5); f.Zone = SmallByte(r, 2);
        if (r.ReadBit())
        {
            f.Radius = Half(r); f.Height = Half(r);
            uint pivot = (uint)r.ReadBits(3);
            f.PrePivot = new FVector((pivot & 1) != 0 ? r.ReadFloat() : 0f,
                (pivot & 2) != 0 ? r.ReadFloat() : 0f, (pivot & 4) != 0 ? r.ReadFloat() : 0f);
        }
        else DefaultShape(pawn, f.Zone, out f.Radius, out f.Height, out f.PrePivot);
        f.Crouched = r.ReadBit(); f.Lying = r.ReadBit(); f.Sleeping = r.ReadBit(); f.Hidden = r.ReadBit();
        f.ReadyBoost = r.ReadBit(); f.ReadyHoist = r.ReadBit();
        f.Grabbable = r.ReadBit(); f.Terminable = r.ReadBit(); f.StickyShocking = r.ReadBit();
        f.Shock = MercShockSample.Read(r, tick);
        uint health = r.ReadBit() ? r.ReadIntPacked() : 200u;
        f.Health = (int)(health >> 1) ^ -(int)(health & 1);
        f.Weapon = SmallByte(r, 2); f.Ik = SmallByte(r, 3); f.Btw = SmallByte(r, 4); f.Pipe = SmallByte(r, 3);
        byte activeSlot = SmallByte(r, 1);
        f.Attachments = new NetworkPawnAttachments(activeSlot, r.ReadBit() ? r.ReadBit() : null);
        uint references = (uint)r.ReadBits(7);
        f.BaseCode = (references & 1) != 0 ? r.ReadIntPacked() : 0;
        f.TargetCode = (references & 2) != 0 ? r.ReadIntPacked() : 0;
        f.AttackerCode = (references & 4) != 0 ? r.ReadIntPacked() : 0;
        f.UsedCode = (references & 8) != 0 ? r.ReadIntPacked() : 0;
        f.InteractionCode = (references & 16) != 0 ? r.ReadIntPacked() : 0;
        f.ReleasedCode = (references & 32) != 0 ? r.ReadIntPacked() : 0;
        f.TerminateCode = (references & 64) != 0 ? r.ReadIntPacked() : 0;
        ResolveReferences(f, map.Driver);
        int root = SmallByte(r, 2);
        f.Root = root == 0 ? default : new PawnRootMotion.State(root, Vec(r), Vec(r), Scalar(r), Signed(r), Signed(r), Signed(r));
    }

    private static void ReadFrame(FBitReader r, Frame f, NetConnection map, SBasePawn pawn, Track? track = null, long tick = -1)
    {
        ReadBody(r, f, map, pawn, tick);
        int recovered = (int)r.ReadBits(3);
        if (recovered > PhaseRecoveryTicks) { map.Close("Malformed animation phase history"); return; }
        uint active = r.ReadIntPacked();
        if ((active >> PawnAnimation.ChannelCount) != 0) { map.Close("Malformed animation channel mask"); return; }
        for (int i = 0; i < f.Pose.Length; i++)
        {
            if ((active & (1u << i)) == 0) { f.Revisions[i] = 0; f.Pose[i] = default; continue; }
            uint revisionValue = r.ReadIntPacked();
            if (revisionValue > ushort.MaxValue) { map.Close("Malformed snapshot animation revision"); return; }
            f.Revisions[i] = (ushort)revisionValue;
            bool sharedCycle = i != 0 && r.ReadBit();
            f.Pose[i] = new NetworkAnimChannel
            {
                Frame = sharedCycle ? f.Pose[0].Frame : r.ReadQuantizedFloat(-1f, 1f, 16),
                Rate = sharedCycle ? f.Pose[0].Rate : Half(r), Alpha = Unit(r), TweenWeight = Unit(r),
            };
            uint recovery = r.ReadBit() ? (uint)r.ReadBits(PhaseRecoveryTicks) : 0;
            float nearerFrame = f.Pose[i].Frame;
            for (int offset = 1; offset <= PhaseRecoveryTicks; offset++)
            {
                NetworkAnimChannel c = f.Pose[i];
                ushort revision = f.Revisions[i];
                float speedRate = c.Rate < 0 ? Math.Max(0.3f, -c.Rate * f.Motion.Velocity.Size()) : c.Rate;
                float frame = c.Frame - offset * Sim.DeltaTime * speedRate;
                float rate = c.Rate, alpha = c.Alpha, tween = c.TweenWeight;
                if ((recovery & (1u << (offset - 1))) != 0)
                {
                    revision = r.ReadBit() ? r.ReadUInt16() : revision;
                    frame = r.ReadBits(2) switch
                    {
                        0 => c.Frame,
                        1 => nearerFrame,
                        2 => nearerFrame - Sim.DeltaTime * speedRate,
                        _ => r.ReadQuantizedFloat(-1, 1, 16),
                    };
                    rate = r.ReadBit() ? Half(r) : rate;
                    alpha = r.ReadBit() ? Unit(r) : alpha;
                    tween = r.ReadBit() ? Unit(r) : tween;
                }
                else if (offset > recovered) continue;
                nearerFrame = frame;
                if (!r.IsError) track?.Channels[i].Push(new Phase { Tick = tick - offset, Revision = revision,
                    Frame = frame, Rate = rate, Alpha = alpha, Tween = tween, Snapshot = true });
            }
        }
    }

    private static void Unit(FBitWriter w, float value)
    {
        bool endpoint = value is 0f or 1f;
        w.WriteBit(endpoint);
        if (endpoint) w.WriteBit(value == 1f);
        else w.WriteQuantizedFloat(value, 0f, 1f, 8);
    }

    private static float Unit(FBitReader r) => r.ReadBit() ? r.ReadBit() ? 1f : 0f : r.ReadQuantizedFloat(0f, 1f, 8);

    private static void AimWord(FBitWriter w, int value, int bodyYaw)
    {
        uint yaw = (uint)value & 65535, pitch = (uint)value >> 16;
        bool sharedYaw = yaw == ((uint)bodyYaw & 65535);
        w.WriteBit(sharedYaw);
        if (!sharedYaw) { w.WriteBit(yaw != 0); if (yaw != 0) w.WriteBits(yaw, 16); }
        w.WriteBit(pitch != 0); if (pitch != 0) w.WriteBits(pitch, 16);
    }

    private static int AimWord(FBitReader r, int bodyYaw)
    {
        uint yaw = r.ReadBit() ? (uint)bodyYaw & 65535 : r.ReadBit() ? (uint)r.ReadBits(16) : 0;
        uint pitch = r.ReadBit() ? (uint)r.ReadBits(16) : 0;
        return (int)(yaw | (pitch << 16));
    }

    private static void SmallByte(FBitWriter w, byte value, int bits)
    {
        bool extended = value >= (1 << bits);
        w.WriteBit(extended);
        if (extended) w.WriteByte(value);
        else w.WriteBits(value, bits);
    }

    private static byte SmallByte(FBitReader r, int bits) => r.ReadBit() ? r.ReadByte() : (byte)r.ReadBits(bits);

    private static void WriteChannel(FBitWriter w, in NetworkAnimChannel c)
    {
        w.WriteBit(!c.Sequence.IsNone);
        if (c.Sequence.IsNone) return;
        w.WriteString(c.Sequence.ToString()); w.WriteInt32(c.Serial); WriteName(w, c.StartBone);
        Scalar(w, c.Frame); Scalar(w, c.Rate); Scalar(w, c.Alpha); Scalar(w, c.TweenRate); Scalar(w, c.TweenWeight);
        Scalar(w, c.BlendIn); Scalar(w, c.BlendOut); Scalar(w, c.BlendTarget); Scalar(w, c.BlendTimer);
        w.WriteBit(c.Loop); w.WriteBit(c.Backward); w.WriteBit(c.Mirror); w.WriteBit(c.GlobalPose); w.WriteBit(c.TimedBlend);
        w.WriteBit(c.NotifyIgnore);
        WriteName(w, c.QueuedSequence);
        if (!c.QueuedSequence.IsNone)
        { Scalar(w, c.QueuedRate); Scalar(w, c.QueuedTween); w.WriteBit(c.QueuedLoop); w.WriteBit(c.QueuedBackward); }
    }

    private static NetworkAnimChannel ReadChannel(FBitReader r)
    {
        if (!r.ReadBit()) return default;
        var channel = new NetworkAnimChannel
        {
            Sequence = r.ReadName(128), Serial = r.ReadInt32(), StartBone = ReadName(r),
            Frame = Scalar(r), Rate = Scalar(r), Alpha = Scalar(r), TweenRate = Scalar(r), TweenWeight = Scalar(r),
            BlendIn = Scalar(r), BlendOut = Scalar(r), BlendTarget = Scalar(r), BlendTimer = Scalar(r),
            Loop = r.ReadBit(), Backward = r.ReadBit(), Mirror = r.ReadBit(), GlobalPose = r.ReadBit(), TimedBlend = r.ReadBit(),
            NotifyIgnore = r.ReadBit(), QueuedSequence = ReadName(r),
        };
        if (!channel.QueuedSequence.IsNone)
        { channel.QueuedRate = Scalar(r); channel.QueuedTween = Scalar(r); channel.QueuedLoop = r.ReadBit(); channel.QueuedBackward = r.ReadBit(); }
        return channel;
    }

    private static void Scalar(FBitWriter w, float value)
    {
        uint kind = value == 0f ? 0u : value == 1f ? 1u : value == -1f ? 2u : 3u;
        w.WriteBits(kind, 2);
        if (kind == 3) w.WriteFloat(value);
    }
    private static float Scalar(FBitReader r) => r.ReadBits(2) switch { 0 => 0f, 1 => 1f, 2 => -1f, _ => r.ReadFloat() };
    private static void WriteName(FBitWriter w, Name name) { w.WriteBit(!name.IsNone); if (!name.IsNone) w.WriteString(name.ToString()); }
    private static Name ReadName(FBitReader r) => r.ReadBit() ? r.ReadName(128) : Name.None;
}
