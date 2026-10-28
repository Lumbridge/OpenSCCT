using System;
using OpenScct.Provenance;

namespace OpenScct.Net.Voice.Dsp;

/// <summary>
/// The microphone clean-up before the transmit gate (docs/VOICE.md "Microphone clean-up"): echo cancellation
/// (<see cref="VoiceEchoCanceller"/>), noise suppression (<see cref="VoiceNoiseSuppressor"/>), then automatic gain
/// (<see cref="VoiceAutoGain"/>), each switched by its <see cref="VoiceSettings"/> key. All managed code, at 48 kHz on
/// 20 ms frames.
/// <para>The echo and noise stages work on <see cref="Block"/> sample blocks; with either on, a frame comes out
/// <see cref="Block"/> to <see cref="Block"/> x 2 samples late (5 to 11 ms). Turning a stage on or off starts the
/// stages afresh. The far end (<see cref="SubmitFarEnd"/>) is the voice mix as it is handed to the audio output; a
/// backlog of more than 100 ms is trimmed so the echo stays inside the filter's reach.</para>
/// <para>Single-threaded: one thread at a time (the voice thread, <see cref="VoiceDspThread"/>, or the caller's).</para>
/// </summary>
[PortOf("rl:voice.noise", Spec = "spec/reloaded/FEATURES.md#rlvoicenoise---noise-suppression")]
[PortOf("rl:voice.gain", Spec = "spec/reloaded/FEATURES.md#rlvoicegain---gain-and-automatic-gain")]
[PortOf("rl:voice.echo", Spec = "spec/reloaded/FEATURES.md#rlvoiceecho---echo-cancellation")]
[Provenance(ProvenanceKind.Estimated, "rewrite's own managed DSP (Wiener suppressor, speex-style AGC, PBFDAF echo canceller); Reloaded's library and tuning are not decoded")]
public sealed class VoiceDsp
{
    /// <summary>Samples per echo and noise block (5.3 ms at 48 kHz).</summary>
    public const int Block = 256;

    /// <summary>The most far-end audio kept waiting for the microphone (100 ms).</summary>
    public const int MaxFarBacklog = VoiceK.SampleRate / 10;

    private readonly VoiceEchoCanceller _echo = new();
    private readonly VoiceNoiseSuppressor _noise = new();
    private readonly VoiceAutoGain _gain = new();
    private readonly float[] _far = new float[VoiceK.SampleRate];
    private readonly float[] _in = new float[VoiceK.FrameSamples + Block];
    private readonly float[] _out = new float[VoiceK.FrameSamples + 3 * Block];
    private readonly float[] _block = new float[Block];
    private readonly float[] _farBlock = new float[Block];
    private int _farStart;
    private int _farCount;
    private int _inCount;
    private int _outCount;
    private bool _blocksOn;
    private bool _echoOn;
    private bool _noiseOn;
    private bool _gainOn;

    /// <summary>The automatic gain in use (1 while it is off).</summary>
    public float AutoGain => _gainOn ? _gain.Gain : 1f;

    /// <summary>Whether a setting asks for any stage.</summary>
    public static bool Wanted(VoiceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.NoiseSuppression || settings.AutoGain || settings.EchoCancellation;
    }

    /// <summary>What the voice output played (48 kHz mono); kept only while echo cancellation is on.</summary>
    public void SubmitFarEnd(ReadOnlySpan<float> samples)
    {
        if (!_echoOn)
        {
            return;
        }

        foreach (float s in samples)
        {
            if (_farCount == _far.Length)
            {
                _farStart = (_farStart + 1) % _far.Length;
                _farCount--;
            }

            _far[(_farStart + _farCount) % _far.Length] = s;
            _farCount++;
        }

        if (_farCount > MaxFarBacklog)
        {
            int drop = _farCount - MaxFarBacklog;
            _farStart = (_farStart + drop) % _far.Length;
            _farCount -= drop;
        }
    }

    /// <summary>Cleans one 20 ms frame in place with <paramref name="settings"/>.</summary>
    public void Process(VoiceSettings settings, Span<float> frame)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool echo = settings.EchoCancellation;
        bool noise = settings.NoiseSuppression;
        if (echo != _echoOn || noise != _noiseOn)
        {
            _echoOn = echo;
            _noiseOn = noise;
            _blocksOn = echo || noise;
            _echo.Reset();
            _noise.Reset();
            _farStart = 0;
            _farCount = 0;
            _inCount = 0;

            // The output starts one block ahead, so a whole frame is always ready (the blocks' delay).
            Array.Clear(_out);
            _outCount = Block;
        }

        if (_blocksOn && frame.Length <= VoiceK.FrameSamples)
        {
            frame.CopyTo(_in.AsSpan(_inCount));
            _inCount += frame.Length;
            int used = 0;
            float floor = VoiceNoiseSuppressor.FloorOf(settings.NoiseSuppressionDecibels);
            while (_inCount - used >= Block)
            {
                _in.AsSpan(used, Block).CopyTo(_block);
                used += Block;
                if (echo)
                {
                    TakeFar(_farBlock);
                    _echo.Process(_farBlock, _block);
                }

                _noise.Process(_block, echo ? _echo.EchoEstimate : ReadOnlySpan<float>.Empty, floor, noise);
                _block.CopyTo(_out.AsSpan(_outCount));
                _outCount += Block;
            }

            Array.Copy(_in, used, _in, 0, _inCount - used);
            _inCount -= used;
            _out.AsSpan(0, frame.Length).CopyTo(frame);
            Array.Copy(_out, frame.Length, _out, 0, _outCount - frame.Length);
            _outCount -= frame.Length;
        }

        if (settings.AutoGain != _gainOn)
        {
            _gainOn = settings.AutoGain;
            _gain.Reset();
        }

        if (_gainOn)
        {
            _gain.Process(frame);
        }
    }

    private void TakeFar(float[] into)
    {
        int take = Math.Min(Block, _farCount);
        for (int i = 0; i < take; i++)
        {
            into[i] = _far[(_farStart + i) % _far.Length];
        }

        Array.Clear(into, take, Block - take);
        _farStart = (_farStart + take) % _far.Length;
        _farCount -= take;
    }
}
