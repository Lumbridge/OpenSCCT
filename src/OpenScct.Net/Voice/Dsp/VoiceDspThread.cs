using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OpenScct.Net.Voice.Dsp;

/// <summary>
/// The voice thread: runs a <see cref="VoiceDsp"/> off the game's main thread, registered as an MMCSS "Pro Audio" task
/// (<see cref="ProAudioThread"/>). Microphone frames and far-end frames go in through one queue, in the order the
/// owner submitted them, so the echo canceller sees them as the main thread did; cleaned microphone frames come back
/// through <see cref="TryTake"/>. Buffers are pooled: no allocation per frame once running.
/// </summary>
internal sealed class VoiceDspThread : IDisposable
{
    private readonly VoiceDsp _dsp;
    private readonly VoiceSettings _settings;
    private readonly ConcurrentQueue<(float[] Samples, bool Far)> _jobs = new();
    private readonly ConcurrentQueue<float[]> _done = new();
    private readonly ConcurrentQueue<float[]> _free = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _thread;
    private volatile bool _stop;
    private long _completed;
    private long _submitted;

    public VoiceDspThread(VoiceDsp dsp, VoiceSettings settings)
    {
        _dsp = dsp;
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "OpenSCCT voice" };
        _thread.Start();
    }

    /// <summary>True when every job submitted has been done (the DSP is free for the owner's thread).</summary>
    public bool Idle => Interlocked.Read(ref _completed) == _submitted && _done.IsEmpty;

    /// <summary>A microphone frame (<see cref="VoiceK.FrameSamples"/> samples).</summary>
    public void SubmitMicrophone(ReadOnlySpan<float> frame) => Submit(frame, far: false);

    /// <summary>Far-end audio, at most <see cref="VoiceK.FrameSamples"/> samples a call.</summary>
    public void SubmitFar(ReadOnlySpan<float> samples)
    {
        while (samples.Length > 0)
        {
            int take = Math.Min(samples.Length, VoiceK.FrameSamples);
            Submit(samples[..take], far: true);
            samples = samples[take..];
        }
    }

    /// <summary>The next cleaned microphone frame, copied into <paramref name="frame"/>.</summary>
    public bool TryTake(Span<float> frame)
    {
        if (!_done.TryDequeue(out float[]? buffer))
        {
            return false;
        }

        buffer.AsSpan(0, frame.Length).CopyTo(frame);
        _free.Enqueue(buffer);
        return true;
    }

    public void Dispose()
    {
        _stop = true;
        _signal.Set();
        if (_thread.Join(500))
        {
            // Only once the thread has left: it waits on the signal.
            _signal.Dispose();
        }
    }

    private void Submit(ReadOnlySpan<float> samples, bool far)
    {
        // Whole frames use the pool; a shorter far-end piece (rare) gets its own array.
        float[] buffer = samples.Length != VoiceK.FrameSamples ? new float[samples.Length]
            : _free.TryDequeue(out float[]? pooled) ? pooled : new float[VoiceK.FrameSamples];
        samples.CopyTo(buffer);
        _jobs.Enqueue((buffer, far));
        _submitted++;
        _signal.Set();
    }

    private void Run()
    {
        using ProAudioThread.Registration mmcss = ProAudioThread.Enter();
        while (!_stop)
        {
            _signal.WaitOne(100);
            while (!_stop && _jobs.TryDequeue(out (float[] Samples, bool Far) job))
            {
                if (job.Far)
                {
                    _dsp.SubmitFarEnd(job.Samples);
                    if (job.Samples.Length == VoiceK.FrameSamples)
                    {
                        _free.Enqueue(job.Samples);
                    }
                }
                else
                {
                    _dsp.Process(_settings, job.Samples);
                    _done.Enqueue(job.Samples);
                }

                Interlocked.Increment(ref _completed);
            }
        }
    }
}
