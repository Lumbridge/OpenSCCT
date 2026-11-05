using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>
/// Renders the real spy body's back-lamp texels through dry and both wet materials. An enlarged UV patch isolates
/// this indicator from the forearm screen and goggle lenses that share its self-illumination image.
/// </summary>
public sealed class SpyBackLightSmoke : SmokeTest
{
    public const string TestName = "spy-back-light";
    private const string Body = "SPersoTextures.Shadw_agent";
    private readonly string[] _names = ["dry-dark", "dry-lit", "wet-spy-night-dark", "wet-spy-night-lit",
        "wet-merc-motion-dark", "wet-merc-motion-lit", "wet-merc-motion-torch", "restored-dry-dark",
        "wet-merc-without-emission", "body-back"];
    private SubViewport? _viewport;
    private Camera3D _camera = null!;
    private DisplaySpaceEffect _display = null!;
    private SkeletalModel _model = null!;
    private MeshInstance3D _patch = null!;
    private Material _dry = null!, _wetSpy = null!, _wetMerc = null!, _withoutEmission = null!;
    private SpotShadowLight _torch = null!;
    private string _folder = "";
    private int _stage, _frames, _teardown;
    private float _darkGreen, _wetMercGreen, _wetMercFrameGreen;
    private SmokeResult? _final;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_final is { } result) return ++_teardown < 6 ? SmokeResult.Running : result;
        if (_viewport is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer");
            string? glb = SkeletalModel.FindGlb("SPerso.ATT_01");
            if (glb is null) return SmokeResult.Fail("converted spy character is required");
            string? failure = Build(context, glb);
            if (failure is not null) return Finish(SmokeResult.Fail(failure));
            Configure();
        }
        _torch.Push();
        if (++_frames < 16) return SmokeResult.Running;
        using Image shot = _viewport!.GetTexture().GetImage();
        shot.SavePng(Path.Combine(_folder, _names[_stage] + ".png"));
        if (_stage < 9)
        {
            // Centre of the lamp: the original SI mask is fully opaque over these texels. Its feathered edge can
            // legitimately brighten with diffuse lighting; testing that edge would mistake it for varying emission.
            float green = 0f;
            for (int y = 122; y < 150; y++)
                for (int x = 106; x < 134; x++) green += shot.GetPixel(x, y).G;
            green /= 28 * 28;
            if (_stage < 8 && green < 0.7f)
                return Finish(SmokeResult.Fail($"{_names[_stage]}: back lamp disappeared (green {green:F3})"));
            if (_stage is 0 or 2 or 4) _darkGreen = green;
            if (_stage is 1 or 3 or 5 or 6 && MathF.Abs(green - _darkGreen) > 0.025f)
                return Finish(SmokeResult.Fail($"{_names[_stage]}: lamp brightness changed with illumination ({_darkGreen:F3} to {green:F3})"));
            if (_stage == 4)
            {
                _wetMercGreen = green;
                _wetMercFrameGreen = MeanGreen(shot);
            }
            if (_stage == 6 && MeanGreen(shot) < _wetMercFrameGreen + 0.005f)
                return Finish(SmokeResult.Fail("torch fixture did not illuminate the non-emissive material beside the lamp"));
            // Negative control: the same wet reflection and diffuse with SI masked out must fail the visibility
            // gate. This proves a bright env-map reflection alone cannot make the original missing-SI bug pass.
            if (_stage == 8 && (green >= 0.7f || _wetMercGreen - green < 0.15f))
                return Finish(SmokeResult.Fail($"missing-emission control still passes the lamp gate ({green:F3} vs {_wetMercGreen:F3})"));
            GD.Print($"SVM_SPY_BACK_LIGHT {_names[_stage]} green={green:F3}");
        }
        if (++_stage == _names.Length)
            return Finish(SmokeResult.Pass("constant back lamp through dry/wet spy/wet merc, dark/bright/torch, night/motion material modes and dry restoration; " + _folder));
        _frames = 0;
        Configure();
        return SmokeResult.Running;
    }

    private string? Build(SmokeContext context, string glb)
    {
        _folder = Path.GetFullPath(ProjectSettings.GlobalizePath("res://../local/spy-back-light"));
        Directory.CreateDirectory(_folder);
        _viewport = new SubViewport { Size = new Vector2I(256, 256), OwnWorld3D = true, UseDebanding = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        // Keep the diagnostic post pass in normal mode, including when the material is in night/motion mode, so
        // the measured value is the lamp's emission before the vision filter remaps colours and adds noise.
        _display = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 2f, Current = true,
            Near = 0.01f, Far = 100, Position = new Vector3(0, 0, 2), CullMask = RenderLayers.MainView,
            Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
            Compositor = new Compositor { CompositorEffects = [_display] } };
        _viewport.AddChild(_camera);
        _model = SkeletalModel.Load(glb);
        _viewport.AddChild(_model.Root);
        _model.Root.Visible = false;
        _model.Place(default, default, default);
        _model.Sampler.Play("WaitStAlFd0");
        _model.Pose();
        foreach (MeshInstance3D mesh in _model.Meshes)
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.Mesh.SurfaceGetMaterial(surface)?.ResourceName == Body)
                    _dry = mesh.GetSurfaceOverrideMaterial(surface)!;
        _wetSpy = WetSkins.For(glb, localSpy: true)?.Over(Body)!;
        _wetMerc = WetSkins.For(glb, localSpy: false)?.Over(Body)!;
        if (_dry is null || _wetSpy is null || _wetMerc is null) return "spy dry/wet materials missing";
        if (_wetMerc is not ShaderMaterial wet || wet.GetShaderParameter("tex_self_illum_mask").AsGodotObject() is not Texture2D)
            return "wet merc body has no self-illumination mask";
        var withoutEmission = (ShaderMaterial)wet.Duplicate();
        using (Image transparent = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8))
        {
            transparent.Fill(Colors.Transparent);
            withoutEmission.SetShaderParameter("tex_self_illum_mask", ImageTexture.CreateFromImage(transparent));
        }
        _withoutEmission = withoutEmission;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0) };
        arrays[(int)Mesh.ArrayType.Normal] = new Vector3[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
        // ATT_01's back indicator is the small SI island at (237..250,128..140) in the 256-square body texture.
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[] { new(236f / 256, 142f / 256), new(252f / 256, 142f / 256),
            new(252f / 256, 126f / 256), new(236f / 256, 126f / 256) };
        arrays[(int)Mesh.ArrayType.Index] = new int[] { 0, 2, 1, 0, 3, 2 };
        var patchMesh = new ArrayMesh();
        patchMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _patch = new MeshInstance3D { Mesh = patchMesh, Layers = RenderLayers.World };
        _viewport.AddChild(_patch);
        _torch = new SpotShadowLight(_viewport, null, SpotShadowShape.Torch with { Cookie = "" });
        _torch.Place(new FVector(0, 200, 0), new FRotator(0, -16384, 0));
        return null;
    }

    private void Configure()
    {
        MaterialCompiler.SetVision(_stage is 2 or 3 ? RenderK.RendMapNight : _stage is 4 or 5 or 6 or 8 ? 37 : RenderK.RendMapNormal);
        _patch.MaterialOverride = _stage == 8 ? _withoutEmission : _stage is 2 or 3 ? _wetSpy : _stage is >= 4 and <= 6 ? _wetMerc : _dry;
        ActorLighting.Apply(_patch, new ActorLightSet { Ambient = _stage is 1 or 3 or 5 ? Vector3.One : Vector3.Zero });
        _torch.On = _stage == 6;
        if (_stage != 9) return;
        _patch.Visible = false;
        _model.Root.Visible = true;
        _model.SetSkins(null);
        _model.Light(new ActorLightSet { Ambient = new Vector3(0.35f, 0.35f, 0.35f) });
        _viewport!.Size = new Vector2I(480, 640);
        _camera.Size = 2.5f;
        Vector3 target = new(0, 0.1f, 0);
        _camera.Position = target + new Vector3(-3, 0, 0);
        _camera.LookAt(target);
    }

    private static float MeanGreen(Image image)
    {
        float sum = 0f;
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++) sum += image.GetPixel(x, y).G;
        return sum / (image.GetWidth() * image.GetHeight());
    }

    private SmokeResult Finish(SmokeResult result)
    {
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
        _torch?.Dispose();
        _display?.Release();
        _viewport?.QueueFree();
        _final = result;
        return SmokeResult.Running;
    }
}
