using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using Godot;
using OpenScct.Server;
using OpenScctGame.Play;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-hitch</c>: SteeE as the spy with three bots, launched as <c>--svm-play=SteeE --side=spy
/// --rules=playlevel --bots=3 --difficulty=normal</c> (host thread, wall clock, HUD, sound), played for
/// <see cref="PlaySeconds"/> of real time. Every frame's wall time is measured; the smoke fails when a frame after
/// the warm-up takes longer than <see cref="MaxFrameMs"/> (windowed) or <see cref="MaxFrameMsHeadless"/>. Each slow
/// frame is printed with what ran in it: the simulation and view costs, the client ticks, the .NET collections and
/// their pause, the bytes allocated, Godot's pipeline compilations and the longest gap between server ticks.
/// <c>SVM_HITCH_SECONDS</c> changes the play time, <c>SVM_HITCH_MS</c> the threshold and <c>SVM_HITCH_LOG_MS</c> the
/// frame time from which a frame is printed (default 20 ms). The summary line has the frame time distribution, the
/// collections and their pause per minute and the allocation rate. docs/TESTING-PLAY.md#frame-time.
/// </summary>
public sealed class PlayHitchSmoke : SmokeTest
{
    public const string TestName = "play-hitch";

    /// <summary>Real seconds of play measured after the warm-up.</summary>
    public const double PlaySeconds = 90.0;

    /// <summary>Seconds after the local pawn exists that are not measured (the first replication, spawns, warm-up).</summary>
    public const double WarmupSeconds = 3.0;

    /// <summary>
    /// Windowed limit: two frames of a 60 Hz display. A frame over it is a visible hitch (a skipped picture and a
    /// skipped sim tick on screen); vsync makes the normal frame 16.7 ms, so a single late present stays under it.
    /// </summary>
    public const double MaxFrameMs = 33.4;

    /// <summary>
    /// Headless limit, the same: without a window the frame loop sleeps on the OS timer (15.6 ms steps on Windows), so
    /// frames of 16 to 22 ms with no work in them are normal there; a hitch of our own still shows over 33 ms.
    /// </summary>
    public const double MaxFrameMsHeadless = 33.4;

    private const double SlowMs = 20.0;

    /// <summary>Frames longer than this are printed with what ran in them (<c>SVM_HITCH_LOG_MS</c>, default 20).</summary>
    private double _logMs = SlowMs;

    private PlayRoot? _root;
    private double _measureFrom = -1;
    private double _measureUntil;
    private long _lastStamp;
    private long _lastTicks;
    private readonly int[] _gcLast = new int[3];
    private readonly int[] _gcDelta = new int[3];
    private TimeSpan _pauseLast;
    private long _allocLast;
    private long _allocMainLast;
    private double _pipeLast;
    private long _serverLastTick;
    private long _serverMaxGap;
    private long _serverTicks;
    private ServerHost? _host;
    private readonly List<double> _frames = new(1 << 16);
    private readonly List<string> _slow = [];
    private readonly int[] _gcStart = new int[3];
    private TimeSpan _pauseStart;
    private long _allocStart;
    private double _startSeconds;
    private int _teardown = -1;
    private SmokeResult _final;
    private double _limit;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < 8 ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            _root = PlayInspectSmoke.StartPlay(context);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session || session.LocalPawn is null)
        {
            return SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        long now = Stopwatch.GetTimestamp();
        if (_measureFrom < 0)
        {
            bool headless = DisplayServer.GetName() == "headless";
            _limit = double.TryParse(System.Environment.GetEnvironmentVariable("SVM_HITCH_MS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms)
                ? ms
                : headless ? MaxFrameMsHeadless : MaxFrameMs;
            double seconds = double.TryParse(System.Environment.GetEnvironmentVariable("SVM_HITCH_SECONDS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double s)
                ? s
                : PlaySeconds;
            _logMs = double.TryParse(System.Environment.GetEnvironmentVariable("SVM_HITCH_LOG_MS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double logMs)
                ? logMs
                : SlowMs;
            _measureFrom = context.ElapsedSeconds + WarmupSeconds;
            _measureUntil = _measureFrom + seconds;
            _host = session.Host;
            _host.BeforeWorldTick += OnServerTick;
            Sample(now, out _, out _, out _, out _, out _, out _);
            return SmokeResult.Running;
        }

        double frameMs = Stopwatch.GetElapsedTime(_lastStamp, now).TotalMilliseconds;
        Sample(now, out int[] gc, out double pauseMs, out long alloc, out long allocMain, out double pipes, out long ticks);
        long gap = Interlocked.Exchange(ref _serverMaxGap, 0);
        if (context.ElapsedSeconds < _measureFrom)
        {
            return SmokeResult.Running;
        }

        if (_frames.Count == 0)
        {
            _startSeconds = context.ElapsedSeconds;
            for (int i = 0; i < 3; i++)
            {
                _gcStart[i] = GC.CollectionCount(i);
            }

            _pauseStart = GC.GetTotalPauseDuration();
            _allocStart = GC.GetTotalAllocatedBytes(false);
        }

        _frames.Add(frameMs);
        if (frameMs > _logMs)
        {
            _slow.Add(string.Create(CultureInfo.InvariantCulture,
                $"t={context.ElapsedSeconds - _startSeconds:F2} at={DateTime.Now.AddMilliseconds(-frameMs):HH:mm:ss.fff} frame={frameMs:F1} sim={_root.Stats.LastSimMs:F1} view={_root.Stats.LastViewMs:F1} ticks={ticks} gc0={gc[0]} gc1={gc[1]} gc2={gc[2]} pause={pauseMs:F1} allocKB={alloc / 1024} mainKB={allocMain / 1024} pipelines={pipes:F0} serverGap={gap / 1000.0:F1} process={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F1} physics={Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:F1}"));
        }

        if (context.ElapsedSeconds < _measureUntil)
        {
            return SmokeResult.Running;
        }

        return Finish(Verdict(context.ElapsedSeconds - _startSeconds));
    }

    private void OnServerTick(ServerHost host)
    {
        long now = Stopwatch.GetTimestamp();
        long last = _serverLastTick;
        _serverLastTick = now;
        _serverTicks++;
        if (last != 0)
        {
            long gapMicros = (long)Stopwatch.GetElapsedTime(last, now).TotalMicroseconds;
            long seen;
            do
            {
                seen = Interlocked.Read(ref _serverMaxGap);
                if (gapMicros <= seen)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(ref _serverMaxGap, gapMicros, seen) != seen);
        }
    }

    private void Sample(long now, out int[] gc, out double pauseMs, out long alloc, out long allocMain, out double pipes, out long ticks)
    {
        gc = _gcDelta;
        for (int i = 0; i < 3; i++)
        {
            int c = GC.CollectionCount(i);
            gc[i] = c - _gcLast[i];
            _gcLast[i] = c;
        }

        TimeSpan pause = GC.GetTotalPauseDuration();
        pauseMs = (pause - _pauseLast).TotalMilliseconds;
        _pauseLast = pause;
        long a = GC.GetTotalAllocatedBytes(false);
        alloc = a - _allocLast;
        _allocLast = a;
        long am = GC.GetAllocatedBytesForCurrentThread();
        allocMain = am - _allocMainLast;
        _allocMainLast = am;
        double p = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas)
            + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh)
            + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface)
            + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw)
            + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSpecialization);
        pipes = p - _pipeLast;
        _pipeLast = p;
        long t = _root?.Stats.Ticks ?? 0;
        ticks = t - _lastTicks;
        _lastTicks = t;
        _lastStamp = now;
    }

    private SmokeResult Verdict(double seconds)
    {
        var sorted = new List<double>(_frames);
        sorted.Sort();
        double P(double q) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];
        int over20 = 0;
        int overLimit = 0;
        foreach (double f in _frames)
        {
            over20 += f > SlowMs ? 1 : 0;
            overLimit += f > _limit ? 1 : 0;
        }

        double minutes = seconds / 60.0;
        double perMin(int gen) => (GC.CollectionCount(gen) - _gcStart[gen]) / minutes;
        double pauseMs = (GC.GetTotalPauseDuration() - _pauseStart).TotalMilliseconds;
        double allocMBs = (GC.GetTotalAllocatedBytes(false) - _allocStart) / (1024.0 * 1024.0) / seconds;
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"seconds={seconds:F0} frames={_frames.Count} frame ms p50={P(0.5):F1} p95={P(0.95):F1} p99={P(0.99):F1} p999={P(0.999):F1} max={(sorted.Count > 0 ? sorted[^1] : 0):F1} over20={over20} over{_limit:F0}={overLimit} gc/min gen0={perMin(0):F1} gen1={perMin(1):F1} gen2={perMin(2):F1} pause ms/min={pauseMs / minutes:F1} alloc MB/s={allocMBs:F2} server ticks={_serverTicks} gc mode={(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")} latency={System.Runtime.GCSettings.LatencyMode} models read={Render.Characters.SkeletalModel.FilesRead} reused={Render.Characters.SkeletalModel.Reused} quad surfaces={Render.Particles.QuadBuffer.Built} fallbacks={Render.Particles.QuadBuffer.Fallbacks}");
        foreach (string line in _slow)
        {
            GD.Print("SVM_HITCH slow " + line);
        }

        GD.Print("SVM_HITCH summary " + summary);
        if (overLimit == 0)
        {
            return SmokeResult.Pass(summary);
        }

        // The failure names the slow frames (the first ten) as well as the counts.
        var log = new StringBuilder();
        log.Append(overLimit.ToString(CultureInfo.InvariantCulture)).Append(" frames over ")
            .Append(_limit.ToString("F1", CultureInfo.InvariantCulture)).Append(" ms: ").Append(summary);
        for (int i = 0; i < Math.Min(10, _slow.Count); i++)
        {
            log.AppendLine().Append("  ").Append(_slow[i]);
        }

        return SmokeResult.Fail(log.ToString());
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        if (_host is not null)
        {
            _host.BeforeWorldTick -= OnServerTick;
        }

        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
