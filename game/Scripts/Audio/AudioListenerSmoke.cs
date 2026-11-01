using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>Original local-pawn routing, tested through actual voices and captured mixed PCM.</summary>
public sealed class AudioListenerSmoke : SmokeTest
{
    public const string TestName = "audio-listener";
    private const string Clip = "Sound_Persos.vetements_materiel.RollGeneric2";
    // The original leaf overrides the call's 60 uu with 28 uu. R/(R+0.5*(300-R)).
    private const float RemoteFarGain = 28f / 164f;
    private readonly List<object> _evidence = [];
    private readonly List<double> _energy = [];
    private AudioLibrary? _library;
    private SoundSystem? _sound;
    private AudioMixCapture? _capture;
    private double _started;
    private int _phase;
    private string _folder = "";
    private SoundVoice _voice;
    private static readonly string[] Labels = ["own-near", "own-far", "remote-near", "remote-far"];
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioPeaks.Quiet();
        if (_sound is null)
        {
            string? map = MapBuilder.FindMapFolder("SteeE", context.App.Options.MapRoots);
            if (map is null) return SmokeResult.Fail("SteeE content is required to locate converted original audio.");
            _library = AudioLibrary.Load(AudioLibrary.SharedDirFor(map), null);
            // A real 3D audio mix needs an active viewport camera, even with an explicit AudioListener3D.
            context.Tree.Root.AddChild(new Camera3D { Current = true });
            _sound = new SoundSystem { LocalPawnId = 1, ViewActorId = 1 };
            context.Tree.Root.AddChild(_sound);
            _sound.Bind(_library, new SourceWorld());
            _sound.SetProcess(false);
            _capture = new AudioMixCapture(ReverbBuses.WorldBus);
            _folder = System.Environment.GetEnvironmentVariable("SVM_AUDIO_CAPTURE_DIR") ??
                Path.Combine(Path.GetTempPath(), "openscct-audio-listener");
            _started = context.ElapsedSeconds;
            return SmokeResult.Running;
        }

        _capture!.Sample();
        _sound.Update(1.0 / 60);
        double elapsed = context.ElapsedSeconds - _started;
        if ((_phase & 1) == 0 && elapsed >= 0.2)
        {
            int sample = _phase / 2;
            _capture.Reset();
            // Deliberately set the remote pawn as ViewActorId: spectating cannot convert it into a locally controlled source.
            _sound.ViewActorId = sample >= 2 ? 2 : 1;
            _sound.SetListener(sample % 2 == 0 ? new FVector(0, 0, 0) : new FVector(-300, 0, 0), default);
            if (!_sound.PlaySound(sample >= 2 ? 2 : 1, Clip, 0, false, default, 8, 60, 1, bone: "BSpine2"))
                return Finish(SmokeResult.Fail("Converted roll sample did not start."));
            _sound.Update(0);
            _voice = _sound.CaptureVoices().Single();
            float expectedDistance = sample == 3 ? 300 : 0;
            if (Math.Abs(_voice.Distance - expectedDistance) > 0.01f)
                return Finish(SmokeResult.Fail($"{Labels[sample]} source distance {_voice.Distance:F1}, expected {expectedDistance:F1}"));
            if (_voice.Radius != 28f) return Finish(SmokeResult.Fail($"Original roll leaf radius {_voice.Radius}, expected 28."));
            float gain = _voice.SourceVolume * (sample == 3 ? RemoteFarGain : 1f);
            if (Math.Abs(_voice.AppliedGain - gain) > 0.0001f)
                return Finish(SmokeResult.Fail($"{Labels[sample]} mixer gain {_voice.AppliedGain:F4}, expected {gain:F4}"));
            if (sample == 1)
            {
                // Native sources were rebound to the controller at start. A possession/view change must not
                // leave a still-playing roll at the old pawn or make the old bone attachment reappear.
                _sound.LocalPawnId = 0;
                _sound.ViewActorId = 2;
                _sound.SetListener(new FVector(-600, 0, 0), default);
                _sound.Update(0);
                SoundVoice carried = _sound.CaptureVoices().Single();
                if (!carried.AtListener || carried.Distance != 0 || carried.Pitch != 1)
                    return Finish(SmokeResult.Fail("A local voice lost its listener binding after the view changed."));
                _sound.LocalPawnId = 1;
                _sound.ViewActorId = 1;
                _sound.SetListener(new FVector(-300, 0, 0), default);
                _sound.Update(0);
            }
            _phase++;
            _started = context.ElapsedSeconds;
        }
        else if ((_phase & 1) != 0 && elapsed >= 1.15)
        {
            int sample = _phase / 2;
            _energy.Add(_capture.Energy());
            _evidence.Add(new { label = Labels[sample], voice = _voice,
                output = _capture.Save(Path.Combine(_folder, Labels[sample] + ".wav")) });
            _sound.StopAll();
            _phase++;
            _started = context.ElapsedSeconds;
            if (_phase == 8)
            {
                File.WriteAllText(Path.Combine(_folder, "report.json"), JsonSerializer.Serialize(_evidence,
                    new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                if (_energy.Any(e => e <= 0)) return Finish(SmokeResult.Fail("No PCM output captured; a real audio mixer is required."));
                double own = Math.Sqrt(_energy[1] / _energy[0]);
                double remote = Math.Sqrt(_energy[3] / _energy[2]);
                if (Math.Abs(own - 1) > 0.08 || Math.Abs(remote - RemoteFarGain) > 0.04)
                    return Finish(SmokeResult.Fail($"Mixed PCM amplitude ratios own={own:F3}, remote={remote:F3}; expected 1 and {RemoteFarGain:F3}."));
                return Finish(SmokeResult.Pass($"Original roll clip: own PCM ratio={own:F3}, remote={remote:F3}; {_folder}"));
            }
        }
        return SmokeResult.Running;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _sound?.StopAll();
        _capture?.Dispose();
        _library?.ReleaseStreams();
        return result;
    }

    private sealed class SourceWorld : IAudioWorld
    {
        public bool TryGetActorLocation(int actorId, out FVector location) { location = default; return true; }
        public bool TryGetBoneLocation(int actorId, Name bone, out FVector location) { location = default; return true; }
        public void CollectAmbient(List<AmbientSource> into) { }
        public string? ZoneIdAt(FVector location) => null;
        public int SurfaceOf(FVector start, FVector end) => 0;
        public int ZoneIndexAt(FVector location) => 0;
        public byte SoundOcclusionOf(int actorId) => AudioK.OcclusionNone;
        public bool OcclusionBetween(FVector listener, FVector source, out OcclusionHits hits) { hits = default; return false; }
    }
}
