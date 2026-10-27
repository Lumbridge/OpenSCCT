using System;

namespace OpenScct.Net.Voice;

/// <summary>A player's voice settings (the Reloaded voice keys, docs/VOICE.md "Settings").</summary>
public sealed class VoiceSettings
{
    private float _threshold = VoiceK.ActivationThreshold;
    private int _holdMs = VoiceK.ActivationHoldMs;
    private float _inputGain = 1f;
    private float _outputVolume = 1f;
    private int _jitterFrames = VoiceK.DefaultJitterFrames;

    /// <summary>Voice on at all (the stock profile's "Allow VoiceChat"). Off: nothing is sent or received.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Transmit only while a push to talk key is held (<c>voice_push_to_talk</c>, false: the activation gate).</summary>
    public bool PushToTalk { get; set; }

    /// <summary>Self mute (<c>voice_muted</c>): nothing is sent; everyone else is still heard.</summary>
    public bool MicMuted { get; set; }

    /// <summary>Gate threshold on the frame RMS, 0 to 1 (<c>voice_activation_threshold</c>).</summary>
    public float ActivationThreshold
    {
        get => _threshold;
        set => _threshold = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Gate hold in milliseconds (<c>voice_activation_hold_ms</c>).</summary>
    public int ActivationHoldMs
    {
        get => _holdMs;
        set => _holdMs = Math.Clamp(value, 0, 5000);
    }

    /// <summary>Microphone gain before the gate and the encoder (<c>voice_input_gain</c>), 0 to 8.</summary>
    public float InputGain
    {
        get => _inputGain;
        set => _inputGain = Math.Clamp(value, 0f, 8f);
    }

    /// <summary>Volume of other players' voices (<c>voice_output_volume</c>), 0 to 1.</summary>
    public float OutputVolume
    {
        get => _outputVolume;
        set => _outputVolume = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Jitter buffer depth in 20 ms frames (<c>voice_jitter_frames</c>), 1 to 12.</summary>
    public int JitterFrames
    {
        get => _jitterFrames;
        set => _jitterFrames = Math.Clamp(value, VoiceK.MinJitterFrames, VoiceK.MaxJitterFrames);
    }

    public VoiceSettings Clone() => (VoiceSettings)MemberwiseClone();
}

/// <summary>
/// Decides, frame by frame, whether the microphone transmits and on which channel (FEATURES rl:voice.activation).
/// <list type="bullet">
/// <item>Self mute: never.</item>
/// <item>The team key held: transmit on the team channel, in either mode.</item>
/// <item>Push to talk: transmit while the talk key is held.</item>
/// <item>Activation: the gate opens when the level reaches the threshold (0 always, 1 never), stays open while the level
/// is at least 60% of it, and closes after the level stayed below that for the hold time.</item>
/// </list>
/// </summary>
public sealed class VoiceGate
{
    private bool _open;
    private int _holdLeftMs;

    /// <summary>True while the activation gate is open (not counting the keys).</summary>
    public bool IsOpen => _open;

    /// <summary>
    /// One frame: <paramref name="level"/> is its RMS after gain (0 to 1). Returns whether to send it, and on which channel.
    /// </summary>
    public bool Step(VoiceSettings settings, float level, bool talkKey, bool teamKey, out EVoiceChannel channel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        channel = EVoiceChannel.All;
        bool gate = StepGate(settings, level);
        if (!settings.Enabled || settings.MicMuted)
        {
            return false;
        }

        if (teamKey)
        {
            channel = EVoiceChannel.Team;
            return true;
        }

        return settings.PushToTalk ? talkKey : gate || talkKey;
    }

    public void Reset()
    {
        _open = false;
        _holdLeftMs = 0;
    }

    private bool StepGate(VoiceSettings settings, float level)
    {
        float threshold = settings.ActivationThreshold;
        if (threshold <= 0f)
        {
            _open = true;
            return true;
        }

        if (threshold >= 1f)
        {
            _open = false;
            return false;
        }

        if (!_open)
        {
            if (level >= threshold)
            {
                _open = true;
                _holdLeftMs = settings.ActivationHoldMs;
            }

            return _open;
        }

        if (level >= threshold * VoiceK.ActivationCloseRatio)
        {
            _holdLeftMs = settings.ActivationHoldMs;
        }
        else
        {
            _holdLeftMs -= VoiceK.FrameMilliseconds;
            if (_holdLeftMs <= 0)
            {
                _open = false;
            }
        }

        return _open;
    }
}
