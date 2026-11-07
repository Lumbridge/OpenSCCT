using System;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>GPU contact shading, flat-surface preservation, vision eligibility, resize and resolved MSAA.</summary>
public sealed class AmbientOcclusionSmoke : SmokeTest
{
    public const string TestName = "ambient-occlusion";
    public override string Name => TestName;
    private SubViewport? _view;
    private Camera3D? _camera;
    private AmbientOcclusionEffect? _effect;
    private DisplaySpaceEffect? _display;
    private Image? _baseline;
    private int _phase, _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_view is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires GPU pixels");
            _view = new SubViewport { Size = new(128, 128), OwnWorld3D = true, UseDebanding = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_view);
            _effect = new AmbientOcclusionEffect();
            _display = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
            _camera = new Camera3D { Current = true, Near = .01f, Far = 100f, Position = new(2, 1.8f, 2),
                Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
                Compositor = new Compositor { CompositorEffects = [_effect, _display] } };
            _view.AddChild(_camera);
            _camera.LookAt(new(0, .25f, -1.5f));
            var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new(.65f, .65f, .65f) };
            void Box(Vector3 size, Vector3 position) => _view.AddChild(new MeshInstance3D
                { Mesh = new BoxMesh { Size = size }, Position = position, MaterialOverride = material });
            Box(new(6, .1f, 6), new(0, -.05f, -1));
            Box(new(6, 3, .1f), new(0, 1.5f, -2));
            Box(new(.6f, .6f, .6f), new(.3f, .3f, -.8f));
        }
        int scenario = _phase % 5;
        if (_frames == 0)
        {
            _view.Size = _phase < 5 ? new(128, 128) : new(192, 128);
            _view.Msaa3D = _phase < 5 ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa4X;
            _effect!.Configure(scenario is 1 or 2 or 3, scenario == 2 ? RenderK.RendMapThermal :
                scenario == 3 ? RenderK.RendMapNight : RenderK.RendMapNormal, _camera!);
        }
        if (++_frames < 8) return SmokeResult.Running;
        if (_effect!.Error is { } error) return Finish(SmokeResult.Fail(error));
        using Image shot = _view.GetTexture().GetImage();
        string captures = OS.GetEnvironment("SVM_PLAY_SHOTS");
        if (scenario <= 1 && !string.IsNullOrEmpty(captures))
        {
            System.IO.Directory.CreateDirectory(captures);
            shot.SavePng(System.IO.Path.Combine(captures, $"ao-{_phase / 5}-{(scenario == 0 ? "off" : "on")}.png"));
        }
        if (scenario == 0)
        {
            _baseline?.Dispose();
            _baseline = (Image)shot.Duplicate();
        }
        else
        {
            int darker = 0, unchanged = 0;
            float maxChange = 0;
            for (int y = 2; y < shot.GetHeight() - 2; y++)
            for (int x = 2; x < shot.GetWidth() - 2; x++)
            {
                Color before = _baseline!.GetPixel(x, y), after = shot.GetPixel(x, y);
                float difference = before.R - after.R;
                maxChange = Math.Max(maxChange, Math.Abs(difference));
                if (after.R > before.R + .01f || after.R < before.R * .50f - .01f)
                    return Finish(SmokeResult.Fail($"phase {_phase}: invalid shading {before} -> {after}"));
                if (before.R > .05f)
                {
                    if (difference > .004f) darker++;
                    else if (Math.Abs(difference) < .002f) unchanged++;
                }
            }
            bool active = scenario is 1 or 3;
            if (active ? darker < 10 || unchanged < 100 : maxChange > .003f)
                return Finish(SmokeResult.Fail($"phase {_phase}: contact pixels={darker}, flat pixels={unchanged}, max change={maxChange}, draws={_effect.Draws}"));
            if (active && _effect.Draws == 0) return Finish(SmokeResult.Fail("AO did not dispatch"));
        }
        _frames = 0;
        return ++_phase < 10 ? SmokeResult.Running : Finish(SmokeResult.Pass("contact shading and flat surfaces, normal/night/thermal/off, resize, MSAA off/4x"));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _baseline?.Dispose();
        _effect?.Release();
        _display?.Release();
        _view?.QueueFree();
        return result;
    }
}
