using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Options;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>Actual light selection, probe upload and GPU scattering across differently lit regions.</summary>
public sealed class ResponsiveSmokeLightingSmoke : SmokeTest
{
    public const string TestName = "responsive-smoke-lighting";
    public override string Name => TestName;
    private readonly World _world = new(9342, ENetMode.NM_Client);
    private RenderMap? _map;
    private SSmokeEmitter? _emitter;
    private readonly List<SSmokeEmitter> _overlap = [];
    private readonly List<double> _gpu = [];
    private readonly bool _benchmark = System.Environment.GetEnvironmentVariable("SVM_RESPONSIVE_SMOKE_BENCH") == "1";
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private SurfaceFogRegistry? _binding;
    private DisplaySpaceEffect? _display;
    private ShaderMaterial? _material;
    private ResponsiveSmokeQuality _oldQuality;
    private int _phase, _frames;
    private float _redBeforeSwitch, _rightBeforeMove, _shadowBeforeBlast;
    private float _bodyBeforeTurn, _sceneMatureTransmission;
    private bool _bodyTurned;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_map is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a rendered lighting fixture");
            _oldQuality = SettingsStore.Current.SmokeQuality;
            SettingsStore.Current.SmokeQuality = ResponsiveSmokeQuality.Medium;
            _world.Level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
            _emitter = _world.Level.Spawn<SSmokeEmitter>(new(0,-500,0))!;
            if (_benchmark) for (int i = 1; i < 6; i++) _overlap.Add(_world.Level.Spawn<SSmokeEmitter>(new((i%3-1)*20,-500-i*10,(i/3)*15))!);
            Apply(false);
            var lighting = new LightingDoc();
            lighting.Lights.Add(Point("red",new(-230,-500,80),0));
            lighting.Lights.Add(Point("blue",new(230,-500,80),170));
            lighting.Lights.Add(Point("baked-only",new(0,-500,300),0) with
            {
                Brightness = 64, Radius = 64, Saturation = 255,
                ApplyInGame = false, ApplyStatic = true, Dynamic = false, InitiallyOn = false,
            });
            var materials = new MaterialsDoc();
            var textures = new TextureCache("");
            _map = new RenderMap("",TestName,new Node3D(),materials,lighting,textures,
                new MaterialCompiler(materials,textures),new ActorLighting(lighting),null);
            if (ValidateProjectionCache(_map) is { } cacheError) return Finish(SmokeResult.Fail(cacheError));
            var sun = new PackedLight(new(.2f,.9f,-.3f,-1),new(.2f,.3f,.4f,1),new(0,0,0,-2));
            var a = ResponsiveSmokeLighting.Evaluate(sun,new(-20,10,37));
            var b = ResponsiveSmokeLighting.Evaluate(sun,new(400,-900,25));
            if (a.Direction.DistanceTo(b.Direction) > .00001f || a.Radiance.DistanceTo(b.Radiance) > .00001f)
                return Finish(SmokeResult.Fail("directional light changed with sample position"));
            _viewport = new SubViewport { Size = _benchmark ? new(1920,1080) : new(512,512), OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseDebanding = false };
            context.Tree.Root.AddChild(_viewport);
            RenderingServer.ViewportSetMeasureRenderTime(_viewport.GetViewportRid(),true);
            _display = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false, SurfaceFogApplied = true } };
            _camera = new Camera3D { Current = true, CullMask = RenderLayers.MainView, Near = .01f, Far = 100,
                Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
                Compositor = new Compositor { CompositorEffects = [_display] } };
            _viewport.AddChild(_camera);
            _binding = new SurfaceFogRegistry(_camera);
            _material = new ShaderMaterial { Shader = new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled;
                #define SVM_RESPONSIVE_SMOKE
                #include "res://Shaders/svm_fog.gdshaderinc"
                uniform int diagnostic = 0;
                uniform bool body_probe = false;
                uniform bool shadow_reverse = false;
                void fragment() {
                    int view = int((CAMERA_VISIBLE_LAYERS>>8u)&127u)*14;
                    vec4 frame = smoke_frame_row(view);
                    int cloud = int(frame.x);
                    if (diagnostic == 0) {
                        vec4 smoke = surface_fog(CAMERA_VISIBLE_LAYERS,VERTEX,FRAGCOORD.xy,VIEWPORT_SIZE);
                        ALBEDO = smoke.rgb;
                    } else if (diagnostic == 1) {
                        vec3 p = vec3(SCREEN_UV.x < .5 ? -150.0 : 150.0,-500.0,0.0);
                        if (body_probe) p = vec3(200.0,-500.0,0.0);
                        ALBEDO = smoke_light(cloud,p,vec3(0.0,-1.0,0.0),1.0,frame.z,int(frame.w),cloud,int(frame.y));
                    } else if (diagnostic == 2) {
                        vec3 p = vec3(SCREEN_UV.x < .5 ? 0.0 : 150.0,-500.0,0.0);
                        float transmission = smoke_light_transmission(cloud,int(frame.y),p,vec3(1.0,0.0,0.0),625.0,frame.z,int(frame.w));
                        ALBEDO = vec3(transmission);
                    } else if (diagnostic == 4) {
                        vec3 p = vec3(SCREEN_UV.x < .5 ? 0.0 : 900.0,-900.0,0.0);
                        ALBEDO = vec3(responsive_smoke_scene_light(CAMERA_VISIBLE_LAYERS,p.xzy*.01,vec3(0.0,0.0,1.0)));
                    } else if (diagnostic == 5) {
                        vec3 p = vec3(SCREEN_UV.x < .5 ? 0.0 : 900.0,SCREEN_UV.x < .5 ? -795.0 : -900.0,0.0);
                        ALBEDO = vec3(responsive_smoke_scene_light(CAMERA_VISIBLE_LAYERS,p.xzy*.01,vec3(0.0,0.0,shadow_reverse ? -1.0 : 1.0)));
                    } else if (diagnostic == 6) {
                        vec3 direction = vec3(SCREEN_UV.x < .5 ? 1.0 : -1.0,0.0,0.0);
                        ALBEDO = vec3(smoke_light_transmission(cloud,int(frame.y),vec3(0.0,-500.0,0.0),direction,625.0,frame.z,int(frame.w)));
                    } else {
                        vec3 p = vec3(SCREEN_UV.x < .5 ? -150.0 : 150.0,-500.0,0.0);
                        vec3 ambient; vec3 direct; vec3 direction; float distance;
                        smoke_sample_light_probes(cloud,p,ambient,direct,direction,distance);
                        ALBEDO = direct*.1;
                    }
                }
                """ } };
            _viewport.AddChild(new MeshInstance3D { Position = new(0,0,-10), Mesh = new QuadMesh { Size = new(40,40) }, MaterialOverride = _material });
            Configure();
        }
        if (ResponsiveSmokeRenderer.UnsupportedReason is { } unsupported) return Finish(SmokeResult.Fail(unsupported));
        if (_display!.Error is { } error) return Finish(SmokeResult.Fail(error));
        ResponsiveSmokeRenderer.Publish(_map,_world.Level);
        _binding!.Update(false,default,0,0);
        ResponsiveSmokeRenderer.BindView(_binding.Slot,_camera!,_map,RenderK.RendMapNormal,_world.Level);
        if (++_frames <= 25) return SmokeResult.Running;
        _gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport!.GetViewportRid())+ResponsiveSmokeRenderer.LastVolumeGpuMilliseconds);
        if (_benchmark && _frames < 85) return SmokeResult.Running;
        using Image image = _viewport!.GetTexture().GetImage();
        string output = Path.Combine(RenderSmokeArgs.RepoRoot,"local","responsive-smoke","lighting-"+RenderingServer.GetCurrentRenderingDriverName());
        Directory.CreateDirectory(output);
        image.SavePng(Path.Combine(output,$"phase-{_phase}.png"));
        int offset = _phase == 0 ? image.GetWidth()/8 : image.GetWidth()/4;
        Color left = Mean(image,image.GetWidth()/2-offset,image.GetHeight()/2), right = Mean(image,image.GetWidth()/2+offset,image.GetHeight()/2);
        if (_phase == 7 && !_bodyTurned)
        {
            // Keep the material point and cloud time fixed while reversing the actual map light. Comparing
            // opposite sides of a rolling cloud also changes albedo and cannot establish directional contrast.
            _bodyBeforeTurn = left.R; _bodyTurned = true;
            image.SavePng(Path.Combine(output,"phase-7-before-turn.png"));
            _map!.ActorLighting.SetSpawned([Point("sun",default,25) with
                { Effect = RenderK.LE_Sunlight, Saturation = 180, Rotation = new FRotator(0,32768,0) }]);
            _frames = 0; _gpu.Clear();
            return SmokeResult.Running;
        }
        string? failure = _phase switch
        {
            0 when ResponsiveSmokeRenderer.LastProbeUploadBytes != 0 => "unchanged frozen light probes were uploaded again",
            0 when left.R <= left.B+.01f || right.B <= right.R+.01f => "rendered cloud lost the red/blue regional lighting",
            1 when left.R <= left.B+.02f || right.B <= right.R+.02f => "probe shading did not retain different light regions",
            2 when left.R >= _redBeforeSwitch*.7f => "switched light remained in cached probes",
            // Compare each frozen material point with itself: the left and right points have different
            // procedural albedo, so their absolute contrast alone cannot establish light movement.
            3 when right.R <= _rightBeforeMove+.02f || left.R >= _redBeforeSwitch*.9f => "moved light did not follow its new region",
            4 when MathF.Abs(left.R-right.R)+MathF.Abs(left.G-right.G)+MathF.Abs(left.B-right.B) > .01f => "directional probe illumination changed across the cloud",
            5 when !ValidDensityShadow(left.R,right.R) => "density-path self-shadow left the analytic core/shell transmission bounds",
            6 when left.R <= _shadowBeforeBlast+.2f => "blast clearing did not transmit more incident light",
            7 when Math.Min(left.R,_bodyBeforeTurn) < .06f || MathF.Abs(left.R-_bodyBeforeTurn) < .01f => "same-point map-lit body lost diffuse support or directional self-shadow contrast",
            8 or 10 or 11 or 12 or 15 or 16 when left.R < .98f || right.R < .98f => "disabled, cleared or unlit scene shadow retained attenuation",
            9 or 13 when left.R < .24f || left.R > .65f || right.R < .98f => "Full scene shadow lost partial light transmission or the clear path",
            14 when left.R < .95f || right.R < .98f => "moving a light at frozen density did not refresh cached transmission",
            17 when left.R < .55f || left.R > .58f || right.R > .01f => "Fast shadow skipped the dense near segment beside a blocked wall",
            18 when left.R < _sceneMatureTransmission+.10f || left.R > .95f || right.R < .98f => "thinning Full smoke did not progressively transmit more scene light",
            19 when left.R < .98f || right.R < .98f => "expired Full smoke retained a scene shadow",
            20 or 21 when Math.Min(left.R, right.R) < .04f => "baked-only room lighting left the rendered smoke black",
            22 when Math.Max(left.R, right.R) > .01f => "switched-off baked light remained in smoke probes",
            _ => null,
        };
        GD.Print($"RESPONSIVE_SMOKE_LIGHTING phase={_phase} left={left} right={right}");
        if (_phase == 7) GD.Print($"RESPONSIVE_SMOKE_LIGHTING body_same_point before={_bodyBeforeTurn:F6} after={left.R:F6}");
        _gpu.Sort();
        GD.Print($"RESPONSIVE_SMOKE_LIGHTING_GPU phase={_phase} clouds={1+_overlap.Count} quality={SettingsStore.Current.SmokeQuality} gpu_p95_ms={_gpu[Math.Min(_gpu.Count-1,(int)(_gpu.Count*.95))]:F3} upload_ms={ResponsiveSmokeRenderer.LastUploadMilliseconds:F3}");
        if (failure is not null) return Finish(SmokeResult.Fail($"phase={_phase}: {failure}; left={left}, right={right}"));
        if (_phase == 1) { _redBeforeSwitch = left.R; _rightBeforeMove = right.R; }
        if (_phase == 5) _shadowBeforeBlast = left.R;
        if (_phase == 13) _sceneMatureTransmission = left.R;
        if (++_phase >= 23) return Finish(SmokeResult.Pass("23 rendered lighting scenarios: spatial scattering, switched/moved lighting, directional invariance, self-shadow, blast transmission, diffuse fill, partial scene shadows, cached receivers, moving-light invalidation, Fast/clear/light-off recovery, Fast wall-adjacent self-shadow, progressive thinning/expiry transmission, baked-only room illumination in Fast/Full and baked-light switching"));
        _frames = 0; _gpu.Clear(); Configure();
        return SmokeResult.Running;
    }

    private static LightEntry Point(string name,FVector position,byte hue) =>
        new(name,position,default,255,hue,0,8,0,0,1,true,false,true,true,null,0,0,false);

    private static bool ValidDensityShadow(float center, float rim)
    {
        // Three cubic segments end at 255/27, 8*255/27 and 255. From the center the first two
        // midpoints are in the unit-density core and the last is >=.55 in the inner shell, making
        // transmission less than one display step.
        // From x=150 the first midpoint is guaranteed >=.93 by the 150..180 core fade, the second
        // >=.55 by the inner shell (192.5 < 222), and the last is outside the 300 uu envelope.
        // These independent bounds hold for every allowed rolling lobe.
        // The diagnostic writes transmission directly; allow one R8 display quantization step.
        const float pixel = 1f/255f, firstSegment = 255f/27f, secondSegment = firstSegment*7;
        float minimumRim = MathF.Exp(-ResponsiveSmokeField.Extinction*(firstSegment+secondSegment));
        float maximumRim = MathF.Exp(-ResponsiveSmokeField.Extinction*(.93f*firstSegment+.55f*secondSegment));
        return center <= pixel && rim >= minimumRim-pixel && rim <= maximumRim+pixel;
    }

    private static string? ValidateProjectionCache(RenderMap map)
    {
        var world = new World(9343,ENetMode.NM_Client);
        world.Level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        SSmokeEmitter emitter = world.Level.Spawn<SSmokeEmitter>()!;
        ResponsiveSmokeWorld smoke = ResponsiveSmokeWorld.For(world.Level);
        var template = new ResponsiveSmokeField(default,0);
        var cells = new byte[ResponsiveSmokeField.CellCount]; Array.Fill(cells,(byte)1);
        var snapshot = new ResponsiveSmokeSnapshot(10,0,2,template.Origin,template.Center,cells,new ushort[cells.Length],[]);
        smoke.ApplySnapshot(emitter,snapshot);
        ResponsiveSmokeLighting.Capture(map,SmokeRenderCache.Capture(world.Level).Clouds[0]);
        long built = ResponsiveSmokeLighting.ProjectionBuilds;
        smoke.ApplyCarves(emitter,20,2,[new(1,ResponsiveSmokeCarveKind.Bullet,new(-300,0,0),new(300,0,0),12,6,2,.1f,.4f,null)]);
        ResponsiveSmokeLighting.Capture(map,SmokeRenderCache.Capture(world.Level).Clouds[0]);
        if (ResponsiveSmokeLighting.ProjectionBuilds != built) return "bullet-only frames rebuilt geometry lighting probes";
        var changed = (byte[])cells.Clone();
        for (int i = 0; i < changed.Length; i++) if (i%25 < 12) changed[i] = 0;
        smoke.ApplySnapshot(emitter,snapshot with { Revision = 15, Cells = changed });
        SmokeRenderCloud cloud = SmokeRenderCache.Capture(world.Level).Clouds[0];
        ResponsiveSmokeLighting.Capture(map,cloud);
        if (cloud.Revision != 20 || ResponsiveSmokeLighting.ProjectionBuilds != built+1)
            return "late geometry baseline failed to rebuild lighting probes beneath a newer bullet revision";
        return null;
    }

    private void Configure()
    {
        _material!.SetShaderParameter("diagnostic",_phase == 0 ? 0 : _phase is 5 or 6 ? 2 : _phase == 4 ? 3 : 1);
        _material.SetShaderParameter("body_probe",_phase == 7);
        if (_phase >= 20)
        {
            _map!.ActorLighting.SetSpawned([]);
            _map.ActorLighting.SetOn("baked-only", _phase != 22);
            if (_map.LightActor(new(0,-500,0),0,0).Count != 0)
                throw new InvalidOperationException("Baked-only smoke lighting changed character light selection");
            SettingsStore.Current.SmokeQuality = _phase == 20 ? ResponsiveSmokeQuality.Low : ResponsiveSmokeQuality.Medium;
            ResponsiveSmokeRenderer.SceneShadows = _phase == 21;
            _material.SetShaderParameter("diagnostic", _phase == 22 ? 3 : 0);
            Apply(false);
            return;
        }
        if (_phase == 2) _map!.ActorLighting.SetOn("red",false);
        if (_phase == 3)
        {
            _map!.ActorLighting.SetOn("red",true); _map.ActorLighting.SetOn("blue",false);
            _map.ActorLighting.SetPlace("red",new(230,-500,80),default);
        }
        if (_phase == 4)
        {
            _map!.ActorLighting.SetOn("red",false);
            _map.ActorLighting.SetSpawned([Point("sun",default,25) with { Effect = RenderK.LE_Sunlight, Saturation = 180 }]);
        }
        if (_phase == 6) Apply(true);
        if (_phase == 7) Apply(false);
        if (_phase == 17)
        {
            SettingsStore.Current.SmokeQuality = ResponsiveSmokeQuality.Low;
            ResponsiveSmokeRenderer.SceneShadows = false;
            _material.SetShaderParameter("diagnostic",6);
            foreach (SSmokeEmitter emitter in new[] { _emitter! }.Concat(_overlap))
            {
                var template = new ResponsiveSmokeField(emitter.Location,0);
                var cells = new byte[ResponsiveSmokeField.CellCount];
                for (int i = 0; i < cells.Length; i++)
                {
                    float x = template.Origin.X + (i%25+.5f)*25;
                    cells[i] = x >= 25 && x <= 175 ? (byte)0 : (byte)1;
                }
                ResponsiveSmokeWorld.For(_world.Level).ApplySnapshot(emitter,
                    new(270,0,2,template.Origin,template.Center,cells,new ushort[cells.Length],[]));
            }
            return;
        }
        if (_phase >= 8)
        {
            _material.SetShaderParameter("diagnostic",_phase >= 13 ? 5 : 4);
            _material.SetShaderParameter("shadow_reverse",_phase == 14);
            ResponsiveSmokeRenderer.SceneShadows = _phase is not (8 or 16);
            _map!.ActorLighting.SetOn("red",false);
            _map.ActorLighting.SetOn("blue",false);
            _map.ActorLighting.SetSpawned(_phase == 12 ? [] :
                [Point("shadow-source",_phase == 14 ? new(0,-1000,0) : default,0) with { Radius = 64, Saturation = 255 }]);
            if (_phase != 14) Apply(_phase is 10 or 15);
            if (_phase == 11)
            {
                var template = new ResponsiveSmokeField(_emitter!.Location,0);
                ResponsiveSmokeWorld.For(_world.Level).ApplySnapshot(_emitter,
                    new((uint)(_phase*10+101),0,2,template.Origin,template.Center,new byte[ResponsiveSmokeField.CellCount],new ushort[ResponsiveSmokeField.CellCount],[]));
            }
            if (_phase is 18 or 19) ResponsiveSmokeWorld.For(_world.Level).SynchronizeTime(_phase == 18 ? 9.25f : 10f);
        }
    }

    private void Apply(bool blast)
    {
        var cells = new byte[ResponsiveSmokeField.CellCount]; Array.Fill(cells,(byte)1);
        ResponsiveSmokeCarve[] carves = blast
            ? [new(1,ResponsiveSmokeCarveKind.Blast,new(_phase >= 8 ? 0 : 100,-500,0),new(_phase >= 8 ? 0 : 100,-500,0),_phase >= 8 ? 300 : 130,25,2,.6f,1.4f,cells)] : [];
        foreach (SSmokeEmitter emitter in new[] { _emitter! }.Concat(_overlap))
        {
            var template = new ResponsiveSmokeField(emitter.Location,0);
            ResponsiveSmokeWorld.For(_world.Level).ApplySnapshot(emitter,new((uint)(_phase >= 8 ? _phase*10+100 : _phase+1),0,2,template.Origin,template.Center,cells,new ushort[cells.Length],carves));
        }
        ResponsiveSmokeWorld.For(_world.Level).SynchronizeTime(2);
    }

    private static Color Mean(Image image,int x,int y)
    {
        Color result = new(0,0,0,0);
        for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) result += image.GetPixel(x+dx,y+dy);
        return result/81;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        ResponsiveSmokeRenderer.SceneShadows = false;
        SettingsStore.Current.SmokeQuality = _oldQuality;
        _binding?.Dispose(); _display?.Release(); _viewport?.QueueFree(); _map?.Root.QueueFree();
        return result;
    }
}
