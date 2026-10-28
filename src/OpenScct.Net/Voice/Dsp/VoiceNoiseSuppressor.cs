using System;

namespace OpenScct.Net.Voice.Dsp;

/// <summary>
/// Noise suppression (FEATURES rl:voice.noise) and the echo canceller's residual echo suppression: a short-time
/// spectral Wiener filter with the decision-directed a priori SNR (Ephraim and Malah), the textbook method behind
/// speexdsp's and WebRTC's classic suppressors.
/// <list type="bullet">
/// <item>Frames of twice <see cref="VoiceDsp.Block"/> samples, half overlap, square-root Hann windows on the way in and
/// out (they add back to the input exactly); one block of delay.</item>
/// <item>The noise estimate follows the minimum of the smoothed power per bin: it falls to a quieter level quickly
/// and rises only slowly (about 2.5 dB a second), so speech does not count as noise; it starts from the average of the
/// first blocks.</item>
/// <item>The gain never goes below the level's floor (Low 15 dB, Standard 30, High 45, Extreme 60 of attenuation).</item>
/// <item>A quarter of the echo canceller's estimate is added to the disturbance, so what is left of the echo is
/// suppressed with the noise.</item>
/// </list>
/// </summary>
internal sealed class VoiceNoiseSuppressor
{
    private const int N = VoiceDsp.Block;
    private const int M = 2 * N;
    private const int Bins = N + 1;

    /// <summary>Blocks averaged for the first noise estimate (about 0.15 s).</summary>
    private const int StartBlocks = 28;

    /// <summary>How fast the noise estimate may rise per block (power).</summary>
    private const float NoiseRise = 1.003f;

    /// <summary>The minimum of a smoothed power sits under its mean: the estimate is raised by this much (3 dB).</summary>
    private const float MinimumBias = 2f;

    /// <summary>The share of the echo estimate's power counted as residual echo.</summary>
    private const float EchoLeak = 0.25f;

    /// <summary>Decision-directed smoothing of the a priori SNR.</summary>
    private const float Alpha = 0.98f;

    private readonly VoiceFft _fft = new(M);
    private readonly float[] _window = new float[M];
    private readonly float[] _inPrevious = new float[N];
    private readonly float[] _echoPrevious = new float[N];
    private readonly float[] _tail = new float[N];
    private readonly float[] _re = new float[M];
    private readonly float[] _im = new float[M];
    private readonly float[] _er = new float[M];
    private readonly float[] _ei = new float[M];
    private readonly float[] _smooth = new float[Bins];
    private readonly float[] _noise = new float[Bins];
    private readonly float[] _gain = new float[Bins];
    private readonly float[] _gamma = new float[Bins];
    private int _blocks;

    public VoiceNoiseSuppressor()
    {
        // sqrt(periodic Hann) = sin(pi n / M).
        (_, double[] sin) = VoiceFft.UnitRoots(2 * M);
        for (int n = 0; n < M; n++)
        {
            _window[n] = (float)sin[n];
        }

        Reset();
    }

    public void Reset()
    {
        Array.Clear(_inPrevious);
        Array.Clear(_echoPrevious);
        Array.Clear(_tail);
        Array.Clear(_smooth);
        Array.Clear(_noise);
        Array.Fill(_gain, 1f);
        Array.Fill(_gamma, 1f);
        _blocks = 0;
    }

    /// <summary>
    /// One block in place. <paramref name="floor"/> is the smallest gain (amplitude); <paramref name="echo"/> is the
    /// echo canceller's estimate for this block, or empty.
    /// </summary>
    public void Process(Span<float> block, ReadOnlySpan<float> echo, float floor, bool suppressNoise)
    {
        bool withEcho = echo.Length >= N;
        for (int i = 0; i < N; i++)
        {
            _re[i] = _inPrevious[i] * _window[i];
            _re[N + i] = block[i] * _window[N + i];
        }

        block[..N].CopyTo(_inPrevious);
        Array.Clear(_im);
        _fft.Forward(_re, _im);
        if (withEcho)
        {
            for (int i = 0; i < N; i++)
            {
                _er[i] = _echoPrevious[i] * _window[i];
                _er[N + i] = echo[i] * _window[N + i];
            }

            echo[..N].CopyTo(_echoPrevious);
            Array.Clear(_ei);
            _fft.Forward(_er, _ei);
        }
        else
        {
            Array.Clear(_echoPrevious);
        }

        _blocks++;
        for (int f = 0; f < Bins; f++)
        {
            float power = _re[f] * _re[f] + _im[f] * _im[f];
            float disturbance = 0f;
            if (suppressNoise)
            {
                _smooth[f] = 0.8f * _smooth[f] + 0.2f * power;
                if (_blocks <= StartBlocks)
                {
                    _noise[f] += (_smooth[f] - _noise[f]) / _blocks;
                }
                else if (_smooth[f] < _noise[f])
                {
                    _noise[f] = 0.7f * _noise[f] + 0.3f * _smooth[f];
                }
                else
                {
                    _noise[f] = _noise[f] * NoiseRise + 1e-12f;
                }

                disturbance = _blocks <= StartBlocks ? _noise[f] : _noise[f] * MinimumBias;
            }

            if (withEcho)
            {
                disturbance += EchoLeak * (_er[f] * _er[f] + _ei[f] * _ei[f]);
            }

            float g = 1f;
            if (disturbance > 0f)
            {
                float gamma = power / (disturbance + 1e-12f);
                float xi = Alpha * _gain[f] * _gain[f] * _gamma[f] + (1f - Alpha) * Math.Max(gamma - 1f, 0f);
                g = Math.Max(xi / (1f + xi), floor);
                _gamma[f] = Math.Min(gamma, 1e4f);
            }

            _gain[f] = g;
            _re[f] *= g;
            _im[f] *= g;
        }

        _im[0] = 0f;
        _im[N] = 0f;
        for (int f = 1; f < N; f++)
        {
            _re[M - f] = _re[f];
            _im[M - f] = -_im[f];
        }

        _fft.Inverse(_re, _im);
        for (int i = 0; i < N; i++)
        {
            block[i] = _re[i] * _window[i] + _tail[i];
            _tail[i] = _re[N + i] * _window[N + i];
        }
    }

    /// <summary>The amplitude floor of a suppression level in dB (15, 30, 45 or 60): 10^(-dB / 20).</summary>
    public static float FloorOf(int decibels) => decibels switch
    {
        <= 15 => 0.17782794f,
        <= 30 => 0.031622777f,
        <= 45 => 0.0056234132f,
        _ => 0.001f,
    };
}
