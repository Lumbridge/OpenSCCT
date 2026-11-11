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
    private Color _rough, _metal, _vertexFlat;
    private byte[]? _parallaxBaseline;
    private byte[]? _samplingBaseline;
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
            using var atlasMips = AuthoredTextureImage.Decode(File.ReadAllBytes(Path.Combine(_folder, "atlas.png")), "baseColor", new() { Srgb = true }, new() { Columns = 2, Rows = 1, Frames = 2 });
            byte[] mipData = atlasMips.GetData(); int mipAt = (int)atlasMips.GetMipmapOffset(4);
            if (mipData[mipAt] < 250 || mipData[mipAt + 2] > 2 || mipData[mipAt + 4] > 2 || mipData[mipAt + 6] < 250) return SmokeResult.Fail("Independent animation mip frames bled into each other.");
            var reduced = new TextureCache(_folder) { MipDrop = 4, MipDropMinSize = 1 }.GetAuthored("baseColor", new() { File = "atlas.png", Srgb = true }, new() { Columns = 2, Frames = 2 });
            using (var reducedImage = reduced!.GetImage())
                if (reducedImage.GetWidth() != 2 || reducedImage.GetHeight() != 1 || reducedImage.GetPixel(0, 0).R < .98f || reducedImage.GetPixel(1, 0).B < .98f) return SmokeResult.Fail("Texture quality reduction lost the independent animation frames.");
            if (System.Environment.GetEnvironmentVariable("OPENSCCT_AUTHORED_SMOKE_MAP") is { Length: > 0 } fixture)
            {
                var map = MapBuilder.Build(fixture, new() { GameplayObjects = false, Emitters = false });
                bool hasDirections = HasDirections(map.Root);
                GD.Print("AUTHORED_COMPILED_MAP " + map.Stats + " directional=" + hasDirections);
                map.Root.Free();
                if (!hasDirections || map.Textures.Missing.Count > 0) return SmokeResult.Fail("Compiled material map lost baked mesh directions or texture references.");
            }
            using var white = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); white.Fill(Colors.White); white.SavePng(Path.Combine(_folder, "light.png"));
            using var normal = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); normal.Fill(new(.75f, .75f, 1)); normal.SavePng(Path.Combine(_folder, "normal.png"));
            using var checker = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
            using var height = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { checker.SetPixel(x, y, ((x / 4 + y / 4) & 1) == 0 ? Colors.White : Colors.Black); height.SetPixel(x, y, new Color(x / 31f, x / 31f, x / 31f)); }
            checker.SavePng(Path.Combine(_folder, "checker.png")); height.SavePng(Path.Combine(_folder, "height.png"));
            using var repeat = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
            using var repeatAtlas = Image.CreateEmpty(128, 64, false, Image.Format.Rgba8);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float v = ((x / 8 + y / 8) & 1) == 0 ? .25f : .75f;
                var pixel = new Color(v, v, 1);
                repeat.SetPixel(x, y, pixel); repeatAtlas.SetPixel(x, y, Colors.Red); repeatAtlas.SetPixel(x + 64, y, pixel);
            }
            repeat.SavePng(Path.Combine(_folder, "repeat.png")); repeatAtlas.SavePng(Path.Combine(_folder, "repeat-atlas.png"));
            height.Fill(Colors.Black); height.SavePng(Path.Combine(_folder, "constant-height.png"));
            using var ramp = Image.CreateEmpty(512, 512, false, Image.Format.Rgba8);
            using var slope = Image.CreateEmpty(512, 512, false, Image.Format.Rgba8);
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
            {
                ramp.SetPixel(x, y, new(x / 511f, y / 511f, .25f));
                float value = .25f + .5f * x / 511f; slope.SetPixel(x, y, new(value, value, value));
            }
            ramp.SavePng(Path.Combine(_folder, "uv-ramp.png")); slope.SavePng(Path.Combine(_folder, "slope-height.png"));
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
        if (_phase == 21) _vertexFlat = center;
        if (_phase == 22 && Math.Abs(center.R - _vertexFlat.R) < .015f) return SmokeResult.Fail("Baked vertex direction did not produce visible static normal response.");
        if (_phase >= 23)
        {
            byte[] pixels = capture.GetData();
            if ((_phase - 23) % 2 == 0) _samplingBaseline = pixels;
            else
            {
                long difference = 0; int peak = 0;
                for (int i = 0; i < pixels.Length; i++) { int error = Math.Abs(pixels[i] - _samplingBaseline![i]); difference += error; peak = Math.Max(peak, error); }
                string test = _phase >= 35 ? "PARALLAX_INTERSECTION" : "AUTHORED_REPEAT";
                GD.Print($"{test} phase={_phase} mean={(double)difference / pixels.Length:F4} peak={peak}");
                if (peak > 4 || difference > pixels.Length) return SmokeResult.Fail((_phase >= 35 ? "Parallax intersection differs from the analytic sloped-height reference: phase " : "Repeated material differs from independent hardware-repeat reference: phase ") + _phase);
            }
        }
        _frames = 0;
        if (++_phase < 41) return SmokeResult.Running;
        _viewport.QueueFree(); MaterialCompiler.SetVision(RenderK.RendMapNormal);
        return SmokeResult.Pass("41 rendered variants: animation/mips, alpha, lighting, refraction, vision modes, normals/conventions, triplanar, finishes, emission, parallax, directional static-mesh normal response, six repeat-boundary comparisons and analytic parallax intersections at qualities 4/16/64; " + _lods + " generated render LODs; " + _folder);
    }

    private static bool HasDirections(Node node)
    {
        if (node is MeshInstance3D { Mesh: ArrayMesh mesh })
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
                if ((mesh.SurfaceGetFormat(i) & Mesh.ArrayFormat.FormatCustom2) != 0) return true;
        foreach (Node child in node.GetChildren()) if (HasDirections(child)) return true;
        return false;
    }

    private void Configure()
    {
        if (_phase >= 35) { ConfigureParallaxProbe(); return; }
        if (_phase >= 23) { ConfigureSamplingProbe(); return; }
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
            if (_phase is 19 or 20) { definition.Textures["baseColor"].File = "checker.png"; definition.Textures["height"] = new() { File = "height.png" }; definition.BaseColor = [1,1,1]; definition.ParallaxDepth = _phase == 20 ? .2f : 0; definition.ParallaxSteps = 48; light = LightSource.None; _quad.Rotation = new(0,.9f,0); }
            if (_phase >= 21)
            {
                definition.Specular = 0; if (_phase == 22) definition.Textures["normal"] = new() { File = "normal.png" };
                light = LightSource.Vertex;
                using var quad = new QuadMesh { Size = Vector2.One * 2 }; using var arrays = quad.GetMeshArrays(); int count = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
                var colors = new Color[count]; var directions = new float[count * 4]; Vector3 direction = new Vector3(1,2,2).Normalized() * .5f + Vector3.One * .5f;
                for (int i = 0; i < count; i++) { colors[i] = new(.3f,.3f,.3f,0); directions[i * 4] = direction.X; directions[i * 4 + 1] = direction.Y; directions[i * 4 + 2] = direction.Z; directions[i * 4 + 3] = 1; }
                arrays[(int)Mesh.ArrayType.Color] = colors; arrays[(int)Mesh.ArrayType.Custom2] = directions;
                var generated = new ArrayMesh(); var blends = new Godot.Collections.Array<Godot.Collections.Array>(); using var lods = new Godot.Collections.Dictionary(); generated.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blends, lods, (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift)); GC.KeepAlive(blends); _quad.Mesh = generated;
            }
        }
        var entry = new MaterialEntry { Name = "authored-check-" + _phase, Texture = "atlas.png", Authored = definition, TwoSided = true, Masked = definition.Surface == "masked", Translucent = definition.Surface == "translucent", Lightmap = _phase == 5 ? "light.png" : null };
        var material = (ShaderMaterial)_compiler!.Build(entry, light);
        if (_phase == 4 || _phase >= 10) { _quad!.SetInstanceShaderParameter("svm_ambient", new Vector4(.02f, .02f, .02f, 1)); OpenScctGame.Render.InstanceParams.Set(_quad, "svm_l0_pos", new Vector4(1, 2, 2, 1)); OpenScctGame.Render.InstanceParams.Set(_quad, "svm_l0_col", new Vector4(1, 1, 1, 1)); }
        _quad!.MaterialOverride = material;
        MaterialCompiler.SetVision(_phase switch { 7 => 34, 8 => 36, 9 => 38, _ => RenderK.RendMapNormal });
    }

    private void ConfigureSamplingProbe()
    {
        int variant = (_phase - 23) / 2;
        bool reference = (_phase - 23) % 2 == 0, atlas = variant % 2 == 1, parallax = variant is 2 or 3, data = variant >= 4;
        var definition = new AuthoredMaterial { Roughness = 1, ParallaxDepth = parallax ? .125f : 0, ParallaxSteps = 16 };
        string file = atlas && !reference ? "repeat-atlas.png" : "repeat.png";
        definition.Textures["baseColor"] = new() { File = file };
        definition.Textures["normal"] = new() { File = file };
        definition.Textures["roughness"] = new() { File = file };
        definition.Textures["height"] = new() { File = "constant-height.png" };
        if (atlas && !reference) definition.Animation = new() { Columns = 2, Frames = 2, Fps = 0, Phase = 1 };
        string evaluate = reference
            ? (parallax ? "p -= vec2(0.0625, 0.03125);" : "") + (data
                ? "vec3 n = normalize(texture(am_tex_normal, p).xyz * 2.0 - 1.0); ALBEDO = vec3(n.xy * 0.25 + 0.5, texture(am_tex_roughness, p).r);"
                : "ALBEDO = texture(am_tex_baseColor, p).rgb;")
            : "AuthoredSample s = am_evaluate(p, vec3(0.0), vec3(0.0, 0.0, 1.0), vec3(0.5, 0.25, 1.0), 0.0); ALBEDO = " + (data ? "vec3(s.normal.xy * 0.25 + 0.5, s.roughness);" : "s.color.rgb;");
        // Atlas sampling is trilinear. Compare like filters: Vulkan's anisotropic
        // hardware sampler can choose a different footprint even on this flat quad.
        string sampling = AuthoredMaterialBinding.ShaderCode.Replace("filter_linear_mipmap_anisotropic", "filter_linear_mipmap", StringComparison.Ordinal);
        var material = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, cull_disabled;\n" + sampling + "\nvoid fragment() { vec2 p = UV * 3.7 - vec2(1.21, 1.37); " + evaluate + " }" } };
        var textures = new TextureCache(_folder);
        AuthoredMaterialBinding.Apply(material, definition, fileName => textures.Get(fileName), (slot, binding) => textures.GetAuthored(slot, binding, definition.Animation));
        _quad!.Rotation = Vector3.Zero; _quad.Mesh = new QuadMesh { Size = Vector2.One * 2 }; _quad.MaterialOverride = material;
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
    }

    private void ConfigureParallaxProbe()
    {
        bool reference = (_phase - 35) % 2 == 0;
        int steps = new[] { 4, 16, 64 }[(_phase - 35) / 2];
        var definition = new AuthoredMaterial { ParallaxDepth = .14f, ParallaxSteps = steps };
        definition.Textures["baseColor"] = new() { File = "uv-ramp.png" };
        definition.Textures["height"] = new() { File = "slope-height.png" };
        // For h(u)=a*u+b and a ray p-k*t, solve t=1-h(p-k*t)
        // independently of the ray marcher. PNG quantization is below one output level.
        string evaluate = reference
            ? "vec2 k = vec2(0.94, 0.17) / 0.34 * 0.14; float a = 256.0 / 511.0; float b = 0.25 - 0.25 / 511.0; float t = (1.0 - b - a * p.x) / (1.0 - a * k.x); ALBEDO = texture(am_tex_baseColor, p - k * t).rgb;"
            : "AuthoredSample s = am_evaluate(p, vec3(0.0), vec3(0.0, 0.0, 1.0), vec3(0.94, 0.17, 0.34), 0.0); ALBEDO = s.color.rgb;";
        var material = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, cull_disabled;\n" + AuthoredMaterialBinding.ShaderCode + "\nvoid fragment() { vec2 p = UV * 0.35 + vec2(0.5, 0.3); " + evaluate + " }" } };
        var textures = new TextureCache(_folder);
        AuthoredMaterialBinding.Apply(material, definition, file => textures.Get(file), (slot, binding) => textures.GetAuthored(slot, binding, definition.Animation));
        _quad!.Rotation = Vector3.Zero; _quad.Mesh = new QuadMesh { Size = Vector2.One * 2 }; _quad.MaterialOverride = material;
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
    }
}
