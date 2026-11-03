using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>Actual Vulkan fog pixels for synthetic clear/fog rooms, duplicate portals and a missed opening.</summary>
public sealed class PortalFogSmoke : SmokeTest
{
    public const string TestName = "portal-fog";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private DisplaySpaceEffect? _effect;
    private int _phase, _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_viewport is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("a Vulkan renderer is required");
            _viewport = new SubViewport { Size = new(128, 128), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_viewport);
            _effect = new DisplaySpaceEffect();
            _camera = new Camera3D { Current = true, CullMask = 1, Near = UeSpace.LengthToGodot(RenderK.NearClipUnits),
                Far = UeSpace.LengthToGodot(RenderK.FarClipUnits), Environment = FaithfulView.CreateEnvironment(),
                Compositor = new Compositor { CompositorEffects = [_effect] } };
            _viewport.AddChild(_camera);
            _viewport.AddChild(new MeshInstance3D { Position = new(0, 0, -1), Mesh = new QuadMesh { Size = new(4, 4) },
                MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded; void fragment(){ ALBEDO=vec3(0.0); }" } } });
        }
        if (_effect!.Error is { } error) return SmokeResult.Fail(error);
        var lighting = new LightingDoc();
        lighting.Zones.Add(new("Clear", 0, false, default, false, default, 0, 0, ""));
        lighting.Zones.Add(new("Fog", 1, false, default, true, Colors.Red, 0, _phase == 1 ? 200 : 100, ""));
        float x = _phase == 2 ? 500 : 0;
        var portal = new FogPortal(new(0, -1, 0, 20), 0, 1,
            [new(x - 100, -20, -100), new(x + 100, -20, -100), new(x + 100, -20, 100), new(x - 100, -20, 100)]);
        lighting.FogPortals.Add(portal);
        if (_phase == 3) lighting.FogPortals.Add(portal);
        _effect.Settings = new DisplayPostSettings { GlowGate = false, PortalFog = PortalFogFrame.Build(lighting, _camera!, FVector.Zero, 0, false, default, 0, 0) };
        if (++_frames < 8) return SmokeResult.Running;
        using Image shot = _viewport.GetTexture().GetImage();
        Color pixel = shot.GetPixel(64, 64);
        float expected = _phase == 1 ? .4f : _phase == 2 ? 0f : .8f;
        string output = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "b5-fixes", "content", "portal-fog");
        Directory.CreateDirectory(output);
        shot.SavePng(Path.Combine(output, $"phase-{_phase}.png"));
        if (MathF.Abs(pixel.R - expected) > .035f || pixel.G > .02f || pixel.B > .02f)
            return SmokeResult.Fail($"phase={_phase} expected red={expected}, actual={pixel}");
        if (++_phase < 4) { _frames = 0; return SmokeResult.Running; }
        _effect.Release();
        _viewport.QueueFree();
        return SmokeResult.Pass("GPU pixels: clear-to-fog portal, authored density, missed polygon, duplicate crossing");
    }
}
