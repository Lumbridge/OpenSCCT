using System;
using Godot;
using OpenScct.Core;
using OpenScct.Server.Hub;
using OpenScctGame.Audio;

namespace OpenScctGame.Hub;

/// <summary>
/// The hub's own sounds (an OpenSCCT addition): the jukebox's music and the activities' chimes, all made here by a
/// small synthesiser (no recorded audio, no samples, nothing from the game), so they carry no licence beyond the
/// project's. The music fades in near the jukebox (<see cref="HubPlayK.JukeboxRange"/>) on the music bus; the chimes
/// play flat on the effects bus.
/// </summary>
public partial class HubAudio : Node
{
    private const int MixRate = 22050;
    private const float BufferSeconds = 0.2f;

    private AudioStreamPlayer? _musicPlayer;
    private AudioStreamPlayer? _chimePlayer;
    private AudioStreamGeneratorPlayback? _music;
    private AudioStreamGeneratorPlayback? _chime;
    private readonly HubSynth _synth = new(MixRate);
    private readonly HubChimes _chimes = new(MixRate);
    private float _gain;

    /// <summary>The listener's place (the camera), or null before the play scene is in.</summary>
    public Func<FVector?>? Ear { get; set; }

    public HubPlayState? Play { get; set; }

    public override void _Ready()
    {
        Name = "HubAudio";
        _musicPlayer = MakePlayer("Music", AudioServer.GetBusIndex(ReverbBuses.MusicBus) >= 0 ? ReverbBuses.MusicBus : "Master");
        _chimePlayer = MakePlayer("Chimes", AudioServer.GetBusIndex(ReverbBuses.FlatBus) >= 0 ? ReverbBuses.FlatBus : "Master");
        _music = _musicPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _chime = _chimePlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    private AudioStreamPlayer MakePlayer(string name, string bus)
    {
        var player = new AudioStreamPlayer
        {
            Name = name,
            Bus = bus,
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = BufferSeconds * 2f },
        };
        AddChild(player);
        player.Play();
        return player;
    }

    /// <summary>A chime for an activity cue ("start", "cp", "finish", "best", "hit", "head", "end", "fanfare").</summary>
    public void Cue(string cue) => _chimes.Play(cue);

    public override void _Process(double delta)
    {
        if (Play is { } play)
        {
            _synth.Track = Math.Max(0, play.Track);
            float target = 0f;
            if (play.Track >= 0 && Ear?.Invoke() is { } ear && play.Nearest("jukebox", ear, out float d) is not null)
            {
                float near = HubPlayK.JukeboxUseRange;
                float t = Mathf.Clamp(1f - (d - near) / (HubPlayK.JukeboxRange - near), 0f, 1f);
                target = t * t * 0.55f;
            }

            _gain = Mathf.Lerp(_gain, target, Mathf.Clamp((float)delta * 3f, 0f, 1f));
        }

        Fill(_music, (buffer, frames) => _synth.Render(buffer, frames, _gain));
        Fill(_chime, (buffer, frames) => _chimes.Render(buffer, frames));
    }

    private static void Fill(AudioStreamGeneratorPlayback? playback, Action<Vector2[], int> render)
    {
        if (playback is null)
        {
            return;
        }

        int frames = playback.GetFramesAvailable();
        int keep = (int)(MixRate * BufferSeconds);
        int queued = (int)(MixRate * BufferSeconds * 2f) - frames;
        int want = Math.Min(frames, keep - queued);
        if (want <= 0)
        {
            return;
        }

        var buffer = new Vector2[want];
        render(buffer, want);
        playback.PushBuffer(buffer);
    }
}

/// <summary>
/// The jukebox's music: four procedural loops of our own (a pad, a bass line, an arpeggio and a soft hi-hat over a
/// four-chord progression; <see cref="HubJukeboxTracks.Titles"/> names them), rendered sample by sample.
/// </summary>
internal sealed class HubSynth
{
    /// <summary>One track: tempo, root note (MIDI), its four chords (root offset in semitones, chord tones above the root).</summary>
    private sealed record Piece(float Bpm, int Root, (int Offset, int[] Tones)[] Chords, float Brightness, float Swing);

    private static readonly int[] Minor = [0, 3, 7];
    private static readonly int[] Major = [0, 4, 7];
    private static readonly int[] Minor7 = [0, 3, 7, 10];
    private static readonly int[] Major7 = [0, 4, 7, 11];

    private static readonly Piece[] Pieces =
    [
        new(92f, 57, [(0, Minor), (-4, Major), (3, Major), (-2, Major)], 0.35f, 0f),
        new(104f, 50, [(0, Minor7), (5, Minor), (-4, Major), (-2, Major)], 0.55f, 0.08f),
        new(124f, 52, [(0, Minor), (8, Major), (3, Major), (10, Major)], 0.7f, 0f),
        new(80f, 53, [(0, Major7), (-1, Minor7), (-3, Minor7), (-5, Major7)], 0.25f, 0.12f),
    ];

    private readonly int _rate;
    private readonly float[] _padPhase = new float[4];
    private float _bassPhase;
    private float _arpPhase;
    private float _bassEnv;
    private float _arpEnv;
    private float _hatEnv;
    private float _bassFreq;
    private float _arpFreq;
    private uint _noise = 0x1234567u;
    private long _sample;
    private int _lastEighth = -1;
    private int _track;
    private float _lowPass;

    public HubSynth(int rate) => _rate = rate;

    public int Track
    {
        get => _track;
        set
        {
            if (value != _track)
            {
                _track = value;
                _sample = 0;
                _lastEighth = -1;
            }
        }
    }

    private static float Freq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

    public void Render(Vector2[] buffer, int frames, float gain)
    {
        Piece piece = Pieces[((_track % Pieces.Length) + Pieces.Length) % Pieces.Length];
        float dt = 1f / _rate;
        float bassDecay = Mathf.Pow(0.001f, dt / 0.45f);
        float arpDecay = Mathf.Pow(0.001f, dt / 0.28f);
        float hatDecay = Mathf.Pow(0.001f, dt / 0.04f);
        float eighthSeconds = 30f / piece.Bpm;
        for (int i = 0; i < frames; i++)
        {
            float t = _sample * dt;
            int eighth = (int)(t / eighthSeconds);
            int bar = eighth / 8 % piece.Chords.Length;
            (int offset, int[] tones) = piece.Chords[bar];
            int root = piece.Root + offset;
            if (eighth != _lastEighth)
            {
                // A swung off-beat comes a little late.
                float into = t - eighth * eighthSeconds;
                bool off = eighth % 2 == 1;
                if (!off || into >= piece.Swing * eighthSeconds)
                {
                    _lastEighth = eighth;
                    int step = eighth % 8;
                    if (step is 0 or 4 || step == 7 && bar % 2 == 1)
                    {
                        _bassFreq = Freq(root - 12 + (step == 7 ? 7 : 0));
                        _bassEnv = 1f;
                    }

                    int[] order = [0, 1, 2, 1, 0, 2, 1, tones.Length - 1];
                    _arpFreq = Freq(root + 12 + tones[order[step] % tones.Length]);
                    _arpEnv = 0.9f;
                    if (off)
                    {
                        _hatEnv = 1f;
                    }
                }
            }

            float pad = 0f;
            float swell = 0.75f + 0.25f * Mathf.Sin(t * 0.7f);
            for (int v = 0; v < tones.Length && v < _padPhase.Length; v++)
            {
                float f = Freq(root + tones[v]) * (1f + (v - 1) * 0.0015f);
                _padPhase[v] = (_padPhase[v] + f * dt) % 1f;
                pad += Mathf.Sin(_padPhase[v] * Mathf.Tau);
            }

            pad *= 0.05f * swell;
            _bassPhase = (_bassPhase + _bassFreq * dt) % 1f;
            float tri = 1f - 4f * Mathf.Abs(_bassPhase - 0.5f);
            float bass = tri * _bassEnv * 0.22f;
            _arpPhase = (_arpPhase + _arpFreq * dt) % 1f;
            float s1 = Mathf.Sin(_arpPhase * Mathf.Tau);
            float s2 = Mathf.Sin(_arpPhase * 2f * Mathf.Tau);
            float arp = (s1 + piece.Brightness * 0.5f * s2) * _arpEnv * 0.09f;
            _noise = _noise * 1664525u + 1013904223u;
            float white = (_noise >> 9) * (2f / 8388608f) - 1f;
            _lowPass += (white - _lowPass) * 0.6f;
            float hat = (white - _lowPass) * _hatEnv * 0.05f * (0.5f + piece.Brightness);
            _bassEnv *= bassDecay;
            _arpEnv *= arpDecay;
            _hatEnv *= hatDecay;
            float mix = (pad + bass + arp + hat) * gain;
            buffer[i] = new Vector2(mix + arp * gain * 0.15f, mix - arp * gain * 0.15f);
            _sample++;
        }
    }
}

/// <summary>The activities' chimes: short runs of bell-like notes (a sine and its octave with a quick decay).</summary>
internal sealed class HubChimes
{
    private readonly int _rate;
    private readonly System.Collections.Generic.List<(float Freq, long Start, float Gain)> _notes = new();
    private long _sample;

    public HubChimes(int rate) => _rate = rate;

    public void Play(string cue)
    {
        (int Midi, float At)[] notes = cue switch
        {
            "start" => [(72, 0f), (79, 0.12f)],
            "cp" => [(84, 0f)],
            "finish" => [(76, 0f), (81, 0.14f)],
            "best" or "fanfare" => [(72, 0f), (76, 0.1f), (79, 0.2f), (84, 0.3f), (88, 0.42f)],
            "hit" => [(91, 0f)],
            "head" => [(96, 0f), (91, 0.06f)],
            "end" => [(79, 0f), (76, 0.14f), (72, 0.28f)],
            _ => [(84, 0f)],
        };
        float gain = cue is "hit" or "head" ? 0.18f : 0.28f;
        foreach (var (midi, at) in notes)
        {
            _notes.Add((440f * Mathf.Pow(2f, (midi - 69) / 12f), _sample + (long)(at * _rate), gain));
        }
    }

    public void Render(Vector2[] buffer, int frames)
    {
        float dt = 1f / _rate;
        for (int i = 0; i < frames; i++)
        {
            float mix = 0f;
            for (int n = _notes.Count - 1; n >= 0; n--)
            {
                (float f, long start, float g) = _notes[n];
                if (_sample < start)
                {
                    continue;
                }

                float age = (_sample - start) * dt;
                if (age > 0.9f)
                {
                    _notes.RemoveAt(n);
                    continue;
                }

                float env = Mathf.Exp(-age * 6f) * Mathf.Min(1f, age * 400f);
                float ph = f * age * Mathf.Tau;
                mix += (Mathf.Sin(ph) + 0.35f * Mathf.Sin(ph * 2f)) * env * g;
            }

            buffer[i] = new Vector2(mix, mix);
            _sample++;
        }
    }
}
