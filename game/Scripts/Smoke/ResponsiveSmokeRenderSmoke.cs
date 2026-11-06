using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Options;
using OpenScctGame.Render;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>Authoritative density versus actual pixels; optional 1080p GPU timing with six overlapping clouds.</summary>
public sealed class ResponsiveSmokeRenderSmoke : SmokeTest
{
    public const string TestName = "responsive-smoke-render";
    public override string Name => TestName;
    private readonly World _world = new(9341, ENetMode.NM_Client);
    private readonly List<SSmokeEmitter> _emitters = [];
    private readonly List<double> _gpu = [];
    private readonly List<double> _surfaceGpu = [], _prefixGpu = [], _flowGpu = [];
    private SubViewport? _viewport, _auxiliary;
    private Camera3D? _camera, _auxiliaryCamera;
    private SurfaceFogRegistry? _binding, _auxiliaryBinding;
    private DisplaySpaceEffect? _display;
    private MeshInstance3D? _surface, _background, _glass;
    private GameReplicationInfo? _replication;
    private ShaderMaterial? _material;
    private int _phase, _frames;
    private float _lowHole;
    private float _lastDrawTime;
    private long _acceptedAtPhaseStart, _rejectedAtPhaseStart;
    private long _flowDispatchesAtPhaseStart, _flowImpulsesAtPhaseStart;
    private sealed record FlowCapture(SmokeFlowReadback State);
    private FlowCapture? _flowCapture;
    private bool _flowCaptureQueued;
    private byte[]? _frozenFlow;
    private int _repeatFlowAfterFrame;
    private readonly List<byte[]> _flowRateReference = [];
    private readonly List<ResponsiveSmokeCarve> _benchmarkShots = [];
    private uint _benchmarkShotSequence = 1000, _benchmarkCarveRevision = 1000;
    private int _lifecycleStage, _lifecycleWaitFrames;
    private bool _cleanupDrained;
    private readonly bool _benchmark = System.Environment.GetEnvironmentVariable("SVM_RESPONSIVE_SMOKE_BENCH") == "1";
    private ResponsiveSmokeQuality _oldQuality;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_emitters.Count == 0)
        {
            _oldQuality = SettingsStore.Current.SmokeQuality;
            _replication = _world.Level.Spawn<GameReplicationInfo>()!;
            _replication.ResponsiveSmokeEnabled = true;
            _emitters.Add(_world.Level.Spawn<SSmokeEmitter>(new(0, -500, 0))!);
            if (_benchmark)
                for (int i = 1; i < 6; i++) _emitters.Add(_world.Level.Spawn<SSmokeEmitter>(new((i%3-1)*40, -500-i*20, (i/3)*30))!);
            Configure();
            if (DisplayServer.GetName() == "headless")
            {
                ResponsiveSmokeField field = ResponsiveSmokeWorld.For(_world.Level).Fields[_emitters[0]];
                if (field.SampleDensity(field.Center, 2) < .99f) return SmokeResult.Fail("headless mature field was not opaque");
                Apply([Hole()], 2);
                if (field.SampleDensity(field.Center, 2) > .001f || field.SampleDensity(field.Center, 3) < .99f)
                    return SmokeResult.Fail("headless analytical carve/refill diverged");
                return Finish(SmokeResult.Pass("headless density/carve/refill fixture; GPU assertions require a rendered run"));
            }
            if (ResponsiveSmokeRenderer.UnsupportedReason is { } unsupported) return SmokeResult.Fail(unsupported);
            _viewport = new SubViewport { Size = _benchmark ? new(1920,1080) : new(256,256), OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseDebanding = false };
            context.Tree.Root.AddChild(_viewport);
            RenderingServer.ViewportSetMeasureRenderTime(_viewport.GetViewportRid(), true);
            _display = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false, SurfaceFogApplied = true } };
            _camera = new Camera3D { Current = true, CullMask = RenderLayers.MainView, Near = .01f, Far = 100,
                Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
                Compositor = new Compositor { CompositorEffects = [_display] } };
            _viewport.AddChild(_camera);
            _binding = new SurfaceFogRegistry(_camera);
            _material = new ShaderMaterial { Shader = new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled;
                #include "res://Shaders/svm_fog.gdshaderinc"
                uniform bool diagnostic = true;
                uniform bool finite_depth = false;
                uniform vec3 backdrop = vec3(1.0);
                void fragment() {
                    vec4 smoke = surface_fog(CAMERA_VISIBLE_LAYERS, VERTEX, FRAGCOORD.xy, VIEWPORT_SIZE);
                    ALBEDO = backdrop*smoke.a + (diagnostic ? vec3(0.0) : smoke.rgb);
                    if (finite_depth) {
                        // R exposes extinction directly; G is actual black/white backdrop contrast with scattering.
                        float white = step(VIEWPORT_SIZE.x*.5,FRAGCOORD.x);
                        ALBEDO = vec3(smoke.a,white*smoke.a+smoke.rgb.r,smoke.rgb.r);
                    }
                }
                """ } };
            _surface = new MeshInstance3D { Position = new(0,0,-10), Mesh = new QuadMesh { Size = new(40,40) }, MaterialOverride = _material };
            _viewport.AddChild(_surface);
            var glass = new ShaderMaterial { Shader = new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
                #include "res://Shaders/svm_fog.gdshaderinc"
                void fragment() {
                    vec4 smoke = surface_fog(CAMERA_VISIBLE_LAYERS, VERTEX, FRAGCOORD.xy, VIEWPORT_SIZE);
                    ALBEDO = vec3(1.0,0.0,0.0)*smoke.a;
                    ALPHA = .5;
                }
                """ } };
            _glass = new MeshInstance3D { Visible = false, Mesh = new QuadMesh { Size = new(40,40) }, MaterialOverride = glass };
            _viewport.AddChild(_glass);
            _background = ResponsiveSmokeRenderer.CreateBackground(_camera);
            Configure();
        }
        if (_display!.Error is { } error) return Finish(SmokeResult.Fail(error));
        if (ResponsiveSmokeRenderer.VolumeError is { } volumeError) return Finish(SmokeResult.Fail(volumeError));
        if (_phase is >= 47 and <= 62 or >= 67 and <= 72)
        {
            ResponsiveSmokeField field = ResponsiveSmokeWorld.For(_world.Level).Fields[_emitters[0]];
            if (field.SampleDensity(field.Center,2) < .99f)
                return Finish(SmokeResult.Fail($"phase={_phase}: finite-depth fixture did not install its mature unit-density core"));
        }
        if (_phase is 36 or 38) return StepLifecycle();
        if (_phase == 23) ResponsiveSmokeWorld.For(_world.Level).SynchronizeTime(2 + _frames / 60f);
        if (_phase is 33 or 34)
        {
            int rate = _phase == 33 ? 30 : 144;
            ResponsiveSmokeWorld.For(_world.Level).SynchronizeTime(2 + Math.Min(_frames,rate)/(float)rate);
        }
        if (_phase == 35)
        {
            ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(_world.Level);
            float now = 2 + _frames/60f;
            world.SynchronizeTime(now);
            if (_frames%6 == 0)
            {
                _benchmarkShots.RemoveAll(carve => now >= carve.CreatedAt+carve.Hold+carve.Refill);
                for (int rifle = 0; rifle < 3; rifle++)
                    _benchmarkShots.Add(new(++_benchmarkShotSequence,ResponsiveSmokeCarveKind.Bullet,
                        new((rifle-1)*80,0,0),new((_frames%30-15)*2,-1000,(rifle-1)*18),12,6,now,.1f,.4f,null));
                uint revision = ++_benchmarkCarveRevision;
                foreach (SSmokeEmitter emitter in _emitters) world.ApplyCarves(emitter,revision,now,_benchmarkShots);
            }
        }
        _binding!.Update(false, default, 0, 0);
        ResponsiveSmokeRenderer.BindView(_binding.Slot, _camera!, null, _phase == 8 ? 36 : _phase is 9 or 31 ? 38 : 5, _world.Level);
        if (_phase == 18)
        {
            if (_auxiliary is null)
            {
                _auxiliary = new SubViewport { Size = new(256,256), World3D = _viewport!.FindWorld3D(),
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                context.Tree.Root.AddChild(_auxiliary);
                _auxiliaryCamera = new Camera3D { Current = true, Position = new(1,0,0), CullMask = RenderLayers.MainView,
                    Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes() };
                _auxiliary.AddChild(_auxiliaryCamera);
                _auxiliaryBinding = new SurfaceFogRegistry(_auxiliaryCamera);
            }
            _auxiliaryBinding!.Update(false,default,0,0);
            ResponsiveSmokeRenderer.BindView(_auxiliaryBinding.Slot,_auxiliaryCamera!,null,38,_world.Level);
        }
        bool immediate = _phase is 22 or >= 24 and <= 32;
        int settleFrames = _phase == 33 ? 31 : _phase == 34 ? 145 : _phase == 23 ? 20 : immediate ? 1 : _benchmark ? 40 : 10;
        if (++_frames <= settleFrames)
        { _lastDrawTime = ResponsiveSmokeWorld.For(_world.Level).CurrentTime; return SmokeResult.Running; }
        if (_phase is 21 or 28 or 33 or 34 or 40 && !_benchmark)
        {
            if (_phase is 21 or 40 && _frozenFlow is not null && _frames < _repeatFlowAfterFrame) return SmokeResult.Running;
            if (!_flowCaptureQueued)
            {
                _flowCaptureQueued = true;
                ResponsiveSmokeVisualFlow.Capture(state => Volatile.Write(ref _flowCapture,new FlowCapture(state)));
                return SmokeResult.Running;
            }
            if (Volatile.Read(ref _flowCapture) is not { } captured) return SmokeResult.Running;
            SmokeFlowReadback flow = captured.State;
            if (_phase == 21)
            {
                if (_frozenFlow is null)
                {
                    if (flow.RgbaHalf.Length == 0) return Finish(SmokeResult.Fail("visual flow did not allocate a readable volume"));
                    _frozenFlow = flow.RgbaHalf; _repeatFlowAfterFrame = _frames+3;
                    _flowCaptureQueued = false; Volatile.Write(ref _flowCapture,null);
                    return SmokeResult.Running;
                }
                if (!_frozenFlow.AsSpan().SequenceEqual(flow.RgbaHalf))
                    return Finish(SmokeResult.Fail("rendering the same smoke time advanced GPU flow more than once"));
            }
            ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte,Half>(flow.RgbaHalf.AsSpan());
            if (_phase == 40)
            {
                if (_frozenFlow is null)
                {
                    if (flow.RgbaHalf.Length == 0) return Finish(SmokeResult.Fail("rim preservation fixture did not have GPU flow"));
                    _frozenFlow = flow.RgbaHalf; _repeatFlowAfterFrame = _frames+3;
                    ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(_world.Level);
                    foreach (SSmokeEmitter emitter in _emitters)
                    {
                        ResponsiveSmokeSnapshot changed = world.Fields[emitter].Capture(2);
                        changed.Cells[0] = 0; // A distant corner changes topology while every measured rim cell stays free.
                        world.ApplySnapshot(emitter,changed with { Revision = changed.Revision+1 });
                    }
                    _flowCaptureQueued = false; Volatile.Write(ref _flowCapture,null);
                    return SmokeResult.Running;
                }
                ReadOnlySpan<Half> before = MemoryMarshal.Cast<byte,Half>(_frozenFlow.AsSpan());
                int compared = 0;
                float worst = 0;
                foreach (SmokeRenderCloud cloud in SmokeRenderCache.Capture(_world.Level).Clouds)
                {
                    int slot = ResponsiveSmokeVisualFlow.GetSlot(cloud.Identity), n = flow.GridSize;
                    if (slot < 0 || n == 0 || before.Length != values.Length)
                        return Finish(SmokeResult.Fail("rim preservation changed its flow allocation"));
                    for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        Vector3 relative = (new Vector3(x+.5f,y+.5f,z+.5f)/n-Vector3.One*.5f)*625;
                        if (relative.Length() is < 280 or > 295) continue;
                        int at = (x+n*(y+n*(z+slot*n)))*4+3;
                        if ((float)before[at] <= .01f) continue;
                        compared++;
                        worst = Math.Max(worst,MathF.Abs((float)values[at]-(float)before[at]));
                    }
                }
                if (compared == 0 || worst > .00001f)
                    return Finish(SmokeResult.Fail($"zero-time topology update decayed persistent rim detail: cells={compared}, difference={worst:F6}"));
                GD.Print($"RESPONSIVE_SMOKE zero_time_rim_cells={compared} density_error={worst:F6}");
            }
            if (_phase == 28) foreach (SmokeRenderCloud cloud in SmokeRenderCache.Capture(_world.Level).Clouds)
            {
                int slot = ResponsiveSmokeVisualFlow.GetSlot(cloud.Identity), count = flow.GridSize*flow.GridSize*flow.GridSize*4;
                if (slot < 0 || count == 0 || (slot+1)*count > values.Length)
                    return Finish(SmokeResult.Fail("blocked-obstacle GPU readback did not contain its active cloud"));
                for (int i = slot*count; i < (slot+1)*count; i++)
                    if (!float.IsFinite((float)values[i]) || MathF.Abs((float)values[i]) > .0001f)
                        return Finish(SmokeResult.Fail($"blocked-obstacle GPU flow retained velocity/detail: component={i}, value={(float)values[i]}"));
            }
            if (_phase is 33 or 34)
            {
                IReadOnlyList<SmokeRenderCloud> clouds = SmokeRenderCache.Capture(_world.Level).Clouds;
                float worstDensity = 0;
                for (int cloudIndex = 0; cloudIndex < clouds.Count; cloudIndex++)
                {
                    int slot = ResponsiveSmokeVisualFlow.GetSlot(clouds[cloudIndex].Identity);
                    int bytes = flow.GridSize*flow.GridSize*flow.GridSize*8;
                    if (slot < 0 || bytes == 0 || (slot+1)*bytes > flow.RgbaHalf.Length)
                        return Finish(SmokeResult.Fail("frame-rate GPU readback did not contain its active cloud"));
                    ReadOnlySpan<byte> current = flow.RgbaHalf.AsSpan(slot*bytes,bytes);
                    if (_phase == 33) _flowRateReference.Add(current.ToArray());
                    else
                    {
                        if (cloudIndex >= _flowRateReference.Count || _flowRateReference[cloudIndex].Length != bytes)
                            return Finish(SmokeResult.Fail("frame-rate GPU reference did not match the replayed cloud"));
                        ReadOnlySpan<Half> expected = MemoryMarshal.Cast<byte,Half>(_flowRateReference[cloudIndex].AsSpan());
                        ReadOnlySpan<Half> actual = MemoryMarshal.Cast<byte,Half>(current);
                        for (int i = 3; i < expected.Length; i += 4)
                        {
                            if (!float.IsFinite((float)actual[i])) return Finish(SmokeResult.Fail("frame-rate GPU detail became nonfinite"));
                            worstDensity = Math.Max(worstDensity,MathF.Abs((float)expected[i]-(float)actual[i]));
                        }
                    }
                }
                if (_phase == 34)
                {
                    if (worstDensity > .002f) return Finish(SmokeResult.Fail($"30/144-frame visual flow diverged by {worstDensity:F5} after the same simulated second"));
                    GD.Print($"RESPONSIVE_SMOKE flow_30_144_density_error={worstDensity:F5}");
                }
            }
        }
        double surfaceGpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport!.GetViewportRid());
        double prefixGpu = ResponsiveSmokeVolumeCache.LastGpuMilliseconds, flowGpu = ResponsiveSmokeVisualFlow.LastGpuMilliseconds;
        _gpu.Add(surfaceGpu+prefixGpu+flowGpu);
        _surfaceGpu.Add(surfaceGpu); _prefixGpu.Add(prefixGpu); _flowGpu.Add(flowGpu);
        if (_benchmark && !immediate && _phase != 23 && _frames < 100)
        { _lastDrawTime = ResponsiveSmokeWorld.For(_world.Level).CurrentTime; return SmokeResult.Running; }
        using Image shot = _viewport!.GetTexture().GetImage();
        string output = Path.Combine(RenderSmokeArgs.RepoRoot,"local","responsive-smoke",_benchmark ? "benchmark" : "render");
        Directory.CreateDirectory(output);
        shot.SavePng(Path.Combine(output,$"phase-{_phase}.png"));
        Color pixel = shot.GetPixel(shot.GetWidth()/2,shot.GetHeight()/2);
        string? failure = _phase switch
        {
            0 when pixel.R > .025f => "mature smoke did not obscure the background",
            1 when pixel.R < .94f => "Low erased the bullet opening",
            2 when MathF.Abs(pixel.R-_lowHole) > .02f => "Medium and Low disagree on the bullet opening",
            3 when pixel.R > .025f => "bullet opening did not refill",
            4 when !_benchmark && pixel.R < .94f => "blast did not clear the cloud",
            5 when pixel.R > .025f => "blast did not refill",
            6 when pixel.R < .98f => "smoke occluded a foreground surface",
            7 when !_benchmark && (pixel.R > .15f || MathF.Abs(pixel.R-ReferenceTransmission(shot.GetWidth()/2,shot.GetHeight()/2)) > .02f) => "inside camera did not integrate only its remaining cloud segment",
            8 when !_benchmark && MathF.Abs(pixel.R-MathF.Pow(ReferenceTransmission(shot.GetWidth()/2,shot.GetHeight()/2),.2f)) > .02f => "thermal did not preserve 0.2 heat opacity",
            9 when pixel.R < .98f => "EMF should see through smoke",
            10 when pixel.R is < .02f or > .6f => "empty-background scattering was missing",
            16 when pixel.R is < .47f or > .54f || pixel.G > .025f => "foreground alpha surface was incorrectly obscured",
            17 when pixel.R > .025f => "transparent surface behind smoke ignored fragment depth",
            18 when pixel.R > .025f => "secondary EMF camera changed the primary camera's smoke",
            19 when pixel.R < .98f || ResponsiveSmokeRenderer.AllocatedFieldBytes != 0 => "disabled smoke retained volume storage or extinction",
            20 when pixel.R > .025f => "re-enabled smoke did not rebuild its released field storage",
            21 when ResponsiveSmokeRenderer.HistoryAcceptedViews <= _acceptedAtPhaseStart => "stable view never accepted temporal history",
            22 when pixel.R < .94f => "temporal history obscured the first rendered bullet opening",
            23 when MathF.Abs(pixel.R - ReferenceTransmission(shot.GetWidth()/2,shot.GetHeight()/2,_lastDrawTime)) > .02f => "evolving visual flow changed authoritative optical depth",
            24 when pixel.R > .02f => "camera cut retained scattered light from its previous position",
            25 or 26 or 27 or 29 or 30 or 32 or 37 or 39 when pixel.R > .025f => "history reset changed shared smoke extinction",
            28 when pixel.R < .98f => "advected flow survived authoritative blocked geometry",
            31 when pixel.R < .98f => "EMF camera cut retained normal smoke history",
            _ => null,
        };
        if (failure is not null) return Finish(SmokeResult.Fail($"phase={_phase}: {failure}; pixel={pixel}"));
        if (_phase == 23 && (ResponsiveSmokeVisualFlow.Dispatches <= _flowDispatchesAtPhaseStart+2*_emitters.Count ||
            ResponsiveSmokeVisualFlow.AppliedImpulses != _flowImpulsesAtPhaseStart))
            return Finish(SmokeResult.Fail("evolving visual flow did not advance, or replayed an already-applied bullet impulse"));
        if (_phase is 22 or 24 or 25 or 26 or 27 or 28 or 29 or 30 or 32 &&
            ResponsiveSmokeRenderer.HistoryRejectedViews <= _rejectedAtPhaseStart)
            return Finish(SmokeResult.Fail($"phase={_phase}: changed view/cloud state did not invalidate temporal history"));
        if (_phase == 18)
        {
            using Image auxiliary = _auxiliary!.GetTexture().GetImage();
            auxiliary.SavePng(Path.Combine(output,"auxiliary-emf.png"));
            if (auxiliary.GetPixel(128,128).R < .98f) return Finish(SmokeResult.Fail("secondary EMF camera inherited primary smoke"));
        }
        if (_phase is 12 or 13)
        {
            // At 256 px and 18 m this 24 uu opening is about two pixels wide. Compare the aperture and its
            // feather against independent 1 uu CPU integration, not merely against another render quality.
            float worst = 0;
            for (int offset = 0; offset < 3; offset++)
            {
                int x = shot.GetWidth()/2+offset, y = shot.GetHeight()/2;
                float expected = ReferenceTransmission(x,y);
                float actual = shot.GetPixel(x,y).R;
                worst = Math.Max(worst,MathF.Abs(actual-expected));
            }
            if (worst > .02f) return Finish(SmokeResult.Fail($"phase={_phase}: two-pixel opening differs from CPU integral by {worst:F4}"));
            GD.Print($"RESPONSIVE_SMOKE two_pixel_cpu_error={worst:F4} quality={SettingsStore.Current.SmokeQuality}");
        }
        if (_phase is >= 41 and <= 46)
        {
            // A two-unit solid slab lies wholly inside one 25-unit voxel. Rays run parallel to its faces,
            // within 0.5 uu of the surface, so treating the entire cell as solid creates an obvious clear gap.
            // Conversely, borrowing the wrong-side donor or blurring across the slab makes the clear cases dark.
            float worst = 0;
            for (int offset = -2; offset <= 2; offset++)
            {
                int x = shot.GetWidth()/2+offset, y = shot.GetHeight()/2;
                float expected = ReferenceTransmission(x,y), actual = shot.GetPixel(x,y).R;
                bool occupied = _phase is 41 or 43 or 45;
                if (occupied ? expected > .01f : expected < .99f)
                    return Finish(SmokeResult.Fail($"phase={_phase}: boundary fixture has unexpected CPU transmission {expected:F4}"));
                worst = Math.Max(worst,MathF.Abs(actual-expected));
            }
            if (worst > .02f) return Finish(SmokeResult.Fail($"phase={_phase}: partial-cell wall contact/solid transmittance differs from CPU by {worst:F4}"));
            GD.Print($"RESPONSIVE_SMOKE boundary_cpu_error={worst:F5} phase={_phase}");
        }
        if (_phase is >= 47 and <= 54)
        {
            int fixture = _phase <= 52 ? _phase-47 : 0;
            bool shell = fixture >= 3;
            int depthIndex = fixture%3;
            int depth = depthIndex == 0 ? 75 : depthIndex == 1 ? 100 : 150;
            int x = shot.GetWidth()/2, y = shot.GetHeight()/2;
            float expected = ReferenceTransmission(x,y);
            Color white = shot.GetPixel(x,y), black = shot.GetPixel(x-1,y);
            float contrast = MathF.Abs(white.G-black.G);
            float maximum = shell ? depthIndex == 0 ? .10f : depthIndex == 1 ? .06f : .025f : .02f;
            if (MathF.Abs(white.R-expected) > .02f || MathF.Abs(contrast-expected) > .02f)
                return Finish(SmokeResult.Fail($"phase={_phase}: finite-depth smoke disagrees with surface-limited CPU: T={white.R:F5}, contrast={contrast:F5}, CPU={expected:F5}"));
            if (white.R > maximum || contrast > maximum || expected > maximum)
                return Finish(SmokeResult.Fail($"phase={_phase}: {depth} uu {(shell ? "shell" : "core")} path leaves excessive backdrop contrast: T={white.R:F5}, contrast={contrast:F5}, CPU={expected:F5}, maximum={maximum:F5}"));
            GD.Print($"RESPONSIVE_SMOKE finite_depth={depth} shell={shell} clouds={_emitters.Count} CPU_T={expected:F5} GPU_T={white.R:F5} contrast={contrast:F5} phase={_phase}");
        }
        if (_phase is >= 55 and <= 66)
        {
            int x = shot.GetWidth()/2, y = shot.GetHeight()/2;
            float expected = ReferenceTransmission(x,y);
            Color white = shot.GetPixel(x,y), black = shot.GetPixel(x-1,y);
            float contrast = MathF.Abs(white.G-black.G);
            if (MathF.Abs(white.R-expected) > .02f || MathF.Abs(contrast-expected) > .02f)
                return Finish(SmokeResult.Fail($"phase={_phase}: shallow/cleared finite-depth error: T={white.R:F5}, contrast={contrast:F5}, CPU={expected:F5}, quality={SettingsStore.Current.SmokeQuality}"));
            if (_phase <= 60)
            {
                int index = (_phase-55)%3;
                int depth = index == 0 ? 10 : index == 1 ? 20 : 35;
                float analytic = MathF.Exp(-ResponsiveSmokeField.Extinction*depth);
                if (MathF.Abs(expected-analytic) > .001f)
                    return Finish(SmokeResult.Fail($"phase={_phase}: {depth} uu core fixture lost constant unit density: CPU={expected:F5}, analytic={analytic:F5}"));
            }
            else if (_phase <= 64 && (expected < .999f || white.R < .98f || contrast < .98f))
                return Finish(SmokeResult.Fail($"phase={_phase}: surface before smoke or within cleared blast retained extinction: T={white.R:F5}, contrast={contrast:F5}, CPU={expected:F5}"));
            else if (_phase >= 65 && (expected <= .001f || expected >= .99f))
                return Finish(SmokeResult.Fail($"phase={_phase}: partial smoke before cleared surface fixture was not partial: CPU={expected:F5}"));
            GD.Print($"RESPONSIVE_SMOKE shallow_surface CPU_T={expected:F5} GPU_T={white.R:F5} contrast={contrast:F5} phase={_phase} quality={SettingsStore.Current.SmokeQuality}");
        }
        if (_phase is >= 67 and <= 72)
        {
            float age = MovingRimAge();
            int center = shot.GetWidth()/2, y = shot.GetHeight()/2;
            float worst = 0;
            for (int offset = -2; offset <= 2; offset++)
            {
                float expected = ReferenceTransmission(center+offset,y,age);
                worst = Math.Max(worst,MathF.Abs(shot.GetPixel(center+offset,y).R-expected));
            }
            float cpu = ReferenceTransmission(center,y,age);
            float contrast = MathF.Abs(shot.GetPixel(center,y).G-shot.GetPixel(center-1,y).G);
            if (worst > .02f || MathF.Abs(contrast-cpu) > .02f)
                return Finish(SmokeResult.Fail($"phase={_phase}: moving rim at age {age:F1} differs from finite 75 uu CPU integral: worst T error={worst:F5}, contrast={contrast:F5}, CPU={cpu:F5}, quality={SettingsStore.Current.SmokeQuality}"));
            GD.Print($"RESPONSIVE_SMOKE moving_rim age={age:F1} depth=75 CPU_T={cpu:F5} GPU_T={shot.GetPixel(center,y).R:F5} contrast={contrast:F5} maximum_error={worst:F5} phase={_phase} quality={SettingsStore.Current.SmokeQuality}");
        }
        if (_phase == 1) _lowHole = pixel.R;
        _gpu.Sort();
        double p95 = _gpu[Math.Min(_gpu.Count-1,(int)(_gpu.Count*.95))];
        GD.Print($"RESPONSIVE_SMOKE phase={_phase} clouds={_emitters.Count} quality={SettingsStore.Current.SmokeQuality} gpu_p95_ms={p95:F3} viewport_p95_ms={Percentile95(_surfaceGpu):F3} prefix_p95_ms={Percentile95(_prefixGpu):F3} flow_p95_ms={Percentile95(_flowGpu):F3} upload_ms={ResponsiveSmokeRenderer.LastUploadMilliseconds:F3} fields_bytes={ResponsiveSmokeRenderer.LastFieldUploadBytes} frame_bytes={ResponsiveSmokeRenderer.LastFrameUploadBytes}");
        if (_phase == 35)
            GD.Print($"RESPONSIVE_SMOKE live_flow_steps={ResponsiveSmokeVisualFlow.Dispatches-_flowDispatchesAtPhaseStart} new_cloud_impulses={ResponsiveSmokeVisualFlow.AppliedImpulses-_flowImpulsesAtPhaseStart} active_shots={_benchmarkShots.Count}; 60Hz published time, three rifles at10Hz, final60frames sampled; completed GPU frame timestamps");
        int phaseCount = _benchmark ? 34 : 72;
        if (++_phase >= (_benchmark ? 36 : 73)) return Finish(SmokeResult.Pass(_benchmark
            ? $"{phaseCount} benchmark phases; phase 35 measures six clouds under sustained fire"
            : $"{phaseCount} rendered scenarios: shared density, Low/Medium holes, blast/refill, depth/vision/alpha surfaces, two-pixel CPU parity, sustained overlap, two cameras, resource release, evolving/idempotent GPU flow, immediate holes, temporal reuse/cuts/resizing/quality/rewind, blocked geometry, cloud identity replacement, queued initialization/draw teardown/rebind, zero-time rim preservation, partial-cell wall contact/both-side/solid/corner parity, finite-depth core/shell contrast with distant clouds, shallow/before-entry/cleared surfaces, and moving rim at ages 1/3/6 on Low and Medium"));
        if (_benchmark && _phase == 33) _phase = 35; // Diagnostic rate-readbacks are excluded from performance runs.
        if (!_benchmark && _phase == 35) _phase = 36; // Live sustained-fire timing is benchmark-only.
        _frames = 0; ClearTimings(); Configure();
        return SmokeResult.Running;
    }

    private static ResponsiveSmokeCarve Hole() => new(1,ResponsiveSmokeCarveKind.Bullet,new(0,0,0),new(0,-1000,0),12,6,2,.1f,.4f,null);

    private float MovingRimAge() => ((_phase-67)%3) switch { 0 => 1, 1 => 3, _ => 6 };

    private SmokeResult StepLifecycle()
    {
        if (_lifecycleStage == 0)
        {
            _binding?.Dispose(); _binding = null;
            QueueCleanupBarrier(); _lifecycleStage = 1;
            return SmokeResult.Running;
        }
        if (!Volatile.Read(ref _cleanupDrained))
            return ++_lifecycleWaitFrames < 600 ? SmokeResult.Running : Finish(SmokeResult.Fail("smoke teardown render-thread barrier did not complete"));
        if (ResponsiveSmokeVolumeCache.HasResources || ResponsiveSmokeVisualFlow.HasResources || ResponsiveSmokeRenderer.AllocatedFieldBytes != 0)
            return Finish(SmokeResult.Fail($"phase={_phase}, stage={_lifecycleStage}: last-view teardown retained smoke pipeline, prefix/history or flow resources"));
        if (_lifecycleStage == 1)
        {
            _binding = new SurfaceFogRegistry(_camera!);
            ResponsiveSmokeRenderer.BindView(_binding.Slot,_camera!,null,5,_world.Level);
            ResponsiveSmokeRenderer.Prewarm(_world.Level);
            if (_phase == 38) ResponsiveSmokeRenderer.FlushForDiagnostics();
            // No frame is yielded between queuing initialization/draw and disposing the sole smoke view.
            _binding.Dispose(); _binding = null;
            QueueCleanupBarrier(); _lifecycleStage = 2;
            return SmokeResult.Running;
        }
        GD.Print($"RESPONSIVE_SMOKE resource_lifecycle phase={_phase} queued_draw={_phase == 38} resources_released=true");
        _binding = new SurfaceFogRegistry(_camera!);
        _phase++; _frames = 0; ClearTimings(); Configure();
        return SmokeResult.Running;
    }

    private void QueueCleanupBarrier()
    {
        _lifecycleWaitFrames = 0; Volatile.Write(ref _cleanupDrained,false);
        RenderingServer.CallOnRenderThread(Callable.From(() => Volatile.Write(ref _cleanupDrained,true)));
    }

    private static double Percentile95(List<double> samples)
    {
        samples.Sort();
        return samples.Count == 0 ? 0 : samples[Math.Min(samples.Count-1,(int)(samples.Count*.95))];
    }

    private void ClearTimings()
    {
        _gpu.Clear(); _surfaceGpu.Clear(); _prefixGpu.Clear(); _flowGpu.Clear();
    }
    private static ResponsiveSmokeCarve[] SustainedFire()
    {
        var shots = new ResponsiveSmokeCarve[15];
        for (int rifle = 0; rifle < 3; rifle++) for (int shot = 0; shot < 5; shot++)
        {
            int i = rifle*5+shot;
            shots[i] = new((uint)(i+1),ResponsiveSmokeCarveKind.Bullet,new((rifle-1)*80,0,0),
                new((shot-2)*18,-1000,(rifle-1)*18),12,6,2-shot*.1f,.1f,.4f,null);
        }
        return shots;
    }

    private float ReferenceTransmission(int x, int y, float now = 2)
    {
        Vector2I size = _viewport!.Size;
        Projection projection = _camera!.GetCameraProjection();
        Vector3 local = new((2*(x+.5f)/size.X-1)/projection.X.X,-(2*(y+.5f)/size.Y-1)/projection.Y.Y,-1);
        Vector3 worldRay = _camera.GlobalBasis*local.Normalized();
        FVector ray = UeSpace.DirectionToUe(worldRay);
        FVector eye = UeSpace.ToUe(_camera.GlobalPosition);
        Vector3 normal = _surface!.GlobalBasis.Z.Normalized();
        float denominator = worldRay.Dot(normal);
        float limit = MathF.Abs(denominator) < .000001f ? 0 : Math.Max(0,(_surface.GlobalPosition-_camera.GlobalPosition).Dot(normal)/denominator*100);
        float tau = 0;
        ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(_world.Level);
        for (float distance = 0; distance < limit; distance += 1)
        {
            float step = Math.Min(1,limit-distance);
            float density = 0;
            foreach (ResponsiveSmokeField field in world.Fields.Values) density += field.SampleDensity(eye+ray*(distance+step*.5f),now);
            tau += Math.Min(density,1)*ResponsiveSmokeField.Extinction*step;
        }
        return MathF.Exp(-tau);
    }
    private static ResponsiveSmokeCarve Blast()
    {
        var mask = new byte[ResponsiveSmokeField.CellCount]; Array.Fill(mask,(byte)1);
        return new(2,ResponsiveSmokeCarveKind.Blast,new(0,-500,0),new(0,-500,0),300,25,2,.6f,1.4f,mask);
    }
    private void Apply(ResponsiveSmokeCarve[] carves, float now)
    {
        var cells = new byte[ResponsiveSmokeField.CellCount]; if (_phase != 28) Array.Fill(cells,(byte)1);
        var activation = new ushort[ResponsiveSmokeField.CellCount];
        foreach (SSmokeEmitter emitter in _emitters)
        {
            var template = new ResponsiveSmokeField(emitter.Location,0);
            // Boundary fixtures use revisions above 200. Later finite-depth fixtures must replace them,
            // while the benchmark's original low revisions remain below its live-shot revision sequence.
            uint revision = (uint)(_phase >= 47 ? _phase+1000 : _phase+1);
            ResponsiveSmokeWorld.For(_world.Level).ApplySnapshot(emitter,new(revision,0,now,template.Origin,template.Center,cells,activation,carves));
        }
        ResponsiveSmokeWorld.For(_world.Level).SynchronizeTime(now);
    }

    private void Configure()
    {
        _replication!.ResponsiveSmokeEnabled = _phase != 19;
        _acceptedAtPhaseStart = ResponsiveSmokeRenderer.HistoryAcceptedViews;
        _rejectedAtPhaseStart = ResponsiveSmokeRenderer.HistoryRejectedViews;
        _flowDispatchesAtPhaseStart = ResponsiveSmokeVisualFlow.Dispatches;
        _flowImpulsesAtPhaseStart = ResponsiveSmokeVisualFlow.AppliedImpulses;
        _flowCaptureQueued = false; Volatile.Write(ref _flowCapture,null);
        _frozenFlow = null;
        _lifecycleStage = 0;
        SettingsStore.Current.SmokeQuality = _phase == 26 ? ResponsiveSmokeQuality.High :
            _phase is 1 or 12 or 14 or 45 or >= 55 and <= 57 or 61 or 63 or 65 or >= 67 and <= 69 ? ResponsiveSmokeQuality.Low : ResponsiveSmokeQuality.Medium;
        if (_phase is 12 or 14)
            foreach (SSmokeEmitter emitter in _emitters) emitter.Location = new(0,_phase == 12 ? -1800 : -500,0);
        if (_phase == 33) _flowRateReference.Clear();
        if (_phase is 30 or 33 or 34)
            for (int i = 0; i < _emitters.Count; i++)
            {
                FVector center = _emitters[i].Location; _emitters[i].Destroy();
                _emitters[i] = _world.Level.Spawn<SSmokeEmitter>(center)!;
            }
        ResponsiveSmokeCarve[] carves = _phase is 1 or 2 or 3 or 22 or 23 ? [Hole()] : _phase is 4 or 5
            ? [Blast()] : _phase is 12 or 13 ? [Hole() with { End = new(0,-2600,0) }] : _phase is 14 or 15 ? SustainedFire() : [];
        if (_phase == 53) _emitters.Add(_world.Level.Spawn<SSmokeEmitter>(new(2000,-4000,0))!);
        if (_phase == 54) _emitters[1].Location = new(0,-5000,0);
        if (_phase == 55)
        {
            ResponsiveSmokeWorld.For(_world.Level).Unregister(_emitters[1]);
            _emitters[1].Destroy(); _emitters.RemoveAt(1);
        }
        if (_phase is >= 63 and <= 66) carves = [Blast() with { Radius = 100, Feather = 10 }];
        Apply(carves,_phase >= 67 ? MovingRimAge() : _phase == 5 ? 4.1f : _phase == 3 ? 3 : _phase == 29 ? 1 : 2);
        if (_phase is >= 41 and <= 46) ApplyBoundaryFixture();
        if (_phase == 46) ApplyBlockedCornerFixture();
        if (_surface is null || _camera is null || _viewport is null) return;
        _viewport.Size = _phase == 27 ? new(384,216) : _phase is 12 or 13 || !_benchmark ? new(256,256) : new(1920,1080);
        _camera.Position = _phase == 24 ? new(10,0,0) : _phase == 7 ? new(0,0,-5) : Vector3.Zero;
        _camera.Fov = _phase >= 41 ? 1 : 75;
        if (_phase is >= 41 and <= 46)
            _camera.Position = new((_phase == 41 ? 4.75f : _phase is 42 or 43 ? 7.75f : _phase == 44 ? 6.25f : 5.75f)/100f,0,0);
        if (_phase == 46) _camera.Position = new(.24f,.24f,0);
        _surface.Position = new(0,0,_phase == 6 ? -1 : _phase is 12 or 13 ? -30 : -10);
        _surface.Visible = _phase != 10;
        if (_glass is not null) { _glass.Visible = _phase is 16 or 17; _glass.Position = new(0,0,_phase == 16 ? -1 : -9); }
        if (_phase == 19 && _auxiliary is not null) { _auxiliaryBinding!.Dispose(); _auxiliary.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; }
        _material!.SetShaderParameter("diagnostic",_phase is not (11 or 24));
        _material.SetShaderParameter("finite_depth",_phase >= 47);
        _material.SetShaderParameter("backdrop",_phase == 24 ? Vector3.Zero : Vector3.One);
        _viewport.Msaa3D = _phase is >= 6 and < 12 ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
        if (_phase is >= 47 and <= 54)
        {
            int fixture = _phase <= 52 ? _phase-47 : 0;
            float depth = fixture%3 == 0 ? 75 : fixture%3 == 1 ? 100 : 150;
            _camera.Position = new(fixture >= 3 ? 2.10f : 0,0,-4.25f);
            _surface.Position = _camera.Position+new Vector3(0,0,-depth/100);
        }
        if (_phase is >= 55 and <= 60)
        {
            int index = (_phase-55)%3;
            float depth = index == 0 ? 10 : index == 1 ? 20 : 35;
            _camera.Position = new(0,0,-4.25f);
            _surface.Position = _camera.Position+new Vector3(0,0,-depth/100);
        }
        if (_phase is 61 or 62)
        {
            _camera.Position = Vector3.Zero;
            _surface.Position = new(0,0,-1);
        }
        if (_phase is >= 63 and <= 66)
        {
            // The surface is inside a genuine masked blast, with dense smoke still behind the surface.
            // The last pair first traverses a short opaque segment before reaching the cleared interval.
            _camera.Position = new(0,0,_phase <= 64 ? -4.25f : -3.5f);
            _surface.Position = new(0,0,-5);
        }
        if (_phase >= 67)
        {
            // A finite surface samples the actual moving rim, rather than the invariant dense core.
            // Radius 245–256 uu lies within the sculpted envelope's changing transition band.
            _camera.Position = new(2.45f,0,-4.25f);
            _surface.Position = new(2.45f,0,-5);
        }
    }

    private void ApplyBoundaryFixture()
    {
        const float wall = 6.25f;
        bool bothSidesReached = _phase is 43 or 44;
        float halfThickness = _phase == 45 ? 0 : 1;
        foreach (SSmokeEmitter emitter in _emitters)
        {
            var template = new ResponsiveSmokeField(emitter.Location,0);
            var cells = new byte[ResponsiveSmokeField.CellCount];
            var activation = new ushort[cells.Length];
            var boundaries = new List<ResponsiveSmokeBoundaryCell>();
            for (int z = 0; z < 25; z++) for (int y = 0; y < 25; y++) for (int x = 0; x < 25; x++)
            {
                int index = x+25*(y+25*z);
                float lo = template.Origin.X+x*25, hi = lo+25, center = lo+12.5f;
                if (lo < wall+halfThickness && hi > wall-halfThickness)
                {
                    ResponsiveSmokePlane[] planes = halfThickness == 0
                        ? [new(new FVector(1,0,0),wall-center)]
                        : [new(new FVector(1,0,0),wall-halfThickness-center),new(new FVector(1,0,0),wall+halfThickness-center)];
                    boundaries.Add(new((ushort)index,planes));
                    activation[index] = ResponsiveSmokeField.Unreached;
                }
                else
                {
                    cells[index] = 1;
                    activation[index] = hi <= wall || bothSidesReached ? (ushort)0 : ResponsiveSmokeField.Unreached;
                }
            }
            ResponsiveSmokeWorld.For(_world.Level).ApplySnapshot(emitter,new((uint)(_phase+100),0,2,template.Origin,template.Center,cells,activation,[],boundaries.ToArray()));
        }
    }

    private void ApplyBlockedCornerFixture()
    {
        foreach (SSmokeEmitter emitter in _emitters)
        {
            var template = new ResponsiveSmokeField(emitter.Location,0);
            var cells = new byte[ResponsiveSmokeField.CellCount]; Array.Fill(cells,(byte)1);
            var activation = new ushort[cells.Length]; Array.Fill(activation,ResponsiveSmokeField.Unreached);
            // Along the whole viewing ray, the nearest cell (12, y, 12) is free but unreached. Its diagonal
            // neighbor has smoke, separated by two solid face neighbors. Trilinear interpolation cannot borrow
            // that smoke through the blocked corner even though the donor itself is free and reached.
            for (int y = 0; y < 25; y++)
            {
                cells[13+25*(y+25*12)] = 0;
                cells[12+25*(y+25*13)] = 0;
                activation[13+25*(y+25*13)] = 0;
            }
            ResponsiveSmokeWorld.For(_world.Level).ApplySnapshot(emitter,new((uint)(_phase+200),0,2,template.Origin,template.Center,cells,activation,[]));
        }
    }

    private SmokeResult Finish(SmokeResult result)
    {
        SettingsStore.Current.SmokeQuality = _oldQuality;
        _binding?.Dispose(); _auxiliaryBinding?.Dispose(); _auxiliary?.QueueFree(); _display?.Release(); _viewport?.QueueFree();
        return result;
    }
}
