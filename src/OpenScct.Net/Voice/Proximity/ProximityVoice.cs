using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Net.Voice.Proximity;

/// <summary>
/// Settings of positional (proximity) voice: an OpenSCCT addition for the hub (spec/additions/hub.md), not part of the
/// original's voice. Distances in uu. The defaults are our own choices (docs/HUB.md).
/// </summary>
public sealed record ProximityVoiceSettings
{
    /// <summary>Within this a talker is heard at full volume.</summary>
    public float FullVolumeDistance { get; init; } = 400f;

    /// <summary>Beyond this a talker is not heard, and the server stops relaying him to the listener.</summary>
    public float HearingDistance { get; init; } = 3000f;

    /// <summary>A listener the server relays a talker to keeps getting him until this much past <see cref="HearingDistance"/>.</summary>
    public float RelayHysteresis { get; init; } = 400f;

    /// <summary>Volume left when a wall is between talker and listener (0 to 1).</summary>
    public float OcclusionGain { get; init; } = 0.45f;

    /// <summary>Low-pass cut-off (Hz) for a talker heard through a wall.</summary>
    public float OccludedLowPassHz { get; init; } = 1200f;

    /// <summary>Low-pass cut-off (Hz) in the open: no audible filtering at the codec's 48 kHz.</summary>
    public float OpenLowPassHz { get; init; } = 20000f;

    /// <summary>Gain and cut-off move toward their targets at this rate (per second), so a talker walking behind a corner does not click.</summary>
    public float SmoothingPerSecond { get; init; } = 8f;

    /// <summary>The per-player volume the voice settings allow (VoiceChat.SetVolume: 0 to 2).</summary>
    public float MaxPlayerVolume { get; init; } = 2f;
}

/// <summary>How one talker should sound to one listener: the gain to apply and the low-pass cut-off.</summary>
public readonly record struct VoiceMix(float Gain, float LowPassHz)
{
    public static readonly VoiceMix Silent = new(0f, 0f);

    public bool Audible => Gain > 0f;
}

/// <summary>
/// The pure rules of proximity voice: distance falloff, occlusion muffling, and the listener's own per-player volume
/// and mute. Deterministic (no transcendental maths) and allocation-free.
/// </summary>
public static class ProximityVoice
{
    /// <summary>
    /// The distance gain: 1 up to <see cref="ProximityVoiceSettings.FullVolumeDistance"/>, then down a smooth step to 0
    /// at <see cref="ProximityVoiceSettings.HearingDistance"/> (no step in volume at either end).
    /// </summary>
    public static float DistanceGain(float distance, ProximityVoiceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float full = settings.FullVolumeDistance;
        float range = settings.HearingDistance;
        if (distance <= full)
        {
            return 1f;
        }

        if (distance >= range || range <= full)
        {
            return 0f;
        }

        float t = (distance - full) / (range - full);
        return 1f - t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The mix of a talker at <paramref name="distance"/> for a listener: distance gain, times
    /// <see cref="ProximityVoiceSettings.OcclusionGain"/> and a low-pass when <paramref name="occluded"/>, times the
    /// listener's <paramref name="playerVolume"/> for that talker (clamped to 0..MaxPlayerVolume); silent when the
    /// listener muted him.
    /// </summary>
    public static VoiceMix Mix(float distance, bool occluded, float playerVolume, bool muted, ProximityVoiceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (muted)
        {
            return VoiceMix.Silent;
        }

        float gain = DistanceGain(distance, settings) * Math.Clamp(playerVolume, 0f, settings.MaxPlayerVolume);
        if (gain <= 0f)
        {
            return VoiceMix.Silent;
        }

        return occluded
            ? new VoiceMix(gain * settings.OcclusionGain, settings.OccludedLowPassHz)
            : new VoiceMix(gain, settings.OpenLowPassHz);
    }

    /// <summary>
    /// The coefficient of the one-pole low-pass (<c>y += a * (x - y)</c>) the playback runs a talker through at
    /// <paramref name="cutoffHz"/>: <c>w / (1 + w)</c> with <c>w = 2 pi fc / fs</c>. 1 (no filtering) for a cut-off of
    /// 0 or at or above 40 % of <paramref name="sampleRate"/>, which covers <see cref="ProximityVoiceSettings.OpenLowPassHz"/>.
    /// </summary>
    public static float LowPassCoefficient(float cutoffHz, int sampleRate)
    {
        if (cutoffHz <= 0f || sampleRate <= 0 || cutoffHz >= 0.4f * sampleRate)
        {
            return 1f;
        }

        float w = 2f * MathF.PI * cutoffHz / sampleRate;
        return w / (1f + w);
    }

    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> over <paramref name="dt"/> seconds (a first-order step, never past the target).</summary>
    public static VoiceMix Smooth(VoiceMix current, VoiceMix target, float dt, ProximityVoiceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float k = Math.Clamp(settings.SmoothingPerSecond * dt, 0f, 1f);
        float hz = current.LowPassHz <= 0f ? target.LowPassHz : current.LowPassHz + (target.LowPassHz - current.LowPassHz) * k;
        return new VoiceMix(current.Gain + (target.Gain - current.Gain) * k, hz);
    }
}

/// <summary>Where each talker is, by talker id (PlayerReplicationInfo.PlayerID): the server fills it from its world.</summary>
public interface IVoicePositions
{
    /// <summary>The talker's ear and mouth position (his view point), or false when he has none (no pawn, a spectator).</summary>
    bool TryGetPosition(ushort talker, out FVector position);
}

/// <summary>
/// The server half of proximity voice: who may hear whom by distance, with hysteresis, so the relay sends a talker's
/// frames only to listeners in range (a 50-player hub would otherwise send every talker to everyone). It answers for
/// pairs the stock routing already allows; it never widens what <see cref="VoiceRouting"/> allows. Mutes stay the
/// relay's (the Join packets); <see cref="ServerMute"/> adds a host-side mute.
/// </summary>
public sealed class ProximityRelayFilter
{
    private readonly IVoicePositions _positions;
    private readonly HashSet<uint> _inRange = new();
    private readonly HashSet<uint> _serverMuted = new();

    public ProximityRelayFilter(IVoicePositions positions, ProximityVoiceSettings? settings = null)
    {
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        Settings = settings ?? new ProximityVoiceSettings();
    }

    public ProximityVoiceSettings Settings { get; }

    /// <summary>Pairs currently in range (diagnostics).</summary>
    public int PairsInRange => _inRange.Count;

    /// <summary>
    /// True when <paramref name="listener"/> should get <paramref name="talker"/>'s frames: both have a position and
    /// are within <see cref="ProximityVoiceSettings.HearingDistance"/> (or within it plus the hysteresis while the pair
    /// was already in range), and the host has not muted the talker for that listener.
    /// </summary>
    public bool CanHear(ushort listener, ushort talker)
    {
        if (listener == talker || _serverMuted.Contains(Key(listener, talker)))
        {
            return false;
        }

        uint key = Key(listener, talker);
        if (!_positions.TryGetPosition(listener, out FVector a) || !_positions.TryGetPosition(talker, out FVector b))
        {
            _inRange.Remove(key);
            return false;
        }

        float range = Settings.HearingDistance + (_inRange.Contains(key) ? Settings.RelayHysteresis : 0f);
        bool hear = (a - b).SizeSquared() <= range * range;
        if (hear)
        {
            _inRange.Add(key);
        }
        else
        {
            _inRange.Remove(key);
        }

        return hear;
    }

    /// <summary>A host-side mute of <paramref name="talker"/> for <paramref name="listener"/> (on) or its removal (off).</summary>
    public void ServerMute(ushort listener, ushort talker, bool on)
    {
        if (on)
        {
            _serverMuted.Add(Key(listener, talker));
        }
        else
        {
            _serverMuted.Remove(Key(listener, talker));
        }
    }

    /// <summary>Forgets every pair of a player who left.</summary>
    public void Forget(ushort player)
    {
        _inRange.RemoveWhere(k => (ushort)(k >> 16) == player || (ushort)k == player);
        _serverMuted.RemoveWhere(k => (ushort)(k >> 16) == player || (ushort)k == player);
    }

    private static uint Key(ushort listener, ushort talker) => ((uint)listener << 16) | talker;
}

/// <summary>
/// The client half of proximity voice: one listener's mix of every talker it hears, from the talkers' positions, a
/// line-of-sight test for occlusion, and the player's own per-talker volume and mute. Call <see cref="Update"/> once a
/// frame; read <see cref="MixOf"/> when filling each talker's audio.
/// </summary>
public sealed class ProximityListener
{
    private readonly Dictionary<ushort, VoiceMix> _mix = new();
    private readonly Dictionary<ushort, float> _volume = new();
    private readonly HashSet<ushort> _muted = new();

    public ProximityListener(ProximityVoiceSettings? settings = null)
    {
        Settings = settings ?? new ProximityVoiceSettings();
    }

    public ProximityVoiceSettings Settings { get; }

    /// <summary>The player's volume for <paramref name="talker"/> (0 to 2; 1 by default).</summary>
    public void SetVolume(ushort talker, float volume) => _volume[talker] = Math.Clamp(volume, 0f, Settings.MaxPlayerVolume);

    public float VolumeOf(ushort talker) => _volume.TryGetValue(talker, out float v) ? v : 1f;

    public void SetMuted(ushort talker, bool muted)
    {
        if (muted)
        {
            _muted.Add(talker);
        }
        else
        {
            _muted.Remove(talker);
        }
    }

    public bool IsMuted(ushort talker) => _muted.Contains(talker);

    /// <summary>
    /// Updates the mix of <paramref name="talker"/> heard from <paramref name="listenerPosition"/>: the target from
    /// <see cref="ProximityVoice.Mix"/> (occluded when <paramref name="blocked"/> says the line is blocked), smoothed
    /// over <paramref name="dt"/>; a talker heard for the first time starts at his target.
    /// </summary>
    public VoiceMix Update(ushort talker, FVector listenerPosition, FVector talkerPosition, Func<FVector, FVector, bool>? blocked, float dt)
    {
        float distance = (talkerPosition - listenerPosition).Size();
        bool occluded = distance < Settings.HearingDistance && blocked is not null && blocked(listenerPosition, talkerPosition);
        VoiceMix target = ProximityVoice.Mix(distance, occluded, VolumeOf(talker), IsMuted(talker), Settings);
        VoiceMix mix = _mix.TryGetValue(talker, out VoiceMix current) ? ProximityVoice.Smooth(current, target, dt, Settings) : target;
        _mix[talker] = mix;
        return mix;
    }

    /// <summary>The last mix of <paramref name="talker"/>, silent when he was never updated.</summary>
    public VoiceMix MixOf(ushort talker) => _mix.TryGetValue(talker, out VoiceMix m) ? m : VoiceMix.Silent;

    /// <summary>Forgets a talker who left.</summary>
    public void Forget(ushort talker)
    {
        _mix.Remove(talker);
        _volume.Remove(talker);
        _muted.Remove(talker);
    }
}
