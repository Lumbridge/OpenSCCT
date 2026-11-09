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

    // Auxiliary viewport timings (profiling only): per recorded frame, gpu ms, cpu ms and count for each kind.
    private static readonly int Kinds = ViewportTimings.Kinds.Count;
    private readonly List<double> _aux = [];
    private readonly double[] _auxGpu = new double[Kinds];
    private readonly double[] _auxCpu = new double[Kinds];
    private readonly int[] _auxCount = new int[Kinds];

    private static readonly (string Name, Performance.Monitor Monitor)[] Pipelines =
    [
        ("canvas", Performance.Monitor.PipelineCompilationsCanvas),
        ("mesh", Performance.Monitor.PipelineCompilationsMesh),
        ("surface", Performance.Monitor.PipelineCompilationsSurface),
        ("draw", Performance.Monitor.PipelineCompilationsDraw),
        ("specialization", Performance.Monitor.PipelineCompilationsSpecialization),
    ];
    private readonly double[] _pipelines = new double[Pipelines.Length];

    public static void Configure(Viewport viewport)
    {
        SettingsStore.Isolated = true;
        var settings = new GameSettings { FpsLimit = DisplayFrameCap.Unlimited, VSync = false, ThrottleStop = true };
        SettingsStore.Save(settings);
        SettingsApplier.ApplyPacing(settings);
        // Window size comes from the camera records; only the 3D scale is taken from the display settings.
        viewport.Scaling3DScale = settings.RenderScale;
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
            if (_profiling)
            {
                RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
                ViewportTimings.SetMeasuring(true);
                _aux.EnsureCapacity(_frames.Capacity * Kinds * 3);
            }
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
        // Sampled in warm-up too, so viewports have measurement on and a baseline before the window starts.
        if (_profiling) SampleAux();
        if (elapsed - _start < _warmup) return;
        // A valid camera request must reach the renderer. Otherwise a frozen spawn view can produce convincing
        // frame times and identical screenshots for every requested pose, especially for first-person records.
        GoldenShot shot = _shots[_index];
        if (_play.Camera is not { } camera ||
            OpenScct.Core.FVector.Dist(camera.Current.Location, shot.CameraLocation) > 0.05f ||
            AngleError(camera.Current.Rotation.Pitch, shot.CameraRotation.Pitch) > 1 ||
            AngleError(camera.Current.Rotation.Yaw, shot.CameraRotation.Yaw) > 1 ||
            AngleError(camera.Current.Rotation.Roll, shot.CameraRotation.Roll) > 1 ||
            Math.Abs(camera.Current.Fov - shot.Fov) > 0.01f)
        {
            Fail($"Rendered camera does not match benchmark view '{shot.Id}'");
            return;
        }
        if (_frames.Count == 0)
        {
            _allocated = GC.GetTotalAllocatedBytes(false);
            _pause = GC.GetTotalPauseDuration();
            for (int generation = 0; generation < 3; generation++) _collections[generation] = GC.CollectionCount(generation);
            for (int i = 0; i < Pipelines.Length; i++) _pipelines[i] = Performance.GetMonitor(Pipelines[i].Monitor);
            // Discard the interval which straddled the warm-up boundary.
            _frames.Add(default);
            if (_profiling) AppendAux();
            return;
        }

        Rid viewport = _profiling ? GetViewport().GetViewportRid() : default;
        _frames.Add(new Frame(ms, _play.Stats.LastSimMs, _play.Stats.LastViewMs,
            _profiling ? RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) : 0,
            _profiling ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport) : 0));
        if (_profiling) AppendAux();
        if (elapsed - _start < _warmup + _seconds) return;
        SaveCase();
        Next(elapsed);
    }

    private void Next(double elapsed)
    {
        if (++_index >= _shots.Count)
        {
            StopMeasuring();
            Window root = GetTree().Root;
            Vector2I window = DisplayServer.WindowGetSize();
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
                gpuVendor = RenderingServer.GetVideoAdapterVendor(),
                gpuType = RenderingServer.GetVideoAdapterType().ToString(),
                gpuApiVersion = RenderingServer.GetVideoAdapterApiVersion(),
                windowSize = new[] { window.X, window.Y },
                viewportSize = new[] { root.Size.X, root.Size.Y },
                scaling3DMode = root.Scaling3DMode.ToString(),
                scaling3DScale = root.Scaling3DScale,
                // Internal 3D size: Godot truncates size x scale for the render buffers.
                render3DSize = new[] { (int)(root.Size.X * root.Scaling3DScale), (int)(root.Size.Y * root.Scaling3DScale) },
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("SVM_BENCHMARK complete " + _output);
            _done = true;
            GetTree().Quit();
            return;
        }

        GoldenShot shot = _shots[_index];
        ViewSample view = new(0, shot.CameraLocation, shot.CameraRotation, shot.Fov, Valid: true);
        _play!.Session!.ViewOverride = _ => view;
        _play.Session.SetViewNow(view);
        _frames.Clear();
        _aux.Clear();
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
        double mainGpuMs = frames.Average(f => f.GpuMs);
        Dictionary<string, AuxView>? auxViews = _profiling ? AuxSummary(frames.Length) : null;
        double? auxGpuMs = auxViews?.Values.Sum(v => v.gpuMs);
        var result = new
        {
            id = _shots[_index].Id, width = _shots[_index].Width, height = _shots[_index].Height,
            frames = times.Length, seconds = total / 1000, fps = 1000 * times.Length / total, meanMs = total / times.Length,
            low1Percent = 1000 / times.TakeLast(tail).Average(), p50Ms = times[times.Length / 2],
            p99Ms = times[Math.Min(times.Length - 1, (int)(times.Length * 0.99))], maxMs = times[^1],
            simMs = frames.Average(f => f.SimMs), viewMs = frames.Average(f => f.ViewMs),
            renderCpuMs = frames.Average(f => f.RenderCpuMs), gpuMs = mainGpuMs,
            // Profiling only: auxiliary viewports per kind, their GPU sum, and main plus auxiliary GPU.
            viewports = auxViews, auxGpuMs, totalGpuMs = mainGpuMs + auxGpuMs,
            allocatedMbPerSecond = allocated / (total * 1000), gcPauseMs = pauseMs, gen0, gen1, gen2,
            pawnLocation = new[] { pawn.Location.X, pawn.Location.Y, pawn.Location.Z },
            pawnDistanceFromReferenceUu = OpenScct.Core.FVector.Dist(pawn.Location, _shots[_index].PawnLocation),
            cameraLocation = new[] { _play.Camera!.Current.Location.X, _play.Camera.Current.Location.Y, _play.Camera.Current.Location.Z },
            cameraRotation = new[] { _play.Camera.Current.Rotation.Pitch, _play.Camera.Current.Rotation.Yaw, _play.Camera.Current.Rotation.Roll },
            cameraFov = _play.Camera.Current.Fov,
            cameraDistanceFromReferenceUu = OpenScct.Core.FVector.Dist(_play.Camera.Current.Location, _shots[_index].CameraLocation),
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            renderedObjects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame),
            renderedPrimitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
            workingSetBytes = process.WorkingSet64,
            privateBytes = process.PrivateMemorySize64,
            gcHeapBytes = GC.GetTotalMemory(false),
            textureMemBytes = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed),
            bufferMemBytes = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed),
            videoMemBytes = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed),
            staticMemBytes = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic),
            objectCount = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount),
            resourceCount = (long)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
            nodeCount = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            orphanNodeCount = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount),
            // Totals since start, and how many happened inside the measured window (a compile there is a hitch).
            pipelineCompilations = Pipelines.Select((p, i) => (p.Name, Total: Performance.GetMonitor(p.Monitor), Start: _pipelines[i]))
                .ToDictionary(p => p.Name, p => new { total = (long)p.Total, window = (long)(p.Total - p.Start) }),
        };
        _results.Add(result);
        var csv = new StringBuilder("frame_ms,sim_ms,view_ms,render_cpu_ms,gpu_ms");
        if (_profiling)
        {
            foreach (string kind in ViewportTimings.Kinds)
            {
                string column = kind.Replace('-', '_');
                csv.Append(CultureInfo.InvariantCulture, $",{column}_gpu_ms,{column}_cpu_ms,{column}_count");
            }
            csv.Append(",total_gpu_ms");
        }
        csv.Append('\n');
        for (int f = 0; f < frames.Length; f++)
        {
            Frame frame = frames[f];
            csv.Append(CultureInfo.InvariantCulture, $"{frame.Ms:F6},{frame.SimMs:F6},{frame.ViewMs:F6},{frame.RenderCpuMs:F6},{frame.GpuMs:F6}");
            if (_profiling)
            {
                double gpu = frame.GpuMs;
                for (int k = 0; k < Kinds; k++)
                {
                    int at = AuxIndex(f + 1, k);
                    gpu += _aux[at];
                    csv.Append(CultureInfo.InvariantCulture, $",{_aux[at]:F6},{_aux[at + 1]:F6},{_aux[at + 2]:F0}");
                }
                csv.Append(CultureInfo.InvariantCulture, $",{gpu:F6}");
            }
            csv.Append('\n');
        }
        File.WriteAllText(Path.Combine(_output, _shots[_index].Id + ".csv"), csv.ToString());
        using Image image = GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(_output, _shots[_index].Id + ".png"));
        GD.Print("SVM_BENCHMARK " + JsonSerializer.Serialize(result));
    }

    // Lower-case names: serialised as they are, next to the anonymous result fields.
    private sealed record AuxView(double gpuMs, double cpuMs, double count);

    /// <summary>Mean GPU ms, CPU ms and rendered-viewport count per kind over the recorded frames (the first is discarded).</summary>
    private Dictionary<string, AuxView> AuxSummary(int frames)
    {
        var views = new Dictionary<string, AuxView>();
        int n = Math.Max(1, frames);
        for (int k = 0; k < Kinds; k++)
        {
            double gpu = 0, cpu = 0, count = 0;
            for (int f = 1; f <= frames && AuxIndex(f, k) + 2 < _aux.Count; f++)
            {
                int at = AuxIndex(f, k);
                gpu += _aux[at];
                cpu += _aux[at + 1];
                count += _aux[at + 2];
            }
            views[ViewportTimings.Kinds[k]] = new AuxView(gpu / n, cpu / n, count / n);
        }
        return views;
    }

    private static int AuxIndex(int frame, int kind) => (frame * Kinds + kind) * 3;

    private void SampleAux()
    {
        Array.Clear(_auxGpu);
        Array.Clear(_auxCpu);
        Array.Clear(_auxCount);
        ViewportTimings.Sample(_auxGpu, _auxCpu, _auxCount);
    }

    private void AppendAux()
    {
        for (int k = 0; k < Kinds; k++)
        {
            _aux.Add(_auxGpu[k]);
            _aux.Add(_auxCpu[k]);
            _aux.Add(_auxCount[k]);
        }
    }

    private void StopMeasuring()
    {
        if (!_profiling) return;
        ViewportTimings.SetMeasuring(false);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false);
    }

    private void Fail(string message)
    {
        StopMeasuring();
        GD.PushError("SVM_BENCHMARK " + message);
        _done = true;
        GetTree().Quit(1);
    }

    private static int AngleError(int actual, int expected) => Math.Abs((int)unchecked((short)(actual - expected)));
}
