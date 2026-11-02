using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScct.Server.Online;
using OpenScctGame.Options;
using OpenScctGame.UI;
using OpenScctGame.Voice;

namespace OpenScctGame.Online;

/// <summary>
/// Voice chat for an online visit (docs/VOICE.md, docs/MENUS.md), in the lobby and in every round: the player's
/// microphone (<see cref="VoiceAudio"/> on the capture bus), the voice keys (Push To Talk, team talk, Mute Microphone),
/// the settings of the options' Voice page, one <see cref="OnlineVoice"/> that follows the visit from map to map and
/// keeps each other player's volume and mute, and Reloaded's voice overlay in a round ("name - volume n%",
/// "Your microphone is muted"). A child of <see cref="OnlineService"/>, so it lives as long as the visit.
/// </summary>
public partial class OnlineVoiceChat : Node
{
    public const string NodeName = "OnlineVoiceChat";

    private readonly Dictionary<ushort, (double Sum, long Count)> _heard = new();
    private readonly VoiceSettings _settings = new();
    private OnlineClient? _client;
    private VBoxContainer? _overlayRows;
    private CanvasLayer? _overlay;
    private double _tonePhase;
    private double _toneDue;
    private double _toneFed;
    private AudioStreamPlayer? _tonePlayer;
    private AudioStreamGeneratorPlayback? _tone;
    private bool _toneDirect;
    private double _toneStart;
    private bool _microphoneTried;

    /// <summary>The visit's voice, or null before <see cref="Attach"/>.</summary>
    public OnlineVoice? Voice { get; private set; }

    public VoiceAudio? Audio { get; private set; }

    /// <summary>The voice settings in use (the options' Voice page).</summary>
    public VoiceSettings Settings => _settings;

    /// <summary>Read the voice keys from the InputMap.</summary>
    public bool ReadKeys { get; set; } = true;

    /// <summary>Show Reloaded's voice overlay (the round, not the lobby, which lists talkers itself).</summary>
    public bool ShowOverlay { get; set; }

    /// <summary>Why the microphone is not capturing, or null.</summary>
    public string? MicrophoneProblem => Audio?.MicrophoneProblem;

    /// <summary>
    /// Tests: a synthetic microphone. A tone of this frequency (0: off) plays on the capture bus, or goes to the client
    /// directly when the audio driver does not mix (headless), with <see cref="ToneAmplitude"/>.
    /// </summary>
    public double ToneHz { get; set; }

    public float ToneAmplitude { get; set; } = 0.3f;

    /// <summary>Tests: how the synthetic microphone reaches the client ("bus" or "direct").</summary>
    public string ToneRoute => _toneDirect ? "direct" : "bus";

    public override void _Ready()
    {
        VoiceInput.EnsureActions();
        Audio = new VoiceAudio { Name = "VoiceAudio" };
        AddChild(Audio);
        Audio.FrameTap = (talker, frame) =>
        {
            double sum = 0;
            foreach (float s in frame)
            {
                sum += (double)s * s;
            }

            (double Sum, long Count) acc = _heard.GetValueOrDefault(talker);
            _heard[talker] = (acc.Sum + sum, acc.Count + frame.Length);
        };
        if (DisplayServer.GetName() == "headless")
        {
            // Without a window the output is not mixed: pace playback by the clock so voices still play out.
            Audio.Pacing = VoicePlaybackPacing.Clock;
        }

        _overlay = new CanvasLayer { Name = "VoiceOverlay", Layer = VoiceIndicator.DefaultLayer, Visible = false };
        _overlayRows = new VBoxContainer
        {
            Name = "Rows", Position = new Vector2(-330f, 12f), CustomMinimumSize = new Vector2(320f, 0f), Theme = UiStyle.Theme,
        };
        _overlayRows.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _overlay.AddChild(_overlayRows);
        AddChild(_overlay);
        SettingsStore.Saved += OnSaved;
        ApplySettings(SettingsStore.Current);
    }

    public override void _ExitTree()
    {
        SettingsStore.Saved -= OnSaved;
        Detach();
    }

    /// <summary>Starts the visit's voice on <paramref name="client"/>.</summary>
    public void Attach(OnlineClient client, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        Detach();
        _client = client;
        var voice = new VoiceClient(_settings, clock) { UseVoiceThread = true };
        Voice = new OnlineVoice(client, voice, PlayerVoiceVolumeStore.Current);
        if (Audio is not null)
        {
            Audio.Client = voice;
            voice.SetCaptureRate(Audio.CaptureRate > 0 ? Audio.CaptureRate : (int)AudioServer.GetMixRate());
        }

        _heard.Clear();
        _microphoneTried = false;
    }

    public void Detach()
    {
        Voice?.Dispose();
        Voice = null;
        _client = null;
        if (Audio is not null)
        {
            Audio.Client = null;
            Audio.StopMicrophone();
        }

        _tonePlayer?.QueueFree();
        _tonePlayer = null;
        _tone?.Dispose();
        _tone = null;
    }

    /// <summary>Root mean square of everything heard from <paramref name="playerName"/> since <see cref="ClearHeard"/>.</summary>
    public (double Rms, long Samples) Heard(string playerName)
    {
        if (Voice?.TalkerOf(playerName) is not { } talker || !_heard.TryGetValue(talker, out (double Sum, long Count) acc) || acc.Count == 0)
        {
            return (0, 0);
        }

        return (Math.Sqrt(acc.Sum / acc.Count), acc.Count);
    }

    public void ClearHeard() => _heard.Clear();

    public override void _Process(double delta)
    {
        if (Voice is not { } voice)
        {
            return;
        }

        // Smoke runs never open the player's microphone; they feed a tone (ToneHz) instead.
        if (!_microphoneTried && _settings.Enabled && ToneHz <= 0 && Audio is not null && !SettingsStore.Isolated)
        {
            _microphoneTried = true;
            if (!Audio.StartMicrophone(SettingsStore.Current.VoiceInputDevice) && Audio.MicrophoneProblem is { } problem)
            {
                GD.Print("SVM_VOICE microphone: " + problem);
            }
        }

        if (ReadKeys)
        {
            voice.Voice.TalkKey = Input.IsActionPressed(VoiceInput.PushToTalk);
            voice.Voice.TeamKey = Input.IsActionPressed(VoiceInput.TeamPushToTalk);
            if (Input.IsActionJustPressed(VoiceInput.ToggleMute))
            {
                _settings.MicMuted = !_settings.MicMuted;
                SettingsStore.Current.VoiceMuted = _settings.MicMuted;
            }
        }

        FeedTone(delta);
        VoiceAudio.SetVolume(_settings.OutputVolume);
        voice.Update();
        DrawOverlay();
    }

    private void OnSaved(GameSettings settings) => ApplySettings(settings);

    /// <summary>
    /// The options' Voice page in the voice client's settings. The synthetic microphone skips the clean-up: a steady
    /// tone is exactly what noise suppression takes away.
    /// </summary>
    public void ApplySettings(GameSettings s)
    {
        CopySettings(s, _settings);
        if (ToneHz > 0)
        {
            _settings.AutoGain = false;
            _settings.NoiseSuppression = false;
            _settings.EchoCancellation = false;
        }
    }

    /// <summary>The options' Voice page into <paramref name="into"/> (the hub's voice reads it too).</summary>
    public static void CopySettings(GameSettings s, VoiceSettings into)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(into);
        into.Enabled = s.VoiceEnabled;
        into.PushToTalk = s.VoicePushToTalk;
        into.MicMuted = s.VoiceMuted;
        into.ActivationThreshold = s.VoiceThreshold;
        into.ActivationHoldMs = s.VoiceHoldMs;
        into.InputGain = s.VoiceInputGain;
        into.OutputVolume = s.VoiceVolume;
        into.JitterFrames = s.VoiceJitterFrames;
        into.AutoGain = s.VoiceAutoGain;
        into.NoiseSuppression = s.VoiceNoiseSuppression;
        into.NoiseSuppressionDecibels = GameSettings.NoiseLevelDecibels(s.VoiceNoiseLevel);
        into.EchoCancellation = s.VoiceEchoCancellation;
    }

    private void FeedTone(double delta)
    {
        if (ToneHz <= 0 || Voice is not { } voice)
        {
            return;
        }

        // Set after the settings were first applied: take the clean-up off now.
        _settings.AutoGain = false;
        _settings.NoiseSuppression = false;
        _settings.EchoCancellation = false;

        if (_tonePlayer is null && !_toneDirect)
        {
            var generator = new AudioStreamGenerator
            {
                MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
                MixRate = VoiceK.SampleRate,
                BufferLength = 0.25f,
            };
            _tonePlayer = new AudioStreamPlayer { Name = "SyntheticMicrophone", Stream = generator, Bus = VoiceAudio.CaptureBus };
            AddChild(_tonePlayer);
            _tonePlayer.Play();
            _tone = (AudioStreamGeneratorPlayback)_tonePlayer.GetStreamPlayback();
            _toneStart = Time.GetTicksMsec() / 1000.0;
        }

        var frame = new Vector2[256];
        while (_tone is not null && _tone.GetFramesAvailable() > (int)(0.15 * VoiceK.SampleRate))
        {
            for (int i = 0; i < frame.Length; i++)
            {
                float s = ToneAmplitude * (float)Math.Sin(_tonePhase);
                _tonePhase += 2 * Math.PI * ToneHz / VoiceK.SampleRate;
                frame[i] = new Vector2(s, s);
            }

            _tone.PushBuffer(frame);
        }

        // A driver that does not mix leaves the capture bus silent: feed the client directly, paced by the clock.
        double now = Time.GetTicksMsec() / 1000.0;
        if (!_toneDirect && now - _toneStart > 1.0 && Audio is { CapturedFrames: 0 })
        {
            _toneDirect = true;
            _tonePlayer?.QueueFree();
            _tonePlayer = null;
            _tone?.Dispose();
            _tone = null;
            voice.Voice.SetCaptureRate(VoiceK.SampleRate);
            _toneDue = 0;
            _toneFed = 0;
            GD.Print("SVM_VOICE the capture bus is silent on this audio driver: the tone goes to the client directly");
        }

        if (!_toneDirect)
        {
            return;
        }

        _toneDue += delta;
        int samples = (int)((_toneDue - _toneFed) * VoiceK.SampleRate);
        if (samples <= 0)
        {
            return;
        }

        var block = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            block[i] = ToneAmplitude * (float)Math.Sin(_tonePhase);
            _tonePhase += 2 * Math.PI * ToneHz / VoiceK.SampleRate;
        }

        voice.Voice.SubmitCapture(block);
        _toneFed += samples / (double)VoiceK.SampleRate;
    }

    /// <summary>Reloaded's overlay: who is speaking, at which volume, and the muted microphone.</summary>
    private void DrawOverlay()
    {
        if (_overlay is null || _overlayRows is null || Voice is not { } voice)
        {
            return;
        }

        _overlay.Visible = ShowOverlay;
        if (!ShowOverlay)
        {
            return;
        }

        var lines = new List<string>();
        if (_settings.MicMuted)
        {
            lines.Add("Your microphone is muted");
        }
        else if (voice.Voice.Transmitting)
        {
            lines.Add("You - Speaking" + (voice.Voice.TransmitChannel == EVoiceChannel.Team ? " (team)" : string.Empty));
        }

        foreach (VoiceTalker talker in voice.Voice.Talkers)
        {
            if (talker.Speaking && voice.NameOf(talker.Id) is { } name)
            {
                lines.Add(LobbyRoomModel.VolumeText(name, talker.Volume) + (talker.Channel == EVoiceChannel.Team ? " (team)" : string.Empty));
            }
        }

        while (_overlayRows.GetChildCount() < lines.Count)
        {
            _overlayRows.AddChild(new Label { HorizontalAlignment = HorizontalAlignment.Right, Modulate = UiStyle.Text });
        }

        for (int i = 0; i < _overlayRows.GetChildCount(); i++)
        {
            var label = (Label)_overlayRows.GetChild(i);
            label.Visible = i < lines.Count;
            if (i < lines.Count)
            {
                label.Text = lines[i];
            }
        }
    }

    /// <summary>A line about the microphone for the lobby: level, transmitting or silent.</summary>
    public string MicrophoneLine()
    {
        if (Voice is not { } voice)
        {
            return string.Empty;
        }

        if (!_settings.Enabled)
        {
            return "Voice chat is off (Options, Voice).";
        }

        if (_settings.MicMuted)
        {
            return "Your microphone is muted";
        }

        string state = voice.Voice.Transmitting ? "transmitting" : "silent";
        string mode = _settings.PushToTalk ? "push to talk" : "voice activated";
        return string.Create(CultureInfo.InvariantCulture, $"Mic {state}, {mode}") +
            (MicrophoneProblem is { } p && ToneHz <= 0 ? " - " + p : string.Empty);
    }
}
