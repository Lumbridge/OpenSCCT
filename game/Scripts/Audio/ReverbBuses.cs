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
    private readonly AudioEffectAmplify? _browserGain;
    private readonly bool _browser;
    private EaxEnvironment? _applied;
    private EaxEnvironment? _selected;
    private bool _decided;
    private bool _disposed;

    public ReverbBuses(MapAudioDoc? map, bool browserBackend = false)
    {
        _browser = browserBackend || OperatingSystem.IsBrowser();
        Ensure(WorldBus);
        Ensure(FlatBus);
        Ensure(MusicBus);
        Ensure(MenuMusicBus);
        Ensure(ObjectiveBus);
        Ensure(MenuSoundBus);
        NativeAvailable = ClassDB.ClassExists(EffectClass) && ClassDB.ClassExists(FilterClass);
        if (map is null) return;
        if (!NativeAvailable && !_browser)
            throw new InvalidOperationException("SCCT EAX audio extension is missing. Run tools/audio/Build-NativeAudio.ps1 before loading map audio.");
        foreach (MapSoundZone zone in map.Zones.Values)
            if (zone.Effect.Count > 0) _environments[zone.Id] = EaxEnvironment.FromParameters(zone.Effect);
        EnvironmentBus = NewBus("SvmReverb" + _id.ToString(CultureInfo.InvariantCulture), "Master");
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(EnvironmentBus),
            AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(WorldBus)));
        _effect = _browser ? new AudioEffectReverb { Dry = 0, Wet = 1 } : Create(EffectClass);
        _effects.Add(_effect);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(EnvironmentBus), _effect);
        if (_browser)
        {
            _browserGain = new AudioEffectAmplify();
            _effects.Add(_browserGain);
            AudioServer.AddBusEffect(AudioServer.GetBusIndex(EnvironmentBus), _browserGain);
        }
        if (!Configure(EaxEnvironment.DriverDefault))
            throw new InvalidOperationException("Could not initialize EAX driver defaults.");
        _applied = EaxEnvironment.DriverDefault;
    }

    public bool NativeAvailable { get; }
    public string? EnvironmentBus { get; }
    public EaxEnvironment? CurrentEnvironment => _applied;
    public int ReverbBusCount => _effect is null ? 0 : 1;
    public int ZoneCount => _environments.Count;
    public string BackendVersion => _effect is null ? "dry" : _browser ? "Browser Godot reverb (EAX approximation)" : _effect.Call("backend_version").AsString();
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
        if (!Configure(environment))
            return; // Preserve the previous room for any additional rejection by the original property validator.
        _applied = environment;
    }

    public EaxSourceRoute? CreateSourceRoute(int voice)
    {
        if ((!NativeAvailable && !_browser) || EnvironmentBus is null) return null;
        string suffix = _id.ToString(CultureInfo.InvariantCulture) + "_" + voice.ToString(CultureInfo.InvariantCulture);
        string dry = NewBus("SvmEaxDry" + suffix, WorldBus);
        string wet = NewBus("SvmEaxSend" + suffix, EnvironmentBus);
        AudioEffect direct = _browser ? new AudioEffectHighShelfFilter { Resonance = 0.5f } : Create(FilterClass);
        AudioEffect room = _browser ? new AudioEffectHighShelfFilter { Resonance = 0.5f } : Create(FilterClass);
        _effects.Add(direct);
        _effects.Add(room);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(dry), direct);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(wet), room);
        return new EaxSourceRoute(dry, wet, direct, room);
    }

    private bool Configure(EaxEnvironment environment)
    {
        if (_effect is not AudioEffectReverb reverb)
            return _effect?.Call("configure_eax", environment.ToEaxArray()).AsBool() == true;
        // Browser builds cannot load the native Creative-compatible DSP extension. Preserve the routing,
        // authored room strength, distance sends, HF filtering and tail lifetime with built-in effects.
        // Freeverb's decay/early-reflection model is an approximation of the EAX environment.
        float[] values = environment.ToArray();
        float feedback = MathF.Exp(-6.907755f * 0.03f / Math.Max(0.1f, environment.DecayTime));
        reverb.RoomSize = Math.Clamp((feedback - 0.7f) / 0.28f, 0, 1);
        reverb.Damping = Math.Clamp(1 - values[6] * MathF.Sqrt(values[3]), 0, 1);
        reverb.Spread = Math.Clamp(values[1], 0, 1);
        reverb.PredelayMsec = Math.Clamp(values[9] * 1000, 20, 500);
        reverb.PredelayFeedback = Math.Clamp(values[19], 0, 0.8f);
        if (_browserGain is not null) _browserGain.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, values[2] * values[13]));
        return true;
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
            if (!Configure(direct, DryBus, dry, reference))
                throw new InvalidOperationException("Invalid EAX direct-source filter.");
            _direct = dry;
        }
        if (_room != wet || _reference != reference)
        {
            if (!Configure(room, RoomBus, wet, reference))
                throw new InvalidOperationException("Invalid EAX room-source filter.");
            _room = wet;
        }
        _reference = reference;
    }

    private static bool Configure(AudioEffect effect, string bus, EaxSourceLevels levels, float reference)
    {
        if (effect is not AudioEffectHighShelfFilter filter)
            return effect.Call("configure", new float[] { levels.Gain, levels.GainHF, reference }).AsBool();
        filter.CutoffHz = Math.Clamp(reference, 1, 20500);
        filter.Gain = Math.Clamp(levels.GainHF, 0.0001f, 4);
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(bus), levels.Gain <= 0 ? -80 : Mathf.LinearToDb(levels.Gain));
        return true;
    }
}
