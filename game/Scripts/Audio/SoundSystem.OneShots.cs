using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScctGame.Audio;

public sealed partial class SoundSystem
{
    // A completed source remains armed-off while audible. Leaving its range,
    // disabling it, or changing its sound allows the next entry to play again.
    private readonly Dictionary<int, (string Sound, Voice Voice, double NextStart, float ChainGain)> _ambientOneShots = [];
    private readonly HashSet<int> _oneShotSeen = [];

    private void UpdateAmbientOneShot(in AmbientSource source, SoundDef sound)
    {
        float radius = sound.Radius > 0 ? sound.Radius : source.SoundRadius;
        float distance = FVector.Dist(source.Location, ListenerLocation);
        float gain = 1, hf = 1, room = 0, volume = AmbientVolume(source);
        if (sound.Positional) gain = AmbientTransitions.RangeGain(distance, AudioK.RangeFactor * radius);
        else if (source.IsZone) (gain, hf, room) = ZoneBed(source.Id);
        else
        {
            volume = SoundRules.Ambient2DPlayingVolume(volume, distance, radius);
            gain = AmbientTransitions.CutGain(SoundRules.SourceVolume(volume, sound.Volume, false), AudioK.Ambient2DCut);
        }
        if (gain <= .001f) return;
        _oneShotSeen.Add(source.ActorId);
        int id = source.ActorId * 16 + AudioK.AmbientSlotId;
        if (_ambientOneShots.TryGetValue(source.ActorId, out var previous))
        {
            if (string.Equals(previous.Sound, source.Sound, StringComparison.OrdinalIgnoreCase))
            {
                if (OwnsAmbient(previous.Voice, id) && string.Equals(previous.Voice.CallPath, source.Sound, StringComparison.OrdinalIgnoreCase))
                {
                    Voice running = previous.Voice;
                    running.AmbientEligible = true;
                    SetAmbientTarget(running, gain, hf, room);
                    running.BaseVolume = volume * previous.ChainGain;
                    running.Volume = SoundRules.SourceVolume(running.BaseVolume, running.Leaf?.Volume ?? sound.Volume, false);
                    running.Pitch = SoundRules.AmbientPitch(source.SoundPitch);
                    if (!running.Follow) running.Location = source.Location;
                    return;
                }
                if (!source.Loop || _now < previous.NextStart) return;
            }
            RemoveAmbientOneShot(source.ActorId);
        }
        var call = new PlayCall(sound, source.ActorId, id, source.Location, volume, radius,
            SoundRules.AmbientPitch(source.SoundPitch), music: false, loop: false, ambient: true,
            occlusion: source.SoundOcclusion, ambientGain: gain, ambientHF: hf, ambientRoom: room);
        if (Start(call, source.ActorId > 0 && source.Follow) && LastStarted is { } voice)
        {
            voice.AmbientZoneId = source.IsZone ? source.Id : null;
            _ambientOneShots[source.ActorId] = (source.Sound, voice, _now + source.RepeatInterval, volume > 0 ? voice.BaseVolume / volume : 1);
        }
    }

    private void RemoveAmbientOneShot(int actorId)
    {
        if (_ambientOneShots.Remove(actorId, out var old) && OwnsAmbient(old.Voice, actorId * 16 + AudioK.AmbientSlotId)
            && string.Equals(old.Voice.CallPath, old.Sound, StringComparison.OrdinalIgnoreCase)) old.Voice.Stop();
    }

    private void PruneAmbientOneShots()
    {
        List<int>? gone = null;
        foreach (int id in _ambientOneShots.Keys)
            if (!_oneShotSeen.Contains(id)) (gone ??= []).Add(id);
        if (gone is not null) foreach (int id in gone) RemoveAmbientOneShot(id);
    }
}
