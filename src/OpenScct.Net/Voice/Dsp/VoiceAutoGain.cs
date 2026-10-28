using System;

namespace OpenScct.Net.Voice.Dsp;

/// <summary>
/// Automatic gain (FEATURES rl:voice.gain, <c>voice_auto_gain</c>): moves the microphone towards a comfortable speech
/// level, in the manner of speexdsp's AGC.
/// <list type="bullet">
/// <item>Only speech moves the gain: a frame counts when its level is 12 dB over the tracked floor (the quietest
/// recent level) and over -50 dBFS, so room noise is never pulled up to speech level.</item>
/// <item>The gain falls fast (1 dB a frame) and rises slowly (0.1 dB a frame, 5 dB a second), between -20 dB and
/// +30 dB; a frame that would clip lowers it at once.</item>
/// <item>The gain ramps across each frame, so a change makes no click.</item>
/// </list>
/// </summary>
internal sealed class VoiceAutoGain
{
    /// <summary>Speech level aimed at, RMS (-20 dBFS).</summary>
    public const float Target = 0.1f;

    private const float MaxGain = 31.622776f;
    private const float MinGain = 0.1f;
    private const float SpeechFloor = 0.0031622776f;
    private const float FloorMargin = 4f;
    private const float Up = 1.0115794f;
    private const float Down = 0.89125094f;
    private const float FloorRise = 1.01f;
    private const float Peak = 0.95f;

    private float _gain = 1f;
    private float _floor = 1f;

    public float Gain => _gain;

    public void Reset()
    {
        _gain = 1f;
        _floor = 1f;
    }

    public void Process(Span<float> frame)
    {
        if (frame.Length == 0)
        {
            return;
        }

        double sum = 0;
        float peak = 0f;
        foreach (float s in frame)
        {
            sum += s * s;
            peak = Math.Max(peak, Math.Abs(s));
        }

        float rms = (float)Math.Sqrt(sum / frame.Length);
        _floor = rms < _floor ? rms : Math.Min(1f, _floor * FloorRise);
        float start = _gain;
        if (rms > SpeechFloor && rms > FloorMargin * _floor)
        {
            float wanted = Math.Clamp(Target / rms, MinGain, MaxGain);
            _gain = wanted < _gain ? Math.Max(wanted, _gain * Down) : Math.Min(wanted, _gain * Up);
        }

        if (peak * _gain > Peak)
        {
            _gain = Peak / peak;
            start = Math.Min(start, _gain);
        }

        float step = (_gain - start) / frame.Length;
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = Math.Clamp(frame[i] * (start + step * (i + 1)), -1f, 1f);
        }
    }
}
