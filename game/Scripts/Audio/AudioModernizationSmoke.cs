using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>Actual Godot mixer coverage using generated audio; no retail assets or manual setup required.</summary>
public sealed class AudioModernizationSmoke : SmokeTest
{
    public const string TestName = "audio-modernization";
    private const string Cue = "OpenScctProbe.Cue", Loop = "OpenScctProbe.Loop";
    private static readonly string[] Labels = ["speakers-front", "headphones-front", "headphones-left",
        "headphones-right", "headphones-back", "headphones-above", "clear", "glass", "stone"];
    private readonly List<object> _evidence = [];
    private readonly List<(double Left, double Right)> _energy = [];
    private readonly ProbeWorld _world = new();
    private SoundSystem? _sound;
    private AudioLibrary? _library;
    private AudioMixCapture? _capture;
    private Camera3D? _camera;
    private string _folder = "";
    private int _phase, _initialReads;
    private double _started, _last;
    private bool _playing;
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioPeaks.Quiet();
        if (_sound is null)
        {
            _folder = System.Environment.GetEnvironmentVariable("SVM_AUDIO_CAPTURE_DIR") ?? Path.Combine(Path.GetTempPath(), "openscct-audio-modernization");
            Directory.CreateDirectory(_folder);
            _library = Fixture(_folder);
            _library.PrepareSession();
            _initialReads = _library.FilesRead;
            _camera = new Camera3D { Current = true };
            context.Tree.Root.AddChild(_camera);
            _sound = new SoundSystem();
            context.Tree.Root.AddChild(_sound);
            _sound.SetProcess(false);
            _sound.Bind(_library, _world);
            _sound.SetListener(default, default);
            _capture = new AudioMixCapture(ReverbBuses.WorldBus);
            _started = _last = context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        double delta = context.ElapsedSeconds - _last;
        _last = context.ElapsedSeconds;
        _sound.Update(delta);
        _capture!.Sample();
        double elapsed = context.ElapsedSeconds - _started;
        if (_phase < Labels.Length)
        {
            if (!_playing)
            {
                if (elapsed < 0.15) return SmokeResult.Running;
                _capture.Reset();
                _sound.Headphones = _phase is > 0 and < 6;
                _world.Material = _phase == 7 ? 3 : _phase == 8 ? 9 : -1;
                _world.Revision++;
                _world.Source = _phase switch
                {
                    2 => new FVector(0, -50, 0), 3 => new FVector(0, 50, 0),
                    4 => new FVector(-50, 0, 0), 5 => new FVector(0, 0, 50), _ => new FVector(50, 0, 0),
                };
                if (!_sound.PlaySound(99, Cue, 0, false, _world.Source, 1, 100, 1))
                    return Finish(SmokeResult.Fail("Synthetic source did not start."));
                _playing = true; _started = context.ElapsedSeconds;
                return SmokeResult.Running;
            }
            if (elapsed < 0.7) return SmokeResult.Running;
            double left = 0, right = 0;
            foreach (Vector2 sample in _capture.Samples) { left += sample.X * sample.X; right += sample.Y * sample.Y; }
            if (left + right <= 1e-6) return Finish(SmokeResult.Fail("Actual mixer produced no PCM for " + Labels[_phase]));
            if (_capture.DiscardedFrames != 0) return Finish(SmokeResult.Fail("Mixer evidence capture lost frames."));
            _energy.Add((left, right));
            _evidence.Add(new { label = Labels[_phase], left, right,
                output = _capture.Save(Path.Combine(_folder, Labels[_phase] + ".wav")) });
            _sound.StopAll(); _playing = false; _started = context.ElapsedSeconds; _phase++;
            return SmokeResult.Running;
        }
        if (_phase == Labels.Length)
        {
            if (_energy[2].Left <= _energy[2].Right * 1.5 || _energy[3].Right <= _energy[3].Left * 1.5)
                return Finish(SmokeResult.Fail("Headphone axes are wrong or rendered audio was panned twice."));
            double clear = _energy[6].Left + _energy[6].Right, glass = _energy[7].Left + _energy[7].Right,
                stone = _energy[8].Left + _energy[8].Right;
            if (!(stone < glass && glass < clear * 0.95))
                return Finish(SmokeResult.Fail($"Material transmission ordering failed: clear={clear}, glass={glass}, stone={stone}"));
            _world.Material = -1; _world.Revision++;
            _sound.Headphones = true;
            _world.Ambient = true;
            _sound.Update(0.016);
            if (_sound.ActiveVoices != 24) return Finish(SmokeResult.Fail("Ambient voices did not leave eight cue channels available."));
            for (int i = 0; i < 9; i++)
                if (!_sound.PlaySound(0, Cue, 0, false, default, 1, 100, 1))
                    return Finish(SmokeResult.Fail("Ambience starved an immediate cue."));
            if (_sound.ActiveVoices != 32 || _sound.VirtualVoices == 0)
                return Finish(SmokeResult.Fail("Loop was not virtualized when a critical cue reclaimed its channel."));
            _started = context.ElapsedSeconds; _phase++;
            return SmokeResult.Running;
        }
        if (_phase == Labels.Length + 1)
        {
            if (elapsed < 0.25) return SmokeResult.Running;
            _sound.StopSound(0, Cue);
            _sound.Update(0.016);
            if (_sound.VirtualPromotions == 0 || !_sound.Starts.Any(s => s.Ambient && s.Offset > 0.1))
                return Finish(SmokeResult.Fail($"Virtual loop restarted: promotions={_sound.VirtualPromotions}, offset={_sound.Starts.Max(s => s.Offset)}."));
            if (_library!.FilesRead != _initialReads || _library.RuntimeFilesRead != 0)
                return Finish(SmokeResult.Fail("A manifest-preloaded cue performed a gameplay file read."));
            if (_library.CachedBytes > _library.CacheBudgetBytes)
                return Finish(SmokeResult.Fail("Audio cache exceeded its budget."));
            _sound.SetDemoSeek(true);
            _sound.AdvanceDemoVoices(0.1);
            _sound.SetDemoSeek(false);
            _world.Ambient = false;
            _sound.StopAll();
            _sound.Update(0.016);
            if (_sound.ActiveVoices != 0 || _sound.VirtualVoices != 0)
                return Finish(SmokeResult.Fail("StopAll leaked a physical or virtual source."));
            if (CheckCache(context.Tree.Root, _folder) is { } cacheProblem)
                return Finish(SmokeResult.Fail(cacheProblem));
            string device = AudioServer.OutputDevice;
            AudioOutput.Configure("Speakers", "OpenSCCT unavailable test device");
            if (AudioServer.OutputDevice != "Default")
                return Finish(SmokeResult.Fail("Missing output device did not fall back to system default."));
            AudioOutput.Configure("Speakers", device == "Default" ? "" : device);
            File.WriteAllText(Path.Combine(_folder, "mixer-evidence.json"), JsonSerializer.Serialize(new
            {
                captures = _evidence, native = _sound.Buses!.CaptureSpatializerMetrics(),
                _sound.StolenVoices, _sound.VirtualPromotions, _sound.OcclusionQueries,
                _sound.MaxUpdateMilliseconds, _library.CachedBytes, _library.FilesRead, _library.RuntimeFilesRead,
                rate = AudioServer.GetMixRate(), adapterLatencyMs = 128000 / AudioServer.GetMixRate(),
            }, new JsonSerializerOptions { WriteIndented = true }));
            return Finish(SmokeResult.Pass("Binaural axes/PCM, material transmission, protected cues, virtual loop phase, preload/cache, demo lifecycle; " + _folder));
        }
        return SmokeResult.Running;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _capture?.Dispose(); _capture = null;
        _sound?.StopAll(); _sound?.Free(); _sound = null;
        _camera?.Free(); _camera = null;
        _library?.ReleaseStreams();
        return result;
    }

    internal static AudioLibrary Fixture(string folder)
    {
        int rate = (int)AudioServer.GetMixRate(), count = rate / 2;
        using (var wav = new BinaryWriter(File.Create(Path.Combine(folder, "noise.wav"))))
        {
            wav.Write("RIFF"u8); wav.Write(36 + count * 2); wav.Write("WAVEfmt "u8); wav.Write(16);
            wav.Write((ushort)1); wav.Write((ushort)1); wav.Write(rate); wav.Write(rate * 2); wav.Write((ushort)2); wav.Write((ushort)16);
            wav.Write("data"u8); wav.Write(count * 2);
            var random = new Random(17);
            for (int i = 0; i < count; i++) wav.Write((short)(i > rate / 10 && i < rate * 0.3 ? random.Next(-3000, 3001) : 0));
        }
        object Definition(bool loop) => new { kind = "wave", flags = 0, volume = 1, radius = 100,
            loop, positional = true, file = "noise.wav", frames = count, duration = 0.5f };
        File.WriteAllText(Path.Combine(folder, "audio.json"), JsonSerializer.Serialize(new
        {
            format = "svm.audio/1", sounds = new Dictionary<string, object> { [Cue] = Definition(false), [Loop] = Definition(true) },
        }));
        return AudioLibrary.Load(folder, null);
    }

    private static string? CheckCache(Node parent, string folder)
    {
        string root = Path.Combine(folder, "cache-fixture");
        Directory.CreateDirectory(root);
        var definitions = new Dictionary<string, object>();
        foreach (string name in new[] { "A", "B", "C" })
        {
            File.Copy(Path.Combine(folder, "noise.wav"), Path.Combine(root, name + ".wav"), true);
            definitions[name] = new { kind = "wave", flags = 0, volume = 1, radius = 100,
                loop = false, positional = true, file = name + ".wav", frames = 24000, duration = 0.5f };
        }
        File.WriteAllText(Path.Combine(root, "audio.json"), JsonSerializer.Serialize(new { format = "svm.audio/1", sounds = definitions }));
        AudioLibrary cache = AudioLibrary.Load(root, null);
        cache.CacheBudgetBytes = new FileInfo(Path.Combine(root, "A.wav")).Length * 2;
        var player = new AudioStreamPlayer();
        parent.AddChild(player);
        try
        {
            AudioStream first = cache.Stream(cache.Find("A")!, false)!;
            player.Stream = first; player.Play();
            AudioStream second = cache.Stream(cache.Find("B")!, false)!;
            _ = cache.Stream(cache.Find("C")!, false);
            if (cache.CacheEvictions != 1 || cache.CachedBytes > cache.CacheBudgetBytes)
                return "Cache did not evict its oldest unpinned inactive stream.";
            if (!ReferenceEquals(first, cache.Stream(cache.Find("A")!, false)) || !player.Playing || second.GetLength() <= 0)
                return "Cache eviction disposed an active or externally owned stream.";
            _ = cache.Stream(cache.Find("B")!, false);
            if (cache.FilesRead != 4) return "Evicted stream was not reloaded correctly.";
        }
        finally { player.Stop(); player.Free(); cache.ReleaseStreams(); }
        return null;
    }

    private sealed class ProbeWorld : IAudioWorld
    {
        public FVector Source = new(50, 0, 0);
        public int Material = -1;
        public uint Revision;
        public bool Ambient;
        public uint AcousticRevision => Revision;
        public bool TryGetActorLocation(int actorId, out FVector at) { at = Source; return true; }
        public void CollectAmbient(List<AmbientSource> into, FVector listener)
        {
            if (Ambient) for (int i = 1; i <= 32; i++)
                into.Add(new AmbientSource(i, "loop" + i, Source, Loop, 64, 100, 64, false, 0, 64));
        }
        public string? ZoneIdAt(FVector location) => null;
        public int SurfaceOf(FVector start, FVector end) => 0;
        public int ZoneIndexAt(FVector location) => 1;
        public byte SoundOcclusionOf(int actorId) => 0;
        public bool OcclusionBetween(FVector listener, FVector source, out OcclusionHits hits)
        {
            hits = new(new FVector(10, 0, 0), new FVector(30, 0, 0), Material, Material, 1, 1);
            return Material >= 0;
        }
    }
}
