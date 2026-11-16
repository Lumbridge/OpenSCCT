using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Smoke;

/// <summary>Normal-input hospital stair traversal plus fixed-pose source beam comparisons.</summary>
public sealed class Ps2HospitalWorldSmoke : SmokeTest
{
    public const string TestName = "ps2-hospital-world";
    public override string Name => TestName;
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private int _phase, _frame, _shot, _teardown;
    private double _since;
    private float _startZ, _topZ;
    private readonly bool _beamsOnly = System.Environment.GetEnvironmentVariable("SVM_MHO_BEAMS_ONLY") == "1";
    private readonly bool _materialOnly = System.Environment.GetEnvironmentVariable("SVM_MHO_MATERIAL_ONLY") == "1";
    private readonly string _renderer = System.Environment.GetEnvironmentVariable("SVM_MHO_RENDERER") == "Fast" ? "Fast" : "Standard";
    private readonly int _firstShot = int.TryParse(System.Environment.GetEnvironmentVariable("SVM_MHO_BEAM_START"),out int first) ? first : 0;
    private readonly int _lastShot = int.TryParse(System.Environment.GetEnvironmentVariable("SVM_MHO_BEAM_END"),out int last) ? last : 14;
    private readonly List<object> _rows = [];
    private static readonly FVector[] Beams = [new(1360.5266f,-48.7901f,1290.8416f),new(2480.7957f,-2782.9746f,1290.918f),
        new(4329.0747f,-4479.4595f,-139.0099f),new(3957.2698f,-1047.5621f,-680.3897f),new(3461.2698f,-1339.5621f,-440.3897f),
        new(4101.2695f,-1655.5621f,-360.3897f),new(4645.2695f,-1655.5621f,-360.3897f)];

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown > 0) return ++_teardown > 8 ? SmokeResult.Pass(_materialOnly ? "Hospital material bindings and explicit GS metadata verified" : _beamsOnly ? "Hospital beam comparison captured" : "Hospital normal stair ascent/descent verified on authority and client") : SmokeResult.Running;
        if (_root is null)
        {
            CheckMaterialMetadata();
            Options.SettingsStore.Current.Renderer = _renderer;
            Options.SettingsApplier.ApplyRendering(Options.SettingsStore.Current, context.Tree.Root);
            if(DisplayServer.GetName()!="headless") { DisplayServer.WindowSetSize(new Vector2I(1280,720)); context.Tree.Root.Size=new Vector2I(1280,720); }
            var launch = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launch with { Settings = launch.Settings with { Map = "MHO05P", Mode = "Neutralization", Side = PlaySide.Spy,
                Rules = PlayRules.MapSettings, HostMode = PlayHostMode.Inline, Bots = 0 }, TicksPerFrame = 2, Audio = false, Render = true, Hud = true });
            _root.ProcessPriority = -10;
            context.Tree.Root.AddChild(_root); return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return SmokeResult.Fail(error);
        if (_root.Session?.ServerFailure is { } failure) return SmokeResult.Fail(failure.Message);
        if (!_root.IsLoaded || _root.Session is not { LocalController.SBPawn: { } pawn, ServerController.SBPawn: { } host } session || _root.View is null) return SmokeResult.Running;
        _keys ??= KeyPilot.Attach(_root); _keys.Update();
        if (!_keys.Ready || session.TicksRun < 360) return SmokeResult.Running;
        var pc = session.LocalController!;
        double elapsed = context.ElapsedSeconds - _since;
        if (_phase == 0)
        {
            DumpBeamMaterial();
            _shot=_firstShot;
            if (_materialOnly) { _root.QueueFree(); _teardown=1; return SmokeResult.Running; }
            if (_beamsOnly) { _phase = 10; return SmokeResult.Running; }
            foreach (var controller in new[] { session.ServerController!, pc })
            {
                var p = controller.SBPawn!;
                bool moved = MovementNatives.Driver(p)?.FarMoveActor(p, new FVector(4420,-3600,-130)) ?? p.SetLocation(new FVector(4420,-3600,-130));
                if (!moved) return SmokeResult.Fail("Stair fixture placement blocked");
                p.Velocity = p.Acceleration = default; p.SetBase(null);
                controller.Rotation = p.Rotation = new FRotator(0,0,0);
            }
            session.Host.Movement.EndpointOf(session.ServerController!)?.Server?.ReinitPawn();
            _since = context.ElapsedSeconds; _phase = 1; return SmokeResult.Running;
        }
        if (_phase < 10)
        {
            FVector target = pawn.Location;
            _root.View.SetUeCamera(new FVector(4750,-3960,40), (target-new FVector(4750,-3960,40)).Rotation(), 70, cut:true);
            if (++_frame % 15 == 0) { _rows.Add(new { phase=_phase, tick=session.TicksRun, host=host.Location.ToString(), client=pawn.Location.ToString(), hostHeight=host.CollisionHeight, clientHeight=pawn.CollisionHeight, hostPhysics=host.Physics.ToString(), clientPhysics=pawn.Physics.ToString() }); }
            if (_phase == 1 && elapsed > 1)
            { _startZ=host.Location.Z; Save(context,"stair-bottom.png"); _keys.Press("BT_Up"); _phase=2; _since=context.ElapsedSeconds; }
            else if (_phase == 2)
            {
                if (host.Location.X > 4690 && pawn.Location.X > 4680)
                {
                    _keys.ReleaseAll(); _topZ=host.Location.Z;
                    if (_topZ-_startZ < 100 || MathF.Abs(host.Location.Z-pawn.Location.Z)>12) return SmokeResult.Fail($"Stair ascent mismatch: bottom={_startZ} top={_topZ}, client={pawn.Location}");
                    Save(context,"stair-top.png"); _phase=3; _since=context.ElapsedSeconds;
                }
                else if (elapsed > 10) return SmokeResult.Fail($"Normal stair ascent blocked: host={host.Location}, client={pawn.Location}");
            }
            else if (_phase == 3 && elapsed > .5) { _keys.Press("BT_Down"); _phase=4; _since=context.ElapsedSeconds; }
            else if (_phase == 4)
            {
                if (host.Location.X < 4440 && pawn.Location.X < 4450)
                {
                    _keys.ReleaseAll();
                    if (_topZ-host.Location.Z < 100 || MathF.Abs(host.Location.Z-pawn.Location.Z)>12) return SmokeResult.Fail($"Stair descent mismatch: host={host.Location}, client={pawn.Location}");
                    Save(context,"stair-return.png"); _rows.Add(new { result="normal-input-ascent-descent", bottomZ=_startZ,topZ=_topZ,returnZ=host.Location.Z, authorityClientDistance=FVector.Dist(host.Location,pawn.Location) }); _phase=10;
                }
                else if (elapsed > 10) return SmokeResult.Fail($"Normal stair descent blocked: host={host.Location}, client={pawn.Location}");
            }
            return SmokeResult.Running;
        }
        if (_shot >= _lastShot)
        {
            string dir=System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") ?? "local/ps2-hospital-world"; Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"hospital-world.json"),JsonSerializer.Serialize(_rows,new JsonSerializerOptions{WriteIndented=true}));
            _root.QueueFree();_teardown=1;return SmokeResult.Running;
        }
        int site=_shot/2; FVector center=Beams[site]+new FVector(0,0,site<2?-250:120);
        FVector at=center+(_shot%2==0 ? new FVector(350,300,70) : new FVector(-350,-300,70));
        _root.View.SetUeCamera(at,(center-at).Rotation(),75,cut:true);
        if (++_frame < 24) return SmokeResult.Running;
        Save(context,$"beam-{_shot}.png"); _rows.Add(new {beam=site,camera=at.ToString(),target=center.ToString()}); _shot++;_frame=0;
        return SmokeResult.Running;
    }

    private static void Save(SmokeContext context,string name)
    { string? dir=System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");if(string.IsNullOrEmpty(dir))return;Directory.CreateDirectory(dir);using Image image=context.Tree.Root.GetTexture().GetImage();image.SavePng(Path.Combine(dir,name)); }

    private void DumpBeamMaterial()
    {
        var map=_root!.View!.Map!; string name="P2_862546.UnknownGlobalPS2_862552";
        var entry=map.Materials.Find(name)!; var rows=new List<object>();
        foreach(Node node in map.Root.FindChildren("*","MeshInstance3D",true,false))
        {
            var instance=(MeshInstance3D)node; if(instance.Mesh is null)continue;
            for(int s=0;s<instance.Mesh.GetSurfaceCount();s++)
            {
                var material=instance.GetActiveMaterial(s) as ShaderMaterial;
                if(material?.ResourceName!=name)continue;
                var texture=material.GetShaderParameter("tex_diffuse").AsGodotObject() as Texture2D;
                using Image? image=texture?.GetImage();
                byte[] bytes=image?.GetData() ?? [];
                Color[] colors=instance.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Color].AsColorArray();
                rows.Add(new {node=node.Name.ToString(),surface=s,textureBound=texture is not null,width=texture?.GetWidth(),height=texture?.GetHeight(),format=image?.GetFormat().ToString(),
                    rgbMax=bytes.Length>0?bytes.Where((_,i)=>i%4!=3).Max():0,alphaMin=bytes.Length>0?bytes.Where((_,i)=>i%4==3).Min():0,
                    colorVertices=colors.Length,colorBlack=colors.Count(c=>c.R==0&&c.G==0&&c.B==0),colorMax=colors.Length>0?colors.Max(c=>c.R):0,zoneMin=colors.Length>0?colors.Min(c=>c.A):0,
                    additiveGain=material.GetShaderParameter("additive_gain").ToString(),shader=material.Shader.Code[..Math.Min(400,material.Shader.Code.Length)]});
                if(rows.Count>=3)break;
            }
            if(rows.Count>=3)break;
        }
        var report=new {mapFolder=map.Folder,entry.Texture,blend=entry.Blend.ToString(),entry.SelfIllum,opacity=entry.Opacity?.ToString(),missing=map.Textures.Missing,rows};
        GD.Print("MHO_MATERIAL "+JsonSerializer.Serialize(report));
        string? dir=System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");if(dir is not null){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"beam-material.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
    }

    private static void CheckMaterialMetadata()
    {
        using var plain=JsonDocument.Parse("{}");
        if(MaterialsDoc.FromFlags("pc-control",plain.RootElement).Ps2GsAlphaFix is not null)throw new InvalidOperationException("PC material gained PS2 blend metadata");
        foreach(int fix in new[]{0,80,128,255})
        {
            using var valid=JsonDocument.Parse("{\"ps2_gs_alpha_fix\":"+fix+"}");
            if(MaterialsDoc.FromFlags("ps2-control",valid.RootElement).Ps2GsAlphaFix!=fix)throw new InvalidOperationException("GS FIX metadata changed");
        }
        foreach(string value in new[]{"-1","256","80.5","\"80\"","null"})
        {
            using var invalid=JsonDocument.Parse("{\"ps2_gs_alpha_fix\":"+value+"}");
            try { MaterialsDoc.FromFlags("bad-control",invalid.RootElement); }
            catch(InvalidDataException) { continue; }
            throw new InvalidOperationException("Invalid GS FIX metadata accepted: "+value);
        }
    }
}
