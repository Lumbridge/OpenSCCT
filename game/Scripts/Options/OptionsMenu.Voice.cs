using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScctGame.Match;
using OpenScctGame.Online;
using OpenScctGame.UI;
using OpenScctGame.Voice;

namespace OpenScctGame.Options;

// The Voice page: Reloaded's voice settings page (spec/game/lobby-and-match-creation.md "Data", rules 21 and 22) with
// the rows the rewrite has: Device, Enable voice chat; Playback: Voice Chat Volume, Buffering; Microphone: Mute,
// Microphone Volume, Auto-Adjust Mic Volume, Noise suppression and its level, Echo cancellation; Transmission: the level
// bar, Activation threshold, Release time, Push to talk and its key; and Test my microphone, which runs the whole send
// chain (clean-up, gain, gate, Opus, a relay, the jitter buffer, decoding) and plays back what other players would hear.
public partial class OptionsMenu
{
    private const double MicTestSeconds = 3.0;
    private const string SystemDevice = "System default";

    private ProgressBar? _micLevel;
    private Label? _micState;
    private Label? _micTestState;
    private Button? _micTestButton;
    private VoiceAudio? _meterAudio;
    private VoiceClient? _meter;
    private MicTest? _micTest;

    private Control VoicePage()
    {
        GridContainer g = Grid("Voice");
        string[] devices = InputDevices();
        Choice(g, "Device", "voice_input_device", devices, _edit.VoiceInputDevice.Length == 0 ? SystemDevice : _edit.VoiceInputDevice,
            v => _edit.VoiceInputDevice = v == SystemDevice ? string.Empty : v);
        Check(g, "Enable voice chat", "allow_voice_chat", _edit.VoiceEnabled, v => _edit.VoiceEnabled = v);
        Slider(g, "Voice Chat Volume", "voice_output_volume", 0f, 100f, 1f, _edit.VoiceVolume * 100f, v => _edit.VoiceVolume = v / 100f, "0");
        Slider(g, "Buffering (20 ms frames)", "voice_jitter_frames", VoiceK.MinJitterFrames, VoiceK.MaxJitterFrames, 1f, _edit.VoiceJitterFrames,
            v => _edit.VoiceJitterFrames = (int)v, "0");
        Check(g, "Mute microphone", "voice_muted", _edit.VoiceMuted, v => _edit.VoiceMuted = v);
        Slider(g, "Microphone Volume", "voice_input_gain", 0f, 4f, 0.05f, _edit.VoiceInputGain, v => _edit.VoiceInputGain = v);
        Check(g, "Auto-Adjust Mic Volume (Microphone Volume is then not used)", "voice_auto_gain", _edit.VoiceAutoGain, v => _edit.VoiceAutoGain = v);
        Check(g, "Noise suppression", "voice_noise_suppression", _edit.VoiceNoiseSuppression, v => _edit.VoiceNoiseSuppression = v);
        Choice(g, "Noise suppression level", "voice_noise_suppression_level", GameSettings.NoiseLevels, _edit.VoiceNoiseLevel,
            v => _edit.VoiceNoiseLevel = v, v => $"{v} ({GameSettings.NoiseLevelDecibels(v)} dB)");
        Check(g, "Echo cancellation (experimental: for speakers)", "voice_echo_cancellation", _edit.VoiceEchoCancellation,
            v => _edit.VoiceEchoCancellation = v);

        g.AddChild(new Label { Text = "Microphone level", CustomMinimumSize = new Vector2(320f, 0f) });
        _micLevel = new ProgressBar { Name = "MicLevel", MinValue = 0, MaxValue = 0.25, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(320f, 18f) };
        g.AddChild(_micLevel);
        _micState = new Label { Name = "MicState", CustomMinimumSize = new Vector2(70f, 0f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        g.AddChild(_micState);
        Slider(g, "Activation threshold", "voice_activation_threshold", 0f, 0.25f, 0.001f, _edit.VoiceThreshold, v => _edit.VoiceThreshold = v, "0.000");
        Slider(g, "Release time (ms)", "voice_activation_hold_ms", 0f, 2000f, 10f, _edit.VoiceHoldMs, v => _edit.VoiceHoldMs = (int)v, "0");
        Check(g, "Push to talk", "voice_push_to_talk", _edit.VoicePushToTalk, v => _edit.VoicePushToTalk = v);
        g.AddChild(new Label { Text = "Push To Talk key" });
        g.AddChild(new Label
        {
            Name = "PttKey", Text = BindText(VoiceInput.PushToTalk) + "   (team: " + BindText(VoiceInput.TeamPushToTalk) + ")", Modulate = UiStyle.Dim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(320f, 0f),
        });
        g.AddChild(new Control());
        g.AddChild(new Label { Text = "Mute Microphone key" });
        g.AddChild(new Label { Name = "MuteKey", Text = BindText(VoiceInput.ToggleMute), Modulate = UiStyle.Dim });
        g.AddChild(new Control());

        _micTestButton = UiStyle.MenuButton("Test my microphone", StartMicTest, "MicTest");
        _micTestButton.CustomMinimumSize = new Vector2(300f, 40f);
        g.AddChild(_micTestButton);
        _micTestState = new Label
        {
            Name = "MicTestState", Text = "Hear exactly what other players hear from you.", Modulate = UiStyle.Dim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(320f, 0f),
        };
        g.AddChild(_micTestState);
        g.AddChild(new Control());
        return Page("Voice", g);
    }

    private static string[] InputDevices()
    {
        var list = new List<string> { SystemDevice };
        foreach (string d in AudioServer.GetInputDeviceList())
        {
            if (!string.Equals(d, "Default", StringComparison.OrdinalIgnoreCase) && !list.Contains(d))
            {
                list.Add(d);
            }
        }

        return list.ToArray();
    }

    /// <summary>"Bind: X" as Reloaded's page writes the key, or "Bind: none - Nobody can hear you".</summary>
    private static string BindText(string action)
    {
        VoiceInput.EnsureActions();
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            if (e is InputEventKey k)
            {
                return "Bind: " + OS.GetKeycodeString(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode);
            }
        }

        return "Bind: none - Nobody can hear you";
    }

    /// <summary>The microphone meter runs while the Voice page shows (outside an online visit, which has its own).</summary>
    private void UpdateVoicePage()
    {
        if (_micLevel is null || _tabs is null || _tabs.GetCurrentTabControl()?.Name != "Voice")
        {
            StopMeter();
            return;
        }

        OnlineVoiceChat? online = OnlineService.Get(GetTree()).VoiceChat;
        float level;
        if (online?.Voice is { } visit)
        {
            level = visit.Voice.InputLevel;
        }
        else
        {
            if (_meter is null)
            {
                _meter = new VoiceClient(new VoiceSettings(), new StopwatchClock());
                _meterAudio = new VoiceAudio { Name = "MeterAudio", Client = _meter };
                AddChild(_meterAudio);
                _meter.SetCaptureRate(_meterAudio.CaptureRate > 0 ? _meterAudio.CaptureRate : (int)AudioServer.GetMixRate());
                // Smoke runs never open the player's microphone.
                if (!SettingsStore.Isolated && !_meterAudio.StartMicrophone(_edit.VoiceInputDevice) && _micTestState is not null)
                {
                    _micTestState.Text = _meterAudio.MicrophoneProblem ?? "The microphone could not start.";
                }
            }

            // The meter shows the level after the page's clean-up and gain, as the gate sees it. The echo canceller has
            // no voices to cancel here.
            _meter.Settings.InputGain = _edit.VoiceInputGain;
            _meter.Settings.AutoGain = _edit.VoiceAutoGain;
            _meter.Settings.NoiseSuppression = _edit.VoiceNoiseSuppression;
            _meter.Settings.NoiseSuppressionDecibels = GameSettings.NoiseLevelDecibels(_edit.VoiceNoiseLevel);
            level = _meter.InputLevel;
        }

        _micLevel.Value = level;
        if (_micState is not null)
        {
            _micState.Text = _edit.VoiceMuted ? "muted" : _edit.VoicePushToTalk ? "push to talk" : level >= _edit.VoiceThreshold ? "transmitting" : "silent";
        }

        _micTest?.Update(this);
    }

    private void StopMeter()
    {
        _meterAudio?.QueueFree();
        _meterAudio = null;
        _meter?.Dispose();
        _meter = null;
    }

    private void StartMicTest()
    {
        if (_micTestState is null || _micTest is { Running: true })
        {
            return;
        }

        if (OnlineService.Get(GetTree()).VoiceChat?.Voice is not null)
        {
            _micTestState.Text = "Leave the session to test the microphone.";
            return;
        }

        StopMeter();
        _micTest = new MicTest(_edit);
        AddChild(_micTest.Audio);
        if (!_micTest.Audio.StartMicrophone(_edit.VoiceInputDevice))
        {
            _micTestState.Text = _micTest.Audio.MicrophoneProblem ?? "The microphone could not start.";
            _micTest.Dispose();
            _micTest = null;
            return;
        }

        _micTestState.Text = "Recording - speak now";
    }

    /// <summary>
    /// Test my microphone: the page's settings on a voice client that talks to a relay in this process, a second client
    /// that listens, three seconds recorded from what the listener decoded, then played back.
    /// </summary>
    private sealed class MicTest : IDisposable
    {
        private readonly LoopbackNetwork _network = new();
        private readonly StopwatchClock _clock = new();
        private readonly VoiceTransport _server;
        private readonly VoiceTransport _talkerTransport;
        private readonly VoiceTransport _listenerTransport;
        private readonly VoiceClient _talker;
        private readonly VoiceClient _listener;
        private readonly List<float> _recorded = new();
        private readonly float[] _frame = new float[VoiceK.FrameSamples];
        private readonly double _start;
        private AudioStreamPlayer? _player;
        private AudioStreamGeneratorPlayback? _playback;
        private int _played;

        public MicTest(GameSettings s)
        {
            _server = new VoiceTransport(_network.Bind("test-relay"));
            var directory = new StaticVoiceDirectory { Rules = new VoiceRules(OpenScct.Core.Engine.EVoiceChatMode.VCM_Open, false, true) };
            directory.Set(VoiceMember.Create(1, 0));
            directory.Set(VoiceMember.Create(2, 0));
            _ = new VoiceRelay(_server, directory, _clock);
            var settings = new VoiceSettings
            {
                PushToTalk = false, ActivationThreshold = s.VoicePushToTalk ? 0f : s.VoiceThreshold, ActivationHoldMs = s.VoiceHoldMs,
                InputGain = s.VoiceInputGain, JitterFrames = s.VoiceJitterFrames, AutoGain = s.VoiceAutoGain,
                NoiseSuppression = s.VoiceNoiseSuppression, NoiseSuppressionDecibels = GameSettings.NoiseLevelDecibels(s.VoiceNoiseLevel),
            };
            _talkerTransport = new VoiceTransport(_network.Bind("test-talker"));
            _talker = new VoiceClient(settings, _clock) { LocalTalker = 1 };
            _talker.Connect(_talkerTransport, _talkerTransport.Connect("test-relay"));
            _listenerTransport = new VoiceTransport(_network.Bind("test-listener"));
            _listener = new VoiceClient(new VoiceSettings { JitterFrames = s.VoiceJitterFrames }, _clock) { LocalTalker = 2 };
            _listener.Connect(_listenerTransport, _listenerTransport.Connect("test-relay"));
            Audio = new VoiceAudio { Name = "MicTestAudio", Client = _talker };
            _talker.SetCaptureRate((int)AudioServer.GetMixRate());
            _start = Time.GetTicksMsec() / 1000.0;
        }

        public VoiceAudio Audio { get; }

        public bool Running { get; private set; } = true;

        public void Update(OptionsMenu page)
        {
            if (!Running)
            {
                return;
            }

            _talkerTransport.DrainVoiceOnly();
            _server.DrainVoiceOnly();
            _listenerTransport.DrainVoiceOnly();
            _talker.Update();
            _listener.Update();
            foreach (VoiceTalker t in _listener.Talkers)
            {
                while (t.Buffer.Count > 0 && t.ReadFrame(_frame))
                {
                    _recorded.AddRange(_frame);
                }
            }

            double elapsed = Time.GetTicksMsec() / 1000.0 - _start;
            if (_player is null && elapsed >= MicTestSeconds)
            {
                Audio.StopMicrophone();
                if (_recorded.Count == 0)
                {
                    page._micTestState!.Text = "Nothing was captured. Voice chat may not be running.";
                    Finish();
                    return;
                }

                var generator = new AudioStreamGenerator
                {
                    MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom, MixRate = VoiceK.SampleRate, BufferLength = 0.5f,
                };
                _player = new AudioStreamPlayer { Name = "MicTestPlayback", Stream = generator, Bus = VoiceAudio.VoiceBus };
                page.AddChild(_player);
                _player.Play();
                _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
                page._micTestState!.Text = "Playing it back";
            }

            if (_playback is not null)
            {
                int room = _playback.GetFramesAvailable();
                var push = new Vector2[Math.Min(room, _recorded.Count - _played)];
                for (int i = 0; i < push.Length; i++)
                {
                    float v = _recorded[_played + i];
                    push[i] = new Vector2(v, v);
                }

                if (push.Length > 0)
                {
                    _playback.PushBuffer(push);
                    _played += push.Length;
                }

                if (_played >= _recorded.Count && elapsed > MicTestSeconds * 2 + 0.5)
                {
                    page._micTestState!.Text = "Hear exactly what other players hear from you.";
                    Finish();
                }
            }
        }

        private void Finish()
        {
            Running = false;
            Dispose();
        }

        public void Dispose()
        {
            Running = false;
            Audio.StopMicrophone();
            Audio.QueueFree();
            _playback?.Dispose();
            _player?.QueueFree();
            _talker.Dispose();
            _listener.Dispose();
            _talkerTransport.Dispose();
            _listenerTransport.Dispose();
            _server.Dispose();
        }
    }
}
