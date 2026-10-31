using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;

namespace OpenScctGame.Audio;

/// <summary>
/// One wave or stream the sound system started: the sound the call named, the leaf it resolved to, the actor (0:
/// none, negative: a client-only map actor), whether it plays 3D, as an ambient loop or as music, its source volume
/// and the sound system's time.
/// </summary>
public readonly record struct SoundStart(string Call, string Leaf, int ActorId, bool Positional, bool Ambient, bool Music,
    float Volume, double Time);

/// <summary>
/// One PlaySound call the sound system handled: the sound, the actor, whether it came from the server
/// (ClientHearSound) and plays 2D, and whether a voice started, the call played nothing (an empty random or switch
/// entry, no voice) or named a sound the library does not have.
/// </summary>
public readonly record struct SoundCall(string Sound, int ActorId, bool Heard, bool Flat, SoundCallOutcome Outcome);

public enum SoundCallOutcome : byte
{
    Started,
    Silent,
    Unknown,
}

/// <summary>
/// The client's sound playback, driven by the simulation's <see cref="SimEvent"/>s (install it as the client
/// world's <see cref="World.EventSink"/>): PlaySound, PlayOwnedSound, StopSound, PlayMusic, StopMusic and SetSwitch,
/// plus the ambient loops of the actors (zone ambiences, AmbientSound actors, movers, ambient triggers) and the
/// listener zone's reverb. The rules are UNIAudio's (spec/native/audio.md): <see cref="AudioK.Voices"/> voices with
/// slot ids and priorities, the inverse distance gain computed here (Godot's own attenuation is off), random, switch
/// and sequence sounds, 2D sounds. MakeNoise is gameplay and never reaches it.
/// <para>Events can arrive on the simulation's thread; they are queued and played in <see cref="_Process"/>.</para>
/// </summary>
public sealed partial class SoundSystem : Node3D, ISimEventSink
{
    private readonly List<Voice> _voices = [];
    private readonly List<SimEvent> _pending = [];
    private readonly List<SimEvent> _work = [];
    private readonly object _gate = new();
    private readonly List<AmbientSource> _ambient = [];
    private readonly Dictionary<int, Voice> _ambientVoices = [];
    private readonly HashSet<int> _ambientSeen = [];
    private readonly Dictionary<string, (int ActorId, float Volume)> _loudest2D = new(StringComparer.OrdinalIgnoreCase);
    private AudioListener3D? _listener;
    private Random _random = new(1);
    private int _freshId;
    private int _pendingSwitch = -1;
    private Voice? _music;
    private int _musicHandle;
    private double _now;
    private string? _listenerZone;
    private int _listenerZoneIndex;

    public AudioLibrary? Library { get; private set; }

    public IAudioWorld? World { get; private set; }

    public ReverbBuses? Buses { get; private set; }

    public FVector ListenerLocation { get; private set; }

    public FRotator ListenerRotation { get; private set; }

    /// <summary>The listener's velocity from its own movement over the last update (uu/s), for the Doppler shift.</summary>
    public FVector ListenerVelocity { get; private set; }

    private FVector _lastListener;
    private bool _lastListenerSet;

    /// <summary>
    /// The actor the player views (0: none). UNIAudio plays this actor's sounds at the listener (PlaySound, game
    /// 0x10b03fe1), so they are never occluded.
    /// </summary>
    public int ViewActorId { get; set; }

    /// <summary>The scope's or binoculars' audio zoom (SetAudioZoom: 0.33, 0.66 or 1; 0 is off).</summary>
    public float AudioZoom { get; private set; }

    /// <summary>The view FOV in degrees the audio zoom is measured against.</summary>
    public float AudioZoomFov { get; private set; } = 90f;

    /// <summary>Sounds started since <see cref="Bind"/>, by leaf sound path (waves and streams), for tests and reports.</summary>
    public List<string> Started { get; } = [];

    /// <summary>Every wave or stream started since <see cref="Bind"/> with its call and how it plays (the newest <see cref="MaxStarts"/>).</summary>
    public List<SoundStart> Starts { get; } = [];

    /// <summary>
    /// Per actor, how often its ambient loop stopped for a reason of the rules: another call took its voice (rule 15) or
    /// it left the range or its zone (rules 20 to 22). Each explains one later start of the loop (the audio smokes).
    /// </summary>
    public Dictionary<int, int> AmbientStops { get; } = [];

    /// <summary>Starts kept in <see cref="Starts"/> and <see cref="Started"/>; older ones are dropped.</summary>
    public const int MaxStarts = 4096;

    /// <summary>Waves and streams started since <see cref="Bind"/>, all of them.</summary>
    public long StartCount { get; private set; }

    /// <summary>The PlaySound calls handled since <see cref="Bind"/> (the newest <see cref="MaxStarts"/>).</summary>
    public List<SoundCall> Calls { get; } = [];

    /// <summary>Events that named a sound the library does not have.</summary>
    public List<string> Unknown { get; } = [];

    /// <summary>Calls that played nothing (random silence, an empty switch, no free voice).</summary>
    public int Silent { get; private set; }

    public int ActiveVoices
    {
        get
        {
            int n = 0;
            foreach (Voice v in _voices)
            {
                n += v.Active ? 1 : 0;
            }

            return n;
        }
    }

    /// <summary>The listener's zone (map id of its ZoneInfo), from the last update.</summary>
    public string? ListenerZone => _listenerZone;

    /// <summary>
    /// Uses <paramref name="library"/> for the sounds and <paramref name="world"/> (may be null) for actor
    /// locations, ambient loops, zones and surfaces. <paramref name="seed"/> seeds the random lists.
    /// </summary>
    public void Bind(AudioLibrary library, IAudioWorld? world, int seed = 1)
    {
        Library = library ?? throw new ArgumentNullException(nameof(library));
        World = world;
        _random = new Random(seed);
        Buses = new ReverbBuses(library.Map);
        if (_listener is null)
        {
            _listener = new AudioListener3D { Name = "Listener" };
            AddChild(_listener);
        }

        while (_voices.Count < AudioK.Voices)
        {
            var v = new Voice(_voices.Count);
            AddChild(v.Player3D);
            AddChild(v.Player2D);
            _voices.Add(v);
        }

        StopAll();
        Started.Clear();
        Starts.Clear();
        Calls.Clear();
        StartCount = 0;
        Unknown.Clear();
        Silent = 0;

        // The engine's map load plays the LevelInfo's Song as music with no fade (game 0x109e82c4 and 0x109eb982:
        // Audio->PlayMusic(Level->Song, 0)).
        if (library.Map?.LevelSong is { } song)
        {
            PlayMusic(song, 0f);
        }
    }


    /// <summary>Replaces the world the sounds read actor locations, ambient loops, zones and surfaces from.</summary>
    public void SetWorld(IAudioWorld? world) => World = world;

    /// <summary>The view the player hears from (the camera), UE space.</summary>
    public void SetListener(FVector location, FRotator rotation)
    {
        ListenerLocation = location;
        ListenerRotation = rotation;
        if (_listener is not null)
        {
            _listener.Transform = FaithfulView.CameraTransform(location, rotation);
            if (_listener.IsInsideTree() && !_listener.IsCurrent())
            {
                _listener.MakeCurrent();
            }
        }
    }

    /// <summary>
    /// SetAudioZoom: 3D sounds near the aim line get louder, the rest quieter (spec/native/audio.md, docs/VOICE.md
    /// "Eavesdropping"). The listening player's AudioZoomFactor and view FOV, every frame.
    /// </summary>
    public void SetAudioZoom(float zoom, float fov)
    {
        AudioZoom = Math.Max(zoom, 0f);
        AudioZoomFov = fov;
    }

    public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
    {
        lock (_gate)
        {
            foreach (SimEvent e in events)
            {
                if (e.Kind is SimEventKind.PlaySound or SimEventKind.StopSound or SimEventKind.PlayMusic or
                    SimEventKind.StopMusic or SimEventKind.SetSwitch)
                {
                    _pending.Add(e);
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        Update(delta);
    }

    /// <summary>One update: queued events, then every voice (follow, gain, sequences, fades), ambient loops, reverb.</summary>
    public void Update(double delta)
    {
        if (Library is null)
        {
            return;
        }

        _now += delta;
        lock (_gate)
        {
            _work.Clear();
            _work.AddRange(_pending);
            _pending.Clear();
        }

        foreach (SimEvent e in _work)
        {
            Handle(e);
        }

        if (World is not null)
        {
            // Rule 23: a point in zone 0 keeps the last zone.
            _listenerZone = World.ZoneIdAt(ListenerLocation) ?? _listenerZone;
            int zone = World.ZoneIndexAt(ListenerLocation);
            _listenerZoneIndex = zone != 0 ? zone : _listenerZoneIndex;
        }

        // Rule 17: the listener's velocity is its movement over this update.
        ListenerVelocity = _lastListenerSet && delta > 0 ? (ListenerLocation - _lastListener) * (float)(1.0 / delta) : default;
        _lastListener = ListenerLocation;
        _lastListenerSet = true;

        string bus = Buses?.BusForZone(_listenerZone) ?? ReverbBuses.WorldBus;
        UpdateAmbient();
        foreach (Voice v in _voices)
        {
            if (v.Active)
            {
                UpdateVoice(v, (float)delta, bus);
            }
        }
    }

    /// <summary>Plays one event now (main thread).</summary>
    public void Handle(in SimEvent e)
    {
        switch (e.Kind)
        {
            case SimEventKind.PlaySound:
                // A PlayOwnedSound (e.Owned) plays the same way: this client's world raised it, so it is not a copy.
                // Rule 5: an unattenuated sound of the actor the player views plays without 3D.
            {
                string sound = e.What.ToString();
                int unknown = Unknown.Count;
                bool started = PlaySound(e.ActorId, sound, e.Slot, e.NoOverride, e.Location, e.Volume, e.Radius, e.Pitch, !e.NoAttenuate,
                    flat: e.Flat, bone: e.Bone);
                if (Calls.Count >= MaxStarts)
                {
                    Calls.RemoveRange(0, MaxStarts / 2);
                }

                Calls.Add(new SoundCall(sound, e.ActorId, e.Heard, e.Flat,
                    started ? SoundCallOutcome.Started : Unknown.Count > unknown ? SoundCallOutcome.Unknown : SoundCallOutcome.Silent));
                break;
            }
            case SimEventKind.StopSound:
                StopSound(e.ActorId, e.What.ToString());
                break;
            case SimEventKind.PlayMusic:
                PlayMusic(e.What.ToString(), e.Radius);
                break;
            case SimEventKind.StopMusic:
                StopMusic(e.Radius);
                break;
            case SimEventKind.SetSwitch:
                _pendingSwitch = e.Slot;
                break;
        }
    }

    /// <summary>Rules 9 to 17 for one call. Returns false when nothing started.</summary>
    public bool PlaySound(int actorId, string sound, int slot, bool noOverride, FVector location, float volume, float radius,
        float pitch, bool attenuate = true, bool flat = false, Name bone = default)
    {
        SoundDef? def = Library?.Find(sound);
        if (def is null)
        {
            if (Unknown.Count >= MaxStarts)
            {
                Unknown.RemoveRange(0, MaxStarts / 2);
            }

            Unknown.Add(sound);
            return false;
        }

        float r = radius == 0f ? AudioK.DefaultHearRadius : radius;
        int id = SoundRules.SoundId(actorId, Math.Clamp(slot, 0, 7), noOverride, ref _freshId);
        var call = new PlayCall(def, actorId, id, location, volume, r, pitch, music: false, loop: false, ambient: false, flat);
        if (!Start(call, follow: actorId != 0))
        {
            return false;
        }

        // Rule 17: a source started on a bone follows that bone of its actor's drawn mesh.
        if (LastStarted is { } started)
        {
            started.Bone = bone;
        }

        return true;
    }

    /// <summary>Stops this actor's sources that play <paramref name="sound"/> (or were started from it).</summary>
    public void StopSound(int actorId, string sound)
    {
        foreach (Voice v in _voices)
        {
            if (v.Active && v.ActorId == actorId && string.Equals(v.CallPath, sound, StringComparison.OrdinalIgnoreCase))
            {
                v.Stop();
            }
        }
    }

    /// <summary>Rule 24: one music track, fading in; the previous one fades out.</summary>
    public int PlayMusic(string song, float fadeIn)
    {
        SoundDef? def = Library?.Find(song);
        if (def is null)
        {
            Unknown.Add(song);
            return 0;
        }

        if (_music is { Active: true, StopWhenFaded: false } && string.Equals(_music.CallPath, song, StringComparison.OrdinalIgnoreCase))
        {
            return _musicHandle;
        }

        Voice? previous = _music;
        var call = new PlayCall(def, 0, 1, default, 1f, AudioK.MusicRadius, 1f, music: true, loop: def.Loop, ambient: false);
        if (!Start(call, follow: false))
        {
            return 0;
        }

        Voice started = LastStarted!;
        if (fadeIn > 0f)
        {
            started.FadeTo(0f, 1f, fadeIn);
        }

        if (previous is not null && previous != started && previous.Active)
        {
            previous.FadeOut(Math.Max(fadeIn, AudioK.MusicMinFadeOut));
        }

        _music = started;
        return ++_musicHandle;
    }

    public void StopMusic(float fadeOut)
    {
        if (_music is { Active: true })
        {
            if (fadeOut > 0f)
            {
                _music.FadeOut(fadeOut);
            }
            else
            {
                _music.Stop();
            }
        }
    }

    public void StopAll()
    {
        foreach (Voice v in _voices)
        {
            v.Stop();
            v.Player3D.Stream = null;
            v.Player2D.Stream = null;
        }

        _ambientVoices.Clear();
        _music = null;
        _pendingSwitch = -1;
    }

    private Voice? LastStarted { get; set; }

    // ---- starting a call ------------------------------------------------------------------------------------

    private readonly record struct PlayCall(SoundDef Def, int ActorId, int Id, FVector Location, float Volume, float Radius,
        float Pitch, bool music, bool loop, bool ambient, bool flat = false, byte occlusion = 0);

    private bool Start(in PlayCall call, bool follow)
    {
        LastStarted = null;
        float chain = 1f;
        SoundDef? leaf = Resolve(call.Def, call.Location, 0, ref chain, call.music);
        if (leaf is null)
        {
            Silent++;
            return false;
        }

        bool music = call.music;
        float source = SoundRules.SourceVolume(call.Volume * chain, leaf.Volume, music);
        float radius = SoundRules.Radius(leaf.Radius, call.Radius);
        int flags = leaf.Flags | (music ? AudioK.FlagMusic | AudioK.Flag2D : 0) | (call.loop ? AudioK.FlagLoop : 0);
        float d2 = FVector.DistSquared(call.Location, ListenerLocation);
        float priority = SoundRules.Priority(d2, radius, source, flags);
        Voice? v = Allocate(call.Id, priority);
        if (v is null)
        {
            Silent++;
            return false;
        }

        v.Begin(call.Def.Path, call.ActorId, call.Id, follow, call.Location, priority);
        v.Music = music;
        v.Flat = call.flat;
        v.Ambient = call.ambient;
        // Rules 20 to 22: an ambient loop is 3D or 2D by its own Sound (the one the actor names), whatever the entry a
        // list sound resolves to; a one-shot plays by its leaf's flag (rule 9).
        v.AmbientPositional = call.ambient && call.Def.Positional;
        v.BaseVolume = call.Volume * chain;
        v.CallRadius = call.Radius;
        // Rule 18: an actor whose SoundOcclusion is OCCLUSION_None is never occluded; a call without an actor is.
        v.SoundOcclusion = call.ActorId > 0 && !call.ambient && World is not null ? World.SoundOcclusionOf(call.ActorId) : call.occlusion;
        v.OcclusionSettled = false;
        v.Pitch = call.ambient ? call.Pitch : SoundRules.Pitch(call.Pitch);
        if (leaf.Kind == "sequence")
        {
            // The sequence's own gain applies to every entry (rule 10 runs before the list dispatch).
            v.BaseVolume *= music ? 1f : leaf.Volume;
            v.Sequence = new SequenceState(leaf);
            if (!NextInSequence(v))
            {
                v.Stop();
                Silent++;
                return false;
            }
        }
        else if (!PlayLeaf(v, leaf, (flags & AudioK.FlagLoop) != 0 || leaf.Loop))
        {
            v.Stop();
            return false;
        }

        LastStarted = v;
        return true;
    }

    /// <summary>
    /// Rules 12 and 13: random and switch sounds down to a wave, stream or sequence; null for silence. Each list on
    /// the way multiplies <paramref name="gain"/> by its own Gain (not for music).
    /// </summary>
    private SoundDef? Resolve(SoundDef def, FVector location, int depth, ref float gain, bool music)
    {
        if (depth > 8)
        {
            return null;
        }

        switch (def.Kind)
        {
            case "random":
            {
                Span<int> weights = stackalloc int[def.Entries.Length];
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] = def.Entries[i].Value;
                }

                int pick = SoundRules.PickRandom(weights, _random.Next(AudioK.RandomTotal));
                gain *= music ? 1f : def.Volume;
                return pick < 0 ? null : Entry(def, pick, location, depth, ref gain, music);
            }
            case "switch":
            {
                int surface = _pendingSwitch;
                _pendingSwitch = -1;
                if (surface < 0 && World is not null)
                {
                    surface = World.SurfaceOf(location + new FVector(0f, 0f, AudioK.SwitchTraceUp),
                        location - new FVector(0f, 0f, AudioK.SwitchTraceDown));
                }

                Span<int> values = stackalloc int[def.Entries.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = def.Entries[i].Value;
                }

                int pick = SoundRules.PickSwitch(values, Math.Max(surface, 0));
                gain *= music ? 1f : def.Volume;
                return pick < 0 ? null : Entry(def, pick, location, depth, ref gain, music);
            }
            default:
                return def;
        }
    }

    private SoundDef? Entry(SoundDef list, int index, FVector location, int depth, ref float gain, bool music)
    {
        SoundDef? next = Library!.Find(list.Entries[index].Sound);
        return next is null ? null : Resolve(next, location, depth + 1, ref gain, music);
    }

    /// <summary>Rule 14: plays the sequence's next entry on its voice; false when the sequence is over.</summary>
    private bool NextInSequence(Voice v)
    {
        SequenceState s = v.Sequence!;
        for (int guard = 0; guard < 64; guard++)
        {
            if (s.Index >= s.Def.Entries.Length)
            {
                return false;
            }

            SoundEntry entry = s.Def.Entries[s.Index];
            SoundDef? def = Library!.Find(entry.Sound);
            float chain = 1f;
            SoundDef? leaf = def is null ? null : Resolve(def, v.Location, 1, ref chain, v.Music);
            bool timed = false;
            if (s.Remaining == 0)
            {
                s.Remaining = entry.Value;
                if ((entry.Value & AudioK.SequenceTimedLoop) != 0)
                {
                    float duration = leaf?.Duration ?? 0f;
                    v.LoopUntil = _now + (duration * (entry.Value & (AudioK.SequenceTimedLoop - 1)));
                    s.Remaining = 1;
                    timed = true;
                }
            }

            if (leaf is null || leaf.Kind == "sequence" || !PlayLeaf(v, leaf, timed, chain))
            {
                s.Index++;
                s.Remaining = 0;
                continue;
            }

            if (--s.Remaining <= 0)
            {
                s.Index++;
                s.Remaining = 0;
            }

            return true;
        }

        return false;
    }

    private bool PlayLeaf(Voice v, SoundDef leaf, bool loop, float chain = 1f)
    {
        AudioStream? stream = Library!.Stream(leaf, loop);
        if (stream is null)
        {
            Silent++;
            return false;
        }

        v.Leaf = leaf;
        v.Radius = SoundRules.Radius(leaf.Radius, v.CallRadius);
        v.MinDistance = SoundRules.MinDistance(v.Radius);
        v.Volume = SoundRules.SourceVolume(v.BaseVolume * chain, leaf.Volume, v.Music);
        v.Positional = (v.Ambient ? v.AmbientPositional : leaf.Positional) && !v.Music && !v.Flat;
        v.Looping = loop;
        v.Play(stream);
        if (Started.Count >= MaxStarts)
        {
            Started.RemoveRange(0, MaxStarts / 2);
            Starts.RemoveRange(0, Math.Min(Starts.Count, MaxStarts / 2));
        }

        Started.Add(leaf.Path);
        Starts.Add(new SoundStart(v.CallPath, leaf.Path, v.ActorId, v.Positional, v.Ambient, v.Music, v.Volume, _now));
        StartCount++;
        return true;
    }

    /// <summary>Rule 15: a free voice, else the same slot (unless the new call keeps it), else the lowest priority.</summary>
    private Voice? Allocate(int id, float priority)
    {
        foreach (Voice v in _voices)
        {
            if (!v.Active)
            {
                return v;
            }
        }

        Voice? chosen = null;
        float lowest = priority;
        foreach (Voice v in _voices)
        {
            if (((v.Id ^ id) & ~1) == 0)
            {
                if ((id & 1) != 0)
                {
                    return null;
                }

                chosen = v;
                break;
            }

            if (v.Priority < lowest)
            {
                lowest = v.Priority;
                chosen = v;
            }
        }

        chosen?.Stop();
        return chosen;
    }

    // ---- every update ---------------------------------------------------------------------------------------

    private void UpdateVoice(Voice v, float dt, string worldBus)
    {
        if (v.Follow && World is not null)
        {
            if (!v.Bone.IsNone && World.TryGetBoneLocation(v.ActorId, v.Bone, out FVector onBone))
            {
                v.Location = onBone;
            }
            else if (World.TryGetActorLocation(v.ActorId, out FVector at))
            {
                v.Location = at;
            }
            else
            {
                v.Follow = false;
            }
        }

        if (v.LoopUntil > 0 && _now >= v.LoopUntil)
        {
            // A timed sequence entry (rule 14) has looped long enough: the sequence moves on below.
            v.LoopUntil = 0;
            v.Looping = false;
            v.Halt();
        }

        if (!v.IsPlaying && !v.Looping)
        {
            if (v.Sequence is null || !NextInSequence(v))
            {
                if (v == _music)
                {
                    _music = null;
                }

                v.Stop();
                return;
            }
        }

        if (!v.StepFade(dt))
        {
            v.Stop();
            return;
        }

        float d = FVector.Dist(v.Location, ListenerLocation);

        // The audio zoom scales the source volume, which DirectSound cannot raise above full.
        float volume = v.Positional && AudioZoom > 0f ? Math.Min(1f, v.Volume * AudioZoomGain(v.Location)) : v.Volume;
        float gain = volume * v.Fade;
        if (v.Positional)
        {
            gain *= SoundRules.DistanceGain(d, OccludedMinDistance(v));
        }

        v.Apply(gain, v.Pitch * Doppler(v, dt), v.Music ? ReverbBuses.MusicBus : v.Positional ? worldBus : ReverbBuses.FlatBus,
            UeSpace.ToGodot(v.Location));
        v.Priority = SoundRules.Priority(d * d, v.Radius, v.Volume, v.Leaf?.Flags ?? 0);
    }

    /// <summary>
    /// Rule 17: the Doppler pitch factor of a 3D source, from its movement over this update and the listener's. The
    /// listener's own sounds (the viewed actor's) get no source velocity.
    /// </summary>
    private float Doppler(Voice v, float dt)
    {
        FVector velocity = v.HasLastLocation && dt > 0f ? (v.Location - v.LastLocation) * (1f / dt) : default;
        v.LastLocation = v.Location;
        v.HasLastLocation = true;
        if (!v.Positional || v.Music)
        {
            return 1f;
        }

        if (v.ActorId != 0 && v.ActorId == ViewActorId)
        {
            velocity = default;
        }

        return SoundRules.DopplerPitch(ListenerLocation, ListenerVelocity, v.Location, velocity);
    }

    /// <summary>
    /// Rule 18 on the DirectSound path (UseEAX off; game 0x10b05e80 and 0x10b0bc80): every update the occlusion
    /// factor between the listener and the source shrinks the source's minimum distance, down to 0.1 uu; the
    /// distance eases toward a new value in proportion to the time since it last settled and settles after 1 s.
    /// </summary>
    private float OccludedMinDistance(Voice v)
    {
        float target = v.MinDistance;
        if (World is not null && v.SoundOcclusion != AudioK.OcclusionNone && (v.ActorId == 0 || v.ActorId != ViewActorId) &&
            World.OcclusionBetween(ListenerLocation, v.Location, out OcclusionHits hits) &&
            SoundRules.Occlusion(FVector.Dist(hits.NearListener, hits.NearSource), hits.TypeNearListener, hits.TypeNearSource,
                hits.ZoneNearListener, hits.ZoneNearSource,
                Library?.Map?.ZoneDistance(hits.ZoneNearListener, hits.ZoneNearSource) ?? 0) is { } factor)
        {
            target = Math.Max(v.MinDistance * factor, AudioK.OccludedMinDistance);
        }

        double since = _now - v.OcclusionSettledAt;
        if (!v.OcclusionSettled || since >= AudioK.OcclusionSettleSeconds)
        {
            v.OccludedMinDistance = target;
            v.OcclusionSettledAt = _now;
            v.OcclusionSettled = true;
        }
        else
        {
            v.OccludedMinDistance += (target - v.OccludedMinDistance) * (float)since;
        }

        return v.OccludedMinDistance;
    }

    private float AudioZoomGain(FVector location)
    {
        ListenerRotation.GetAxes(out FVector x, out _, out _);
        FVector to = location - ListenerLocation;
        float forward = to | x;
        float sideways = MathF.Sqrt(MathF.Max(to.SizeSquared() - (forward * forward), 0f));
        return SoundRules.AudioZoomGain(AudioZoom, AudioZoomFov, forward, sideways);
    }

    /// <summary>Rules 19 to 22: start, keep or stop each actor's ambient loop.</summary>
    private void UpdateAmbient()
    {
        _ambient.Clear();
        if (World is not null)
        {
            World.CollectAmbient(_ambient);
        }

        AddStaticAmbient();
        _ambientSeen.Clear();
        _loudest2D.Clear();
        // Pass 1: which actor is the loudest for each 2D sound (rule 22).
        foreach (AmbientSource a in _ambient)
        {
            SoundDef? def = Library!.Find(a.Sound);
            if (def is null || def.Positional || a.IsZone)
            {
                continue;
            }

            float v2 = SoundRules.Ambient2DVolume(AmbientVolume(a), def.Volume, FVector.Dist(a.Location, ListenerLocation),
                def.Radius > 0f ? def.Radius : a.SoundRadius);
            if (v2 >= AudioK.Ambient2DCut && (!_loudest2D.TryGetValue(def.Path, out var best) || v2 > best.Volume))
            {
                _loudest2D[def.Path] = (a.ActorId, v2);
            }
        }

        foreach (AmbientSource a in _ambient)
        {
            SoundDef? def = Library!.Find(a.Sound);
            if (def is null)
            {
                continue;
            }

            float volume = AmbientVolume(a);
            float radius = def.Radius > 0f ? def.Radius : a.SoundRadius;
            float d = FVector.Dist(a.Location, ListenerLocation);
            bool audible;
            float playVolume = volume;
            if (def.Positional)
            {
                // Rule 20: the range shrinks with the portal distance between the actor's zone and the listener's.
                int zoneDistance = World is null ? 0 : Library.Map?.ZoneDistance(World.ZoneIndexAt(a.Location), _listenerZoneIndex) ?? 0;
                audible = d <= AudioK.RangeFactor * radius * SoundRules.AmbientZoneFactor(zoneDistance);
            }
            else if (a.IsZone)
            {
                audible = string.Equals(a.Id, _listenerZone, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                audible = _loudest2D.TryGetValue(def.Path, out var best) && best.ActorId == a.ActorId;
                if (audible)
                {
                    // The fall-off is applied as volume; the source plays 2D.
                    playVolume = best.Volume / Math.Max(def.Volume, AudioK.MinVolume);
                }
            }

            if (!audible)
            {
                continue;
            }

            _ambientSeen.Add(a.ActorId);
            int id = (a.ActorId * 16) + AudioK.AmbientSlotId;
            // A voice another call took over (rule 15) belongs to that call now: it is neither this loop nor stopped.
            Voice? running = _ambientVoices.TryGetValue(a.ActorId, out Voice? kept) && OwnsAmbient(kept, id) ? kept : null;
            if (kept is not null && running is null)
            {
                // Its voice went to another call (rule 15): the loop starts again when it wins a voice back.
                _ambientVoices.Remove(a.ActorId);
                CountAmbientStop(a.ActorId);
            }

            if (running is null && !def.Positional)
            {
                // Rules 21 and 22: a 2D loop already playing this sound for another actor carries on for this one.
                if (Shared2DLoop(def.Path, a) is { } shared)
                {
                    running = shared;
                }
                else if (!a.IsZone && ListenerZoneLoop(def.Path) is not null)
                {
                    // The listener's zone ambience keeps a sound it plays; no second copy starts.
                    continue;
                }
            }

            if (running is not null && string.Equals(running.CallPath, def.Path, StringComparison.OrdinalIgnoreCase))
            {
                running.BaseVolume = playVolume;
                running.Volume = SoundRules.SourceVolume(playVolume, running.Leaf?.Volume ?? def.Volume, false);
                running.Pitch = SoundRules.AmbientPitch(a.SoundPitch);
                running.CallRadius = a.SoundRadius;
                if (!running.Follow)
                {
                    running.Location = a.Location;
                }

                continue;
            }

            running?.Stop();
            var call = new PlayCall(def, a.ActorId, id, a.Location, playVolume, radius, SoundRules.AmbientPitch(a.SoundPitch),
                music: false, loop: true, ambient: true, occlusion: a.SoundOcclusion);
            if (Start(call, follow: a.ActorId > 0) && LastStarted is { } v)
            {
                v.AmbientZoneId = a.IsZone ? a.Id : null;
                _ambientVoices[a.ActorId] = v;
            }
        }

        // Loops whose actor no longer has its sound, or left the range.
        List<int>? gone = null;
        foreach (KeyValuePair<int, Voice> kv in _ambientVoices)
        {
            if (!_ambientSeen.Contains(kv.Key) || !OwnsAmbient(kv.Value, (kv.Key * 16) + AudioK.AmbientSlotId))
            {
                (gone ??= []).Add(kv.Key);
            }
        }

        if (gone is not null)
        {
            foreach (int k in gone)
            {
                if (_ambientVoices.Remove(k, out Voice? v))
                {
                    CountAmbientStop(k);
                    if (OwnsAmbient(v, (k * 16) + AudioK.AmbientSlotId))
                    {
                        v.Stop();
                    }
                }
            }
        }
    }

    /// <summary>
    /// The 2D ambient loop playing <paramref name="sound"/> for another actor, handed over to <paramref name="a"/> without
    /// a restart (UpdateAmbientSounds, editor 0x10f6d390: a 2D candidate whose sound a source already plays takes that
    /// source over, its actor, id, location and volume, when it is louder or the source's actor is the zone the listener
    /// left). A non-zone actor never takes the listener zone's loop. Null when no such loop plays.
    /// </summary>
    private Voice? Shared2DLoop(string sound, in AmbientSource a)
    {
        int owner = 0;
        Voice? found = null;
        foreach (KeyValuePair<int, Voice> kv in _ambientVoices)
        {
            Voice v = kv.Value;
            if (kv.Key != a.ActorId && OwnsAmbient(v, (kv.Key * 16) + AudioK.AmbientSlotId) && !v.AmbientPositional &&
                string.Equals(v.CallPath, sound, StringComparison.OrdinalIgnoreCase))
            {
                owner = kv.Key;
                found = v;
                break;
            }
        }

        if (found is null || (!a.IsZone && found.AmbientZoneId is not null &&
                              string.Equals(found.AmbientZoneId, _listenerZone, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        _ambientVoices.Remove(owner);
        found.ActorId = a.ActorId;
        found.Id = (a.ActorId * 16) + AudioK.AmbientSlotId;
        found.Follow = a.ActorId > 0;
        found.Location = a.Location;
        found.AmbientZoneId = a.IsZone ? a.Id : null;
        _ambientVoices[a.ActorId] = found;
        return found;
    }

    /// <summary>The listener zone's ambience loop when it plays <paramref name="sound"/>, else null.</summary>
    private Voice? ListenerZoneLoop(string sound)
    {
        foreach (KeyValuePair<int, Voice> kv in _ambientVoices)
        {
            Voice v = kv.Value;
            if (v.AmbientZoneId is not null && string.Equals(v.AmbientZoneId, _listenerZone, StringComparison.OrdinalIgnoreCase) &&
                OwnsAmbient(v, (kv.Key * 16) + AudioK.AmbientSlotId) &&
                string.Equals(v.CallPath, sound, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        return null;
    }

    private void CountAmbientStop(int actorId) => AmbientStops[actorId] = AmbientStops.TryGetValue(actorId, out int n) ? n + 1 : 1;

    /// <summary>Whether <paramref name="v"/> still plays the ambient loop of sound id <paramref name="id"/>.</summary>
    private static bool OwnsAmbient(Voice v, int id) => v.Active && v.Ambient && v.Id == id;

    /// <summary>The client-only actors the simulation does not have (AmbientSound actors and other render actors).</summary>
    private void AddStaticAmbient()
    {
        MapAudioDoc? map = Library!.Map;
        if (map is null)
        {
            return;
        }

        bool fromWorld = World is not null;
        for (int i = 0; i < map.Actors.Count; i++)
        {
            MapSoundActor a = map.Actors[i];
            if (a.AmbientSound is null || (fromWorld && a.Category != "render"))
            {
                continue;
            }

            // Negative ids keep these apart from simulated actors.
            _ambient.Add(new AmbientSource(-(i + 1), a.Id, a.Location, a.AmbientSound, a.SoundVolume, a.SoundRadius,
                a.SoundPitch, a.Class.EndsWith("ZoneInfo", StringComparison.Ordinal) || a.Class.EndsWith("LevelInfo", StringComparison.Ordinal),
                a.LightType, a.LightBrightness, a.SoundOcclusion));
        }
    }

    /// <summary>Rule 19: SoundVolume / 255 (ambient volume setting 1), times LightBrightness / 255 for lights.</summary>
    private static float AmbientVolume(in AmbientSource a)
    {
        float v = a.SoundVolume / AudioK.VolumeUnit;
        if (a.LightType != 0)
        {
            v *= a.LightBrightness / AudioK.VolumeUnit;
        }

        return v;
    }

    // ---- voices ---------------------------------------------------------------------------------------------

    private sealed class SequenceState(SoundDef def)
    {
        public SoundDef Def { get; } = def;

        public int Index;
        public int Remaining;
    }

    private sealed class Voice
    {
        public Voice(int index)
        {
            Player3D = new AudioStreamPlayer3D
            {
                Name = "Voice3D" + index,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
                // Godot's high-shelf attenuation also responds to VolumeDb when distance attenuation is
                // disabled. Our own distance/occlusion gain must not additionally muffle quiet sources.
                AttenuationFilterDb = 0f,
                AttenuationFilterCutoffHz = 20500f,
                MaxDistance = 0f,
                DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
                Bus = ReverbBuses.WorldBus,
            };
            Player2D = new AudioStreamPlayer { Name = "Voice2D" + index, Bus = ReverbBuses.FlatBus };
            _bus3D = ReverbBuses.WorldBus;
            _bus2D = ReverbBuses.FlatBus;
        }

        // The bus each player was last given: the Bus property takes a StringName, so setting it every frame made a
        // StringName (a finalizable wrapper) per voice per frame.
        private string _bus3D;
        private string _bus2D;
        private float _db3D = float.NaN, _db2D = float.NaN;
        private float _pitch3D = float.NaN, _pitch2D = float.NaN;
        private Vector3? _position3D;

        public AudioStreamPlayer3D Player3D { get; }

        public AudioStreamPlayer Player2D { get; }

        public bool Active;
        public string CallPath = "";
        public SoundDef? Leaf;
        public int ActorId;
        public int Id;
        public bool Follow;

        /// <summary>The bone of the actor's mesh the source follows (None: the actor's location).</summary>
        public Name Bone;
        public FVector Location;

        /// <summary>Where the source was at the last update (its Doppler velocity).</summary>
        public FVector LastLocation;
        public bool HasLastLocation;
        public float Priority;
        public bool Music;
        public bool Flat;
        public bool Ambient;
        public bool AmbientPositional;

        /// <summary>The map id of the ZoneInfo whose ambience this loop is (rule 21), else null.</summary>
        public string? AmbientZoneId;
        public float BaseVolume;
        public float Volume;
        public float CallRadius;
        public float Radius;
        public float MinDistance;

        /// <summary>The actor's SoundOcclusion (rule 18) and the eased, occluded minimum distance.</summary>
        public byte SoundOcclusion;
        public float OccludedMinDistance;
        public double OcclusionSettledAt;
        public bool OcclusionSettled;
        public float Pitch = 1f;
        public bool Positional;
        public bool Looping;
        public double LoopUntil;
        public SequenceState? Sequence;
        public float Fade = 1f;
        private float _fadeTarget = 1f;
        private float _fadeRate;
        public bool StopWhenFaded;

        public bool IsPlaying => Positional ? Player3D.Playing : Player2D.Playing;

        public void Begin(string callPath, int actorId, int id, bool follow, FVector location, float priority)
        {
            Active = true;
            CallPath = callPath;
            ActorId = actorId;
            Id = id;
            Follow = follow;
            Bone = default;
            Location = location;
            HasLastLocation = false;
            Priority = priority;
            Sequence = null;
            LoopUntil = 0;
            Fade = 1f;
            _fadeTarget = 1f;
            _fadeRate = 0f;
            StopWhenFaded = false;
        }

        public void Play(AudioStream stream)
        {
            Halt();
            if (Positional)
            {
                Player3D.Stream = stream;
                Player3D.PitchScale = Pitch;
                _pitch3D = Pitch;
                Player3D.Play();
            }
            else
            {
                Player2D.Stream = stream;
                Player2D.PitchScale = Pitch;
                _pitch2D = Pitch;
                Player2D.Play();
            }
        }

        public void Apply(float gain, float pitch, string bus, Vector3 position)
        {
            float db = Mathf.LinearToDb(Math.Max(gain, AudioK.MinVolume));
            if (Positional)
            {
                if (_db3D != db) { _db3D = db; Player3D.VolumeDb = db; }
                if (_pitch3D != pitch) { _pitch3D = pitch; Player3D.PitchScale = pitch; }
                if (!string.Equals(_bus3D, bus, StringComparison.Ordinal))
                {
                    _bus3D = bus;
                    Player3D.Bus = bus;
                }

                if (_position3D != position) { _position3D = position; Player3D.Position = position; }
            }
            else
            {
                if (_db2D != db) { _db2D = db; Player2D.VolumeDb = db; }
                if (_pitch2D != pitch) { _pitch2D = pitch; Player2D.PitchScale = pitch; }
                if (!string.Equals(_bus2D, bus, StringComparison.Ordinal))
                {
                    _bus2D = bus;
                    Player2D.Bus = bus;
                }
            }
        }

        public void FadeTo(float from, float to, float seconds)
        {
            Fade = from;
            _fadeTarget = to;
            _fadeRate = seconds > 0f ? Math.Abs(to - from) / seconds : float.PositiveInfinity;
        }

        public void FadeOut(float seconds)
        {
            FadeTo(Fade, 0f, seconds);
            StopWhenFaded = true;
        }

        /// <summary>Moves the fade; false when a fade-out has finished.</summary>
        public bool StepFade(float dt)
        {
            if (Fade != _fadeTarget)
            {
                float step = _fadeRate * dt;
                Fade = Fade < _fadeTarget ? Math.Min(_fadeTarget, Fade + step) : Math.Max(_fadeTarget, Fade - step);
            }

            return !(StopWhenFaded && Fade <= 0f);
        }

        public void Halt()
        {
            Player3D.Stop();
            Player2D.Stop();
        }

        public void Stop()
        {
            Halt();
            Active = false;
            Sequence = null;
            Leaf = null;
            Ambient = false;
            AmbientPositional = false;
            AmbientZoneId = null;
            Music = false;
            Flat = false;
            Id = 0;
            Priority = 0f;
        }
    }
}
