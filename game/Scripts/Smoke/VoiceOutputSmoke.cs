using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScctGame.Audio;
using OpenScctGame.Match;
using OpenScctGame.Voice;

namespace OpenScctGame.Smoke;

/// <summary>Measures real mixed voice PCM after the output-volume bus, without opening a microphone.</summary>
public sealed class VoiceOutputSmoke : SmokeTest
{
    public const string TestName = "voice-output";
    private const string ProbeBus = "SvmVoiceOutputProbe";
    private static readonly float[] Volumes = [1, 0.5f, 0.25f];
    private readonly StopwatchClock _clock = new();
    private readonly List<object> _evidence = [];
    private readonly double[] _energy = new double[Volumes.Length];
    private readonly byte[] _opus = new byte[VoiceK.MaxOpusPacketBytes];
    private VoiceClient? _client;
    private VoiceAudio? _audio;
    private AudioMixCapture? _capture;
    private string _oldSend = "Master";
    private string _folder = "";
    private double _started;
    private int _phase, _opusBytes;
    private ushort _sequence;
    private bool _capturing;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioPeaks.Quiet();
        if (_audio is null)
        {
            // Godot routes only to earlier buses. Create the probe before the voice bus if necessary.
            int probe = AudioServer.GetBusIndex(ProbeBus);
            if (probe < 0)
            {
                AudioServer.AddBus(1);
                AudioServer.SetBusName(1, ProbeBus);
                AudioServer.SetBusSend(1, "Master");
            }
            _client = new VoiceClient(new VoiceSettings { JitterFrames = 1 }, _clock);
            _audio = new VoiceAudio { Name = "VoiceOutputFixture", Client = _client };
            context.Tree.Root.AddChild(_audio);
            int voice = AudioServer.GetBusIndex(VoiceAudio.VoiceBus);
            _oldSend = AudioServer.GetBusSend(voice);
            AudioServer.SetBusSend(voice, ProbeBus);
            _capture = new AudioMixCapture(ProbeBus);
            _folder = System.Environment.GetEnvironmentVariable("SVM_AUDIO_CAPTURE_DIR") ??
                Path.Combine(Path.GetTempPath(), "openscct-voice-output");
            var samples = new short[VoiceK.FrameSamples];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (short)(8000 * Math.Sin(i * 2 * Math.PI * 400 / VoiceK.SampleRate));
            _opusBytes = new VoiceEncoder().Encode(samples, _opus);
            _client.SetVolume(1, 1);
            _started = Time.GetTicksMsec() / 1000.0;
        }

        _client!.Settings.OutputVolume = Volumes[_phase];
        VoiceAudio.SetVolume(Volumes[_phase]);
        VoiceTalker talker = _client.FindTalker(1)!;
        // Keep the real output fed, letting its clock pace decoding as in a live visit.
        while (talker.Buffer.Count < 6)
            talker.Buffer.Push(_sequence++, _sequence == 1 ? EVoiceFlags.TalkStart : EVoiceFlags.None,
                EVoiceChannel.All, _opus.AsSpan(0, _opusBytes));
        double elapsed = Time.GetTicksMsec() / 1000.0 - _started;
        if (!_capturing && elapsed >= 0.25)
        {
            _capture!.Reset();
            _capturing = true;
        }
        if (_capturing) _capture!.Sample();
        if (elapsed < 1.25) return SmokeResult.Running;

        _energy[_phase] = _capture!.Frames == 0 ? 0 : _capture.Energy() / _capture.Frames;
        _evidence.Add(new { volume = Volumes[_phase],
            output = _capture.Save(Path.Combine(_folder, $"voice-{Volumes[_phase]:0.00}.wav")) });
        if (++_phase < Volumes.Length)
        {
            _started = Time.GetTicksMsec() / 1000.0;
            _capturing = false;
            return SmokeResult.Running;
        }

        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "voice-output.json"), JsonSerializer.Serialize(_evidence,
            new JsonSerializerOptions { WriteIndented = true }));
        SmokeResult result;
        if (_energy[0] <= 0 || _energy[1] <= 0 || _energy[2] <= 0)
            result = SmokeResult.Fail("No mixed voice PCM captured; this check requires a real audio mixer.");
        else
        {
            double half = Math.Sqrt(_energy[1] / _energy[0]);
            double quarter = Math.Sqrt(_energy[2] / _energy[0]);
            result = Math.Abs(half - 0.5) <= 0.04 && Math.Abs(quarter - 0.25) <= 0.025
                ? SmokeResult.Pass($"Mixed voice amplitude at 50%={half:F3}, 25%={quarter:F3}; {_folder}")
                : SmokeResult.Fail($"Mixed voice amplitude at 50%={half:F3}, 25%={quarter:F3}; expected .5 and .25.");
        }
        _capture.Dispose();
        _audio.Client = null;
        _audio.QueueFree();
        _client.Dispose();
        AudioServer.SetBusSend(AudioServer.GetBusIndex(VoiceAudio.VoiceBus), _oldSend);
        return result;
    }
}
