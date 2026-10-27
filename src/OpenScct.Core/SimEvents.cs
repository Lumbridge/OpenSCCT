using System;
using OpenScct.Core.Engine;

namespace OpenScct.Core;

/// <summary>Kind of presentation event.</summary>
public enum SimEventKind : byte
{
    PlaySound = 1,
    SpawnEmitter = 2,
    Notify = 3,

    /// <summary>StopSound: stop the sound <see cref="SimEvent.What"/> playing on the actor.</summary>
    StopSound = 4,

    /// <summary>PlayMusic: start the track <see cref="SimEvent.What"/>, fading in over <see cref="SimEvent.Radius"/> seconds.</summary>
    PlayMusic = 5,

    /// <summary>StopMusic: stop the track <see cref="SimEvent.What"/>, fading out over <see cref="SimEvent.Radius"/> seconds.</summary>
    StopMusic = 6,

    /// <summary>SetSwitch: the surface type <see cref="SimEvent.Slot"/> that the next switch sound plays (spec/native/audio.md rule 6).</summary>
    SetSwitch = 7,
}

/// <summary>
/// One presentation event raised by the simulation for views (sound, particles, generic notifies). Views read these;
/// they never feed back into the simulation.
/// </summary>
public struct SimEvent
{
    public SimEventKind Kind;

    /// <summary>Tick the event was raised on.</summary>
    public long Tick;

    /// <summary>Source or owner actor id, 0 when none.</summary>
    public int ActorId;

    /// <summary>Sound name, emitter class or notify name.</summary>
    public Name What;

    public FVector Location;
    public FRotator Rotation;

    /// <summary>Sound slot, or the notify parameter.</summary>
    public int Slot;

    public float Volume;
    public float Radius;
    public float Pitch;

    /// <summary>PlaySound's bNoOverride: the sound does not replace one playing in the same slot.</summary>
    public bool NoOverride;

    /// <summary>PlaySound's Attenuate, inverted so the default is the usual attenuated sound.</summary>
    public bool NoAttenuate;

    /// <summary>PlaySound's BoneName: the bone of the source's mesh the sound follows, or None.</summary>
    public Name Bone;

    /// <summary>
    /// Raised by PlayOwnedSound: the owner's machine plays the sound through its own simulated call, so a view that
    /// also receives the owner's copy from elsewhere drops it (spec/native/audio.md rule 4).
    /// </summary>
    public bool Owned;

    /// <summary>
    /// Raised by ClientHearSound: the server's call of a sound that reached this player (spec/native/audio.md rule 5),
    /// rather than a call this machine's own simulation made.
    /// </summary>
    public bool Heard;

    /// <summary>
    /// Played without 3D: an unattenuated call of the actor the local player views (spec/native/audio.md rule 5),
    /// decided on this machine when the call was made.
    /// </summary>
    public bool Flat;
}

/// <summary>Receives each tick's presentation events right after the tick (for views that run several ticks per frame).</summary>
public interface ISimEventSink
{
    void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events);
}

/// <summary>
/// Per-tick buffer of presentation events. Cleared at the start of every <see cref="World.Step()"/>; read it after
/// the step through <see cref="Current"/> or a <see cref="World.EventSink"/>. Events raised between two ticks (a
/// network call received before the step, an input command, a view after it) are in <see cref="Late"/> until the
/// next step clears them. Nothing is recorded while the world is replaying (resimulating ticks that were already
/// presented).
/// </summary>
public sealed class SimEvents
{
    private readonly World _world;
    private SimEvent[] _buffer = new SimEvent[256];
    private int _count;
    private int _closed;
    private long _tick;

    internal SimEvents(World world)
    {
        _world = world;
    }

    /// <summary>True while events are being dropped (the world is replaying).</summary>
    public bool IsSuppressed => _world.IsReplaying;

    /// <summary>Events raised so far in the current (or just finished) tick.</summary>
    public ReadOnlySpan<SimEvent> Current => _buffer.AsSpan(0, _count);

    public int Count => _count;

    /// <summary>
    /// The events raised since the last tick ended (all of them before the first tick): a reader that wants them takes
    /// them before the next step, which drops them.
    /// </summary>
    public ReadOnlySpan<SimEvent> Late => _buffer.AsSpan(_closed, _count - _closed);

    internal void BeginTick(long tick)
    {
        _count = 0;
        _closed = 0;
        _tick = tick;
    }

    /// <summary>The tick's own events end here; anything raised after it is <see cref="Late"/>.</summary>
    internal void EndTick() => _closed = _count;

    /// <summary>Adds an event. Its Tick is set to the current tick.</summary>
    public void Add(in SimEvent e)
    {
        if (_world.IsReplaying)
        {
            return;
        }

        if (_count == _buffer.Length)
        {
            Array.Resize(ref _buffer, _buffer.Length * 2);
        }

        ref SimEvent slot = ref _buffer[_count++];
        slot = e;
        slot.Tick = _tick;
    }

    public void PlaySound(Actor? source, Name sound, int slot = 0, float volume = 1f, float radius = 0f, float pitch = 1f,
        bool noOverride = false, bool attenuate = true, Name bone = default, bool owned = false, bool flat = false)
    {
        Add(new SimEvent
        {
            Kind = SimEventKind.PlaySound,
            ActorId = source?.ActorId ?? 0,
            What = sound,
            Location = source?.Location ?? default,
            Rotation = source?.Rotation ?? default,
            Slot = slot,
            Volume = volume,
            Radius = radius,
            Pitch = pitch,
            NoOverride = noOverride,
            NoAttenuate = !attenuate,
            Bone = bone,
            Owned = owned,
            Flat = flat,
        });
    }

    /// <summary>Raises <see cref="SimEventKind.SetSwitch"/>: the surface type the next switch sound plays.</summary>
    public void SetSwitch(Actor? source, byte surface)
    {
        Add(new SimEvent
        {
            Kind = SimEventKind.SetSwitch,
            ActorId = source?.ActorId ?? 0,
            Location = source?.Location ?? default,
            Slot = surface,
        });
    }

    public void SpawnEmitter(Actor? owner, Name emitter, FVector location, FRotator rotation)
    {
        Add(new SimEvent
        {
            Kind = SimEventKind.SpawnEmitter,
            ActorId = owner?.ActorId ?? 0,
            What = emitter,
            Location = location,
            Rotation = rotation,
        });
    }

    public void Notify(Actor? source, Name notify, int param = 0)
    {
        Add(new SimEvent
        {
            Kind = SimEventKind.Notify,
            ActorId = source?.ActorId ?? 0,
            What = notify,
            Location = source?.Location ?? default,
            Rotation = source?.Rotation ?? default,
            Slot = param,
        });
    }
}
