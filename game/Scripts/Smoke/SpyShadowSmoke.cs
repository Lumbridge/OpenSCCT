using System;
using System.IO;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Projectors;

namespace OpenScctGame.Smoke;

/// <summary>Rendered own-spy contact shadow: actual skinning, crouch, motion, vision and native suppression.</summary>
public sealed class SpyShadowSmoke : SmokeTest
{
    public const string TestName = "spy-shadow";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private SkeletalModel _spy = null!;
    private SpyShadowProjector _shadow = null!;
    private byte[]? _before;
    private byte[]? _standingMask;
    private int _case, _frames;
    private string Folder => Path.Combine(RenderSmokeArgs.RepoRoot, "local", "spy-shadow-smoke");

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("Rendered shadow pixels required");
        if (_viewport is null) Setup(context);
        int scene = _case / 2; bool active = (_case & 1) != 0;
        if (_frames == 0)
        {
            MaterialCompiler.SetVision(scene == 4 ? 36 : scene == 5 ? 38 : scene == 2 ? 34 : 5);
            _spy.Sampler.Play(scene == 0 ? "WaitStAlFd0" : "WaitCrAlFd0"); _spy.Pose();
            Aabb? bounds = null;
            foreach (var m in _spy.Meshes) bounds = bounds is { } b ? b.Merge(m.GlobalTransform * m.GetAabb()) : m.GlobalTransform * m.GetAabb();
            _spy.Root.Position += Vector3.Up * -bounds!.Value.Position.Y;
            _spy.Root.Position = new Vector3(scene == 3 ? .5f : 0, _spy.Root.Position.Y + (scene == 9 ? 5 : 0), 0);
            _spy.SetGlass(scene == 6 ? .5f : 0);
            _spy.Root.Visible = scene != 8;
        }
        if (active) _shadow.Draw(_spy, scene is 6 or 7 ? (byte)0 : (byte)100, Vector3.Up, scene == 4 ? 36 : scene == 5 ? 38 : scene == 2 ? 34 : 5);
        else _shadow.HideShadow();
        if (++_frames < 10) return SmokeResult.Running;
        using Image shot = _viewport!.GetTexture().GetImage(); shot.Convert(Image.Format.Rgba8);
        if (active)
        {
            using Image mask = _shadow.Mask.GetImage(); mask.SavePng(Path.Combine(Folder, $"{scene}-mask.png"));
            mask.Convert(Image.Format.Rgba8); byte[] data = mask.GetData();
            if (scene == 0) _standingMask = data;
            if (scene == 1)
            {
                int difference = 0;
                for (int i = 3; i < data.Length; i += 4) if (data[i] != _standingMask![i]) difference++;
                if (difference < 50) return SmokeResult.Fail("Shadow silhouette did not follow the crouch pose");
            }
        }
        shot.SavePng(Path.Combine(Folder, $"{scene}-{(active ? "on" : "off")}.png"));
        byte[] pixels = shot.GetData();
        if (!active) _before = pixels;
        else
        {
            int dark = 0, changed = 0, bright = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int delta = _before![i] - pixels[i];
                if (delta > 4) dark++;
                if (Math.Abs(delta) > 2) changed++;
                if (delta < -4) bright++;
            }
            bool expected = scene < 4;
            GD.Print($"SPY_SHADOW case={scene} darker={dark} changed={changed} brighter={bright}");
            if (expected ? dark < 50 || bright > 10 : changed > 10)
                return SmokeResult.Fail($"case {scene}: darker={dark}, changed={changed}, brighter={bright}");
        }
        _frames = 0;
        if (++_case < 20) return SmokeResult.Running;
        MaterialCompiler.SetVision(5); _viewport.QueueFree();
        return SmokeResult.Pass("posed/skinned standing and crouched spy, moving root, normal/night; thermal/EMF/cloak/zero/hidden/out-of-range suppression");
    }

    private void Setup(SmokeContext context)
    {
        Directory.CreateDirectory(Folder);
        _viewport = new SubViewport { Size = new(512,512), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        var camera = new Camera3D { Current = true, Position = new(2.6f, 2.5f, 3.5f),
            CullMask = RenderLayers.MainView, Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes() };
        _viewport.AddChild(camera); camera.LookAt(new(0,.35f,0));
        var world = new Node3D(); _viewport.AddChild(world);
        var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(""));
        Material material = compiler.Build(new MaterialEntry { Name = "shadow-floor", Unlit = true }, LightSource.None);
        using var grey = Image.CreateEmpty(1,1,false,Image.Format.Rgba8); grey.Fill(new Color(.65f,.65f,.65f));
        ((ShaderMaterial)material).SetShaderParameter("tex_diffuse",ImageTexture.CreateFromImage(grey));
        world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(8,.1f,8) }, Position = new(0,-.05f,0), MaterialOverride = material });
        _spy = SkeletalModel.Load(SkeletalModel.FindGlb("SPerso.ATT_01") ?? throw new InvalidOperationException("Spy fixture missing"));
        _viewport.AddChild(_spy.Root); _spy.Place(default, default, default);
        _spy.Light(new ActorLightSet { Ambient = new(.6f,.6f,.6f) });
        _shadow = new SpyShadowProjector(world); _viewport.AddChild(_shadow);
    }
}
