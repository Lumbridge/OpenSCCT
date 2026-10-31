using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>Ordered pawn sounds, including stops, at the same display time as their animation.</summary>
public sealed class PawnSoundReplication
{
    private const string Function = "SvmPawnSound";
    private const int Capacity = 1024;
    private readonly NetDriver _driver;
    private readonly Pending[] _pending = new Pending[Capacity];
    private readonly Dictionary<Name, ushort> _outNames = new(Capacity);
    private readonly Name[] _inNames = new Name[Capacity];
    private readonly Dictionary<NetConnection, Viewer> _viewers = new();
    private readonly List<NetConnection> _closed = new();
    private int _start, _count;
    public long Sent { get; private set; }
    public long Presented { get; private set; }
    public long Obsolete { get; private set; }
    public long BudgetExpired { get; private set; }

    private sealed class Viewer
    {
        public readonly bool[] Names = new bool[Capacity];
        public readonly Outgoing[] Pending = new Outgoing[Capacity];
        public int Start, Count;
        public uint Sequence;
        public readonly Dictionary<(int Source, Name Sound), SoundState> Sounds = new(256);
        public readonly List<(int Source, Name Sound)> Gone = new(256);
        public long PrunedAt;
        public PlayerController Listener = null!;
    }

    private struct Outgoing
    {
        public SBasePawn Source;
        public long Tick;
        public SimEvent Event;
        public uint Sequence;
    }

    private struct SoundState
    {
        public uint Sequence;
        public SimEventKind Kind;
        public bool Sent;
    }

    private struct Pending
    {
        public uint Source;
        public long Tick;
        public SimEvent Event;
    }

    private PawnSoundReplication(NetDriver driver) => _driver = driver;

    public static PawnSoundReplication Of(NetDriver driver)
    {
        Level level = driver.World!.Level;
        if (level.GetService<PawnSoundReplication>() is { } existing) return existing;
        var stream = new PawnSoundReplication(driver);
        level.SetService(stream);
        return stream;
    }

    public static RepExtension CreateExtension()
    {
        var extension = new RepExtension();
        extension.Functions.Add(new(Function, true, false, "pawn-sound-2", Receive));
        return extension;
    }

    public bool Send(PlayerController listener, SBasePawn source, in SimEvent e, long tick)
    {
        NetConnection? connection = _driver.OwningConnection(listener);
        if (connection is null) return false;
        if (!_viewers.TryGetValue(connection, out Viewer? viewer))
        {
            foreach (var prior in _viewers)
                if (prior.Key.State == EConnectionState.USOCK_Closed) _closed.Add(prior.Key);
            foreach (NetConnection closed in _closed) _viewers.Remove(closed);
            _closed.Clear();
            _viewers.Add(connection, viewer = new Viewer());
        }
        viewer.Listener = listener;
        var key = (source.ActorId, e.What);
        viewer.Sounds.TryGetValue(key, out SoundState state);
        // State entries often stop a traversal loop which this listener never heard. Such stops must not build
        // an unbounded backlog in a crowd, or prevent the visible players from being initialized.
        if (e.Kind == SimEventKind.StopSound && (state.Kind == SimEventKind.StopSound ||
            (!state.Sent && state.Kind != SimEventKind.PlaySound))) return true;
        if (viewer.Count == Capacity) Compact(viewer, tick);
        if (viewer.Count == Capacity) { connection.Close("Pawn sound send queue exhausted"); return false; }
        uint sequence = ++viewer.Sequence;
        state.Sequence = sequence; state.Kind = e.Kind;
        viewer.Sounds[key] = state;
        viewer.Pending[(viewer.Start + viewer.Count++) & (Capacity - 1)] =
            new Outgoing { Source = source, Tick = tick, Event = e, Sequence = sequence };
        return true;
    }

    /// <summary>Spend the remaining per-client budget after owner corrections and active pawn state.</summary>
    public void Flush(long tick)
    {
        foreach (var entry in _viewers)
        {
            NetConnection connection = entry.Key;
            Viewer viewer = entry.Value;
            if (tick - viewer.PrunedAt >= Sim.Hz)
            {
                viewer.PrunedAt = tick;
                foreach (var sound in viewer.Sounds)
                    if (_driver.World!.Level.FindActor(sound.Key.Source) is not SBasePawn { bDeleteMe: false })
                        viewer.Gone.Add(sound.Key);
                foreach (var key in viewer.Gone) viewer.Sounds.Remove(key);
                viewer.Gone.Clear();
            }
            while (viewer.Count > 0)
            {
                ref Outgoing pending = ref viewer.Pending[viewer.Start];
                bool expired = Expired(pending, tick);
                if (expired) Expire(viewer, pending);
                else
                {
                    ActorChannel? channel = connection.FindActorChannel(viewer.Listener);
                    if (!connection.IsNetReady || channel is not { Closing: false, NumOutRec: < 96 } ||
                        channel.Layout?.FindFunction(Function) is not { } function) break;
                    if (!SendNow(channel, function, viewer.Names, pending.Source, pending.Event, pending.Tick)) break;
                    var key = (pending.Source.ActorId, pending.Event.What);
                    if (viewer.Sounds.TryGetValue(key, out SoundState state))
                    {
                        state.Sent = pending.Event.Kind == SimEventKind.PlaySound;
                        if (!state.Sent && state.Sequence == pending.Sequence) viewer.Sounds.Remove(key);
                        else viewer.Sounds[key] = state;
                    }
                }
                pending = default;
                viewer.Start = (viewer.Start + 1) & (Capacity - 1); viewer.Count--;
            }
        }
    }

    private static bool Expired(in Outgoing pending, long tick) => pending.Source.bDeleteMe ||
        (pending.Event.Kind == SimEventKind.PlaySound && tick - pending.Tick > 6);

    private void Expire(Viewer viewer, in Outgoing pending)
    {
        BudgetExpired++;
        var key = (pending.Source.ActorId, pending.Event.What);
        if (!viewer.Sounds.TryGetValue(key, out SoundState state) || state.Sequence != pending.Sequence) return;
        if (state.Sent) { state.Kind = 0; viewer.Sounds[key] = state; }
        else viewer.Sounds.Remove(key);
    }

    private void Compact(Viewer viewer, long tick)
    {
        int kept = 0;
        for (int i = 0; i < viewer.Count; i++)
        {
            Outgoing pending = viewer.Pending[(viewer.Start + i) & (Capacity - 1)];
            if (Expired(pending, tick)) Expire(viewer, pending);
            else viewer.Pending[(viewer.Start + kept++) & (Capacity - 1)] = pending;
        }
        for (int i = kept; i < viewer.Count; i++) viewer.Pending[(viewer.Start + i) & (Capacity - 1)] = default;
        viewer.Count = kept;
    }

    private bool SendNow(ActorChannel channel, RepFunction function, bool[] known, SBasePawn source, in SimEvent e, long tick)
    {
        if (!_outNames.TryGetValue(e.What, out ushort soundId) && _outNames.Count < Capacity - 1)
        {
            soundId = (ushort)(_outNames.Count + 1);
            _outNames.Add(e.What, soundId);
        }
        bool definition = soundId == 0 || !known[soundId];
        FOutBunch bunch = channel.BeginFunction(function, true);
        FBitWriter w = bunch.Writer;
        w.WriteUInt32((uint)tick);
        w.WriteUInt32((uint)source.ActorId);
        w.WriteBit(e.Kind == SimEventKind.StopSound);
        w.WriteUInt16(soundId); w.WriteBit(definition);
        if (definition) w.WriteString(e.What.ToString());
        if (e.Kind != SimEventKind.StopSound)
        {
            w.WritePackedVector(e.Location);
            w.WriteBits((uint)e.Slot & 7u, 3);
            w.WriteBit(e.NoOverride); w.WriteBit(e.NoAttenuate);
            w.WriteFloat(e.Volume); w.WriteFloat(e.Radius); w.WriteFloat(e.Pitch);
        }
        if (channel.SendFunction(bunch) < 0) return false;
        known[soundId] = true;
        Sent++;
        return true;
    }

    private static void Receive(Actor actor, FBitReader r, NetConnection connection)
    {
        var pending = new Pending { Tick = r.ReadUInt32(), Source = r.ReadUInt32() };
        pending.Event.Kind = r.ReadBit() ? SimEventKind.StopSound : SimEventKind.PlaySound;
        ushort soundId = r.ReadUInt16(); bool definition = r.ReadBit();
        if (soundId >= Capacity) { connection.Close("Malformed pawn sound id"); return; }
        PawnSoundReplication stream = Of(connection.Driver);
        pending.Event.What = definition ? r.ReadName(256) : stream._inNames[soundId];
        if (soundId != 0 && definition) stream._inNames[soundId] = pending.Event.What;
        pending.Event.Heard = true;
        if (pending.Event.Kind == SimEventKind.PlaySound)
        {
            pending.Event.Location = r.ReadPackedVector();
            pending.Event.Slot = (int)r.ReadBits(3);
            pending.Event.NoOverride = r.ReadBit(); pending.Event.NoAttenuate = r.ReadBit();
            pending.Event.Volume = r.ReadFloat(); pending.Event.Radius = r.ReadFloat(); pending.Event.Pitch = r.ReadFloat();
        }
        if (r.IsError) return;
        if (stream._count == Capacity)
        {
            connection.Close("Pawn sound presentation queue exhausted");
            return;
        }
        stream._pending[(stream._start + stream._count++) & (Capacity - 1)] = pending;
    }

    public void Pump(long renderTimeFixed)
    {
        if (_driver.IsServer || renderTimeFixed < 0) return;
        int available = _count;
        for (int i = 0; i < available; i++)
        {
            Pending pending = _pending[_start];
            _pending[_start] = default;
            _start = (_start + 1) & (Capacity - 1); _count--;
            long sourceTime = pending.Tick * NetTimeline.Fraction;
            SBasePawn? source = _driver.FindActorByNetGuid(pending.Source) as SBasePawn;
            bool expired = renderTimeFixed - sourceTime > 30L * NetTimeline.Fraction;
            if (sourceTime > renderTimeFixed || (!expired &&
                (source is null || source is { Role: ENetRole.ROLE_SimulatedProxy, NetworkPoseReady: false })))
            {
                // An unresolved pawn must not stall presentation events for all the other players.
                _pending[(_start + _count++) & (Capacity - 1)] = pending;
                continue;
            }
            if (source is { bDeleteMe: false } &&
                (pending.Event.Kind == SimEventKind.StopSound ||
                 (_driver.World!.Level.GetService<PawnStateReplication>()?.OldestTick(source) ?? -1) <= pending.Tick))
            {
                SimEvent e = pending.Event;
                // Stops remain useful after an outage. Late one-shots belong to an obsolete displayed action.
                if (e.Kind == SimEventKind.StopSound || renderTimeFixed - sourceTime <= 6L * NetTimeline.Fraction)
                {
                    e.ActorId = source.ActorId;
                    if (_driver.ServerConnection?.Actor is PlayerController listener)
                        e.Flat = e.NoAttenuate && ReferenceEquals(source, listener.ViewTarget ?? listener);
                    if (e.Radius == 0f) e.Radius = SoundK.HearRadiusForZero;
                    _driver.World!.Events.Add(e);
                    Presented++;
                }
                else Obsolete++;
            }
            else Obsolete++;
        }
    }
}
