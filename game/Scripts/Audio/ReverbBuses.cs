using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Godot;

namespace OpenScctGame.Audio;

/// <summary>One native EAX environment per viewport listener; zone changes retain its existing reverb tail.</summary>
public sealed class ReverbBuses : IDisposable
{
    public const string WorldBus = "SvmWorld";
    public const string FlatBus = "SvmFlat";
    /// <summary>The music of a round (a map's Song, the end of round music).</summary>
    public const string MusicBus = "SvmMusic";

    /// <summary>The menus' music (the Entry and Lobby songs, the hub's jukebox).</summary>
    public const string MenuMusicBus = "SvmMenuMusic";

    /// <summary>The objective jingles (<see cref="SoundRules.IsObjectiveJingle"/>).</summary>
    public const string ObjectiveBus = "SvmObjective";

    /// <summary>The menus' own sounds (button feedback, the lobby's join sound, the random map draw).</summary>
    public const string MenuSoundBus = "SvmMenuSound";
    private const string EffectClass = "ScctEaxReverbEffect";
    private const string FilterClass = "ScctEaxSourceFilter";
    private static int _nextId;
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly Dictionary<string, EaxEnvironment> _environments = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _owned = [];
    private readonly List<AudioEffect> _effects = [];
    private readonly AudioEffect? _effect;
    private EaxEnvironment? _applied;
    private EaxEnvironment? _selected;
    private bool _decided;
    private bool _disposed;

    public ReverbBuses(MapAudioDoc? map)
    {
        Ensure(WorldBus);
        Ensure(FlatBus);
        Ensure(MusicBus);
        Ensure(MenuMusicBus);
        Ensure(ObjectiveBus);
        Ensure(MenuSoundBus);
        NativeAvailable = ClassDB.ClassExists(EffectClass) && ClassDB.ClassExists(FilterClass);
        if (map is null) return;
        if (!NativeAvailable)
            throw new InvalidOperationException("SCCT EAX audio extension is missing. Run tools/audio/Build-NativeAudio.ps1 before loading map audio.");
        foreach (MapSoundZone zone in map.Zones.Values)
            if (zone.Effect.Count > 0) _environments[zone.Id] = EaxEnvironment.FromParameters(zone.Effect);
        EnvironmentBus = NewBus("SvmReverb" + _id.ToString(CultureInfo.InvariantCulture), "Master");
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(EnvironmentBus),
            AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(WorldBus)));
        _effect = Create(EffectClass);
        _effects.Add(_effect);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(EnvironmentBus), _effect);
        if (!_effect.Call("configure_eax", EaxEnvironment.DriverDefault.ToEaxArray()).AsBool())
            throw new InvalidOperationException("Could not initialize EAX driver defaults.");
        _applied = EaxEnvironment.DriverDefault;
    }

    public bool NativeAvailable { get; }
    public string? EnvironmentBus { get; }
    public EaxEnvironment? CurrentEnvironment => _applied;
    public int ReverbBusCount => _effect is null ? 0 : 1;
    public int ZoneCount => _environments.Count;
    public string BackendVersion => _effect is null ? "dry" : _effect.Call("backend_version").AsString();
    public string BusForZone(string? zoneId) => EnvironmentBus ?? WorldBus;

    public void SetEnvironment(string? zoneId)
    {
        if (_effect is null) return;
        EaxEnvironment? selected = zoneId is not null && _environments.TryGetValue(zoneId, out EaxEnvironment? found) ? found : null;
        // The first selection always goes to the effect, a zone without one included. The original leaves the
        // driver's Generic room in place then (spec/native/audio.md rule 25), and in ours that room sends every 3D
        // source to the reverb at full level whatever its distance (room rolloff 0, reverb +200 mB): on a map whose
        // zones have no effect (MoonD) every loop and mine in range was heard all over the map and covered the
        // 2D music. The NULL packet (room rolloff 0.1, reverb -1927 mB) is what such a zone gets after any effect.
        if (_decided && ReferenceEquals(selected, _selected)) return;
        _decided = true;
        _selected = selected;
        EaxEnvironment environment = selected ?? EaxEnvironment.NullZoneEffect;
        // The original records the selected object before calling EAX, ignores a failed SetAll, and leaves
        // the previous environment/tail in place. Fourteen original blocks are rejected by the vendor.
        if (!environment.IsOriginalValid) return;
        if (!_effect.Call("configure_eax", environment.ToEaxArray()).AsBool())
            return; // Preserve the previous room for any additional rejection by the original property validator.
        _applied = environment;
    }

    public EaxSourceRoute? CreateSourceRoute(int voice)
    {
        if (!NativeAvailable || EnvironmentBus is null) return null;
        string suffix = _id.ToString(CultureInfo.InvariantCulture) + "_" + voice.ToString(CultureInfo.InvariantCulture);
        string dry = NewBus("SvmEaxDry" + suffix, WorldBus);
        string wet = NewBus("SvmEaxSend" + suffix, EnvironmentBus);
        AudioEffect direct = Create(FilterClass), room = Create(FilterClass);
        _effects.Add(direct);
        _effects.Add(room);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(dry), direct);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(wet), room);
        return new EaxSourceRoute(dry, wet, direct, room);
    }

    private static AudioEffect Create(string name) => ClassDB.Instantiate(name).AsGodotObject() as AudioEffect ??
        throw new InvalidOperationException("Could not create native SCCT audio effect " + name);

    private string NewBus(string name, string send)
    {
        int index = Ensure(name);
        AudioServer.SetBusSend(index, send);
        _owned.Add(name);
        return name;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Players stop first. Source buses retire before the environment destination; the audio server retains
        // effect instances and their native state until the mix thread finishes its outstanding work.
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            int index = AudioServer.GetBusIndex(_owned[i]);
            if (index >= 0) AudioServer.RemoveBus(index);
        }
        _owned.Clear();
        foreach (AudioEffect effect in _effects) effect.Dispose();
        _effects.Clear();
    }

    private static int Ensure(string name)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index >= 0) return index;
        AudioServer.AddBus();
        index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
        return index;
    }
}

/// <summary>Independent per-source direct/room spectral filters before the signals mix.</summary>
public sealed class EaxSourceRoute(string dryBus, string roomBus, AudioEffect direct, AudioEffect room)
{
    public string DryBus { get; } = dryBus;
    public string RoomBus { get; } = roomBus;
    private EaxSourceLevels _direct = new(float.NaN, float.NaN), _room = new(float.NaN, float.NaN);
    private float _reference = float.NaN;
    public void Set(EaxSourceLevels dry, EaxSourceLevels wet, float reference)
    {
        if (_direct != dry || _reference != reference)
        {
            if (!direct.Call("configure", new float[] { dry.Gain, dry.GainHF, reference }).AsBool())
                throw new InvalidOperationException("Invalid EAX direct-source filter.");
            _direct = dry;
        }
        if (_room != wet || _reference != reference)
        {
            if (!room.Call("configure", new float[] { wet.Gain, wet.GainHF, reference }).AsBool())
                throw new InvalidOperationException("Invalid EAX room-source filter.");
            _room = wet;
        }
        _reference = reference;
    }
}
