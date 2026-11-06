using System;
using Godot;

namespace OpenScctGame.Audio;

public sealed partial class SoundSystem
{
    private bool _demoSeeking;
    private bool _demoPaused;

    /// <summary>Advance audio in silence during a demo seek and start only the voices that survive it.</summary>
    public void SetDemoSeek(bool seeking, bool paused = false)
    {
        bool pauseChanged = _demoPaused != paused;
        _demoPaused = paused;
        if (_demoSeeking == seeking)
        {
            if (!seeking && pauseChanged)
                foreach (Voice voice in _voices)
                    if (voice.Active) voice.SetPaused(paused);
            return;
        }

        _demoSeeking = seeking;
        foreach (Voice voice in _voices)
        {
            if (!voice.Active) continue;
            if (seeking) voice.Suspend();
            else if (voice.DemoStream is { } stream)
                StartStream(voice, stream, voice.OffsetIn(stream));
        }
    }

    /// <summary>Advance one replay tick, then dispatch sounds raised on that tick.</summary>
    public void AdvanceDemoTick(double seconds)
    {
        if (!_demoSeeking) return;
        AdvanceDemoVoices(seconds);
        DispatchPending();
    }

    /// <summary>Advance saved sounds by the interval between two recorded presentation events.</summary>
    public void AdvanceDemoVoices(double seconds)
    {
        if (!_demoSeeking || seconds <= 0 || !double.IsFinite(seconds)) return;

        // A sequence may finish multiple leaves in one interval. Step at leaf boundaries so the surviving leaf
        // inherits only the time it actually played. Ordinary seeking calls this once per replay tick.
        for (int transitions = 0; seconds > 0.000001; transitions++)
        {
            double step = seconds;
            if (transitions < 256)
                foreach (Voice voice in _voices)
                    if (voice.Active) step = Math.Min(step, voice.TimeUntilSequenceStep(_now));
            _now += step;
            seconds -= step;
            foreach (Voice voice in _voices)
            {
                if (!voice.Active) continue;
                voice.Advance(step);
                UpdateVoice(voice, (float)step, ReverbBuses.WorldBus);
            }
        }
    }

    private sealed partial class Voice
    {
        public AudioStream? DemoStream { get; private set; }
        private double _demoLength;
        private double DemoPosition { get; set; }

        public void Simulate(AudioStream stream, double offset = 0)
        {
            DemoStream = stream;
            DemoPosition = offset;
            _demoLength = stream.GetLength();
        }

        public void Suspend()
        {
            AudioStream? stream = Positional ? Player3D.Stream : Player2D.Stream;
            if (!IsPlaying || stream is null)
            {
                Stop();
                return;
            }

            double position = Positional ? Player3D.GetPlaybackPosition() : Player2D.GetPlaybackPosition();
            Halt();
            Simulate(stream, position);
        }

        public void SetPaused(bool paused)
        {
            Player3D.StreamPaused = paused;
            Player2D.StreamPaused = paused;
            RoomPlayer.StreamPaused = paused;
        }

        public void Advance(double seconds) => DemoPosition += seconds * Pitch;

        public double TimeUntilSequenceStep(double now)
        {
            if (Sequence is null) return double.PositiveInfinity;
            double remaining = LoopUntil > 0 ? LoopUntil - now
                : (_demoLength - DemoPosition) / Math.Max(Pitch, 0.01f);
            return Math.Max(remaining, 0.00001);
        }

        public float OffsetIn(AudioStream stream)
        {
            double start = 0;
            double end = _demoLength;
            if (stream is AudioStreamWav { MixRate: > 0 } wav)
            {
                start = Math.Clamp(wav.LoopBegin / (double)wav.MixRate, 0, end);
                if (wav.LoopEnd > wav.LoopBegin)
                    end = Math.Min(end, wav.LoopEnd / (double)wav.MixRate);
            }
            else if (stream is AudioStreamOggVorbis ogg)
                start = Math.Clamp(ogg.LoopOffset, 0, end);

            double position = DemoPosition;
            if (Looping && end > start && position >= end)
                position = start + (position - start) % (end - start);
            return (float)Math.Clamp(position, 0, _demoLength);
        }
    }
}
