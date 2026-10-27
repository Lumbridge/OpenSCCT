using System;

namespace OpenScctGame.Audio;

/// <summary>
/// The UNIAudio rules as plain functions (spec/native/audio.md, "Playing one sound" and "Ambient sounds"): volume,
/// radius and pitch of a source, the inverse distance gain, the priority, and the list picks. No Godot types.
/// </summary>
public static class SoundRules
{
    /// <summary>Rule 10: the call's volume times the Sound's gain (not for music), clamped to the source range.</summary>
    public static float SourceVolume(float volume, float gain, bool music)
    {
        float v = music ? volume : volume * gain;
        return Math.Clamp(Math.Max(v, AudioK.MinVolume), AudioK.MinVolume, 1f);
    }

    /// <summary>Rule 11: the Sound's own radius when above 0, else the call's.</summary>
    public static float Radius(float soundRadius, float callRadius) => soundRadius > 0f ? soundRadius : callRadius;

    /// <summary>Rule 11: the DirectSound minimum distance of a source of that radius.</summary>
    public static float MinDistance(float radius) =>
        radius == 0f ? AudioK.ZeroRadiusMinDistance : Math.Max(radius, AudioK.MinMinDistance);

    /// <summary>Rule 11: inverse distance gain, 1 inside the minimum distance.</summary>
    public static float DistanceGain(float distance, float minDistance, float rolloff = AudioK.Rolloff)
    {
        if (distance <= minDistance || minDistance <= 0f)
        {
            return 1f;
        }

        return minDistance / (minDistance + (rolloff * (distance - minDistance)));
    }

    /// <summary>
    /// The audio zoom of the scope and binoculars (SetAudioZoom): the volume factor of a 3D sound
    /// <paramref name="forward"/> uu ahead of the view and <paramref name="sideways"/> uu off its axis, for a zoom
    /// factor (0.33, 0.66, 1; 0 is off) and the view's FOV in degrees. Sounds on the aim line get louder (up to
    /// 1 + 3 x zoom), sounds near the edge of the view or behind it quieter (down to 1 - 0.9 x zoom).
    /// </summary>
    public static float AudioZoomGain(float zoom, float fov, float forward, float sideways)
    {
        if (zoom <= 0f || fov <= 0f)
        {
            return 1f;
        }

        float distance = MathF.Sqrt(forward * forward + sideways * sideways);
        float r = forward <= 0f || distance <= 0f ? 1f : Math.Min(1f, AudioK.AudioZoomFovScale / fov * (sideways / distance));
        return ((AudioK.AudioZoomCentre - (r * AudioK.AudioZoomEdge)) * zoom) + 1f;
    }

    /// <summary>Rules 10 and 19: the pitch factor of a call, or of SoundPitch / 64 for ambient sounds.</summary>
    public static float Pitch(float pitch) => Math.Clamp(pitch, AudioK.MinPitch, AudioK.MaxPitch);

    public static float AmbientPitch(byte soundPitch) => Pitch(soundPitch / AudioK.PitchUnit);

    /// <summary>Rule 16: priority of a source from the listener.</summary>
    public static float Priority(float distanceSquared, float radius, float volume, int flags)
    {
        float p = 1f;
        if (radius != 0f)
        {
            float reach = AudioK.RangeFactor * radius;
            p = Math.Clamp(1f - (distanceSquared / (reach * reach)), AudioK.PriorityFloor, 1f);
        }

        return (p * volume) + ((flags >> 4) & 1) + ((flags >> 2) & 2) + ((flags >> 2) & 1);
    }

    /// <summary>Rule 22: volume of a 2D ambient of a non-zone actor at a distance.</summary>
    public static float Ambient2DVolume(float volume, float gain, float distance, float radius, float rolloff = AudioK.Rolloff)
    {
        if (radius <= 0f)
        {
            return volume * gain;
        }

        return volume * gain / ((((distance - radius) / radius) * rolloff) + 1f);
    }

    /// <summary>Rule 3: the sound id of a call. Slot 0 takes a fresh id from <paramref name="counter"/> every call.</summary>
    public static int SoundId(int actorIndex, int slot, bool noOverride, ref int counter)
    {
        int id = (actorIndex * 16) + (slot * 2) + (noOverride ? 1 : 0);
        if ((id & 0xe) == 0)
        {
            counter--;
            id = counter * 16;
        }

        return id;
    }

    /// <summary>Rule 12: the entry a roll (0 to 9999) picks from the weights, or -1 for silence.</summary>
    public static int PickRandom(ReadOnlySpan<int> weights, int roll)
    {
        int sum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            sum += weights[i];
            if (roll < sum)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Rule 13: the entry for a surface, else the one with value 0, else -1.</summary>
    public static int PickSwitch(ReadOnlySpan<int> values, int surface)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == surface)
            {
                return i;
            }
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
