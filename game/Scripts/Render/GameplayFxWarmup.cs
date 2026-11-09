using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Render;

/// <summary>Render first-use effects in a private world while the play scene is loading.</summary>
public partial class GameplayFxWarmup : SubViewport
{
    private int _frames;
    private bool _subscribed;
    private Action? _completed;
    private readonly Camera3D _camera = new() { Position = new Vector3(0f, 0f, 3f), Current = true, Far = 1000f };
    private Level? _smokeLevel;
    private RenderMap? _smokeMap;
    private Task? _smokeNoise;
    private SurfaceFogRegistry? _smokeView;
    private SmokeRenderCloud? _smokeCloud;
    private int _smokeFrames;
    private FaithfulView? _view;
    private ulong _started;
    private static readonly string[] WarmupFrames = ["warmup.frame1", "warmup.frame2", "warmup.frame3"];
    public Node3D Samples { get; } = new() { Name = "Samples" };

    public GameplayFxWarmup()
    {
        Name = "GameplayFxWarmup";
        Size = new Vector2I(64, 64);
        OwnWorld3D = true;
        RenderTargetUpdateMode = UpdateMode.Always;
        AddChild(Samples);
        AddChild(_camera);
    }

    public static GameplayFxWarmup For(Viewport target) => new()
    {
        // MSAA and motion-vector variants must match the viewport used for gameplay.
        Msaa3D = target.Msaa3D,
        UseTaa = target.UseTaa,
        ScreenSpaceAA = target.ScreenSpaceAA,
        UseDebanding = target.UseDebanding,
    };

    /// <summary>Warm persistent post resources on the gameplay view itself, including its real MSAA/resolution.</summary>
    public void PreloadView(FaithfulView? view)
    {
        _view = view;
        view?.Effect.Preload();
        view?.Remanence.Preload();
        view?.DoubleVision.Preload();
    }

    public void Begin(Action completed)
    {
        _completed = completed;
        _started = Time.GetTicksMsec();
        if (DisplayServer.GetName() == "headless") Finish();
        else
        {
            _subscribed = true;
            RenderingServer.FramePostDraw += AfterDraw;
        }
    }

    /// <summary>Exercise real smoke uploads, flow, view-volume and surface shader in this private loading world.</summary>
    public void PreloadResponsiveSmoke(Level level, RenderMap? map, Viewport target)
    {
        if (!ResponsiveSmokeWorld.IsEnabled(level) || DisplayServer.GetName() == "headless") return;
        ResponsiveSmokeRenderer.Publish(map,level);
        ResponsiveSmokeRenderer.Preparing = true;
        _smokeLevel = level;
        _smokeMap = map;
        // Pure immutable lattice generation is safe off-thread, including a late online rules arrival.
        _smokeNoise = Task.Run(ResponsiveSmokeNoise.Warmup);
        Vector2I pixels = (Vector2I)target.GetVisibleRect().Size;
        Size = new(Math.Max(1,pixels.X),Math.Max(1,pixels.Y));
    }

    private bool PrepareSmokeFrame()
    {
        if (_smokeLevel is not { } level || !ResponsiveSmokeWorld.IsEnabled(level)) return true;
        if (_smokeNoise is not { IsCompleted: true }) return false;
        _smokeNoise.GetAwaiter().GetResult();
        if (_smokeView is null)
        {
            ResponsiveSmokeRenderer.Prewarm(level);
            var cells = new byte[ResponsiveSmokeField.CellCount]; Array.Fill(cells,(byte)1);
            var activation = new ushort[cells.Length];
            FVector center = new(0,100,0);
            // Include both disturbance descriptor paths, using only private immutable fixture data.
            ResponsiveSmokeCarve[] carves = [
                new(1,ResponsiveSmokeCarveKind.Bullet,center-new FVector(0,400,0),center+new FVector(0,400,0),12,6,2,.1f,.4f,null),
                new(2,ResponsiveSmokeCarveKind.Blast,center,center,30,25,2,.6f,1.4f,(byte[])cells.Clone())];
            _smokeCloud = new(new object(),new(1,0,2,center-new FVector(312.5f,312.5f,312.5f),center,cells,activation,carves),1);
            _smokeView = new(_camera);
            ResponsiveSmokeRenderer.CreateBackground(_camera);
            PreloadSmokeSurfaces();
            ResponsiveSmokeRenderer.Preparing = false;
        }
        ResponsiveSmokeRenderer.BindWarmupView(_smokeView.Slot,_camera,
            new(true,2+_smokeFrames/60f,[_smokeCloud!]));
        // Three presented warm frames exercise reset and time-advancing flow, then drain the queued pipeline.
        return _smokeFrames++ >= 3 && (ResponsiveSmokeRenderer.IsPrepared || ResponsiveSmokeRenderer.VolumeError is not null);
    }

    private void PreloadSmokeSurfaces()
    {
        if (_smokeMap is not { } map) return;
        // Use actual map shader materials at finite depths inside the cloud: background-only warming would
        // never execute the surface-depth reconstruction/refinement path. No material or live mesh is changed.
        var pending = new Stack<Node>(); pending.Push(map.Root);
        var shaders = new HashSet<ulong>();
        while (pending.Count != 0 && shaders.Count < 4)
        {
            Node node = pending.Pop();
            if (node is MeshInstance3D { Mesh: { } mesh } instance)
                for (int surface = 0; surface < mesh.GetSurfaceCount() && shaders.Count < 4; surface++)
                    if (instance.GetActiveMaterial(surface) is ShaderMaterial { Shader: { } shader } material
                        && shaders.Add(shader.GetInstanceId()))
                        Samples.AddChild(new MeshInstance3D { Name = "SmokeSurfaceWarmup",
                            Position = new((shaders.Count-2)*1.5f,0,-1.5f-(shaders.Count%2)),
                            Mesh = new QuadMesh { Size = new(1.5f,2) }, MaterialOverride = material,
                            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            foreach (Node child in node.GetChildren()) pending.Push(child);
        }
    }

    private void AfterDraw()
    {
        // Let CPU particles emit and render their instanced geometry as well as the static samples.
        bool smokeReady = PrepareSmokeFrame();
        bool viewReady = _view is null || (_view.Effect.WarmupComplete &&
            _view.Remanence.WarmupComplete && _view.DoubleVision.WarmupComplete);
        // A hidden/disabled target may never deliver a compositor callback. It can warm on its first real draw;
        // don't strand loading forever on a window/renderer configuration that cannot draw this view yet.
        bool viewTimedOut = Time.GetTicksMsec() - _started >= 5000;
        // The first warm frames draw (and compile) the samples: time each of them.
        if (_frames < 3) OpenScctGame.Diagnostics.LoadTimer.Mark(WarmupFrames[_frames]);
        if (++_frames >= 3 && smokeReady && (viewReady || viewTimedOut))
        {
            if (!viewReady) GD.PushWarning("Gameplay post warmup did not draw before loading timeout.");
            Finish();
        }
    }

    private void Finish()
    {
        OpenScctGame.Diagnostics.LoadTimer.Mark("warmup.finish");
        Unsubscribe();
        RenderTargetUpdateMode = UpdateMode.Disabled;
        _smokeView?.Dispose(); _smokeView = null;
        if (_smokeLevel is not null) ResponsiveSmokeRenderer.Preparing = false;
        var completed = _completed;
        _completed = null;
        completed?.Invoke();
        QueueFree();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        RenderingServer.FramePostDraw -= AfterDraw;
        _subscribed = false;
    }

    public override void _ExitTree()
    {
        Unsubscribe();
        _smokeView?.Dispose(); _smokeView = null;
        if (_smokeLevel is not null) ResponsiveSmokeRenderer.Preparing = false;
        _completed = null;
    }
}
