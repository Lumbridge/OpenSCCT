using System;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Smoke;

/// <summary>A corona's visible rim stays occluded as its centre crosses each screen edge, with both depth sources.</summary>
public sealed class CameraEdgeSmoke : SmokeTest
{
    public const string TestName = "camera-edge";
    public override string Name => TestName;
    private readonly SubViewport[] _views = new SubViewport[2];
    private readonly MeshInstance3D[] _coronas = new MeshInstance3D[2], _occluders = new MeshInstance3D[2];
    private CoronaSampleEffect? _copies;
    private bool _copyReady;
    private int _phase, _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires rendered pixels");
        if (_views[0] is null) Setup(context);
        int edge = _phase % 24 / 6;
        bool hidden = _phase % 6 < 3;
        float offset = 1.98f + (_phase % 3) * .02f;
        Vector3 center = edge switch
        {
            0 => new(offset, 0, -2),
            1 => new(-offset, 0, -2),
            2 => new(0, offset, -2),
            _ => new(0, -offset, -2),
        };
        for (int i = 0; i < 2; i++)
        {
            _coronas[i].Position = center;
            _occluders[i].Visible = hidden;
            if (_frames == 0)
            {
                _views[i].Msaa3D = _phase < 24 ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa4X;
                // Only this edge is blocked: sampling the opposite edge through a repeating sampler must fail too.
                _occluders[i].Position = center * new Vector3(.4f, .4f, 0) + new Vector3(0, 0, -1);
                ((QuadMesh)_occluders[i].Mesh).Size = edge < 2 ? new(.6f, 3) : new(3, .6f);
            }
        }
        _copies!.Publish([new(center, .4f)]);
        if (++_frames < 12 || !_copyReady) return SmokeResult.Running;
        Vector2I pixel = edge switch { 0 => new(126, 64), 1 => new(1, 64), 2 => new(64, 1), _ => new(64, 126) };
        for (int i = 0; i < 2; i++)
        {
            using Image image = _views[i].GetTexture().GetImage();
            Color c = image.GetPixel(pixel.X, pixel.Y);
            float light = MathF.Max(c.R, MathF.Max(c.G, c.B));
            if (hidden ? light > .01f : light < .15f)
            {
                string file = SmokePaths.Local("camera-edge", $"corona-{_phase}-{i}.png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                image.SavePng(file);
                return Finish(SmokeResult.Fail($"edge={edge} offset={offset} hidden={hidden} copies={i == 1} pixel={c}; {file}"));
            }
        }
        _frames = 0;
        return ++_phase < 48 ? SmokeResult.Running : Finish(SmokeResult.Pass("96 rendered corona cases: on, inside and outside all four edges, occluded and clear, screen and copied depth, with and without 4x MSAA"));
    }

    private void Setup(SmokeContext context)
    {
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
        using Image white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        white.Fill(Colors.White);
        Texture2D texture = ImageTexture.CreateFromImage(white);
        var black = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, fog_disabled; void fragment(){ ALBEDO=vec3(0.0); }" } };
        for (int i = 0; i < 2; i++)
        {
            _views[i] = new SubViewport { Size = new(128, 128), OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseDebanding = false };
            var camera = new Camera3D { Current = true, CullMask = RenderLayers.World, Fov = 90, Near = .01f, Far = 10,
                Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes() };
            _views[i].AddChild(camera);
            context.Tree.Root.AddChild(_views[i]);
            var screen = new ShaderMaterial { Shader = ResponsiveSmokeShaders.Load(Coronas.ShaderPath) };
            screen.SetShaderParameter("corona_tex", texture);
            _coronas[i] = new MeshInstance3D { Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = screen, ExtraCullMargin = 5 };
            _views[i].AddChild(_coronas[i]);
            InstanceParams.Set(_coronas[i], "svm_corona", new Vector4(.4f, 0, 0, 0));
            InstanceParams.Set(_coronas[i], "svm_corona_colour", Vector4.One);
            _views[i].AddChild(new MeshInstance3D { Position = new(0, 0, -4), Mesh = new QuadMesh { Size = new(12, 12) }, MaterialOverride = black });
            _occluders[i] = new MeshInstance3D { Position = new(0, 0, -1), Mesh = new QuadMesh { Size = new(4, 4) }, MaterialOverride = black };
            _views[i].AddChild(_occluders[i]);
            if (i == 0) continue;
            _copies = new CoronaSampleEffect(camera);
            camera.Compositor = new Compositor { CompositorEffects = [_copies] };
            var copies = new ShaderMaterial { Shader = ResponsiveSmokeShaders.Load(Coronas.CopiesShaderPath) };
            copies.SetShaderParameter("corona_tex", texture);
            copies.SetShaderParameter("depth_tex", _copies.Depth);
            copies.SetShaderParameter("scene_tex", _copies.Scene);
            _copies.UseCopies = ready => { _copyReady = ready; _coronas[1].MaterialOverride = ready ? copies : screen; };
        }
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _copies?.Release();
        foreach (SubViewport view in _views) view.QueueFree();
        return result;
    }
}
