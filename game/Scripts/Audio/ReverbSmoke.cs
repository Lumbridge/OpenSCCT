using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>Real mixer impulse, source-send normalization, zone/tail lifetime and flat/music isolation.</summary>
public sealed class ReverbSmoke : SmokeTest
{
    public const string TestName = "reverb";
    private SoundSystem? _sound;
    private AudioLibrary? _library;
    private AudioMixCapture? _input, _wet, _dry;
    private string _folder = "";
    private double _started;
    private double _lastStep;
    private int _phase;
    private int _busCount;
    private readonly ProbeWorld _world = new();
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioPeaks.Quiet();
        if (_sound is null)
        {
            _folder = System.Environment.GetEnvironmentVariable("SVM_REVERB_CAPTURE_DIR") ??
                Path.Combine(Path.GetTempPath(), "openscct-reverb");
            Directory.CreateDirectory(_folder);
            _library = Fixture(_folder);
            context.Tree.Root.AddChild(new Camera3D { Current = true });
            _sound = new SoundSystem();
            context.Tree.Root.AddChild(_sound);
            _sound.SetProcess(false);
            using (var sharedBuses = new ReverbBuses(null)) { }
            _busCount = AudioServer.BusCount;
            _sound.Bind(_library, _world);
            _sound.SetListener(default, default);
            _sound.Update(0);
            ReverbBuses buses = _sound.Buses!;
            if (!buses.BackendVersion.Contains("HRVB", StringComparison.OrdinalIgnoreCase))
                return Finish(SmokeResult.Fail("Actual Creative-compatible HRVB backend is required: " + buses.BackendVersion));
            if (_library.Map!.Zones["Pan"].Effect["ReflectionsPanX"] != 0.2f)
                return Finish(SmokeResult.Fail("Map reader dropped EAX pan vectors."));
            if (!ReferenceEquals(buses.CurrentEnvironment, EaxEnvironment.NullZoneEffect))
                return Finish(SmokeResult.Fail("Initial missing effect did not apply the NULL fallback."));
            _world.Zone = "Room";
            _sound.Update(0);
            EaxEnvironment room = buses.CurrentEnvironment!;
            ulong instance = AudioServer.GetBusEffectInstance(AudioServer.GetBusIndex(buses.EnvironmentBus!), 0).GetInstanceId();
            buses.SetEnvironment("Bad");
            if (!ReferenceEquals(room, buses.CurrentEnvironment)) return Finish(SmokeResult.Fail("Rejected original environment replaced the active room."));
            buses.SetEnvironment("NoFx");
            if (!ReferenceEquals(buses.CurrentEnvironment, EaxEnvironment.NullZoneEffect))
                return Finish(SmokeResult.Fail("Leaving an effect did not apply the original NULL fallback."));
            buses.SetEnvironment("Room");
            if (AudioServer.GetBusEffectInstance(AudioServer.GetBusIndex(buses.EnvironmentBus!), 0).GetInstanceId() != instance)
                return Finish(SmokeResult.Fail("Zone changes replaced the native effect instance instead of retaining its tail."));
            _input = new AudioMixCapture(buses.EnvironmentBus!, 0);
            _wet = new AudioMixCapture(buses.EnvironmentBus!);
            _dry = new AudioMixCapture(ReverbBuses.WorldBus);
            _started = context.ElapsedSeconds;
            _lastStep = context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        _sound.Update((float)(context.ElapsedSeconds - _lastStep));
        _lastStep = context.ElapsedSeconds;
        _input?.Sample(); _wet?.Sample(); _dry?.Sample();
        double elapsed = context.ElapsedSeconds - _started;
        if (_phase == 0)
        {
            if (elapsed < 0.3) return SmokeResult.Running;
            AudioServer.Lock();
            try { _input!.Reset(); _wet!.Reset(); _dry!.Reset(); }
            finally { AudioServer.Unlock(); }
            if (!_sound.PlaySound(2, "Audit.Impulse", 0, false, default, 1, 100, 1))
                return Finish(SmokeResult.Fail("Impulse voice did not start."));
            _sound.Update(0);
            _phase = 1;
            _started = context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        if (elapsed < 3.6) return SmokeResult.Running;
        if (_phase == 1)
        {
            float peak = Peak(_input!.Samples);
            float expected = 3277f / 32768;
            if (Math.Abs(peak / expected - 1) > 0.02)
                return Finish(SmokeResult.Fail($"Room send input {peak:F6}, expected unpanned source {expected:F6}."));
            int first = First(_input.Samples, 0.001f), wetFirst = First(_wet!.Samples, 0.000001f);
            int dryFirst = First(_dry!.Samples, 0.001f);
            if (dryFirst != first)
                return Finish(SmokeResult.Fail($"Dry/room source starts differ: dry {dryFirst}, room input {first}."));
            if (_wet!.Energy() <= 0 || _dry!.Energy() <= 0 || first < 0 || wetFirst <= first + 2)
                return Finish(SmokeResult.Fail("Missing wet/dry PCM or a dry impulse leaked into the wet-only effect."));
            var evidence = new { backend = _sound.Buses!.BackendVersion, rate = AudioServer.GetMixRate(),
                roomInputPeak = peak, expectedInputPeak = expected, inputFirst = first, dryFirst, wetFirst,
                input = _input.Save(Path.Combine(_folder, "godot-room-input.wav")),
                wet = _wet.Save(Path.Combine(_folder, "godot-room-wet.wav")),
                dry = _dry.Save(Path.Combine(_folder, "godot-room-dry.wav")) };
            File.WriteAllText(Path.Combine(_folder, "godot-room.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            _sound.StopAll();
            // A new environment instance isolates the next phase from the preceding room's tail.
            _input.Dispose(); _wet.Dispose(); _dry.Dispose();
            _sound.Bind(_library!, _world);
            _sound.Update(0);
            _wet = new AudioMixCapture(_sound.Buses!.EnvironmentBus!);
            _input = null; _dry = null;
            _sound.PlaySound(2, "Audit.Impulse", 0, false, default, 1, 100, 1, flat: true);
            _sound.PlayMusic("Audit.Impulse", 0);
            _phase = 2;
            _started = context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        if (_wet!.Energy() > 1e-15) return Finish(SmokeResult.Fail("Flat or music playback leaked into the room send."));
        string detail = $"{_sound.Buses!.BackendVersion}; wet-only PCM, unity unpanned send, retained zone effect, rejected update, NULL fallback, flat/music isolation";
        return Finish(SmokeResult.Pass(detail));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _sound?.StopAll();
        _input?.Dispose(); _wet?.Dispose(); _dry?.Dispose();
        _sound?.Buses?.Dispose();
        _library?.ReleaseStreams();
        if (result.State == SmokeState.Passed && AudioServer.BusCount != _busCount)
            return SmokeResult.Fail("Owned EAX buses were not released after the sound system retired.");
        return result;
    }

    private static float Peak(ReadOnlySpan<Vector2> frames)
    {
        float peak = 0;
        foreach (Vector2 frame in frames) peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
        return peak;
    }
    private static int First(ReadOnlySpan<Vector2> frames, float threshold)
    {
        for (int i = 0; i < frames.Length; i++) if (Math.Max(Math.Abs(frames[i].X), Math.Abs(frames[i].Y)) > threshold) return i;
        return -1;
    }

    private static AudioLibrary Fixture(string folder)
    {
        string shared = Path.Combine(folder, "fixture-shared"), map = Path.Combine(folder, "fixture-map");
        Directory.CreateDirectory(shared); Directory.CreateDirectory(Path.Combine(map, "audio"));
        int rate = (int)AudioServer.GetMixRate(), count = rate * 3;
        using (var wav = new BinaryWriter(File.Create(Path.Combine(shared, "impulse.wav"))))
        {
            wav.Write("RIFF"u8); wav.Write(36 + count * 2); wav.Write("WAVEfmt "u8); wav.Write(16);
            wav.Write((ushort)1); wav.Write((ushort)1); wav.Write(rate); wav.Write(rate * 2); wav.Write((ushort)2); wav.Write((ushort)16);
            wav.Write("data"u8); wav.Write(count * 2);
            for (int i = 0; i < count; i++) wav.Write((short)(i == rate / 4 ? 3277 : 0));
        }
        File.WriteAllText(Path.Combine(shared, "audio.json"), JsonSerializer.Serialize(new
        {
            format = "svm.audio/1", sounds = new Dictionary<string, object>
            {
                ["Audit.Impulse"] = new { kind = "wave", flags = 0, volume = 1, radius = 100, loop = false,
                    positional = true, file = "impulse.wav", frames = count, duration = 3 },
            },
        }));
        var parameters = new Dictionary<string, object> { ["DecayTime"] = 0.4f, ["RoomRolloffFactor"] = 0.1f };
        object Zone(string id, Dictionary<string, object>? p) => p is null ? new { id } : new { id, effect = new { @class = "Audit.Room", @params = p } };
        File.WriteAllText(Path.Combine(map, "audio/audio.json"), JsonSerializer.Serialize(new
        {
            format = "svm.mapaudio/1", actors = Array.Empty<object>(), sounds = new Dictionary<string, object>(),
            zones = new[] { Zone("Room", parameters), Zone("Bad", new() { ["EnvironmentDiffusion"] = 1.2f }),
                Zone("NoFx", null), Zone("Pan", new() { ["ReflectionsPan"] = new { X = 0.2f, Y = 0, Z = 0 } }) },
        }));
        return AudioLibrary.Load(shared, map);
    }

    private sealed class ProbeWorld : IAudioWorld
    {
        public string Zone { get; set; } = "NoFx";
        public bool TryGetActorLocation(int actorId, out FVector location) { location = default; return true; }
        public void CollectAmbient(List<AmbientSource> into, FVector listener) { }
        public string? ZoneIdAt(FVector location) => Zone;
        public int SurfaceOf(FVector start, FVector end) => 0;
        public int ZoneIndexAt(FVector location) => 1;
        public byte SoundOcclusionOf(int actorId) => AudioK.OcclusionNone;
        public bool OcclusionBetween(FVector listener, FVector source, out OcclusionHits hits) { hits = default; return false; }
    }
}
