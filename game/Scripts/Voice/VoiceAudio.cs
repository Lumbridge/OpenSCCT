using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Net.Voice;

namespace OpenScctGame.Voice;

/// <summary>How a talker's playback is paced.</summary>
public enum VoicePlaybackPacing
{
    /// <summary>By the audio output: a frame is decoded whenever the generator has room for one (the game).</summary>
    Output,

    /// <summary>By the wall clock, 50 frames a second, whether or not the output consumes them (headless smoke runs).</summary>
    Clock,
}

/// <summary>
/// Voice chat's audio in Godot. Capture: an <see cref="AudioStreamMicrophone"/> plays on the muted <c>SvmVoiceCapture</c>
/// bus, whose <see cref="AudioEffectCapture"/> hands the samples (at the mix rate, mixed to mono) to the
/// <see cref="VoiceClient"/>. Playback: one <see cref="AudioStreamGenerator"/> player at 48 kHz per talker on the
/// <c>SvmVoice</c> bus (FEATURES rl:voice.mix), fed frame by frame from the talker's jitter buffer, so the audio output's
/// clock drives the playout. The bus volume is the player's voice volume.
/// </summary>
public partial class VoiceAudio : Node
{
    public const string CaptureBus = "SvmVoiceCapture";
    public const string VoiceBus = "SvmVoice";

    /// <summary>Generator buffer length; only <see cref="OutputQueueFrames"/> of it is kept filled.</summary>
    private const float GeneratorSeconds = 0.25f;

    /// <summary>Decoded frames kept queued in a talker's generator (40 ms) when the output paces playback.</summary>
    private const int OutputQueueFrames = 2;

    private readonly Dictionary<ushort, TalkerOutput> _players = new();
    private readonly float[] _frame = new float[VoiceK.FrameSamples];
    private readonly Vector2[] _push = new Vector2[VoiceK.FrameSamples];
    private float[] _mono = [];
    private AudioEffectCapture? _capture;
    private AudioStreamPlayer? _microphone;
    private double _clockFrames;

    /// <summary>The client whose microphone and talkers this node serves.</summary>
    public VoiceClient? Client { get; set; }

    public VoicePlaybackPacing Pacing { get; set; } = VoicePlaybackPacing.Output;

    /// <summary>Called with every frame played, per talker (tests and the smoke read the decoded audio here).</summary>
    public Action<ushort, ReadOnlySpan<float>>? FrameTap { get; set; }

    /// <summary>Mix rate of the capture bus (the audio server's).</summary>
    public int CaptureRate { get; private set; }

    /// <summary>Capture frames read from the bus so far.</summary>
    public long CapturedFrames { get; private set; }

    /// <summary>Why the microphone is not capturing, or null.</summary>
    public string? MicrophoneProblem { get; private set; }

    /// <summary>Talker frames handed to the output so far.</summary>
    public long PlayedFrames { get; private set; }

    /// <summary>Builds the buses (idempotent). Called by <see cref="_Ready"/>; usable before the node enters the tree.</summary>
    public void EnsureBuses()
    {
        int capture = EnsureBus(CaptureBus);
        AudioServer.SetBusMute(capture, true);
        if (_capture is null)
        {
            for (int i = 0; i < AudioServer.GetBusEffectCount(capture); i++)
            {
                if (AudioServer.GetBusEffect(capture, i) is AudioEffectCapture existing)
                {
                    _capture = existing;
                }
            }

            if (_capture is null)
            {
                _capture = new AudioEffectCapture { BufferLength = 0.5f };
                AudioServer.AddBusEffect(capture, _capture);
            }
        }

        EnsureBus(VoiceBus);
        CaptureRate = (int)AudioServer.GetMixRate();
        Client?.SetCaptureRate(CaptureRate);
    }

    /// <summary>Starts the microphone on the capture bus (needs the project setting audio/driver/enable_input).</summary>
    public bool StartMicrophone(string? device = null)
    {
        EnsureBuses();
        if (!string.IsNullOrEmpty(device))
        {
            foreach (string name in AudioServer.GetInputDeviceList())
            {
                if (name == device)
                {
                    AudioServer.InputDevice = device;
                }
            }

            if (AudioServer.InputDevice != device)
            {
                MicrophoneProblem = $"'{device}' is not connected - falling back to the system's choice";
            }
        }

        if (!ProjectSettings.GetSetting("audio/driver/enable_input", false).AsBool())
        {
            MicrophoneProblem = "audio input is disabled in the project settings";
            return false;
        }

        if (AudioServer.GetInputDeviceList().Length == 0)
        {
            MicrophoneProblem = "the system reports no microphones";
            return false;
        }

        _microphone ??= new AudioStreamPlayer { Name = "VoiceMicrophone", Stream = new AudioStreamMicrophone(), Bus = CaptureBus };
        if (!_microphone.IsInsideTree())
        {
            AddChild(_microphone);
        }

        _microphone.Play();
        return true;
    }

    public void StopMicrophone() => _microphone?.Stop();

    /// <summary>Output volume of every voice, 0 to 1 (<c>voice_output_volume</c>).</summary>
    public static void SetVolume(float volume)
    {
        int bus = AudioServer.GetBusIndex(VoiceBus);
        if (bus >= 0)
        {
            AudioServer.SetBusVolumeDb(bus, volume <= 0.0001f ? -80f : Mathf.LinearToDb(volume));
        }
    }

    /// <summary>Silences voice output completely (headless runs).</summary>
    public static void MuteOutput(bool mute)
    {
        int bus = AudioServer.GetBusIndex(VoiceBus);
        if (bus >= 0)
        {
            AudioServer.SetBusMute(bus, mute);
        }
    }

    public override void _Ready() => EnsureBuses();

    public override void _Process(double delta)
    {
        VoiceClient? client = Client;
        if (client is null)
        {
            return;
        }

        PumpCapture(client);
        PumpPlayback(client, delta);
    }

    public override void _ExitTree()
    {
        foreach (TalkerOutput output in _players.Values)
        {
            output.Player.Stop();

            // Release the playback wrapper now, on the main thread: left to the garbage collector it is released on
            // the finalizer thread, after the engine may have shut down.
            output.Playback.Dispose();
        }

        _players.Clear();
        StopMicrophone();
    }

    private void PumpCapture(VoiceClient client)
    {
        if (_capture is null)
        {
            return;
        }

        int available = _capture.GetFramesAvailable();
        if (available <= 0)
        {
            return;
        }

        Vector2[] stereo = _capture.GetBuffer(available);
        if (_mono.Length < stereo.Length)
        {
            _mono = new float[stereo.Length];
        }

        for (int i = 0; i < stereo.Length; i++)
        {
            _mono[i] = 0.5f * (stereo[i].X + stereo[i].Y);
        }

        CapturedFrames += stereo.Length;
        client.SubmitCapture(_mono.AsSpan(0, stereo.Length));
    }

    private void PumpPlayback(VoiceClient client, double delta)
    {
        float master = client.Settings.OutputVolume;
        int clockFrames = 0;
        if (Pacing == VoicePlaybackPacing.Clock)
        {
            _clockFrames += delta * VoiceK.FramesPerSecond;
            clockFrames = (int)Math.Min(_clockFrames, VoiceK.MaxJitterFrames);
            _clockFrames -= clockFrames;
        }

        foreach (VoiceTalker talker in client.Talkers)
        {
            if (!_players.TryGetValue(talker.Id, out TalkerOutput? output))
            {
                if (talker.Buffer.Count == 0)
                {
                    continue;
                }

                output = CreatePlayer(talker.Id);
            }

            // Output pacing keeps OutputQueueFrames decoded frames queued in the generator: the jitter buffer does
            // the banking, the generator only bridges the gaps between process frames.
            for (int f = 0; Pacing == VoicePlaybackPacing.Clock ? f < clockFrames : output.Queued < OutputQueueFrames * VoiceK.FrameSamples; f++)
            {
                if (!talker.ReadFrame(_frame, master))
                {
                    break;
                }

                FrameTap?.Invoke(talker.Id, _frame);
                if (output.Playback.CanPushBuffer(VoiceK.FrameSamples))
                {
                    for (int i = 0; i < VoiceK.FrameSamples; i++)
                    {
                        _push[i] = new Vector2(_frame[i], _frame[i]);
                    }

                    output.Playback.PushBuffer(_push);
                }

                PlayedFrames++;
            }
        }
    }

    private TalkerOutput CreatePlayer(ushort talker)
    {
        var generator = new AudioStreamGenerator
        {
            MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
            MixRate = VoiceK.SampleRate,
            BufferLength = GeneratorSeconds,
        };
        var player = new AudioStreamPlayer
        {
            Name = "Talker" + talker.ToString(System.Globalization.CultureInfo.InvariantCulture), Stream = generator, Bus = VoiceBus,
        };
        AddChild(player);
        player.Play();
        var playback = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
        var output = new TalkerOutput(player, playback, playback.GetFramesAvailable());
        _players[talker] = output;
        return output;
    }

    /// <summary>One talker's generator and how many frames it held free when empty.</summary>
    private sealed class TalkerOutput(AudioStreamPlayer player, AudioStreamGeneratorPlayback playback, int capacity)
    {
        public AudioStreamPlayer Player { get; } = player;

        public AudioStreamGeneratorPlayback Playback { get; } = playback;

        /// <summary>Samples queued and not yet played.</summary>
        public int Queued => Math.Max(0, capacity - Playback.GetFramesAvailable());
    }

    private static int EnsureBus(string name)
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
