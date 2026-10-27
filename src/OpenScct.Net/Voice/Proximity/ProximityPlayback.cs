using System;
using OpenScct.Core;

namespace OpenScct.Net.Voice.Proximity;

/// <summary>
/// The client half of proximity voice applied to a <see cref="VoiceClient"/>'s talkers: once a frame, each talker's
/// mix (distance falloff, occlusion muffling, the player's volume and mute for him) from the listener's position and
/// the talker's, written to <see cref="VoiceTalker.Spatial"/>, which the playback applies to the talker's samples (the
/// gain and a one-pole low-pass). A talker with no position (no pawn) is silent. Used by the hub only; a match never
/// sets <see cref="VoiceTalker.Spatial"/>.
/// </summary>
public sealed class ProximityPlayback
{
    public ProximityPlayback(ProximityVoiceSettings? settings = null)
    {
        Listener = new ProximityListener(settings);
    }

    /// <summary>The listener's mixes (diagnostics and tests).</summary>
    public ProximityListener Listener { get; }

    /// <summary>
    /// Updates every talker of <paramref name="client"/> as heard from <paramref name="listenerPosition"/> (the
    /// listener's camera), the talkers placed by <paramref name="talkers"/>; <paramref name="blocked"/> is the world
    /// line check for occlusion (null: never occluded). No listener position: every talker is silent.
    /// </summary>
    public void Update(VoiceClient client, FVector? listenerPosition, IVoicePositions talkers, Func<FVector, FVector, bool>? blocked, float dt)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(talkers);
        foreach (VoiceTalker talker in client.Talkers)
        {
            Listener.SetVolume(talker.Id, talker.Volume);
            Listener.SetMuted(talker.Id, talker.Muted);
            if (listenerPosition is { } ear && talkers.TryGetPosition(talker.Id, out FVector mouth))
            {
                talker.Spatial = Listener.Update(talker.Id, ear, mouth, blocked, dt);
            }
            else
            {
                Listener.Forget(talker.Id);
                talker.Spatial = VoiceMix.Silent;
            }
        }
    }

    /// <summary>Back to flat playback (leaving the hub).</summary>
    public static void Release(VoiceClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        foreach (VoiceTalker talker in client.Talkers)
        {
            talker.Spatial = null;
        }
    }
}
