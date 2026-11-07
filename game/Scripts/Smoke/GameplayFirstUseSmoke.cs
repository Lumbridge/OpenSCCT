using System;
using System.Diagnostics;
using Godot;
using OpenScct.Core;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Smoke;

/// <summary>Measure first/repeat vision, flash, double vision and legacy smoke draws after the loading gate.</summary>
public sealed class GameplayFirstUseSmoke : SmokeTest
{
    public const string TestName = "gameplay-first-use";
    public override string Name => TestName;
    private static readonly int[] Modes = [5, 34, 36, 37, 38, 5, 34, 36, 37, 38, 5, 5, 5, 5];
    private static readonly string[] Labels = ["normal", "night-first", "thermal-first", "motion-first", "emf-first", "normal-repeat",
        "night-repeat", "thermal-repeat", "motion-repeat", "emf-repeat", "flash-first", "double-first", "legacy-smoke-first", "legacy-smoke-repeat"];
    private PlayRoot? _root;
    private ParticleSystemView? _smoke;
    private int _settle, _phase = -1, _frames, _teardown;
    private long _stamp;
    private double _max, _pipes;
    private int _flashAllocations, _doubleAllocations;
    private double _nextLoadReport;
    private SmokeResult? _result;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_result is { } result)
        {
            if (++_teardown == 5) _root?.QueueFree();
            return _teardown < 15 ? SmokeResult.Running : result;
        }
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("first-use timing requires a RenderingDevice");
        if (_root is null)
        {
            context.Tree.Root.Msaa3D = Viewport.Msaa.Msaa4X;
            _root = context.App.Options.PlayMap is null ? TestRange.Start(context, PlaySide.Merc, 0)
                : PlayRoot.Create(PlayConfig.FromOptions(context.App.Options));
            if (_root.GetParent() is null) context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (!_root.IsLoaded || _root.Session?.LocalPawn is null)
        {
            if (context.ElapsedSeconds >= _nextLoadReport)
            {
                _nextLoadReport = context.ElapsedSeconds + 10;
                GD.Print($"SVM_FIRST_USE loading loaded={_root.IsLoaded} pawn={_root.Session?.LocalPawn is not null} warmup={_root.GetNodeOrNull("GameplayFxWarmup") is not null} post={_root.View?.Effect.WarmupComplete}/{_root.View?.Remanence.WarmupComplete}/{_root.View?.DoubleVision.WarmupComplete} smoke={ResponsiveSmokeRenderer.IsPrepared} views={ResponsiveSmokeRenderer.DescribeViews()}");
            }
            return SmokeResult.Running;
        }
        if (++_settle < 90) return SmokeResult.Running;
        if (_root.View is not { } view) return Finish(SmokeResult.Fail("missing faithful view"));
        if (_phase < 0)
        {
            if (view.Effect.Error is not null || view.Remanence.Error is not null || view.DoubleVision.Error is not null)
                return Finish(SmokeResult.Fail($"post effect failed: {view.Effect.Error}/{view.Remanence.Error}/{view.DoubleVision.Error}"));
            if (!view.Effect.WarmupComplete || !view.Remanence.WarmupComplete || !view.DoubleVision.WarmupComplete)
                return Finish(SmokeResult.Fail("view resources were not prepared by loading"));
            if (view.Remanence.CaptureAllocations == 0 || view.DoubleVision.CaptureAllocations == 0)
                return Finish(SmokeResult.Fail("loading did not allocate capture resources"));
            if (view.Remanence.Captures != 0 || view.Remanence.Draws != 0 || view.DoubleVision.Draws != 0)
                return Finish(SmokeResult.Fail("loading warmup changed gameplay flash counters"));
            if (OpenScct.Game.Equipment.ResponsiveSmokeWorld.IsEnabled(_root.Session.Client.World.Level) && !view.SmokeBackgroundPrepared)
                return Finish(SmokeResult.Fail("responsive smoke backdrop was not prepared by loading"));
            _flashAllocations = view.Remanence.CaptureAllocations;
            _doubleAllocations = view.DoubleVision.CaptureAllocations;
        }
        // Isolate the renderer from gameplay and drive the same view/post effects the player uses.
        _root.SetProcess(false);
        long now = Stopwatch.GetTimestamp();
        if (_phase >= 0)
        {
            _max = Math.Max(_max, Stopwatch.GetElapsedTime(_stamp, now).TotalMilliseconds);
            if (++_frames < 12) { _stamp = now; return SmokeResult.Running; }
            double late = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw) - _pipes;
            GD.Print(FormattableString.Invariant($"SVM_FIRST_USE {Labels[_phase]} maxFrameMs={_max:F2} drawPipelines={late:F0}"));
            if (view.Effect.LastDrawnRendMap != Modes[_phase]) return Finish(SmokeResult.Fail("vision did not draw requested mode"));
            if (view.Remanence.CaptureAllocations != _flashAllocations || view.DoubleVision.CaptureAllocations != _doubleAllocations)
                return Finish(SmokeResult.Fail("first use allocated a capture after loading"));
            if (late != 0) return Finish(SmokeResult.Fail($"{Labels[_phase]} compiled {late} draw pipelines after loading"));
        }
        if (++_phase >= Modes.Length) return Finish(SmokeResult.Pass("first/repeat vision and legacy smoke timings recorded"));
        _stamp = Stopwatch.GetTimestamp(); _frames = 0; _max = 0;
        _pipes = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw);
        view.RendMap = Modes[_phase];
        view.Remanence.Factor = _phase == 10 ? .5f : 0f;
        if (_phase == 10) view.Remanence.RequestCapture();
        view.DoubleVision.Factor = _phase == 11 ? .5f : 0f;
        if (_phase >= 12)
        {
            _smoke?.Root.QueueFree();
            var library = _root.Fx?.Effects?.Library;
            if (library?.CreateSystem("SBase.SSmokeEmitter", 1) is not { } sim) return Finish(SmokeResult.Fail("missing legacy smoke"));
            _smoke = new ParticleSystemView(sim, library, _root, "FirstUseSmoke", 1f);
            // Live smoke starts as an emitter actor; Trigger() would toggle its TriggerDisabled default off.
            var camera = view.UeCamera;
            FVector center = camera.Location + camera.Rotation.Vector() * 100f;
            sim.Step(.2f, center, default, default);
            _smoke.Draw(new ParticleOwner(center, default, default, sim.GlobalOffset), ParticleCamera.From(camera.Location, camera.Rotation));
            if (_smoke.Drawn == 0) return Finish(SmokeResult.Fail("legacy smoke fixture drew no particles"));
        }
        return SmokeResult.Running;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _smoke?.Free(); _smoke = null;
        if (_root?.View is { } view)
        {
            view.Remanence.Factor = 0;
            view.DoubleVision.Factor = 0;
            view.RendMap = RenderK.RendMapNormal;
        }
        _root?.SetProcess(true);
        _result = result;
        return SmokeResult.Running;
    }
}
