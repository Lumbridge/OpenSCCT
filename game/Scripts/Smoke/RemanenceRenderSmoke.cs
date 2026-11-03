using System;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>Capture and display-space after-image blending through the resolved color buffer with MSAA off/on.</summary>
public sealed class RemanenceRenderSmoke : SmokeTest
{
    public const string TestName = "remanence-render";
    public override string Name => TestName;
    private SubViewport? _view;
    private DisplaySpaceEffect? _display;
    private RemanenceEffect? _remanence;
    private ShaderMaterial? _material;
    private int _phase, _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_view is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires Vulkan pixels");
            MaterialCompiler.SetVision(RenderK.RendMapNormal);
            _view = new SubViewport { Size = new(64, 64), OwnWorld3D = true, UseDebanding = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_view);
            _display = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
            _remanence = new RemanenceEffect();
            _view.AddChild(new Camera3D { Current = true, CullMask = RenderLayers.MainView, Near = .01f, Far = 10,
                Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
                Compositor = new Compositor { CompositorEffects = [_display, _remanence] } });
            var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(""));
            _material = (ShaderMaterial)compiler.Build(new MaterialEntry { Name = "remanence-color", Unlit = true }, LightSource.None);
            _view.AddChild(new MeshInstance3D { Position = new(0, 0, -1), Mesh = new QuadMesh { Size = new(4, 4) }, MaterialOverride = _material });
        }

        int scenario = _phase % 4;
        if (_frames == 0)
        {
            _view.Msaa3D = _phase < 4 ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa4X;
            using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            image.Fill(scenario == 0 ? Colors.Red : Colors.Blue);
            _material!.SetShaderParameter("tex_diffuse", ImageTexture.CreateFromImage(image));
            _remanence!.Factor = scenario switch { 1 => 1, 2 => .25f, _ => 0 };
        }
        // Capture after the new target/texture has reached the render thread.
        if (++_frames == 6 && scenario == 0) _remanence!.RequestCapture();
        if (_frames < 12) return SmokeResult.Running;
        using Image shot = _view.GetTexture().GetImage();
        Color pixel = shot.GetPixel(32, 32);
        Vector3 expected = scenario switch { 0 or 1 => Vector3.Right, 2 => new(.25f, 0, .75f), _ => Vector3.Back };
        Vector3 difference = (new Vector3(pixel.R, pixel.G, pixel.B) - expected).Abs();
        float error = MathF.Max(difference.X, MathF.Max(difference.Y, difference.Z));
        if (error > .015f || _remanence!.Captures != (_phase < 4 ? 1 : 2))
            return Finish(SmokeResult.Fail($"phase={_phase} expected={expected} pixel={pixel} error={error} captures={_remanence!.Captures}"));
        _frames = 0;
        return ++_phase < 8 ? SmokeResult.Running : Finish(SmokeResult.Pass("8 GPU samples: capture, hold, partial fade and clear, MSAA off/4x"));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _display?.Release();
        _remanence?.Release();
        _view?.QueueFree();
        return result;
    }
}
