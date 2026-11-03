using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Post;
using NVector3 = System.Numerics.Vector3;

namespace OpenScctGame.Smoke;

/// <summary>Vulkan pixels from synthetic fog brushes with measured extinction, height and open-face fade.</summary>
public sealed class FogVolumeSmoke : SmokeTest
{
    public const string TestName = "fog-volume";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private DisplaySpaceEffect? _effect;
    private SurfaceFogRegistry? _surface;
    private MeshInstance3D? _mesh;
    private int _phase, _frames;
    private readonly Dictionary<int, Color[]> _referencePixels = [];

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
            _mesh = new MeshInstance3D { Position = new(0, 0, -1), Mesh = new QuadMesh { Size = new(4, 4) },
                MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded; void fragment(){ ALBEDO=vec3(0.0); }" } } };
            _viewport.AddChild(_mesh);
        }
        if (_effect!.Error is { } error) return SmokeResult.Fail(error);
        if (_phase == 6 && _surface is null)
        {
            _surface = new SurfaceFogRegistry(_camera!);
            _mesh!.MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = """
                shader_type spatial;
                render_mode unshaded;
                #include "res://Shaders/svm_fog.gdshaderinc"
                void fragment() { ALBEDO = surface_fog(CAMERA_VISIBLE_LAYERS, VERTEX, FRAGCOORD.xy, VIEWPORT_SIZE).rgb; }
                """ } };
        }
        int scene = _phase % 6;
        var lighting = new LightingDoc();
        float offset = scene == 3 ? 500 : 0;
        var piece = new FogVolumePiece(NVector3.UnitX, 3f / 240f, scene == 1 ? -20 : 0, scene == 1 ? 20 : 0,
            scene == 2 ? 20 : 0, scene == 4 ? 2 : -1,
            [new(0, 1, 0, -20), new(0, -1, 0, 100), new(1, 0, 0, offset + 100), new(-1, 0, 0, 100 - offset),
                new(0, 0, 1, 100), new(0, 0, -1, 100)], scene == 2 ? 2 : 0)
        {
            Minimum = new(offset - 100, -100, -100), Maximum = new(offset + 100, -20, 100),
        };
        lighting.FogVolumes.Add(piece);
        if (scene == 4)
            lighting.FogPortals.Add(new(new(0, -1, 0, 60), 1, 2,
                [new(-100, -60, -100), new(100, -60, -100), new(100, -60, 100), new(-100, -60, 100)]));
        if (scene == 5) lighting.FogVolumes.Add(piece with { Color = NVector3.UnitZ });
        var settings = new DisplayPostSettings { GlowGate = false, SurfaceFogApplied = _surface is not null,
            PortalFog = PortalFogFrame.Build(lighting, _camera!, FVector.Zero, 1, false, default, 0, 0) };
        _effect.Settings = settings;
        _surface?.Update(settings);
        if (++_frames < 8) return SmokeResult.Running;
        using Image shot = _viewport.GetTexture().GetImage();
        Color pixel = shot.GetPixel(64, 64);
        float expected = scene switch { 1 or 4 => 1f - MathF.Exp(-.5f), 2 => 1f - MathF.Exp(-.75f), 3 => 0f,
            5 => (1f - MathF.Exp(-2f)) * .5f, _ => 1f - MathF.Exp(-1f) };
        string output = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "b5-fixes", "network", "fog-volume");
        Directory.CreateDirectory(output);
        shot.SavePng(Path.Combine(output, $"phase-{_phase}.png"));
        if (MathF.Abs(pixel.R - expected) > .035f || pixel.G > .02f || MathF.Abs(pixel.B - (scene == 5 ? expected : 0f)) > .035f)
            return SmokeResult.Fail($"phase={_phase} expected red={expected}, actual={pixel}");
        var samples = new Color[16];
        for (int i = 0; i < samples.Length; i++) samples[i] = shot.GetPixel(16 + (i % 4) * 32, 16 + (i / 4) * 32);
        if (_phase < 6) _referencePixels[scene] = samples;
        else
            for (int i = 0; i < samples.Length; i++)
            {
                Color expectedPixel = _referencePixels[scene][i], actual = samples[i];
                if (MathF.Max(MathF.Abs(expectedPixel.R - actual.R), MathF.Max(MathF.Abs(expectedPixel.G - actual.G), MathF.Abs(expectedPixel.B - actual.B))) > .025f)
                    return SmokeResult.Fail($"phase={_phase} off-center sample={i} compute={expectedPixel} surface={actual}");
            }
        if (++_phase < 12) { _frames = 0; return SmokeResult.Running; }
        _surface?.Dispose();
        _effect.Release();
        _viewport.QueueFree();
        return SmokeResult.Pass("12 GPU compute/surface scenarios + 96 off-center comparisons: extinction, height, open edges, culling, zones and overlap");
    }
}
