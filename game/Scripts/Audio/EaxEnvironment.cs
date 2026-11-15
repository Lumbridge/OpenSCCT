using System;
using System.Collections.Generic;

namespace OpenScctGame.Audio;

/// <summary>Complete native EAX 3 listener packet, with an EFX view for comparison tools.</summary>
public sealed class EaxEnvironment
{
    public const int PropertyCount = 27;
    private readonly float[] _values;
    private readonly float[] _eax;
    private EaxEnvironment(float[] values, string? invalidProperty = null, float[]? eax = null)
    { _values = values; InvalidProperty = invalidProperty; _eax = eax ?? []; }
    public string? InvalidProperty { get; }
    public bool IsOriginalValid => InvalidProperty is null;
    public float RoomRolloff => _values[25];
    public float DecayTime => _values[5];
    public float TailSeconds => _eax.Length==28 ?
        2*Math.Max(_eax[6],Math.Max(_eax[6]*_eax[7],_eax[6]*_eax[8]))+_eax[10]+_eax[15]+_eax[19]+_eax[21] : 2*DecayTime+1;
    public float HighFrequencyReference => _values[23];
    public float AirAbsorptionMillibels => _eax[23];
    public float[] ToArray() => (float[])_values.Clone();
    public float[] ToEaxArray() => (float[])_eax.Clone();

    public EaxEnvironment WithGeometry(ReverbGeometry room)
    {
        if (!room.Valid) return this;
        if (!IsOriginalValid) return NullZoneEffect.WithGeometry(room);
        float[] eax=ToEaxArray(),efx=ToArray();
        eax[1]=room.Size; efx[0]=Math.Min(room.Size*room.Size*room.Size/16,1);
        eax[2]=efx[1]=Math.Clamp(room.Diffusion*0.65f+eax[2]*0.35f,0,1);
        eax[3]=MathF.Round(Math.Clamp(-600*0.65f+eax[3]*0.35f+2000*MathF.Log10(Math.Max(room.WetGain,0.00001f)),-10000,0));
        efx[2]=MathF.Pow(10,eax[3]/2000);
        eax[6]=efx[5]=room.Decay; eax[7]=efx[6]=room.HFDecayRatio; eax[8]=efx[7]=room.LFDecayRatio;
        eax[9]=MathF.Round(Math.Clamp(-600*0.75f+eax[9]*0.25f-200*MathF.Log10(1+room.EarlyDelay*343/6),-10000,1000));
        efx[8]=MathF.Pow(10,eax[9]/2000); eax[10]=efx[9]=room.EarlyDelay;
        eax[14]=MathF.Round(Math.Clamp(eax[14]*0.25f,-10000,2000)); efx[13]=MathF.Pow(10,eax[14]/2000);
        eax[15]=efx[14]=room.LateDelay;
        eax[19]=efx[18]=Math.Clamp(room.LateDelay*2,0.075f,0.25f);
        eax[20]=efx[19]=Math.Clamp(eax[20]*0.25f+0.2f*Math.Min(room.Size/20,1)*(1-room.Openness),0,0.3f);
        // Geometry supplies absolute dimensions/times; retain only the HF-limit flag.
        eax[27]=(int)eax[27]&32;
        return new EaxEnvironment(efx,eax:eax);
    }

    public EaxEnvironment Approach(EaxEnvironment target,float seconds)
    {
        if (!(seconds>0)) return this;
        float t=1-MathF.Exp(-Math.Max(0,seconds)/0.5f);
        float[] efx=ToArray(),eax=ToEaxArray();
        for (int i=0;i<efx.Length;i++) efx[i]+=(target._values[i]-efx[i])*t;
        for (int i=1;i<27;i++) eax[i]+=(target._eax[i]-eax[i])*t;
        eax[0]=target._eax[0]; eax[27]=target._eax[27];
        foreach (int index in new[] {3,4,5,9,14}) eax[index]=MathF.Round(eax[index]);
        efx[2]=MathF.Pow(10,eax[3]/2000); efx[3]=MathF.Pow(10,eax[4]/2000); efx[4]=MathF.Pow(10,eax[5]/2000);
        efx[8]=MathF.Pow(10,eax[9]/2000); efx[13]=MathF.Pow(10,eax[14]/2000); efx[26]=target._values[26];
        return new EaxEnvironment(efx,eax:eax);
    }

    public bool Near(EaxEnvironment other)
    {
        for (int i=0;i<_eax.Length;i++)
            if (Math.Abs(_eax[i]-other._eax[i])>0.001f) return false;
        return true;
    }

    /// <summary>
    /// Default EAX3 source levels, preserving ALchemy's millibel quantization. Adapted from reAL eax.c
    /// (Copyright 2026 Neth, MIT; see the native dependency notice). The supplied original's short occlusion
    /// setter is rejected, and default source air absorption is zero, so neither adds a distance/wall filter.
    /// </summary>
    public EaxSourceGains SourceGains(float distance, float minimumDistance)
    {
        double x = Math.Max(0, (distance - minimumDistance) / Math.Max(minimumDistance, AudioK.MinMinDistance));
        float dryQ = (float)((double)AudioK.Rolloff * x + 1);
        float dryLog = (float)Math.Log10(dryQ);
        float dryMb = -(float)((double)dryLog * 2000);
        float roomQ = (float)((double)_eax[26] * x + 1);
        float roomLog = (float)Math.Log10(roomQ);
        float roomMb = -(float)((double)roomLog * 2000);
        roomMb = (float)((double)AudioK.DistanceFactor * -6000 * AudioK.Rolloff * x / ((double)_eax[6] * 342.3800048828125) + roomMb);
        float decayHF = (float)((double)_eax[6] * _eax[7]);
        if (((int)_eax[27] & 32) != 0 && _eax[23] != 0)
            decayHF = Math.Min(decayHF, (float)(-17.52438785685992 / _eax[23]));
        float roomHF = (float)((double)AudioK.DistanceFactor * -6000 * AudioK.Rolloff * x / ((double)decayHF * 342.3800048828125));
        return new EaxSourceGains(SourceGain((int)dryMb), SourceGain((int)roomMb), SourceGain((int)roomHF));
    }

    private static float SourceGain(int millibels)
    {
        if (millibels <= -10000) return 0;
        if (millibels >= 0) return 1;
        float exponent = (float)(millibels * 0.0005000000237487257);
        return (float)Math.Pow(2, 3.321928024291992f * (double)exponent);
    }

    public static EaxEnvironment FromParameters(IReadOnlyDictionary<string, float> p)
    {
        ArgumentNullException.ThrowIfNull(p);
        string? invalid = null;
        float Get(string name, float fallback)
        {
            float value = p.TryGetValue(name, out float found) ? found : fallback;
            return float.IsFinite(value) ? value : throw new ArgumentException($"Non-finite EAX property {name}.", nameof(p));
        }
        float Clamp(string name, float fallback, float min, float max)
        {
            float value = Get(name, fallback);
            if (value < min || value > max) invalid ??= name;
            return Math.Clamp(value, min, max);
        }
        float Gain(string name, float fallback, float max = 0) => MathF.Pow(10, Clamp(name, fallback, -10000, max) / 2000);
        float size = Clamp("EnvironmentSize", 7.5f, 1, 100);
        // UNIAudio applies this floor before the provider validates the packet, including negative authored values.
        float roomRolloff = Math.Max(0.1f, Get("RoomRolloffFactor", 0));
        if (roomRolloff > 10) invalid ??= "RoomRolloffFactor";
        // EAX EnvironmentSize is in metres; EFX density is the normalized room-volume equivalent.
        // Set-all-parameters supplies the authored decay/delay values directly. The EAX size-change flags
        // do not rescale them a second time (only bDecayHFLimit is a runtime EFX property).
        float[] efx = [
            Math.Min(size * size * size / 16, 1), Clamp("EnvironmentDiffusion", 1, 0, 1),
            Gain("Room", -1000), Gain("RoomHF", -100), Gain("RoomLF", 0),
            Clamp("DecayTime", 1.49f, 0.1f, 20), Clamp("DecayHFRatio", 0.83f, 0.1f, 2), Clamp("DecayLFRatio", 1, 0.1f, 2),
            Math.Min(3.16f, Gain("Reflections", -2602, 1000)), Clamp("ReflectionsDelay", 0.007f, 0, 0.3f),
            Get("ReflectionsPanX", 0), Get("ReflectionsPanY", 0), Get("ReflectionsPanZ", 0),
            Gain("Reverb", 200, 2000), Clamp("ReverbDelay", 0.011f, 0, 0.1f),
            Get("ReverbPanX", 0), Get("ReverbPanY", 0), Get("ReverbPanZ", 0),
            Clamp("EchoTime", 0.25f, 0.075f, 0.25f), Clamp("EchoDepth", 0, 0, 1),
            Clamp("ModulationTime", 0.25f, 0.04f, 4), Clamp("ModulationDepth", 0, 0, 1),
            Math.Max(0.892f, MathF.Pow(10, Clamp("AirAbsorptionHF", -5, -100, 0) / 2000)),
            Clamp("HFReference", 5000, 1000, 20000), Clamp("LFReference", 250, 20, 1000),
            Math.Min(roomRolloff, 10), Get("bDecayHFLimit", 1) != 0 ? 1 : 0,
        ];
        int Flags()
        {
            string[] names = ["bDecayTimeScale", "bReflectionsScale", "bReflectionsDelayScale", "bReverbScale",
                "bReverbDelayScale", "bDecayHFLimit", "bEchoTimeScale", "bModulationTimeScale"];
            int result = 0;
            for (int i = 0; i < names.Length; i++) if (Get(names[i], i == 7 ? 0 : 1) != 0) result |= 1 << i;
            return result;
        }
        float[] eax = [0, Get("EnvironmentSize", 7.5f), Get("EnvironmentDiffusion", 1),
            Get("Room", -1000), Get("RoomHF", -100), Get("RoomLF", 0),
            Get("DecayTime", 1.49f), Get("DecayHFRatio", 0.83f), Get("DecayLFRatio", 1),
            Get("Reflections", -2602), Get("ReflectionsDelay", 0.007f),
            Get("ReflectionsPanX", 0), Get("ReflectionsPanY", 0), Get("ReflectionsPanZ", 0),
            Get("Reverb", 200), Get("ReverbDelay", 0.011f), Get("ReverbPanX", 0), Get("ReverbPanY", 0), Get("ReverbPanZ", 0),
            Get("EchoTime", 0.25f), Get("EchoDepth", 0), Get("ModulationTime", 0.25f), Get("ModulationDepth", 0),
            Get("AirAbsorptionHF", -5), Get("HFReference", 5000), Get("LFReference", 250),
            Math.Max(0.1f, Get("RoomRolloffFactor", 0)), Flags()];
        return new EaxEnvironment(efx, invalid, eax);
    }

    /// <summary>EAX's initial backend state; unlike a game setter call, its room rolloff has not been floored.</summary>
    public static EaxEnvironment DriverDefault { get; } = CreateDriverDefault();
    private static EaxEnvironment CreateDriverDefault()
    {
        EaxEnvironment defaults = FromParameters(new Dictionary<string, float>());
        float[] values = defaults.ToArray(), eax = defaults.ToEaxArray();
        values[25] = 0;
        eax[26] = 0;
        eax[27] = 63;
        return new EaxEnvironment(values, eax: eax);
    }

    /// <summary>Retail 0x10b0c437..0x10b0c50d: null ZoneEffect still installs this complete EAX environment.</summary>
    public static EaxEnvironment NullZoneEffect { get; } = FromParameters(new Dictionary<string, float>
    {
        ["EnvironmentSize"] = 42.5f, ["EnvironmentDiffusion"] = 0.21f,
        ["Room"] = -1000, ["RoomHF"] = -2000, ["RoomLF"] = 0,
        ["DecayTime"] = 1.49f, ["DecayHFRatio"] = 0.5f, ["DecayLFRatio"] = 1,
        ["Reflections"] = -2466, ["ReflectionsDelay"] = 0.179f,
        ["Reverb"] = -1927, ["ReverbDelay"] = 0.1f, ["RoomRolloffFactor"] = 0.1f,
        ["bEchoTimeScale"] = 0,
    });
}

/// <summary>Source send levels: broadband/LF gain and the relative gain at HFReference.</summary>
public readonly record struct EaxSourceLevels(float Gain, float GainHF)
{
    public static EaxSourceLevels Occlusion(float factor, float lowFrequencyRatio, float pathRatio)
    {
        factor = Math.Clamp(factor, 0.00001f, 1);
        float low = MathF.Pow(factor, Math.Max(pathRatio + lowFrequencyRatio - 1, pathRatio * lowFrequencyRatio));
        return new EaxSourceLevels(low, Math.Min(MathF.Pow(factor, pathRatio) / low, 1));
    }

    public static float RoomDistanceGain(float distance, float radius, float rolloff)
    {
        radius = Math.Max(radius, AudioK.MinMinDistance);
        return radius / (radius + Math.Max(distance - radius, 0) * rolloff);
    }

}

public readonly record struct EaxSourceGains(float Direct, float Room, float RoomHF);
