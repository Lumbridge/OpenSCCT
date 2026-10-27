using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScctGame.Match;
using OpenScctGame.Smoke;

namespace OpenScctGame.Voice;

/// <summary>
/// <c>--svm-smoke=voice</c>: voice chat end to end in one process, with a synthetic tone for a microphone. A 440 Hz tone
/// plays on the muted capture bus; player 1's <see cref="VoiceChat"/> captures it through the bus's
/// <see cref="AudioEffectCapture"/>, encodes it and sends it over a loopback transport to a <see cref="VoiceRelay"/>,
/// which routes it to player 2 (same team) and not to player 3 (the other team). Player 2's chat decodes it through
/// the jitter buffer into its talker's generator on the (muted) voice bus. When the audio driver does not mix (some
/// headless drivers), the tone is fed to player 1's client directly and the detail says so.
/// <para>Passes when player 2 hears a 440 Hz tone, player 3 hears nothing, the speaking indicator goes on and off with
/// push to talk, and the uplink stays within 69 bytes per 20 ms frame.</para>
/// </summary>
public sealed class VoiceSmoke : SmokeTest
{
    public const string TestName = "voice";

    private const double ToneHz = 440;
    private const double TalkSeconds = 3.0;
    private const double CaptureGraceSeconds = 1.0;
    private const double ReleaseSeconds = 0.8;

    private readonly StopwatchClock _clock = new();
    private readonly List<float> _heard = new();
    private readonly List<string> _notes = new();
    private readonly LoopbackNetwork _network = new();
    private VoiceTransport? _serverTransport;
    private VoiceRelay? _relay;
    private StaticVoiceDirectory? _directory;
    private VoiceTransport? _transportA;
    private VoiceTransport? _transportB;
    private VoiceTransport? _transportC;
    private VoiceChat? _a;
    private VoiceChat? _b;
    private VoiceClient? _c;
    private VoiceIndicator? _indicatorB;
    private AudioStreamPlayer? _tonePlayer;
    private AudioStreamGeneratorPlayback? _tone;
    private double _tonePhase;
    private double _directSeconds;
    private bool _direct;
    private bool _sawSpeaking;
    private bool _sawSilent;
    private bool _indicatorShowed;
    private int _phase;
    private double _phaseStart;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        switch (_phase)
        {
            case 0:
                Setup(context);
                _phase = 1;
                _phaseStart = context.ElapsedSeconds;
                return SmokeResult.Running;
            case 1:
                Pump(context);
                if (!_direct && context.ElapsedSeconds - _phaseStart > CaptureGraceSeconds && _a!.Audio!.CapturedFrames == 0)
                {
                    _direct = true;
                    _notes.Add($"capture bus silent on the '{AudioServer.OutputDevice}' driver: tone fed to the client directly");
                }

                if (context.ElapsedSeconds - _phaseStart < TalkSeconds)
                {
                    return SmokeResult.Running;
                }

                _a!.Client!.TalkKey = false;
                _phase = 2;
                _phaseStart = context.ElapsedSeconds;
                return SmokeResult.Running;
            case 2:
                Pump(context);
                return context.ElapsedSeconds - _phaseStart < ReleaseSeconds ? SmokeResult.Running : Finish();
            default:
                return SmokeResult.Fail("unexpected phase");
        }
    }

    private void Setup(SmokeContext context)
    {
        _serverTransport = new VoiceTransport(_network.Bind("voice-server"));
        _directory = new StaticVoiceDirectory { Rules = VoiceRules.Default };
        _directory.Set(VoiceMember.Create(1, 0));
        _directory.Set(VoiceMember.Create(2, 0));
        _directory.Set(VoiceMember.Create(3, 1));
        _relay = new VoiceRelay(_serverTransport, _directory, _clock);

        Node root = context.Tree.Root;
        _transportA = new VoiceTransport(_network.Bind("player-1"));
        _a = new VoiceChat { Name = "VoiceChat1", ReadKeys = false };
        _a.Settings.PushToTalk = true;
        root.AddChild(_a);
        _a.Connect(_transportA, _transportA.Connect("voice-server"), _clock).LocalTalker = 1;
        _a.Client!.TalkKey = true;

        _transportB = new VoiceTransport(_network.Bind("player-2"));
        _b = new VoiceChat { Name = "VoiceChat2", ReadKeys = false };
        root.AddChild(_b);
        _b.Connect(_transportB, _transportB.Connect("voice-server"), _clock).LocalTalker = 2;
        _b.Audio!.Pacing = VoicePlaybackPacing.Clock;
        _b.Audio.FrameTap = (talker, frame) =>
        {
            if (talker == 1)
            {
                for (int i = 0; i < frame.Length; i++)
                {
                    _heard.Add(frame[i]);
                }
            }
        };
        _b.SpeakerChanged += s =>
        {
            if (s.Talker == 1 && !s.IsLocal)
            {
                _sawSpeaking |= s.Speaking;
                _sawSilent |= _sawSpeaking && !s.Speaking;
            }
        };
        _indicatorB = new VoiceIndicator { Name = "VoiceIndicator2", Chat = _b };
        root.AddChild(_indicatorB);

        _transportC = new VoiceTransport(_network.Bind("player-3"));
        _c = new VoiceClient(new VoiceSettings(), _clock) { LocalTalker = 3 };
        _c.Connect(_transportC, _transportC.Connect("voice-server"));

        VoiceAudio.MuteOutput(true);

        // The synthetic microphone: a tone generator playing on the capture bus.
        var generator = new AudioStreamGenerator
        {
            MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
            MixRate = VoiceK.SampleRate,
            BufferLength = 0.25f,
        };
        _tonePlayer = new AudioStreamPlayer { Name = "VoiceSmokeTone", Stream = generator, Bus = VoiceAudio.CaptureBus };
        root.AddChild(_tonePlayer);
        _tonePlayer.Play();
        _tone = (AudioStreamGeneratorPlayback)_tonePlayer.GetStreamPlayback();
        _notes.Add($"mix rate {AudioServer.GetMixRate():0} Hz, output '{AudioServer.OutputDevice}'");
    }

    /// <summary>One frame: feed the tone, move the datagrams, run the client that has no node.</summary>
    private void Pump(SmokeContext context)
    {
        FeedTone(context);
        _transportA!.DrainVoiceOnly();
        _serverTransport!.DrainVoiceOnly();
        _transportB!.DrainVoiceOnly();
        _transportC!.DrainVoiceOnly();
        _c!.Update();
        if (_b!.Speakers.Count > 0 && _indicatorB!.Text.Contains("Player 1", StringComparison.Ordinal))
        {
            _indicatorShowed = true;
        }
    }

    private void FeedTone(SmokeContext context)
    {
        // Keep the generator about 100 ms ahead.
        var frame = new Vector2[256];
        while (_tone is not null && _tone.GetFramesAvailable() > (int)(0.15 * VoiceK.SampleRate))
        {
            for (int i = 0; i < frame.Length; i++)
            {
                float s = 0.3f * (float)Math.Sin(_tonePhase);
                _tonePhase += 2 * Math.PI * ToneHz / VoiceK.SampleRate;
                frame[i] = new Vector2(s, s);
            }

            _tone.PushBuffer(frame);
        }

        if (!_direct)
        {
            return;
        }

        // Direct feed: the samples the bus would have given, at 48 kHz, paced by the wall clock.
        double due = context.ElapsedSeconds - _phaseStart;
        int samples = (int)((due - _directSeconds) * VoiceK.SampleRate);
        if (samples <= 0)
        {
            return;
        }

        var block = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            block[i] = 0.3f * (float)Math.Sin(_tonePhase);
            _tonePhase += 2 * Math.PI * ToneHz / VoiceK.SampleRate;
        }

        _a!.Client!.SetCaptureRate(VoiceK.SampleRate);
        _a.Client.SubmitCapture(block);
        _directSeconds += samples / (double)VoiceK.SampleRate;
    }

    private SmokeResult Finish()
    {
        try
        {
            VoiceClient a = _a!.Client!;
            VoiceClient b = _b!.Client!;
            VoiceRelay relay = _relay!;
            _notes.Add($"captured {_a.Audio!.CapturedFrames} bus frames, sent {a.Stats.FramesSent} frames ({a.Stats.BytesSent} bytes, largest Opus {a.Stats.LargestPacket})");
            if (a.Stats.FramesSent < 50)
            {
                return SmokeResult.Fail("player 1 sent fewer than 50 frames; " + string.Join("; ", _notes));
            }

            double upRate = relay.BytesOf(1).Up / TalkSeconds;
            _notes.Add($"uplink {upRate:0} B/s (budget {VoiceWire.MaxAudioDatagramBytes * VoiceK.FramesPerSecond} B/s)");
            if (a.Stats.LargestPacket > VoiceK.MaxOpusPacketBytes || upRate > VoiceWire.MaxAudioDatagramBytes * VoiceK.FramesPerSecond * 1.1)
            {
                return SmokeResult.Fail("uplink over budget; " + string.Join("; ", _notes));
            }

            if (_c!.Stats.PacketsReceived != 0 || _c.Talkers.Count != 0)
            {
                return SmokeResult.Fail($"player 3 (other team) received {_c.Stats.PacketsReceived} packets");
            }

            VoiceTalker? talker = b.FindTalker(1);
            if (talker is null || _heard.Count < VoiceK.SampleRate)
            {
                return SmokeResult.Fail($"player 2 heard {_heard.Count} samples; " + string.Join("; ", _notes));
            }

            float[] heard = _heard.ToArray();
            ReadOnlySpan<float> steady = heard.AsSpan(heard.Length / 4, heard.Length / 2);
            double hz = ZeroCrossingHz(steady);
            double rms = Rms(steady);
            VoiceJitterStats js = talker.Buffer.Stats;
            _notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"player 2 heard {heard.Length / VoiceK.FrameSamples} frames at {hz:0.0} Hz, rms {rms:0.000}; jitter played {js.Played}, fec {js.Fec}, concealed {js.Concealed} (stretched {js.Stretched}), late {js.Late}, underruns {js.Underruns}; output pushed {_b.Audio!.PlayedFrames} frames"));
            if (Math.Abs(hz - ToneHz) > 20 || rms < 0.05)
            {
                return SmokeResult.Fail("player 2 did not hear the tone; " + string.Join("; ", _notes));
            }

            if (!_sawSpeaking || !_sawSilent || !_indicatorShowed)
            {
                return SmokeResult.Fail($"speaking indicator: on {_sawSpeaking}, off {_sawSilent}, overlay {_indicatorShowed}; " + string.Join("; ", _notes));
            }

            _notes.Add($"relay down {relay.Stats.DownPackets} packets, unrouted {relay.Stats.Unrouted}");
            return SmokeResult.Pass(string.Join("; ", _notes));
        }
        finally
        {
            Teardown();
        }
    }

    private void Teardown()
    {
        _tonePlayer?.Stop();
        _tonePlayer?.QueueFree();
        _a?.QueueFree();
        _b?.QueueFree();
        _indicatorB?.QueueFree();
        _c?.Dispose();
        VoiceAudio.MuteOutput(false);
        _transportA?.Dispose();
        _transportB?.Dispose();
        _transportC?.Dispose();
        _serverTransport?.Dispose();
    }

    private static double ZeroCrossingHz(ReadOnlySpan<float> samples)
    {
        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] < 0f) != (samples[i] < 0f))
            {
                crossings++;
            }
        }

        return crossings / 2.0 * VoiceK.SampleRate / samples.Length;
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (float s in samples)
        {
            sum += (double)s * s;
        }

        return samples.Length == 0 ? 0 : Math.Sqrt(sum / samples.Length);
    }
}
