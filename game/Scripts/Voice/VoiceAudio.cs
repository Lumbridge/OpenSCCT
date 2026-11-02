using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Net.Voice;
using OpenScctGame.Audio;

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
/// clock drives the playout. The bus volume is the player's voice volume. With echo cancellation on, the mix of what
/// was handed to the output goes back to the client as the canceller's reference.
/// </summary>
public partial class VoiceAudio : Node
{
    public const string CaptureBus = "SvmVoiceCapture";
    public const string VoiceBus = "SvmVoice";

    /// <summary>Generator buffer length; only <see cref="OutputQueueFrames"/> of it is kept filled.</summary>
    private const float GeneratorSeconds = 0.25f;

    /// <summary>Decoded frames kept queued in a talker's generator (40 ms) when the output paces playback.</summary>
    private const int OutputQueueFrames = 2;

    /// <summary>Godot's name for the system's input device.</summary>
    private const string DefaultInputDevice = "Default";

    /// <summary>
    /// The capture bus read this process frame, shared by every voice node: each one reading the capture effect itself
    /// would split the samples between them (the hub's voice chat and the Options meter got a share each).
    /// </summary>
    private static float[] s_mono = [];
    private static int s_monoCount;
    private static ulong s_monoFrame = ulong.MaxValue;

    private readonly Dictionary<ushort, TalkerOutput> _players = new();
    private readonly float[] _frame = new float[VoiceK.FrameSamples];
    private readonly Vector2[] _push = new Vector2[VoiceK.FrameSamples];
    private float[] _mix = new float[VoiceK.FrameSamples * OutputQueueFrames];
    private AudioEffectCapture? _capture;
    private VoiceClient? _client;
    private bool _usingMicrophone;
    private double _clockFrames;

    /// <summary>The client whose microphone and talkers this node serves. Changing visits releases the old playback.</summary>
    public VoiceClient? Client
    {
        get => _client;
        set
        {
            if (ReferenceEquals(_client, value)) return;
            ClearPlayback();
            _client = value;
        }
    }

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

    /// <summary>
    /// Starts the microphone on the capture bus (needs the project setting audio/driver/enable_input). Every user shares
    /// one microphone stream (<see cref="SharedMicrophone"/>): Godot's input start and stop are not counted, so a
    /// second stream stopping, or a stream stopped and another started in the same frame, silences the input.
    /// </summary>
    public bool StartMicrophone(string? device = null)
    {
        EnsureBuses();
        MicrophoneProblem = null;
        string wanted = string.IsNullOrEmpty(device) ? DefaultInputDevice : device;
        if (AudioServer.InputDevice != wanted)
        {
            if (Array.IndexOf(AudioServer.GetInputDeviceList(), wanted) >= 0)
            {
                AudioServer.InputDevice = wanted;
            }
            else
            {
                MicrophoneProblem = $"'{device}' is not connected - falling back to the system's choice";
                AudioServer.InputDevice = DefaultInputDevice;
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

        if (!_usingMicrophone)
        {
            _usingMicrophone = true;
            SharedMicrophone.Acquire();
        }

        return true;
    }

    public void StopMicrophone()
    {
        if (_usingMicrophone)
        {
            _usingMicrophone = false;
            SharedMicrophone.Release();
        }
    }

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
        _client = null;
        ClearPlayback();
        StopMicrophone();
    }

    private void ClearPlayback()
    {
        foreach (TalkerOutput output in _players.Values)
        {
            output.Player.Stop();
            output.Player.Stream = null;

            // GeneratorPlayback keeps the native generator alive until the mixer's final playback reference ends.
            // We can release our wrappers now, even during tree shutdown. The capture effect remains on its shared bus.
            output.Playback.Dispose();
            output.Generator.Dispose();
            output.Player.QueueFree();
        }

        _players.Clear();
        _clockFrames = 0;
        Array.Clear(_mix);
    }

    private void PumpCapture(VoiceClient client)
    {
        if (_capture is null)
        {
            return;
        }

        ulong frame = Engine.GetProcessFrames();
        if (s_monoFrame != frame)
        {
            s_monoFrame = frame;
            s_monoCount = 0;
            int available = _capture.GetFramesAvailable();
            if (available > 0)
            {
                Vector2[] stereo = _capture.GetBuffer(available);
                if (s_mono.Length < stereo.Length)
                {
                    s_mono = new float[stereo.Length];
                }

                for (int i = 0; i < stereo.Length; i++)
                {
                    s_mono[i] = 0.5f * (stereo[i].X + stereo[i].Y);
                }

                s_monoCount = stereo.Length;
            }
        }

        if (s_monoCount <= 0)
        {
            return;
        }

        CapturedFrames += s_monoCount;
        client.SubmitCapture(s_mono.AsSpan(0, s_monoCount));
    }

    /// <summary>
    /// The one microphone stream on the capture bus, counted by its users. It plays while anyone uses it and stops a
    /// moment after the last one lets go, so a user handing over to the next (the Options meter to Test my microphone)
    /// never stops and restarts Godot's input.
    /// </summary>
    private sealed partial class SharedMicrophone : AudioStreamPlayer
    {
        private const double StopAfterSeconds = 2.0;

        private static SharedMicrophone? s_instance;
        private static int s_users;
        private double _idle;

        public static void Acquire()
        {
            s_users++;
            if ((s_instance is null || !IsInstanceValid(s_instance)) && Engine.GetMainLoop() is SceneTree tree)
            {
                s_instance = new SharedMicrophone
                {
                    Name = "VoiceMicrophone", Stream = new AudioStreamMicrophone(), Bus = CaptureBus, ProcessMode = ProcessModeEnum.Always,
                };
                tree.Root.CallDeferred(Node.MethodName.AddChild, s_instance);
            }
        }

        public static void Release() => s_users = Math.Max(0, s_users - 1);

        public override void _Process(double delta)
        {
            if (s_users > 0)
            {
                _idle = 0;
                if (!Playing)
                {
                    Play();
                }
            }
            else if (Playing && (_idle += delta) >= StopAfterSeconds)
            {
                Stop();
            }
        }

        public override void _ExitTree()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }
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

        // The mix handed to the output this process frame: the echo canceller's reference (FEATURES rl:voice.echo).
        bool reference = client.Settings.EchoCancellation;
        int mixed = 0;
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
                    if (reference)
                    {
                        MixReference(f, master);
                        mixed = Math.Max(mixed, f + 1);
                    }
                }

                PlayedFrames++;
            }
        }

        if (mixed > 0)
        {
            client.SubmitPlayback(_mix.AsSpan(0, mixed * VoiceK.FrameSamples));
            Array.Clear(_mix, 0, mixed * VoiceK.FrameSamples);
        }
    }

    /// <summary>
    /// Adds the frame just pushed (the talker's <paramref name="index"/>th this process frame) to the reference mix,
    /// at the voice bus volume the output plays it at.
    /// </summary>
    private void MixReference(int index, float busVolume)
    {
        int need = (index + 1) * VoiceK.FrameSamples;
        if (_mix.Length < need)
        {
            Array.Resize(ref _mix, need);
        }

        int at = index * VoiceK.FrameSamples;
        for (int i = 0; i < VoiceK.FrameSamples; i++)
        {
            _mix[at + i] += _frame[i] * busVolume;
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
        AudioStreamGeneratorPlayback playback = GeneratorPlayback.Get(player);
        var output = new TalkerOutput(player, generator, playback, playback.GetFramesAvailable());
        _players[talker] = output;
        return output;
    }

    /// <summary>One talker's generator and how many frames it held free when empty.</summary>
    private sealed class TalkerOutput(AudioStreamPlayer player, AudioStreamGenerator generator, AudioStreamGeneratorPlayback playback, int capacity)
    {
        public AudioStreamPlayer Player { get; } = player;

        public AudioStreamGenerator Generator { get; } = generator;

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
