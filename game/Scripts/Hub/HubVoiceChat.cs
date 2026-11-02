using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScct.Server.Hub;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.Voice;

namespace OpenScctGame.Hub;

/// <summary>
/// Voice chat in the hub (docs/HUB.md): the player's microphone (<see cref="VoiceAudio"/> on the capture bus), the
/// voice keys (Push To Talk, team talk, Mute Microphone) and the settings of the options' Voice page, as in an online
/// match, and one <see cref="HubVoice"/> that plays each talker by distance from the camera to his pawn, muffled
/// through walls. A child of <see cref="HubService"/>, so it lives as long as the visit.
/// </summary>
public partial class HubVoiceChat : Node
{
    public const string NodeName = "HubVoiceChat";

    private readonly Dictionary<ushort, (double Sum, long Count)> _heard = new();
    private readonly VoiceSettings _settings = new();
    private bool _microphoneTried;

    /// <summary>The visit's voice, or null before <see cref="Attach"/>.</summary>
    public HubVoice? Voice { get; private set; }

    public VoiceAudio? Audio { get; private set; }

    /// <summary>The voice settings in use (the options' Voice page).</summary>
    public VoiceSettings Settings => _settings;

    /// <summary>The listener's ear in UE space (the play scene's camera), or null to use the local pawn's view point.</summary>
    public Func<FVector?>? Ear { get; set; }

    /// <summary>True while the voice keys must not be read (the player types a chat line).</summary>
    public Func<bool>? KeysBlocked { get; set; }

    /// <summary>Read the voice keys from the InputMap.</summary>
    public bool ReadKeys { get; set; } = true;

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

        SettingsStore.Saved += OnSaved;
        OnlineVoiceChat.CopySettings(SettingsStore.Current, _settings);
    }

    public override void _ExitTree()
    {
        SettingsStore.Saved -= OnSaved;
        Detach();
    }

    /// <summary>Starts the visit's voice on <paramref name="client"/>.</summary>
    public void Attach(HubClient client, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        Detach();
        var voice = new VoiceClient(_settings, clock) { UseVoiceThread = true };
        Voice = new HubVoice(client, voice, volumes: PlayerVoiceVolumeStore.Current);
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
        if (Audio is not null)
        {
            Audio.Client = null;
            Audio.StopMicrophone();
        }
    }

    /// <summary>Root mean square of everything heard from <paramref name="playerName"/> so far, and the samples.</summary>
    public (double Rms, long Samples) Heard(string playerName)
    {
        if (Voice?.TalkerOf(playerName) is not { } talker || !_heard.TryGetValue(talker, out (double Sum, long Count) acc) || acc.Count == 0)
        {
            return (0, 0);
        }

        return (Math.Sqrt(acc.Sum / acc.Count), acc.Count);
    }

    public override void _Process(double delta)
    {
        if (Voice is not { } voice)
        {
            return;
        }

        // Smoke runs and tests (isolated settings) never open the player's microphone.
        if (!_microphoneTried && _settings.Enabled && Audio is not null && !SettingsStore.Isolated)
        {
            _microphoneTried = true;
            if (!Audio.StartMicrophone(SettingsStore.Current.VoiceInputDevice) && Audio.MicrophoneProblem is { } problem)
            {
                GD.Print("SVM_VOICE microphone: " + problem);
            }
        }

        if (ReadKeys)
        {
            bool blocked = KeysBlocked?.Invoke() ?? false;
            voice.Voice.TalkKey = !blocked && Input.IsActionPressed(VoiceInput.PushToTalk);
            voice.Voice.TeamKey = !blocked && Input.IsActionPressed(VoiceInput.TeamPushToTalk);
            if (!blocked && Input.IsActionJustPressed(VoiceInput.ToggleMute))
            {
                _settings.MicMuted = !_settings.MicMuted;
                SettingsStore.Current.VoiceMuted = _settings.MicMuted;
            }
        }

        VoiceAudio.SetVolume(_settings.OutputVolume);
        voice.Update(Ear?.Invoke(), (float)delta);
    }

    /// <summary>The voice overlay's lines: the muted microphone or the player's own talk, then each talker heard.</summary>
    public void OverlayLines(List<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (Voice is not { } voice || !_settings.Enabled)
        {
            return;
        }

        if (_settings.MicMuted)
        {
            into.Add("Your microphone is muted");
        }
        else if (voice.Voice.Transmitting)
        {
            into.Add("You - Speaking" + (voice.Voice.TransmitChannel == EVoiceChannel.Team ? " (team)" : string.Empty));
        }

        foreach (VoiceTalker talker in voice.Voice.Talkers)
        {
            if (talker.Speaking && voice.NameOf(talker.Id) is { } name)
            {
                into.Add(UI.LobbyRoomModel.VolumeText(name, talker.Volume) + (talker.Channel == EVoiceChannel.Team ? " (team)" : string.Empty));
            }
        }
    }

    private void OnSaved(GameSettings settings) => OnlineVoiceChat.CopySettings(settings, _settings);
}
