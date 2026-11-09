using System;
using System.IO;
using Godot;
using OpenScct.Content.Materials;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>Pixel checks of the actual game material compiler, including all lighting and vision variants.</summary>
public sealed class AuthoredMaterialSmoke : SmokeTest
{
    public const string TestName = "authored-material";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private MeshInstance3D? _quad;
    private MaterialCompiler? _compiler;
    private int _phase, _frames;
    private Color _flat, _normal;
    private Color _rough, _metal;
    private byte[]? _parallaxBaseline;
    private int _lods;
    private readonly string _folder = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "authored-material-smoke");
    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("This check requires rendered pixels.");
        if (_viewport is null)
        {
            Directory.CreateDirectory(_folder);
            using var atlas = Image.CreateEmpty(32, 16, false, Image.Format.Rgba8);
            for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++) atlas.SetPixel(x, y, x < 16 ? Colors.Red : Colors.Blue);
            atlas.SavePng(Path.Combine(_folder, "atlas.png"));
            using var white = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); white.Fill(Colors.White); white.SavePng(Path.Combine(_folder, "light.png"));
            using var normal = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); normal.Fill(new(.75f, .75f, 1)); normal.SavePng(Path.Combine(_folder, "normal.png"));
            using var checker = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
            using var height = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { checker.SetPixel(x, y, ((x / 4 + y / 4) & 1) == 0 ? Colors.White : Colors.Black); height.SetPixel(x, y, new Color(x / 31f, x / 31f, x / 31f)); }
            checker.SavePng(Path.Combine(_folder, "checker.png")); height.SavePng(Path.Combine(_folder, "height.png"));
            using var grid = new PlaneMesh { Size = Vector2.One, SubdivideWidth = 16, SubdivideDepth = 16 };
            using var arrays = grid.GetMeshArrays(); using var lods = AuthoredMeshLods.Create(arrays); _lods = lods.Count;
            if (_lods == 0) return SmokeResult.Fail("Generated geometry has no render LODs.");
            _compiler = new(new MaterialsDoc(), new TextureCache(_folder));
            _viewport = new() { Size = new(128, 128), OwnWorld3D = true, UseDebanding = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_viewport);
            var camera = new Camera3D { Position = new(0, 0, 2), Current = true, CullMask = RenderLayers.MainView, Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
                Compositor = new Compositor { CompositorEffects = [new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false, SurfaceFogApplied = true } }] } };
            _viewport.AddChild(camera); _ = new SurfaceFogRegistry(camera);
            _quad = new() { Mesh = new QuadMesh { Size = new(2, 2) }, Layers = RenderLayers.MainView }; _viewport.AddChild(_quad);
        }
        if (_frames == 0) Configure();
        if (++_frames < 12) return SmokeResult.Running;
        using Image capture = _viewport.GetTexture().GetImage();
        Color center = capture.GetPixel(64, 64);
        capture.SavePng(Path.Combine(_folder, $"phase-{_phase}.png"));
        if (!float.IsFinite(center.R) || !float.IsFinite(center.G) || !float.IsFinite(center.B)) return SmokeResult.Fail("Nonfinite material pixel.");
        if (_phase == 0 && (center.R < .5f || center.B > .1f)) return SmokeResult.Fail("First atlas frame should be red: " + center);
        if (_phase == 1 && (center.B < .5f || center.R > .1f)) return SmokeResult.Fail("Second atlas frame should be blue: " + center);
        if (_phase == 10) _flat = center;
        if (_phase == 11) { _normal = center; if (Math.Abs(center.R - _flat.R) < .015f) return SmokeResult.Fail("The normal map did not change the lit pixel."); }
        if (_phase == 12 && Math.Abs(center.R - _normal.R) < .015f) return SmokeResult.Fail("DirectX/OpenGL normal conventions did not change the lit pixel.");
        if (_phase == 14) _rough = center;
        if (_phase == 15) { _metal = center; if (Math.Abs(center.R - _rough.R) < .015f) return SmokeResult.Fail("Metal/reflection controls did not change the pixel."); }
        if (_phase == 16 && Math.Abs(center.R - _rough.R) < .001f) return SmokeResult.Fail("Roughness did not change the direct highlight.");
        if (_phase == 17 && (center.G < .7f || center.R > .1f)) return SmokeResult.Fail("Green emission is not visible in the unlit game material.");
        if (_phase == 19) _parallaxBaseline = capture.GetData();
        if (_phase == 20) { byte[] bytes = capture.GetData(); long difference = 0; for (int i = 0; i < bytes.Length; i++) difference += Math.Abs(bytes[i] - _parallaxBaseline![i]); if (difference < 2000) return SmokeResult.Fail("Parallax did not shift the grazing-angle checker texture."); }
        _frames = 0;
        if (++_phase < 21) return SmokeResult.Running;
        _viewport.QueueFree(); MaterialCompiler.SetVision(RenderK.RendMapNormal);
        return SmokeResult.Pass("21 rendered variants: animation, alpha, lighting, refraction, vision modes, normals, conventions, triplanar, metal, roughness, emission, grazing parallax; " + _lods + " generated render LODs; " + _folder);
    }

    private void Configure()
    {
        var definition = new AuthoredMaterial { TwoSided = true, Roughness = .3f, Clearcoat = .5f, Anisotropy = .2f };
        definition.Textures["baseColor"] = new() { File = "atlas.png", Srgb = true };
        definition.Animation = new() { Columns = 2, Frames = 2, Fps = 0, Phase = _phase == 1 ? 1 : 0 };
        if (_phase >= 4) definition.Textures["normal"] = new() { File = "normal.png" };
        definition.Surface = _phase == 2 ? "masked" : _phase is 3 or 6 ? "translucent" : "opaque";
        definition.Opacity = definition.Surface == "translucent" ? .6f : 1;
        definition.Refraction = _phase == 6 ? .03f : 0;
        LightSource light = _phase == 4 ? LightSource.Actor : _phase == 5 ? LightSource.Lightmap : LightSource.None;
        if (_phase >= 10)
        {
            definition.Textures["baseColor"].File = "light.png"; definition.Animation = new(); definition.Clearcoat = 0; definition.Specular = 0;
            if (_phase == 10) definition.Textures.Remove("normal");
            definition.NormalDirectX = _phase == 12; definition.Triplanar = _phase == 13;
            light = LightSource.Actor;
        }
        _quad!.Rotation = Vector3.Zero;
        if (_phase >= 14)
        {
            definition.Textures.Remove("normal"); definition.Specular = 1; definition.Roughness = .8f; definition.BaseColor = [.25f,.25f,.25f];
            if (_phase == 15) { definition.Metallic = 1; definition.Textures["environment"] = new() { File = "light.png", Srgb = true }; }
            if (_phase == 16) definition.Roughness = .15f;
            if (_phase == 17) { definition.BaseColor = [0,0,0]; definition.EmissionEnergy = 4; definition.EmissionColor = [0,1,0]; light = LightSource.None; }
            if (_phase == 18) { definition.Surface = "translucent"; definition.Opacity = .25f; }
            if (_phase >= 19) { definition.Textures["baseColor"].File = "checker.png"; definition.Textures["height"] = new() { File = "height.png" }; definition.BaseColor = [1,1,1]; definition.ParallaxDepth = _phase == 20 ? .2f : 0; definition.ParallaxSteps = 48; light = LightSource.None; _quad.Rotation = new(0,.9f,0); }
        }
        var entry = new MaterialEntry { Name = "authored-check-" + _phase, Texture = "atlas.png", Authored = definition, TwoSided = true, Masked = definition.Surface == "masked", Translucent = definition.Surface == "translucent", Lightmap = _phase == 5 ? "light.png" : null };
        var material = (ShaderMaterial)_compiler!.Build(entry, light);
        if (_phase == 4 || _phase >= 10) { _quad!.SetInstanceShaderParameter("svm_ambient", new Vector4(.02f, .02f, .02f, 1)); _quad.SetInstanceShaderParameter("svm_l0_pos", new Vector4(1, 2, 2, 1)); _quad.SetInstanceShaderParameter("svm_l0_col", new Vector4(1, 1, 1, 1)); }
        _quad!.MaterialOverride = material;
        MaterialCompiler.SetVision(_phase switch { 7 => 34, 8 => 36, 9 => 38, _ => RenderK.RendMapNormal });
    }
}
