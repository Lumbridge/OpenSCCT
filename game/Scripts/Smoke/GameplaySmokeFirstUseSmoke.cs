using System;
using System.Diagnostics;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;

namespace OpenScctGame.Smoke;

/// <summary>First/repeat authoritative smoke on an actual map, including voxel classification and lighting probes.</summary>
public sealed class GameplaySmokeFirstUseSmoke : SmokeTest
{
    public const string TestName = "gameplay-smoke-first-use";
    public override string Name => TestName;
    private PlayRoot? _root;
    private SSmokeEmitter? _emitter;
    private ResponsiveSmokeWorld? _world;
    private ResponsiveSmokeWorldDiagnostics _before;
    private int _stage, _settle, _teardown;
    private double _started, _maxFrame, _maxSim, _maxView, _pipes;
    private long _stamp, _fields, _prefix, _flow, _probes;
    private bool _drawn;
    private FVector _position;
    private SmokeResult? _result;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_result is { } result) return ++_teardown < 15 ? SmokeResult.Running : result;
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a RenderingDevice");
        if (_root is null)
        {
            context.Tree.Root.Msaa3D = Viewport.Msaa.Msaa4X;
            PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launcher with { Settings = launcher.Settings with {
                Map = context.App.Options.PlayMap ?? "AquaE", Mode = "Story", Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Inline, Bots = 0, Side = PlaySide.Merc, ResponsiveSmoke = true } });
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (!_root.IsLoaded || _root.Session?.LocalPawn is null || ++_settle < 90) return SmokeResult.Running;
        var session = _root.Session;
        if (_root.View is not { } view || !ResponsiveSmokeWorld.IsEnabled(session.Client.World.Level))
            return Finish(SmokeResult.Fail("missing responsive smoke view/rules"));
        if (_stage == 0)
        {
            if (!ResponsiveSmokeRenderer.IsPrepared || !view.SmokeBackgroundPrepared)
                return Finish(SmokeResult.Fail("responsive resources were not prepared before play"));
            _world = ResponsiveSmokeWorld.For(session.Host.World.Level);
            _world.DiagnosticsEnabled = true;
            _position = view.UeCamera.Location + view.UeCamera.Rotation.Vector() * 150;
            Pop(context); _stage = 1;
            return SmokeResult.Running;
        }
        long now = Stopwatch.GetTimestamp();
        if (_stage is 1 or 3)
        {
            _maxFrame = Math.Max(_maxFrame, Stopwatch.GetElapsedTime(_stamp,now).TotalMilliseconds);
            _maxSim = Math.Max(_maxSim,_root.Stats.LastSimMs); _maxView = Math.Max(_maxView,_root.Stats.LastViewMs);
            _stamp = now;
            _drawn |= ResponsiveSmokeRenderer.LastCacheViewCount > 0;
            if (context.ElapsedSeconds - _started < 1.5) return SmokeResult.Running;
            if (!_drawn || ResponsiveSmokeLighting.ProjectionBuilds <= _probes)
                return Finish(SmokeResult.Fail("actual-map smoke did not draw with spatial lighting probes"));
            double pipes = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw)-_pipes;
            long fields = ResponsiveSmokeRenderer.FieldAllocationCount-_fields;
            long prefix = ResponsiveSmokeVolumeCache.AllocationCount-_prefix;
            long flow = ResponsiveSmokeVisualFlow.AllocationCount-_flow;
            var diag = _world!.Diagnostics;
            GD.Print(FormattableString.Invariant($"SVM_FIRST_USE actual-map-responsive-{(_stage == 1 ? "first" : "repeat")} map={_root.Map?.MapId} position={_position} maxFrameMs={_maxFrame:F2} simMs={_maxSim:F2} viewMs={_maxView:F2} registerMs={diag.Register.Milliseconds-_before.Register.Milliseconds:F2} classifyTotalMs={diag.ClassifyFront.Milliseconds-_before.ClassifyFront.Milliseconds:F2} drawPipelines={pipes} allocations={fields}/{prefix}/{flow} probeBuilds={ResponsiveSmokeLighting.ProjectionBuilds-_probes}"));
            if (pipes != 0 || fields != 0 || prefix != 0 || flow != 0)
                return Finish(SmokeResult.Fail("actual-map first pop created draw pipelines or resized warmed GPU storage"));
            _emitter!.Destroy(); _emitter = null;
            if (_stage == 3) return Finish(SmokeResult.Pass("actual-map first/repeat responsive smoke reused pipelines/storage; geometry and lighting measured"));
            _stage = 2; _started = context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        if (context.ElapsedSeconds-_started < .5 || HasCloud(session.Client.World.Level)) return SmokeResult.Running;
        Pop(context); _stage = 3;
        return SmokeResult.Running;
    }

    private void Pop(SmokeContext context)
    {
        _started = context.ElapsedSeconds; _stamp = Stopwatch.GetTimestamp(); _maxFrame = _maxSim = _maxView = 0; _drawn = false;
        _before = _world!.Diagnostics;
        _pipes = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw);
        _fields = ResponsiveSmokeRenderer.FieldAllocationCount; _prefix = ResponsiveSmokeVolumeCache.AllocationCount;
        _flow = ResponsiveSmokeVisualFlow.AllocationCount; _probes = ResponsiveSmokeLighting.ProjectionBuilds;
        // Inline host: this uses the real emitter registration, geometry front, replication and live view paths.
        _emitter = _root!.Session!.Host.World.Level.Spawn<SSmokeEmitter>(_position);
    }

    private static bool HasCloud(Level level)
    {
        foreach (SSmokeEmitter emitter in level.AllActors<SSmokeEmitter>())
            if (!emitter.bDeleteMe) return true;
        return false;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _emitter?.Destroy(); _root?.QueueFree(); _result = result;
        return SmokeResult.Running;
    }
}
