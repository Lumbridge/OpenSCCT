using System;

namespace OpenScct.Net.Voice.Dsp;

/// <summary>
/// Echo cancellation (FEATURES rl:voice.echo): a partitioned-block frequency-domain adaptive filter (the textbook
/// overlap-save PBFDAF, the method of speexdsp's MDF canceller) that learns the path from the voice playback to the
/// microphone and subtracts its estimate. The reference is the voice mix the game plays, so only other players' voices
/// are cancelled, as in Reloaded.
/// <list type="bullet">
/// <item>Blocks of <see cref="VoiceDsp.Block"/> samples, FFTs of twice that, <see cref="Partitions"/> partitions
/// (the echo tail it can cancel, about 200 ms at 48 kHz).</item>
/// <item>Normalised step per frequency bin (the far end's power summed over the partitions); one partition per block
/// gets the time-domain constraint, round robin, as MDF does.</item>
/// <item>Double talk: once the filter has converged, a block whose residual is larger than the echo estimate is taken
/// as the local player speaking and the filter learns 20 times slower. A filter that makes the signal louder is not
/// used, and is reset if it stays that way.</item>
/// </list>
/// </summary>
internal sealed class VoiceEchoCanceller
{
    /// <summary>Partitions of <see cref="VoiceDsp.Block"/> samples: 38 x 256 = 9,728 samples, about 203 ms.</summary>
    public const int Partitions = 38;

    private const int N = VoiceDsp.Block;
    private const int M = 2 * N;
    private const int Bins = N + 1;

    /// <summary>Normalised step size.</summary>
    private const float Step = 0.7f;

    /// <summary>The step while the local player talks over the echo.</summary>
    private const float DoubleTalkStep = Step / 20f;

    /// <summary>The far end's power floor per bin (keeps the step finite in near silence).</summary>
    private const float PowerFloor = M * 1e-7f;

    /// <summary>Echo return loss enhancement (linear power ratio) above which the filter counts as converged.</summary>
    private const float Converged = 4f;

    /// <summary>Blocks the filter may make the signal louder before it is cleared.</summary>
    private const int DivergedBlocks = 40;

    private readonly VoiceFft _fft = new(M);
    private readonly float[][] _xr = new float[Partitions][];
    private readonly float[][] _xi = new float[Partitions][];
    private readonly float[][] _wr = new float[Partitions][];
    private readonly float[][] _wi = new float[Partitions][];
    private readonly float[] _farPrevious = new float[N];
    private readonly float[] _re = new float[M];
    private readonly float[] _im = new float[M];
    private readonly float[] _power = new float[Bins];
    private int _head;
    private int _constrain;
    private int _farActive;
    private int _diverged;
    private float _erle = 1f;

    public VoiceEchoCanceller()
    {
        for (int k = 0; k < Partitions; k++)
        {
            _xr[k] = new float[Bins];
            _xi[k] = new float[Bins];
            _wr[k] = new float[Bins];
            _wi[k] = new float[Bins];
        }
    }

    /// <summary>The echo estimate subtracted from the last block (zero when nothing was cancelled).</summary>
    public float[] EchoEstimate { get; } = new float[N];

    public void Reset()
    {
        for (int k = 0; k < Partitions; k++)
        {
            Array.Clear(_xr[k]);
            Array.Clear(_xi[k]);
            Array.Clear(_wr[k]);
            Array.Clear(_wi[k]);
        }

        Array.Clear(_farPrevious);
        Array.Clear(EchoEstimate);
        _head = 0;
        _constrain = 0;
        _farActive = 0;
        _diverged = 0;
        _erle = 1f;
    }

    /// <summary>One block: <paramref name="far"/> is what played, <paramref name="mic"/> is cleaned in place.</summary>
    public void Process(ReadOnlySpan<float> far, Span<float> mic)
    {
        // The far end's newest block spectrum, from [previous block, this block].
        double farEnergy = 0;
        for (int i = 0; i < N; i++)
        {
            _re[i] = _farPrevious[i];
            _re[N + i] = far[i];
            farEnergy += far[i] * far[i];
        }

        Array.Clear(_im);
        far[..N].CopyTo(_farPrevious);
        _head = (_head + Partitions - 1) % Partitions;
        _fft.Forward(_re, _im);
        Array.Copy(_re, _xr[_head], Bins);
        Array.Copy(_im, _xi[_head], Bins);
        _farActive = farEnergy > 1e-10 ? Partitions + 1 : Math.Max(0, _farActive - 1);
        if (_farActive == 0)
        {
            // Nothing in the filter's reach has played: nothing to cancel.
            Array.Clear(EchoEstimate);
            return;
        }

        // Echo estimate: the sum over the partitions of W_k X_k, back to time; its last half is this block.
        Array.Clear(_re);
        Array.Clear(_im);
        for (int k = 0; k < Partitions; k++)
        {
            float[] xr = _xr[(_head + k) % Partitions];
            float[] xi = _xi[(_head + k) % Partitions];
            float[] wr = _wr[k];
            float[] wi = _wi[k];
            for (int f = 0; f < Bins; f++)
            {
                _re[f] += wr[f] * xr[f] - wi[f] * xi[f];
                _im[f] += wr[f] * xi[f] + wi[f] * xr[f];
            }
        }

        Mirror(_re, _im);
        _fft.Inverse(_re, _im);
        double micEnergy = 0, errorEnergy = 0, echoEnergy = 0;
        Span<float> error = stackalloc float[N];
        for (int i = 0; i < N; i++)
        {
            float y = _re[N + i];
            EchoEstimate[i] = y;
            error[i] = mic[i] - y;
            micEnergy += mic[i] * mic[i];
            errorEnergy += error[i] * error[i];
            echoEnergy += y * y;
        }

        // A filter that makes things louder is not used; one that stays that way is cleared.
        bool useful = errorEnergy <= micEnergy;
        _diverged = errorEnergy > 4 * micEnergy + 1e-9 ? _diverged + 1 : 0;
        if (_diverged >= DivergedBlocks)
        {
            Reset();
            return;
        }

        float erle = (float)(micEnergy / (errorEnergy + 1e-12));
        bool doubleTalk = _erle > Converged && errorEnergy > echoEnergy;
        if (!doubleTalk)
        {
            _erle = 0.95f * _erle + 0.05f * Math.Min(erle, 1000f);
        }

        if (useful)
        {
            error.CopyTo(mic);
        }
        else
        {
            Array.Clear(EchoEstimate);
        }

        // Learn: the error's spectrum from [zeros, error], normalised by the far end's power over the partitions.
        for (int i = 0; i < N; i++)
        {
            _re[i] = 0f;
            _re[N + i] = error[i];
        }

        Array.Clear(_im);
        _fft.Forward(_re, _im);
        Array.Clear(_power);
        for (int k = 0; k < Partitions; k++)
        {
            float[] xr = _xr[k];
            float[] xi = _xi[k];
            for (int f = 0; f < Bins; f++)
            {
                _power[f] += xr[f] * xr[f] + xi[f] * xi[f];
            }
        }

        float step = doubleTalk ? DoubleTalkStep : Step;
        for (int f = 0; f < Bins; f++)
        {
            _power[f] = step / (_power[f] + PowerFloor);
        }

        for (int k = 0; k < Partitions; k++)
        {
            float[] xr = _xr[(_head + k) % Partitions];
            float[] xi = _xi[(_head + k) % Partitions];
            float[] wr = _wr[k];
            float[] wi = _wi[k];
            for (int f = 0; f < Bins; f++)
            {
                // conj(X) E
                float gr = xr[f] * _re[f] + xi[f] * _im[f];
                float gi = xr[f] * _im[f] - xi[f] * _re[f];
                wr[f] += _power[f] * gr;
                wi[f] += _power[f] * gi;
            }
        }

        Constrain(_constrain);
        _constrain = (_constrain + 1) % Partitions;
    }

    /// <summary>Keeps one partition's impulse response in its first half (the overlap-save constraint).</summary>
    private void Constrain(int k)
    {
        Array.Copy(_wr[k], _re, Bins);
        Array.Copy(_wi[k], _im, Bins);
        Mirror(_re, _im);
        _fft.Inverse(_re, _im);
        for (int i = N; i < M; i++)
        {
            _re[i] = 0f;
        }

        Array.Clear(_im);
        _fft.Forward(_re, _im);
        Array.Copy(_re, _wr[k], Bins);
        Array.Copy(_im, _wi[k], Bins);
    }

    /// <summary>Fills the upper bins with the conjugates of the lower ones (a real signal's spectrum).</summary>
    private static void Mirror(float[] re, float[] im)
    {
        im[0] = 0f;
        im[N] = 0f;
        for (int f = 1; f < N; f++)
        {
            re[M - f] = re[f];
            im[M - f] = -im[f];
        }
    }
}
