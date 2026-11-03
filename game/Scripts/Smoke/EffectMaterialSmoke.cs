using System;
using System.IO;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>Effect material migration compared with live StandardMaterial3D references, then two-camera surface fog.</summary>
public sealed class EffectMaterialSmoke : SmokeTest
{
    public const string TestName = "effect-material";
    public override string Name => TestName;
    private const int ReferenceCases = 13;
    private readonly SubViewport[] _views = new SubViewport[2];
    private readonly MeshInstance3D[] _fronts = new MeshInstance3D[2];
    private readonly MeshInstance3D[] _occluders = new MeshInstance3D[2];
    private readonly DisplaySpaceEffect[] _posts = new DisplaySpaceEffect[2];
    private readonly SurfaceFogRegistry[] _fog = new SurfaceFogRegistry[2];
    private StandardMaterial3D? _reference;
    private EffectMaterial? _effect;
    private Texture2D? _texture;
    private ShaderMaterial? _stencilWriter;
    private int _phase, _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires Vulkan pixels");
        if (_texture is null) Setup(context);
        if (_frames == 0) Configure();
        if (++_frames < 12) return SmokeResult.Running;
        using Image left = _views[0].GetTexture().GetImage();
        using Image right = _views[1].GetTexture().GetImage();
        string folder = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "b5-fixes", "effect-material");
        Directory.CreateDirectory(folder);
        left.SavePng(Path.Combine(folder, $"phase-{_phase}-reference.png"));
        right.SavePng(Path.Combine(folder, $"phase-{_phase}-effect.png"));
        float worst = 0;
        if (_phase < ReferenceCases)
        {
            // Compare a grid, including edges and texture details, rather than a single uniform center pixel.
            for (int y = 8; y < 88; y += 3)
                for (int x = 8; x < 88; x += 3)
                    worst = MathF.Max(worst, Difference(left.GetPixel(x, y), right.GetPixel(x, y)));
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                Vector3 fog = i == 0 ? Vector3.Right : Vector3.Up;
                float alpha = _phase == ReferenceCases ? 1 : .5f;
                Vector3 background = new Vector3(.1f, .2f, .3f) * .2f + fog * .8f;
                Vector3 expected = _phase == ReferenceCases + 2
                    ? background + Vector3.One * (new Color(.35f, .35f, .35f).SrgbToLinear().R * .84f)
                    : background * (1 - alpha) + (Vector3.One * .84f + fog * .16f) * alpha;
                worst = MathF.Max(worst, Difference((i == 0 ? left : right).GetPixel(48, 48), new Color(expected.X, expected.Y, expected.Z)));
            }
        }
        if (worst > .012f) return Finish(SmokeResult.Fail($"phase={_phase} max RGB error={worst}; {folder}"));
        _frames = 0;
        return ++_phase < ReferenceCases + 3 ? SmokeResult.Running
            : Finish(SmokeResult.Pass("13 paired StandardMaterial references plus opaque/alpha/additive surface depth in two independently fogged views"));
    }

    private void Setup(SmokeContext context)
    {
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
        using Image image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++) image.SetPixel(x, y, new Color(x / 7f, y / 7f, .6f, (x + y + 2f) / 16f));
        _texture = ImageTexture.CreateFromImage(image);
        _stencilWriter = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, depth_draw_never; stencil_mode write, compare_always, 1; void fragment(){ ALBEDO=vec3(0.2); }" } };
        var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(""));
        for (int i = 0; i < 2; i++)
        {
            _views[i] = new SubViewport { Size = new(96, 96), OwnWorld3D = true, UseDebanding = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_views[i]);
            _posts[i] = new DisplaySpaceEffect();
            var camera = new Camera3D { Current = true, CullMask = RenderLayers.MainView, Near = .01f, Far = 20,
                Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
                Compositor = new Compositor { CompositorEffects = [_posts[i]] } };
            _views[i].AddChild(camera);
            _fog[i] = new SurfaceFogRegistry(camera);
            var background = (ShaderMaterial)compiler.Build(new MaterialEntry { Name = "effect-test-background", Unlit = true }, LightSource.None);
            using Image black = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            black.Fill(new Color(.1f, .2f, .3f));
            background.SetShaderParameter("tex_diffuse", ImageTexture.CreateFromImage(black));
            _views[i].AddChild(new MeshInstance3D { Position = new(0, 0, -1), Mesh = new QuadMesh { Size = new(4, 4) }, MaterialOverride = background });
            _fronts[i] = new MeshInstance3D();
            _views[i].AddChild(_fronts[i]);
            // The mask also verifies the merc laser's custom stencil read when placed behind a marked surface.
            var occluder = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded; stencil_mode write, compare_always, 1; void fragment(){ ALBEDO=vec3(0.2); }" } };
            _occluders[i] = new MeshInstance3D { Position = new(-.07f, 0, -.15f), Mesh = new QuadMesh { Size = new(.08f, .5f) }, MaterialOverride = occluder, Visible = false };
            _views[i].AddChild(_occluders[i]);
        }
    }

    private void Configure()
    {
        if (_phase != 12)
        {
            _reference = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new(.35f, .6f, .8f) };
            _effect = new EffectMaterial { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = _reference.AlbedoColor };
        }
        StandardMaterial3D reference = _reference!;
        EffectMaterial effect = _effect!;
        for (int i = 0; i < 2; i++)
        {
            _fronts[i].Mesh = new QuadMesh { Size = new(.2f, .2f) };
            _fronts[i].Position = new(0, 0, -.2f);
            _fronts[i].Rotation = Vector3.Zero;
            _fronts[i].MaterialOverride = i == 0 ? reference : effect;
            _occluders[i].Visible = false;
            _posts[i].Settings = new DisplayPostSettings { GlowGate = false, SurfaceFogApplied = true };
            _fog[i].Update(_posts[i].Settings);
        }
        switch (_phase)
        {
            case 1:
                reference.Transparency = effect.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                reference.AlbedoColor = effect.AlbedoColor = new(.35f, .6f, .8f, .4f);
                break;
            case 2:
            case 3:
            case 4:
                reference.Transparency = effect.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                reference.BlendMode = effect.BlendMode = _phase == 2 ? BaseMaterial3D.BlendModeEnum.Add : _phase == 3 ? BaseMaterial3D.BlendModeEnum.Mul : BaseMaterial3D.BlendModeEnum.Sub;
                reference.AlbedoColor = effect.AlbedoColor = new(.2f, .3f, .4f, .65f);
                break;
            case 5:
                reference.AlbedoTexture = effect.AlbedoTexture = _texture;
                reference.Uv1Scale = effect.Uv1Scale = new(.7f, .5f, 1);
                reference.Uv1Offset = effect.Uv1Offset = new(.15f, .25f, 0);
                break;
            case 6:
                reference.VertexColorUseAsAlbedo = effect.VertexColorUseAsAlbedo = true;
                reference.AlbedoTexture = effect.AlbedoTexture = _texture;
                foreach (MeshInstance3D mesh in _fronts) mesh.Mesh = ColoredQuad();
                break;
            case 7:
                reference.BillboardMode = effect.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
                foreach (MeshInstance3D mesh in _fronts) mesh.Rotation = new(.3f, .7f, .2f);
                break;
            case 8:
                reference.FixedSize = effect.FixedSize = true;
                foreach (MeshInstance3D mesh in _fronts) mesh.Position = new(0, 0, -2);
                break;
            case 9:
                reference.NextPass = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new(.8f, .15f, .1f, .4f) };
                effect.NextPass = new EffectMaterial { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new(.8f, .15f, .1f, .4f) };
                // Rebuilding the generated variant must retain the existing second pass.
                reference.CullMode = effect.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                break;
            case 10:
                reference.NoDepthTest = effect.NoDepthTest = true;
                reference.Transparency = effect.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                foreach (MeshInstance3D mesh in _occluders) mesh.Visible = true;
                break;
            case 11:
                reference.CullMode = effect.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                reference.StencilMode = effect.StencilMode = BaseMaterial3D.StencilModeEnum.Custom;
                reference.StencilFlags = effect.StencilFlags = (int)BaseMaterial3D.StencilFlagsEnum.Read;
                reference.StencilCompare = effect.StencilCompare = BaseMaterial3D.StencilCompareEnum.NotEqual;
                reference.StencilReference = effect.StencilReference = 1;
                reference.Transparency = effect.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                foreach (MeshInstance3D mesh in _fronts) mesh.Rotation = new(0, MathF.PI, 0);
                // Stencil must hide this strip even though it never writes depth. A depth-only rejection
                // would let a broken stencil implementation accidentally match the reference.
                foreach (MeshInstance3D mesh in _occluders) { mesh.MaterialOverride = _stencilWriter; mesh.Visible = true; }
                break;
            case 12:
                // Reuse the previous material while rebuilding its feature key and mutating live uniform data.
                reference.StencilMode = effect.StencilMode = BaseMaterial3D.StencilModeEnum.Disabled;
                reference.BlendMode = effect.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                reference.AlbedoColor = effect.AlbedoColor = new(.9f, .35f, .55f, .7f);
                reference.AlbedoTexture = effect.AlbedoTexture = _texture;
                reference.Uv1Offset = effect.Uv1Offset = new(.25f, .125f, 0);
                break;
            default:
                if (_phase >= ReferenceCases)
                {
                    effect.AlbedoColor = new(1, 1, 1, _phase == ReferenceCases ? 1 : .5f);
                    effect.Transparency = _phase == ReferenceCases ? BaseMaterial3D.TransparencyEnum.Disabled : BaseMaterial3D.TransparencyEnum.Alpha;
                    if (_phase == ReferenceCases + 2)
                    {
                        effect.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                        effect.AlbedoColor = new(.35f, .35f, .35f);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        _fronts[i].MaterialOverride = effect; // Same object rendered by both independently fogged cameras.
                        var settings = new DisplayPostSettings { GlowGate = false, SurfaceFogApplied = true, Fog = true,
                            FogColor = i == 0 ? Colors.Red : Colors.Green, FogStart = 0, FogEnd = 125 };
                        _posts[i].Settings = settings;
                        _fog[i].Update(settings);
                    }
                }
                break;
        }
    }

    private static ArrayMesh ColoredQuad()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(-.1f, .1f, 0), new(.1f, .1f, 0), new(.1f, -.1f, 0), new(-.1f, -.1f, 0) };
        arrays[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        arrays[(int)Mesh.ArrayType.Color] = new Color[] { new(.4f, .6f, .8f, .5f), new(.7f, .5f, .3f, .8f), Colors.White, new(.2f, .4f, .6f) };
        arrays[(int)Mesh.ArrayType.Index] = new int[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static float Difference(Color a, Color b) => MathF.Max(MathF.Abs(a.R - b.R), MathF.Max(MathF.Abs(a.G - b.G), MathF.Abs(a.B - b.B)));
    private SmokeResult Finish(SmokeResult result)
    {
        for (int i = 0; i < 2; i++) { _fog[i]?.Dispose(); _posts[i]?.Release(); _views[i]?.QueueFree(); }
        return result;
    }
}
