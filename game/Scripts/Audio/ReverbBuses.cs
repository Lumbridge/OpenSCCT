using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Provenance;

namespace OpenScctGame.Audio;

/// <summary>
/// The audio buses of the sound system: <c>SvmWorld</c> (3D sounds in a zone without reverb), one reverb bus per
/// distinct zone effect (the I3DL2 parameters of ZoneInfo.ZoneEffect, spec/native/audio.md rule 25), <c>SvmFlat</c>
/// (2D sounds: the original's reverb only reaches 3D sources) and <c>SvmMusic</c>. All send to Master. Godot has no
/// I3DL2 reverb: <see cref="ToGodot"/> maps the parameters onto its AudioEffectReverb.
/// </summary>
public sealed class ReverbBuses
{
    public const string WorldBus = "SvmWorld";
    public const string FlatBus = "SvmFlat";
    public const string MusicBus = "SvmMusic";

    private readonly Dictionary<string, string> _busOfZone = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _busOfParams = new(StringComparer.Ordinal);

    public ReverbBuses(MapAudioDoc? map)
    {
        Ensure(WorldBus);
        Ensure(FlatBus);
        Ensure(MusicBus);
        if (map is null)
        {
            return;
        }

        foreach (MapSoundZone z in map.Zones.Values)
        {
            if (z.Effect.Count == 0)
            {
                continue;
            }

            string key = Key(z.Effect);
            if (!_busOfParams.TryGetValue(key, out string? bus))
            {
                bus = "SvmReverb" + _busOfParams.Count.ToString(CultureInfo.InvariantCulture);
                int index = Ensure(bus);
                while (AudioServer.GetBusEffectCount(index) > 0)
                {
                    AudioServer.RemoveBusEffect(index, 0);
                }

                AudioServer.AddBusEffect(index, ToGodot(z.Effect));
                _busOfParams[key] = bus;
            }

            _busOfZone[z.Id] = bus;
        }
    }

    /// <summary>Reverb buses made for this map.</summary>
    public int ReverbBusCount => _busOfParams.Count;

    /// <summary>Zones that have a reverb.</summary>
    public int ZoneCount => _busOfZone.Count;

    /// <summary>The bus 3D sources play on while the listener is in <paramref name="zoneId"/>.</summary>
    public string BusForZone(string? zoneId) =>
        zoneId is not null && _busOfZone.TryGetValue(zoneId, out string? bus) ? bus : WorldBus;

    /// <summary>
    /// I3DL2 to Godot: wet level from Room + Reverb (millibels), room size from the decay time, damping from RoomHF
    /// and the high-frequency decay ratio, spread from the diffusion, pre-delay from the reflection and reverb delays.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Godot's reverb is not I3DL2; the mapping is a listening match, not decoded")]
    public static AudioEffectReverb ToGodot(IReadOnlyDictionary<string, float> p)
    {
        ArgumentNullException.ThrowIfNull(p);
        float Get(string k, float d) => p.TryGetValue(k, out float v) ? v : d;
        float room = Get("Room", -1000f);
        float reverb = Get("Reverb", 200f);
        float decay = Get("DecayTime", 1.49f);
        float wet = Math.Clamp(MathF.Pow(10f, (room + reverb) / 2000f), 0f, 1f);
        return new AudioEffectReverb
        {
            RoomSize = Math.Clamp(decay / 6f, 0.05f, 1f),
            Damping = Math.Clamp((-Get("RoomHF", -100f) / 2000f) + ((1f - Get("DecayHFRatio", 0.83f)) * 0.5f), 0f, 1f),
            Spread = Math.Clamp(Get("EnvironmentDiffusion", 1f), 0f, 1f),
            PredelayMsec = Math.Clamp((Get("ReflectionsDelay", 0.007f) + Get("ReverbDelay", 0.011f)) * 1000f, 20f, 500f),
            PredelayFeedback = 0.2f,
            Hipass = 0f,
            Dry = 1f,
            Wet = wet,
        };
    }

    private static string Key(IReadOnlyDictionary<string, float> p)
    {
        var keys = new List<string>(p.Keys);
        keys.Sort(StringComparer.Ordinal);
        var parts = new List<string>(keys.Count);
        foreach (string k in keys)
        {
            parts.Add(k + "=" + p[k].ToString("R", CultureInfo.InvariantCulture));
        }

        return string.Join(";", parts);
    }

    private static int Ensure(string name)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index >= 0)
        {
            return index;
        }

        AudioServer.AddBus();
        index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
        return index;
    }
}
