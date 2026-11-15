using System;
using System.Collections.Generic;

namespace OpenScctGame.Audio;

public sealed partial class SoundSystem
{
    // Leave eight channels available for immediate events before any stealing.
    private const int MaxRenderedAmbient = 24;
    private const int MaxVirtualLoops = 512;
    private readonly List<Voice> _virtualVoices = [];
    public int VirtualVoices => _virtualVoices.Count;
    public long VirtualPromotions { get; private set; }

    private IEnumerable<Voice> AllVoices()
    {
        foreach (Voice v in _voices) yield return v;
        foreach (Voice v in _virtualVoices) yield return v;
    }

    private Voice Virtualize(Voice voice)
    {
        int index = _voices.IndexOf(voice);
        voice.Suspend();
        if (voice.Active && voice.DemoStream is not null)
        {
            voice.Virtual = true;
            if (_virtualVoices.Count == MaxVirtualLoops)
            {
                _virtualVoices[0].Stop();
                _virtualVoices.RemoveAt(0);
            }
            _virtualVoices.Add(voice);
        }
        // Transfer the physical nodes/route to a fresh logical voice. The saved
        // emitter advances only its cursor and never touches these nodes again.
        var replacement = new Voice(voice);
        _voices[index] = replacement;
        return replacement;
    }

    private void UpdateVirtualVoices(double delta)
    {
        if (_demoSeeking) return;
        int count = _virtualVoices.Count;
        for (int i = count - 1; i >= 0; i--)
        {
            Voice voice = _virtualVoices[i];
            if (!voice.Active) { _virtualVoices.RemoveAt(i); continue; }
            if (!_demoSeeking)
            {
                UpdateVoice(voice, (float)delta, ReverbBuses.WorldBus);
                voice.AppliedPitch = voice.Pitch * Doppler(voice, (float)delta);
                voice.Advance(delta, voice.AppliedPitch);
            }
            if (!voice.Active || voice.DemoStream is not { } stream) { _virtualVoices.RemoveAt(i); continue; }
            if (voice.Ambient && !voice.AmbientEligible) continue;
            if (voice.Follow && World is not null && !World.TryGetActorLocation(voice.ActorId, out _))
            {
                voice.Stop(); _virtualVoices.RemoveAt(i); continue;
            }
            float priority = SoundRules.Priority((voice.Location - ListenerLocation).SizeSquared(), voice.Radius, voice.Volume,
                voice.Leaf?.Flags ?? 0, voice.PriorityRadiusFactor);
            voice.Priority = priority;
            Voice? available = Allocate(voice.Id, priority, voice.Ambient, promoting: true);
            if (available is null) continue;
            _virtualVoices.RemoveAt(i);
            int index = _voices.IndexOf(available);
            float offset = voice.OffsetIn(stream);
            voice.TakePlayback(available);
            voice.Virtual = false;
            voice.RenderedAt = _now;
            _voices[index] = voice;
            voice.FadeTo(0, 1, 0.015f);
            StartStream(voice, stream, offset, resumed: true);
            VirtualPromotions++;
        }
    }

    private sealed partial class Voice
    {
        public bool Virtual;
        public bool AmbientEligible = true;
        public double RenderedAt;

        public Voice(Voice physical) { _bus3D = _bus2D = ""; TakePlayback(physical); }

        public void TakePlayback(Voice physical)
        {
            Player3D = physical.Player3D; Player2D = physical.Player2D; RoomPlayer = physical.RoomPlayer;
            Route = physical.Route;
            _bus3D = _bus2D = "";
            _db3D = _db2D = _pitch3D = _pitch2D = float.NaN;
            _position3D = null;
        }
    }
}
