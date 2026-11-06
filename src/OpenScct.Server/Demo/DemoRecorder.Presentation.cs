using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>A presentation input from before a demo checkpoint, with a tick on the original server timeline.</summary>
public readonly record struct DemoPresentationSample(uint Tick, SimEvent Event)
{
    /// <summary>The sound had a source in the recording, but that actor no longer exists at the checkpoint.</summary>
    public bool MissingSource { get; init; }

    /// <summary>True for an effect attached to an actor whose view will be created from the restored world.</summary>
    public bool IsLiveEmitter => Event.Kind == SimEventKind.SpawnEmitter && Event.Slot == DemoPresentationWire.LiveEmitter;
}

public sealed partial class DemoRecorder
{
    private readonly DemoPresentationHistory? _presentation;

    internal void RecordPresentation(in SimEvent e, uint tick)
    {
        if (!IsFinished) _presentation?.Add(e, tick);
    }

    internal void RecordMusicTrigger(MusicTrigger trigger, bool start, Pawn? instigator, uint tick)
    {
        if (!IsFinished && (!trigger.bInstigatorOnly || instigator is null))
            _presentation?.TriggerMusic(trigger, start, tick);
    }

    private bool WriteDemoCheckpointPresentation(NetConnection bootstrap) =>
        _presentation?.Write(bootstrap, Connection) == true &&
        PawnSoundReplication.Of(_host.Driver).WriteDemoCheckpointNames(Connection, bootstrap);
}

/// <summary>Bounded presentation history kept only for the recorder, without requiring server-side audio assets.</summary>
internal sealed class DemoPresentationHistory
{
    private const int RecentCapacity = 256;
    private const uint RecentTicks = 30 * Sim.Hz;
    private const int OlderSounds = 256;
    private const int OlderEmitters = 256;
    private const int MusicCapacity = 8;
    private readonly World _world;
    private readonly Queue<Entry> _audio = new();
    private readonly Queue<Entry> _effects = new();
    private readonly Queue<Entry> _music = new();
    private readonly Dictionary<(int Actor, int Slot, Name Sound), Entry> _possibleLoops = new();
    private readonly Dictionary<int, OldTriggers> _oldTriggers = new();
    private readonly List<(int Actor, int Slot, Name Sound)> _remove = new();
    private readonly List<Entry> _write = new();
    private long _order;
    private Name _triggerTrack;

    private readonly record struct Entry(DemoPresentationSample Sample, long Order);
    private readonly record struct OldTriggers(Entry Previous, Entry Last, bool Odd);

    internal DemoPresentationHistory(World world)
    {
        _world = world;
        if (world.Level.Info.Song.Path is { Length: > 0 } song)
            Add(new SimEvent { Kind = SimEventKind.PlayMusic, What = new Name(song), Volume = 1f }, (uint)world.TickCount);
    }

    internal void TriggerMusic(MusicTrigger trigger, bool start, uint tick)
    {
        Name song = new(trigger.Song.Path);
        if (song.IsNone) return;
        if (start && _triggerTrack != song)
        {
            Add(new SimEvent { Kind = SimEventKind.PlayMusic, What = song, Volume = 1f, Radius = trigger.FadeInTime }, tick);
        }
        else if (trigger.bToggle || !start)
        {
            Add(new SimEvent { Kind = SimEventKind.StopMusic, What = song, Radius = trigger.FadeOutTime }, tick);
        }
    }

    internal void Add(in SimEvent e, uint tick)
    {
        var entry = new Entry(new DemoPresentationSample(tick, e), _order++);
        switch (e.Kind)
        {
            case SimEventKind.PlayMusic or SimEventKind.StopMusic:
                if (e.Kind == SimEventKind.PlayMusic) _triggerTrack = e.What;
                else if (e.What.IsNone || e.What == _triggerTrack) _triggerTrack = Name.None;
                if (_music.Count == MusicCapacity) _music.Dequeue();
                _music.Enqueue(entry);
                break;
            case SimEventKind.PlaySound or SimEventKind.StopSound or SimEventKind.SetSwitch:
                if (_audio.Count == RecentCapacity) FoldSound(_audio.Dequeue());
                _audio.Enqueue(entry);
                break;
            case SimEventKind.SpawnEmitter:
            case SimEventKind.Notify when e.What == Emitter.NAME_ReplicateTrigger && e.ActorId != 0:
                if (_effects.Count == RecentCapacity) FoldTrigger(_effects.Dequeue());
                _effects.Enqueue(entry);
                break;
        }
    }

    // The server cannot tell which converted sounds loop. Keep possible active starts after their stop and
    // replacement events, with their original age so the rendering client can discard expired one-shots.
    private void FoldSound(Entry entry)
    {
        SimEvent e = entry.Sample.Event;
        if (e.Kind == SimEventKind.PlaySound)
        {
            if (e.Slot != 0 && !e.NoOverride)
            {
                foreach (var key in _possibleLoops.Keys)
                    if (key.Actor == e.ActorId && key.Slot == e.Slot) _remove.Add(key);
                RemoveSounds();
            }

            var sound = (e.ActorId, e.Slot, e.What);
            if (_possibleLoops.Count == OlderSounds && !_possibleLoops.ContainsKey(sound))
            {
                long oldest = long.MaxValue;
                (int, int, Name) replace = default;
                foreach (var pair in _possibleLoops)
                {
                    if (pair.Value.Order >= oldest) continue;
                    oldest = pair.Value.Order;
                    replace = pair.Key;
                }
                _possibleLoops.Remove(replace);
            }
            _possibleLoops[sound] = entry;
        }
        else if (e.Kind == SimEventKind.StopSound)
        {
            foreach (var key in _possibleLoops.Keys)
                if ((e.ActorId == 0 || key.Actor == e.ActorId) && (e.What.IsNone || key.Sound == e.What))
                    _remove.Add(key);
            RemoveSounds();
        }
    }

    private void RemoveSounds()
    {
        foreach (var key in _remove) _possibleLoops.Remove(key);
        _remove.Clear();
    }

    private void FoldTrigger(Entry entry)
    {
        SimEvent e = entry.Sample.Event;
        if (e.Kind != SimEventKind.Notify || e.What != Emitter.NAME_ReplicateTrigger) return;
        if (_oldTriggers.TryGetValue(e.ActorId, out OldTriggers prior))
            _oldTriggers[e.ActorId] = new OldTriggers(prior.Last, entry, !prior.Odd);
        else if (_oldTriggers.Count < OlderEmitters)
            _oldTriggers.Add(e.ActorId, new OldTriggers(default, entry, true));
    }

    internal bool Write(NetConnection bootstrap, NetConnection source)
    {
        if (bootstrap.ControlChannel is not { } control) return false;
        uint tick = source.Driver.CurrentTick;
        while (_audio.TryPeek(out Entry sound) && tick > sound.Sample.Tick && tick - sound.Sample.Tick > RecentTicks)
            FoldSound(_audio.Dequeue());
        while (_effects.TryPeek(out Entry effect) && tick > effect.Sample.Tick && tick - effect.Sample.Tick > RecentTicks)
            FoldTrigger(_effects.Dequeue());

        _write.Clear();
        _write.AddRange(_music);
        foreach (var pair in _possibleLoops)
        {
            if (pair.Key.Actor != 0 && _world.Level.FindActor(pair.Key.Actor) is not { bDeleteMe: false })
                _remove.Add(pair.Key);
            else _write.Add(pair.Value);
        }
        RemoveSounds();
        _write.AddRange(_audio);
        foreach (OldTriggers old in _oldTriggers.Values)
        {
            // One old trigger for odd parity, two for even. Retaining the last trigger age also lets the
            // renderer reproduce persistent toggles without serializing every elapsed burst.
            if (!old.Odd) _write.Add(old.Previous);
            _write.Add(old.Last);
        }
        _write.AddRange(_effects);
        foreach (ActorChannel channel in source.ActorChannels)
        {
            if (channel.IsMapActor || channel.Actor is not { bDeleteMe: false } actor ||
                actor is not (Emitter or SProjGrenade)) continue;
            var e = new SimEvent
            {
                Kind = SimEventKind.SpawnEmitter, What = new Name(actor.GetUcClass().Path),
                ActorId = actor.ActorId, Location = actor.Location, Rotation = actor.Rotation,
                Slot = DemoPresentationWire.LiveEmitter, Radius = actor is Emitter ? actor.LifeSpan : 0f,
            };
            _write.Add(new Entry(new DemoPresentationSample(channel.DemoSpawnTick, e), _order));
        }
        _write.Sort(static (a, b) => a.Sample.Tick != b.Sample.Tick
            ? a.Sample.Tick.CompareTo(b.Sample.Tick)
            : a.Order.CompareTo(b.Order));

        foreach (Entry item in _write)
        {
            if (item.Sample.Tick <= tick && DemoPresentationWire.Encode(item.Sample, source.Driver) is { } line)
                control.Send(ControlMessage.TextLine(line));
        }
        _write.Clear();
        control.Send(ControlMessage.TextLine(DemoPresentationWire.MusicPrefix +
            (_triggerTrack.IsNone ? string.Empty : _triggerTrack.ToString())));
        return bootstrap.State == EConnectionState.USOCK_Open;
    }
}
