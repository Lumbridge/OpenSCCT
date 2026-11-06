using System;
using Godot;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Check loading completion, warmup cleanup, and first-use pipeline reuse with a real renderer.</summary>
public sealed class GameplayFxWarmupSmoke : SmokeTest
{
    public const string TestName = "gameplay-fx-warmup";
    public override string Name => TestName;
    private PlayRoot? _root;
    private GameplayFxWarmup? _repeat;
    private int _frames;
    private bool _complete;
    private double _drawCompiles;
    private int _securitySamples;
    private int _teardown;
    private SmokeResult? _result;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_result is { } result) return ++_teardown < 5 ? SmokeResult.Running : result;
        if (_root is null)
        {
            context.Tree.Root.Msaa3D = Viewport.Msaa.Msaa4X;
            if (context.App.Options.PlayMap is not null)
            {
                _root = PlayRoot.Create(PlayConfig.FromOptions(context.App.Options));
                context.Tree.Root.AddChild(_root);
            }
            else
            {
                _root = TestRange.Start(context, PlaySide.Merc, 0);
            }
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (!_root.IsLoaded || ++_frames < 30) return SmokeResult.Running;
        if (_root.GetNodeOrNull("GameplayFxWarmup") is not null) return Finish(SmokeResult.Fail("loading viewport leaked into gameplay"));
        if (_root.Fx?.Effects is not { } effects || _root.MercLasers is not { } lasers)
            return Finish(SmokeResult.Fail("missing effects views"));
        if (_repeat is null)
        {
            int started = effects.Started.Count;
            int cameras = _root.Fx.DisabledCameraCount;
            int beams = lasers.BeamsDrawn;
            _drawCompiles = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw);
            _repeat = GameplayFxWarmup.For(_root.GetViewport());
            if (_repeat.Msaa3D != Viewport.Msaa.Msaa4X) return Finish(SmokeResult.Fail("warmup lost gameplay MSAA setting"));
            context.Tree.Root.AddChild(_repeat);
            _root.Fx.Preload(_repeat.Samples);
            lasers.Preload(_repeat.Samples);
            _root.SpyLasers?.Preload(_repeat.Samples);
            _root.SpyFirstPerson?.Warmup(_repeat.Samples);
            int effectSamples = _repeat.Samples.GetChildCount();
            if (_root.Map is { } map) DeviceViews.PreloadSecuritySkins(map, _repeat.Samples);
            _securitySamples = _repeat.Samples.GetChildCount() - effectSamples;
            if (effects.Started.Count != started || _root.Fx.DisabledCameraCount != cameras || lasers.BeamsDrawn != beams)
                return Finish(SmokeResult.Fail("warmup changed live effect counters"));
            _repeat.Begin(() => _complete = true);
            return SmokeResult.Running;
        }
        if (!_complete) return SmokeResult.Running;
        double late = Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw) - _drawCompiles;
        if (late != 0) return Finish(SmokeResult.Fail($"first-use sample draw compiled {late} new pipelines after loading"));
        return Finish(SmokeResult.Pass($"effects warmed during loading; no gameplay effects spawned; cleanup complete; security skin samples={_securitySamples}; repeat draw pipeline compilations={late}"));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        if (GodotObject.IsInstanceValid(_repeat)) _repeat!.QueueFree();
        _root?.QueueFree();
        _result = result;
        return SmokeResult.Running;
    }
}
