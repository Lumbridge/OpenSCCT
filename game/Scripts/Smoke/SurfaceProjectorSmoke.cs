using System;
using System.IO;
using System.Linq;
using OpenScctGame.Render.Characters;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;
using OpenScctGame.Render.Projectors;
using OpenScctGame.Render.Geometry;

namespace OpenScctGame.Smoke;

/// <summary>Compare actual surface pixels with the original volume, including a moved smooth receiver and fog/vision.</summary>
public sealed class SurfaceProjectorSmoke : SmokeTest
{
    public const string TestName = "surface-projector";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private Camera3D _camera = null!;
    private Node3D _receivers = null!;
    private MeshInstance3D _volume = null!, _sphere = null!;
    private MeshInstance3D? _decal;
    private TextureCache _textures = null!;
    private SurfaceFogRegistry _fog = null!;
    private MaterialCompiler _compiler = null!;
    private ProjectorEntry _source = null!;
    private int _step, _frames;
    private SkeletalModel? _spy;
    private ShaderMaterial? _liveMaterial;
    private byte[]? _reference;
    private readonly string _folder = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "surface-projector-smoke");

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("This check requires rendered pixels.");
        if (_viewport is null) Setup(context);
        if (_frames++ == 0) Configure();
        if (_step / 2 == 18 && _frames == 4 && _liveMaterial is not null)
        {
            using var color = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8); color.Fill(new Color(.2f, .5f, .7f));
            _liveMaterial.SetShaderParameter("tex_diffuse", ImageTexture.CreateFromImage(color));
        }
        if (_frames < 8) return SmokeResult.Running;
        using Image image = _viewport!.GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        image.SavePng(Path.Combine(_folder, $"{_step / 2}-{(_step % 2 == 0 ? "volume" : "surface")}.png"));
        byte[] pixels = image.GetData();
        if (_step == 26)
        {
            using Image empty = Image.LoadFromFile(Path.Combine(_folder, "0-volume.png")); empty.Convert(Image.Format.Rgba8);
            byte[] background = empty.GetData(); int changed = 0;
            for (int i=0;i<pixels.Length;i+=4)
                if (Math.Abs(pixels[i]-background[i]) > 8 || Math.Abs(pixels[i+1]-background[i+1]) > 8 || Math.Abs(pixels[i+2]-background[i+2]) > 8) changed++;
            if (changed < 1000) return SmokeResult.Fail($"Spy fixture is not visible: only {changed} changed pixels");
            GD.Print($"G16_SPY visible_pixels={changed}");
        }
        if ((_step & 1) == 0) _reference = pixels;
        else
        {
            long total = 0;
            int different = 0, peak = 0, compared = 0;
            bool[]? discontinuity = null;
            if (_step / 2 == 17)
            {
                // The old volume reconstructs normals across cut-out depth discontinuities. Compare interiors
                // strictly; its two-pixel quad/edge artefacts are not the receiver's true geometric normal.
                int width = image.GetWidth(), height = image.GetHeight();
                discontinuity = new bool[width * height];
                foreach (byte[] data in new[] { pixels, _reference! })
                    for (int y = 1; y < height; y++) for (int x = 1; x < width; x++)
                    {
                        int at = (y * width + x) * 4;
                        bool edge = false;
                        for (int c=0;c<3;c++) edge |= Math.Abs(data[at+c]-data[at-4+c]) > 8 || Math.Abs(data[at+c]-data[at-width*4+c]) > 8;
                        if (!edge) continue;
                        for (int yy=Math.Max(0,y-2); yy<=Math.Min(height-1,y+2); yy++)
                            for (int xx=Math.Max(0,x-2); xx<=Math.Min(width-1,x+2); xx++) discontinuity[yy*width+xx] = true;
                    }
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                if (discontinuity is not null && discontinuity[i/4]) continue;
                compared++;
                int error = Math.Abs(pixels[i] - _reference![i]);
                total += error; peak = Math.Max(peak, error); if (error > 4) different++;
            }
            if (compared < pixels.Length * (_step / 2 == 17 ? .85 : .9)) return SmokeResult.Fail("Too little masked interior was compared");
            double mean = (double)total / compared, fraction = (double)different / compared;
            GD.Print($"SURFACE_PROJECTOR case={_step / 2} compared={(double)compared/pixels.Length:P2} mean={mean:F4} peak={peak} over4={fraction:P4}");
            // Allow one-level attachment rounding and a small reconstruction difference at silhouette/texel edges.
            if (mean > .35 || fraction > .003) return SmokeResult.Fail($"case {_step / 2}: receiver pixels differ from volume (mean={mean:F4}, over4={fraction:P4})");
        }
        _frames = 0;
        if (++_step < 46) return SmokeResult.Running;
        if (SurfaceProjector.RuntimeFallback || _volume.Visible || _decal is null || !_decal.Visible)
            return SmokeResult.Fail("moving receiver re-enabled the full projector");
        _sphere.Position = new Vector3(50, 50, 50);
        SurfaceProjector.RefreshFallback();
        if (SurfaceProjector.RuntimeFallback || _volume.Visible || !_decal.Visible)
            return SmokeResult.Fail("moving receiver did not release the volume");
        var entering = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = _receivers.GetChild<MeshInstance3D>(0).MaterialOverride, Layers = RenderLayers.World, Position = new Vector3(.5f, 0, 0) };
        Material originalEntering = entering.MaterialOverride;
        _receivers.AddChild(entering); SurfaceProjector.RefreshFallback();
        if (SurfaceProjector.RuntimeFallback || SurfaceProjector.MovingReceivers == 0) return SmokeResult.Fail("newly added receiver was not projected cheaply");
        if (entering.MaterialOverride == originalEntering) return SmokeResult.Fail("shared fixed material did not get a receiver-local copy");
        entering.Visible = false; SurfaceProjector.RefreshFallback();
        if (SurfaceProjector.RuntimeFallback) return SmokeResult.Fail("invisible receiver forced fallback");
        if (entering.MaterialOverride != originalEntering) return SmokeResult.Fail("invisible receiver retained its projected material");
        entering.Visible = true; _receivers.RemoveChild(entering); entering.Free(); SurfaceProjector.RefreshFallback();
        if (SurfaceProjector.RuntimeFallback) return SmokeResult.Fail("removed receiver forced fallback");
        var unknown = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = new StandardMaterial3D(), Layers = RenderLayers.World, Position = new Vector3(.5f, 0, 0) };
        _receivers.AddChild(unknown); SurfaceProjector.RefreshFallback();
        if (!SurfaceProjector.RuntimeFallback || !_volume.Visible || _decal.Visible)
            return SmokeResult.Fail("unknown opaque material lost its compatibility projection");
        _receivers.RemoveChild(unknown); unknown.Free(); SurfaceProjector.RefreshFallback();
        if (SurfaceProjector.RuntimeFallback || _volume.Visible || !_decal.Visible)
            return SmokeResult.Fail("unknown opaque receiver did not release its compatibility projection");
        foreach (bool animated in new[] { false, true })
        {
            SurfaceProjector.Configure([_source], _textures);
            Collect(_receivers.GetChild<MeshInstance3D>(0), _sphere.MaterialOverride, animated ? MaterialBlend.Opaque : MaterialBlend.Masked, animated);
            if (SurfaceProjector.Enabled) return SmokeResult.Fail("masked/animated receiver bypassed the volume");
        }
        foreach (ProjectorEntry unsupported in new[] { _source with { TextureFormat = 12 }, _source with { Static = false },
                     _source with { ProjectStaticMesh = false }, _source with { FrameBufferBlending = 3 }, _source with { Size = Vector2.Zero } })
        {
            SurfaceProjector.Configure([unsupported], _textures);
            if (SurfaceProjector.Enabled) return SmokeResult.Fail("unsupported projector bypassed the volume fallback");
        }
        SurfaceProjector.Configure([_source, _source], _textures);
        if (SurfaceProjector.Enabled) return SmokeResult.Fail("overlapping projectors bypassed the ordered volume fallback");
        SurfaceProjector.Configure(null, _textures);
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
        _fog.Dispose(); _viewport.QueueFree();
        return SmokeResult.Pass("twenty-three native-G16 volume/surface pixel pairs (including sparse coverage): fullbright, BSP, vertex-lit, moved/scaled smooth receiver, backfaces, fog, night/thermal/motion/EMF, moving receiver, half/full cloak, posed/skinned spy, masks and live uniforms; original receiver streams preserved; moving/new/hidden/removed receivers without full-pass fallback; masked/animated/multiple/re-entry guards");
    }

    private void Setup(SmokeContext context)
    {
        Directory.CreateDirectory(_folder);
        _textures = new TextureCache(_folder);
        using var projection = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float value = x is >= 27 and <= 35 || y is >= 27 and <= 35 ? 0 : 127f / 255f;
            projection.SetPixel(x, y, new Color(value, value, value));
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(_folder, "fixture.g16"))))
        {
            writer.Write(new byte[] { (byte)'G', (byte)'1', (byte)'6', 0 }); writer.Write(64); writer.Write(64);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) writer.Write((ushort)(x is >= 27 and <= 35 || y is >= 27 and <= 35 ? 0 : 32767));
        }
        var texture = _textures.Get("fixture.g16")!;
        if (_textures.GetImage("fixture.g16") is not { } raw || MathF.Abs(raw.GetPixel(0, 0).R - 32767f / 65535f) > 1e-7f)
            throw new InvalidOperationException("G16 precision was reduced to eight bits");
        _textures.Add("fixture", texture);
        _compiler = new MaterialCompiler(new MaterialsDoc(), _textures);
        MeshLightSpheres.EnsureGlobals();
        ResponsiveSmokeShaders.Use(false); ResponsiveSmokeShaders.UsePortalFog(false);
        _source = new ProjectorEntry("fixture", FVector.Zero, default, new FVector(1, 1, 1), "fixture", new Vector2(200, 200),
            30, 200, 0, 3, false, null, null, TextureFormat: 10, LuminanceTexture: "fixture.g16");
        _viewport = new SubViewport { Size = new Vector2I(512, 512), OwnWorld3D = true, UseDebanding = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        _camera = new Camera3D { Position = new Vector3(-2, .4f, 0), Current = true, CullMask = RenderLayers.MainView,
            Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes() };
        _viewport.AddChild(_camera);
        _camera.LookAt(new Vector3(1, 0, 0));
        _fog = new SurfaceFogRegistry(_camera, main: true);
        _receivers = new Node3D(); _viewport.AddChild(_receivers);
        var board = new SurfaceBuilder(hasUv2: true, hasColour: false);
        board.Add(Transform3D.Identity, new MeshPrimitive { Material = "board", Positions = [1,-2,-2, 1,2,-2, 1,2,2, 1,-2,2],
            Normals = [-1,0,0, -1,0,0, -1,0,0, -1,0,0], Uvs = [0,0, 1,0, 1,1, 0,1], Indices = [0,2,1, 0,3,2] }, 0);
        var boardMesh = new ArrayMesh(); board.AddTo(boardMesh, null);
        _receivers.AddChild(new MeshInstance3D { Mesh = boardMesh });
        _sphere = new MeshInstance3D { Mesh = new SphereMesh { Radius = .35f, Height = .7f }, Position = new Vector3(.5f, -.25f, 0) };
        _receivers.AddChild(_sphere);
        _volume = MapProjectors.CreateNode(_source, texture); _viewport.AddChild(_volume);
        string? path = SkeletalModel.FindGlb("SPerso.ATT_01");
        if (path is null) throw new InvalidOperationException("Converted spy content required for skinned projector pixels");
        _spy = SkeletalModel.Load(path); _viewport.AddChild(_spy.Root);
        _spy.Place(default, default, default); _spy.Sampler.Play("WaitStAlFd0"); _spy.Pose();
        if (!_spy.Meshes.Any(m => m.Skin is not null)) throw new InvalidOperationException("Spy fixture has no skinning");
        Aabb box = _spy.Meshes.Select(m => m.GlobalTransform * m.GetAabb()).Aggregate((a,b) => a.Merge(b));
        GD.Print($"G16_SPY bounds={box} mesh={_spy.Meshes[0].Mesh!.GetAabb()} instance={_spy.Meshes[0].GetAabb()} scale={_spy.Pivot.Scale}");
        float fit = .8f / box.Size.Y;
        _spy.Root.Scale = Vector3.One * fit;
        _spy.Root.Position = new Vector3(.55f, .15f, .48f) - box.GetCenter() * fit;
        _spy.Light(new ActorLightSet { Ambient = new Vector3(.6f,.6f,.6f) });
        _spy.Root.Visible = false;
    }

    private void Configure()
    {
        int scene = _step / 2;
        bool fused = (_step & 1) != 0;
        SurfaceProjector.Configure(fused ? [_source] : null, _textures);
        if (_decal is not null) { _decal.Visible = false; _decal.QueueFree(); _decal = null; }
        _volume.Visible = !fused;
        MaterialCompiler.SetVision(scene is 6 or 20 ? 36 : scene is 7 or 22 ? 38 : scene is 8 or 19 ? 34 : scene is 9 or 21 ? 37 : RenderK.RendMapNormal);
        _fog.Update(scene == 5, new Color(.12f, .2f, .18f), 50, 600);
        _camera.Position = scene == 4 ? new Vector3(3, .4f, 0) : new Vector3(-2, .4f, 0);
        _camera.LookAt(new Vector3(1, 0, 0));
        _sphere.Visible = scene != 0 && (scene < 13 || scene is 17 or 18);
        _sphere.Position = scene == 3 ? new Vector3(.15f, .4f, -.55f) : new Vector3(.5f, -.25f, 0);
        _sphere.Scale = scene == 17 ? Vector3.One * 1.5f : scene == 3 ? new Vector3(.8f, 1.3f, .6f) : Vector3.One;
        _sphere.Rotation = scene == 3 ? new Vector3(.2f, .6f, .3f) : Vector3.Zero;
        LightSource light = scene == 1 ? LightSource.Lightmap : scene == 2 ? LightSource.Vertex : LightSource.None;
        var material = (ShaderMaterial)_compiler.Build(new MaterialEntry { Name = "surface-projector-" + _step,
            Unlit = light == LightSource.None, ListBlend = MaterialBlend.Opaque, TwoSided = true }, light);
        using var baseImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8); baseImage.Fill(new Color(.6f, .6f, .6f));
        material.SetShaderParameter("tex_diffuse", ImageTexture.CreateFromImage(baseImage));
        material.SetShaderParameter("tex_lightmap", ImageTexture.CreateFromImage(baseImage));
        foreach (Node child in _receivers.GetChildren())
        {
            var receiver = (MeshInstance3D)child; receiver.MaterialOverride = material;
            receiver.Layers = scene >= 10 && receiver == _sphere ? RenderLayers.World : RenderLayers.StaticWorld;
        }
        _liveMaterial = material;
        if (_spy is { } spy)
        {
            spy.Root.Visible = scene is >= 13 and <= 16 or >= 19;
            spy.SetGlass(scene == 15 ? .5f : scene == 16 || scene >= 19 ? 1 : 0);
            spy.Sampler.Play("WaitStAlFd0", frame: scene == 14 ? .17f : 0);
            spy.Pose();
            spy.Root.Rotation = scene == 14 ? new Vector3(0,.65f,0) : Vector3.Zero;
        }
        if (scene == 17)
        {
            var masked = (ShaderMaterial)_compiler.Build(new MaterialEntry { Name = "projector-mask-" + _step, Unlit = true,
                TwoSided = true, ListBlend = MaterialBlend.Masked }, LightSource.None);
            using var mask = Image.CreateEmpty(32,32,false,Image.Format.Rgba8);
            for(int y=0;y<32;y++) for(int x=0;x<32;x++) mask.SetPixel(x,y,new Color(.6f,.6f,.6f, (x/4+y/4)%2 == 0 ? 1 : 0));
            mask.GenerateMipmaps(); masked.SetShaderParameter("tex_diffuse",ImageTexture.CreateFromImage(mask));
            _sphere.MaterialOverride = masked;
        }
        if (scene is 11 or 12)
        {
            var cloaks = new CloakMaterials();
            cloaks.SetAmount(scene == 11 ? .5f : 1);
            _sphere.MaterialOverride = cloaks.Get(material);
        }
        if (fused)
        {
            foreach (Node child in _receivers.GetChildren())
                if (((MeshInstance3D)child).Visible && (scene < 10 || child != _sphere)) Collect((MeshInstance3D)child, material);
            _decal = SurfaceProjector.CreateNode(_textures.Get("fixture")!) ?? throw new InvalidOperationException("No clipped projector triangles");
            _viewport!.AddChild(_decal);
            SurfaceProjector.SelectGeometry(_decal, [_camera]);
            if (scene == 0 && !SurfaceProjector.Sparse) throw new InvalidOperationException("Sparse coverage was not exercised");
            if (scene == 0)
            {
                _viewport.Scaling3DScale = .25f;
                SurfaceProjector.SelectGeometry(_decal, [_camera]);
                if (SurfaceProjector.Sparse) throw new InvalidOperationException("Reduced 3D scale lost minification coverage");
                _viewport.Scaling3DScale = 1;
                ResponsiveSmokeShaders.Use(true);
                ShaderGlobals.Set("svm_smoke_active", true);
                SurfaceProjector.SelectGeometry(_decal, [_camera]);
                if (SurfaceProjector.Sparse) throw new InvalidOperationException("Active smoke lost neutral modulation");
                ShaderGlobals.Set("svm_smoke_active", false);
                SurfaceProjector.SelectGeometry(_decal, [_camera]);
                if (!SurfaceProjector.Sparse) throw new InvalidOperationException("Inactive smoke needlessly disabled sparse geometry");
                ResponsiveSmokeShaders.Use(false);
            }
            if (scene == 5 && SurfaceProjector.Sparse) throw new InvalidOperationException("Fog lost neutral modulation");
            if (scene >= 10) SurfaceProjector.Bind(_receivers, _volume, _decal);
            var sm = (ShaderMaterial)_decal.Mesh!.SurfaceGetMaterial(0);
            if (Render.Rd.RdWorldGeometry.ActorMaterial(sm, 1) is not { } translated ||
                !translated.Shader.Vertex.Contains("gl_Position = POSITION", StringComparison.Ordinal))
                throw new InvalidOperationException("Fast renderer lost the clipped projector depth bias");
        }
    }

    private static void Collect(MeshInstance3D node, Material material, MaterialBlend blend = MaterialBlend.Opaque, bool animated = false)
    {
        var arrays = node.Mesh!.SurfaceGetArrays(0);
        Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (indices.Length == 0) { indices = new int[positions.Length]; for (int i = 0; i < indices.Length; i++) indices[i] = i; }
        for (int i = 0; i < indices.Length; i += 3) (indices[i+1],indices[i+2]) = (indices[i+2],indices[i+1]);
        var xyz = new float[positions.Length * 3];
        for (int i = 0; i < positions.Length; i++) { xyz[i*3] = positions[i].X; xyz[i*3+1] = positions[i].Y; xyz[i*3+2] = positions[i].Z; }
        var builder = new SurfaceBuilder(false, false);
        builder.Add(node.Transform, new MeshPrimitive { Material = "receiver", Positions = xyz, Indices = indices }, 0);
        var before = builder.RdStreams();
        SurfaceProjector.Collect(builder, new MaterialEntry { Name = "receiver", TwoSided = true, ListBlend = blend }, material, animated);
        var after = builder.RdStreams();
        if (!ReferenceEquals(before.Positions, after.Positions) || !ReferenceEquals(before.Indices, after.Indices))
            throw new InvalidOperationException("Clipping changed original receiver geometry");
    }
}
