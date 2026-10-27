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
    /// I3DL2 to Godot's AudioEffectReverb (a Freeverb: eight damped feedback combs, then four allpasses). Where the
    /// two models line up the mapping follows the I3DL2 definitions:
    /// <list type="bullet">
    /// <item>decay: the comb feedback g that makes a comb of the mean Freeverb length L lose 60 dB in DecayTime,
    /// g = 10^(-3 L / DecayTime); Godot's room size is (g - 0.7) / 0.28 (its feedback range 0.7 to 0.98, so decays
    /// below about 0.6 s or above about 10 s are clamped);</item>
    /// <item>high-frequency decay: the comb's one-pole low-pass is set so a loop at HFReference keeps the gain that
    /// gives DecayTime x DecayHFRatio there; Godot's damping picks the low-pass cutoff, (damping / 2 + 0.5)^2 x 10 kHz,
    /// so its range is 2.5 to 10 kHz (a ratio of 1 or more takes the 10 kHz cutoff, the least Godot can damp);</item>
    /// <item>level: Room (the room effect's attenuation) plus Reflections and Reverb (each relative to Room) as linear
    /// amplitudes, early and late summed as energy, sent as the wet level, the dry path at full level;</item>
    /// <item>pre-delay: ReflectionsDelay + ReverbDelay (when the late reverb starts), within Godot's 20 to 500 ms,
    /// with no pre-delay feedback (I3DL2 has none).</item>
    /// </list>
    /// Not representable and left out: RoomHF and RoomLF (a shelf on the room effect: Godot's reverb has only a
    /// high-pass on its input), EnvironmentDiffusion and Density (the allpass gains are fixed at 0.5), the
    /// reflections' own pattern, and RoomRolloffFactor (Godot's buses do not see the source distance). The absolute
    /// wet level is uncalibrated: Freeverb's output gain is not the I3DL2 reference level.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Godot's reverb is not I3DL2: decay, HF decay, levels and pre-delay are derived from the I3DL2 definitions and Freeverb's structure; the wet calibration, RoomHF, diffusion and density are not reproduced")]
    public static AudioEffectReverb ToGodot(IReadOnlyDictionary<string, float> p)
    {
        ArgumentNullException.ThrowIfNull(p);
        float Get(string k, float d) => p.TryGetValue(k, out float v) ? v : d;
        float room = Get("Room", -1000f);
        float reflections = Get("Reflections", -2602f);
        float reverb = Get("Reverb", 200f);
        float decay = Math.Max(Get("DecayTime", 1.49f), 0.1f);
        float hfRatio = Math.Max(Get("DecayHFRatio", 0.83f), 0.1f);
        float hfReference = Math.Clamp(Get("HFReference", 5000f), 1000f, 20000f);

        // Comb feedback for the decay time (60 dB over DecayTime along a loop of the mean comb length).
        float g = MillibelsToLinear(-6000f * FreeverbMeanComb / decay);
        float roomSize = Math.Clamp((g - FreeverbRoomOffset) / FreeverbRoomScale, 0f, 1f);
        float feedback = (roomSize * FreeverbRoomScale) + FreeverbRoomOffset;

        // The loop gain wanted at HFReference over the loop gain: what the comb's low-pass must pass there.
        float damping = 1f;
        if (hfRatio < 1f)
        {
            float want = MillibelsToLinear(-6000f * FreeverbMeanComb / (decay * hfRatio)) / feedback;
            double c = DampFor(Math.Clamp(want, 0f, 1f), hfReference);
            double cutoff = -Math.Log(Math.Max(c, 1e-6)) * GodotMixRate / (2.0 * Math.PI);
            damping = Math.Clamp((float)((Math.Sqrt(cutoff / GodotDampCutoff) - 0.5) * 2.0), 0f, 1f);
        }

        float early = MillibelsToLinear(room + reflections);
        float late = MillibelsToLinear(room + reverb);
        return new AudioEffectReverb
        {
            RoomSize = roomSize,
            Damping = damping,
            Spread = 1f,
            PredelayMsec = Math.Clamp((Get("ReflectionsDelay", 0.007f) + Get("ReverbDelay", 0.011f)) * 1000f, 20f, 500f),
            PredelayFeedback = 0f,
            Hipass = 0f,
            Dry = 1f,
            Wet = Math.Clamp(MathF.Sqrt((early * early) + (late * late)), 0f, 1f),
        };
    }

    /// <summary>Mean length of the eight comb tunings of Godot's reverb (25.3 to 36.7 ms), seconds.</summary>
    private const float FreeverbMeanComb = 0.0312483f;

    /// <summary>Freeverb's room size to comb feedback: feedback = size x 0.28 + 0.7.</summary>
    private const float FreeverbRoomScale = 0.28f;

    private const float FreeverbRoomOffset = 0.7f;

    /// <summary>Godot's reverb damping: the comb low-pass cutoff is (damping / 2 + 0.5)^2 x this, in Hz.</summary>
    private const double GodotDampCutoff = 10000.0;

    /// <summary>The rate the low-pass is solved at (its cutoff barely depends on it).</summary>
    private const double GodotMixRate = 44100.0;

    private static float MillibelsToLinear(float mB) => MathF.Pow(10f, mB / 2000f);

    /// <summary>The one-pole coefficient d whose low-pass (1 - d) / (1 - d z^-1) passes <paramref name="gain"/> at <paramref name="hz"/>.</summary>
    private static double DampFor(float gain, float hz)
    {
        double w = 2.0 * Math.PI * hz / GodotMixRate;
        double lo = 0.0;
        double hi = 0.999;
        for (int i = 0; i < 40; i++)
        {
            double d = (lo + hi) * 0.5;
            double h = (1.0 - d) / Math.Sqrt(1.0 - (2.0 * d * Math.Cos(w)) + (d * d));
            if (h > gain)
            {
                lo = d;
            }
            else
            {
                hi = d;
            }
        }

        return (lo + hi) * 0.5;
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
