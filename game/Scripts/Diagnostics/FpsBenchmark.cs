using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Environment = System.Environment;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render.Golden;

namespace OpenScctGame.Diagnostics;

/// <summary>Opt-in, windowed FPS capture of recorded original-game cameras through the normal play path.</summary>
public partial class FpsBenchmark : Node
{
    public static bool Wanted => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SVM_BENCHMARK"));

    private readonly List<GoldenShot> _shots = [];
    private readonly List<object> _results = [];
    private readonly List<Frame> _frames = new(65536);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private PlayRoot? _play;
    private int _index = -1;
    private double _start;
    private long _last;
    private long _allocated;
    private TimeSpan _pause;
    private readonly int[] _collections = new int[3];
    private string _output = "";
    private double _seconds = 10;
    private double _warmup = 3;
    private bool _done;
    private readonly bool _gameplay = Environment.GetEnvironmentVariable("SVM_BENCHMARK_GAMEPLAY") == "1";
    private readonly bool _profiling = Environment.GetEnvironmentVariable("SVM_BENCHMARK_PROFILING") == "1";
    private readonly record struct Frame(double Ms, double SimMs, double ViewMs, double RenderCpuMs, double GpuMs);

    public static void Configure(Viewport viewport)
    {
        SettingsStore.Isolated = true;
        var settings = new GameSettings { FpsSpy = 0, FpsMerc = 0, VSync = false, ThrottleStop = true };
        SettingsStore.Save(settings);
        SettingsApplier.ApplyPacing(settings);
        SettingsApplier.ApplyRendering(settings, viewport);
        SettingsApplier.ApplyAudio(settings);
    }

    public override void _Ready()
    {
        ProcessPriority = 1000;
        if (DisplayServer.GetName() == "headless")
        {
            Fail("FPS benchmarks require a real renderer");
            return;
        }

        try
        {
            _shots.AddRange(GoldenShots.Load(Environment.GetEnvironmentVariable("SVM_BENCHMARK")!));
            _output = Path.GetFullPath(Environment.GetEnvironmentVariable("SVM_BENCHMARK_OUT") ?? "benchmark");
            if (double.TryParse(Environment.GetEnvironmentVariable("SVM_BENCHMARK_SECONDS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds > 0)
                _seconds = seconds;
            if (double.TryParse(Environment.GetEnvironmentVariable("SVM_BENCHMARK_WARMUP"), NumberStyles.Float, CultureInfo.InvariantCulture, out double warmup) && warmup >= 0)
                _warmup = warmup;
            if (_shots.Count == 0) throw new InvalidDataException("No camera records");
            if (_shots.Any(s => s.Width != _shots[0].Width || s.Height != _shots[0].Height))
                throw new InvalidDataException("All benchmark cameras must have the same resolution");
            Directory.CreateDirectory(_output);
            DisplayServer.WindowSetSize(new Vector2I(_shots[0].Width, _shots[0].Height));
            GetTree().Root.Size = new Vector2I(_shots[0].Width, _shots[0].Height);
            if (_profiling) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            Fail(error.Message);
        }
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        if (_clock.Elapsed.TotalSeconds > 300 + _shots.Count * (_seconds + _warmup))
        {
            Fail("Timed out waiting for benchmark play scene");
            return;
        }

        _play ??= GetTree().CurrentScene as PlayRoot;
        if (_play?.LoadError is { } error) { Fail(error); return; }
        if (_play is not { IsLoaded: true, Session: { LocalPawn: not null } session }) return;
        if (session.ServerFailure is { } failure) { Fail(failure.Message); return; }
        if (_play.Controls is { } controls) controls.Enabled = false;
        // Environment/render comparison: neither run draws the HUD or the local character.
        if (!_gameplay)
        {
            if (_play.Hud is { } hud) hud.Visible = false;
            if (_play.Pawns is { } pawns) pawns.Visible = false;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsed = _clock.Elapsed.TotalSeconds;
        if (_index < 0) { Next(elapsed); _last = now; return; }
        double ms = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
        if (elapsed - _start < _warmup) return;
        if (_frames.Count == 0)
        {
            _allocated = GC.GetTotalAllocatedBytes(false);
            _pause = GC.GetTotalPauseDuration();
            for (int generation = 0; generation < 3; generation++) _collections[generation] = GC.CollectionCount(generation);
            // Discard the interval which straddled the warm-up boundary.
            _frames.Add(default);
            return;
        }

        Rid viewport = _profiling ? GetViewport().GetViewportRid() : default;
        _frames.Add(new Frame(ms, _play.Stats.LastSimMs, _play.Stats.LastViewMs,
            _profiling ? RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) : 0,
            _profiling ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport) : 0));
        if (elapsed - _start < _warmup + _seconds) return;
        SaveCase();
        Next(elapsed);
    }

    private void Next(double elapsed)
    {
        if (++_index >= _shots.Count)
        {
            File.WriteAllText(Path.Combine(_output, "summary.json"), JsonSerializer.Serialize(new
            {
                format = "svm.fps-benchmark/1", engine = "OpenSCCT", gpu = RenderingServer.GetVideoAdapterName(),
                godot = Engine.GetVersionInfo()["string"].AsString(), settings = SettingsStore.Current,
                map = _play!.Config.Settings.Map, side = _play.Config.Settings.Side.ToString().ToLowerInvariant(),
                bots = _play.Config.Settings.Bots, renderingDriver = RenderingServer.GetCurrentRenderingDriverName(),
                secondsPerView = _seconds, warmupSeconds = _warmup, cases = _results,
                gameplay = _gameplay,
                profiling = _profiling,
                depthPrepass = ProjectSettings.GetSetting("rendering/driver/depth_prepass/enable").AsBool(),
                serverGc = System.Runtime.GCSettings.IsServerGC,
                gcLatency = System.Runtime.GCSettings.LatencyMode.ToString(),
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("SVM_BENCHMARK complete " + _output);
            _done = true;
            GetTree().Quit();
            return;
        }

        GoldenShot shot = _shots[_index];
        ViewSample view = new(0, shot.CameraLocation, shot.CameraRotation, shot.Fov, shot.BehindView);
        _play!.Session!.ViewOverride = _ => view;
        _play.Session.SetViewNow(view);
        _frames.Clear();
        _start = _clock.Elapsed.TotalSeconds;
        GD.Print("SVM_BENCHMARK view " + shot.Id);
    }

    private void SaveCase()
    {
        long allocated = GC.GetTotalAllocatedBytes(false) - _allocated;
        double pauseMs = (GC.GetTotalPauseDuration() - _pause).TotalMilliseconds;
        int gen0 = GC.CollectionCount(0) - _collections[0], gen1 = GC.CollectionCount(1) - _collections[1],
            gen2 = GC.CollectionCount(2) - _collections[2];
        var pawn = _play!.Session!.LocalPawn!;
        using Process process = Process.GetCurrentProcess();
        Frame[] frames = _frames.Skip(1).ToArray();
        double[] times = frames.Select(f => f.Ms).Order().ToArray();
        double total = times.Sum();
        int tail = Math.Max(1, (int)Math.Ceiling(times.Length * 0.01));
        var result = new
        {
            id = _shots[_index].Id, width = _shots[_index].Width, height = _shots[_index].Height,
            frames = times.Length, seconds = total / 1000, fps = 1000 * times.Length / total,
            low1Percent = 1000 / times.TakeLast(tail).Average(), p50Ms = times[times.Length / 2],
            p99Ms = times[Math.Min(times.Length - 1, (int)(times.Length * 0.99))], maxMs = times[^1],
            simMs = frames.Average(f => f.SimMs), viewMs = frames.Average(f => f.ViewMs),
            renderCpuMs = frames.Average(f => f.RenderCpuMs), gpuMs = frames.Average(f => f.GpuMs),
            allocatedMbPerSecond = allocated / (total * 1000), gcPauseMs = pauseMs, gen0, gen1, gen2,
            pawnLocation = new[] { pawn.Location.X, pawn.Location.Y, pawn.Location.Z },
            pawnDistanceFromReferenceUu = OpenScct.Core.FVector.Dist(pawn.Location, _shots[_index].PawnLocation),
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            renderedObjects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame),
            renderedPrimitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
            workingSetBytes = process.WorkingSet64,
        };
        _results.Add(result);
        var csv = new StringBuilder("frame_ms,sim_ms,view_ms,render_cpu_ms,gpu_ms\n");
        foreach (Frame frame in frames)
            csv.Append(CultureInfo.InvariantCulture, $"{frame.Ms:F6},{frame.SimMs:F6},{frame.ViewMs:F6},{frame.RenderCpuMs:F6},{frame.GpuMs:F6}\n");
        File.WriteAllText(Path.Combine(_output, _shots[_index].Id + ".csv"), csv.ToString());
        using Image image = GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(_output, _shots[_index].Id + ".png"));
        GD.Print("SVM_BENCHMARK " + JsonSerializer.Serialize(result));
    }

    private void Fail(string message)
    {
        GD.PushError("SVM_BENCHMARK " + message);
        _done = true;
        GetTree().Quit(1);
    }
}
