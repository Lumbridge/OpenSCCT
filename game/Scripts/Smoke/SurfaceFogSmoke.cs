using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Post;
using NVector3 = System.Numerics.Vector3;

namespace OpenScctGame.Smoke;

/// <summary>Shared materials in simultaneous, co-located views: fog before blending, including MSAA.</summary>
public sealed class SurfaceFogSmoke : SmokeTest
{
    public const string TestName = "surface-fog";
    public override string Name => TestName;
    private readonly SubViewport[] _views = new SubViewport[2];
    private readonly Camera3D[] _cameras = new Camera3D[2];
    private readonly DisplaySpaceEffect[] _effects = new DisplaySpaceEffect[2];
    private readonly SurfaceFogRegistry[] _bindings = new SurfaceFogRegistry[2];
    private const int Scenarios = 9;
    private readonly ShaderMaterial[] _materials = new ShaderMaterial[6];
    private MeshInstance3D? _front;
    private int _phase, _frames;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_front is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires Vulkan pixels");
            MaterialCompiler.SetVision(RenderK.RendMapNormal);
            var compiler = new MaterialCompiler(new MaterialsDoc(), new TextureCache(""));
            var blends = new[] { MaterialBlend.Translucent, MaterialBlend.Additive, MaterialBlend.Modulate, MaterialBlend.Darken };
            for (int i = 0; i < blends.Length; i++)
            {
                _materials[i] = (ShaderMaterial)compiler.Build(new MaterialEntry { Name = "fog-" + blends[i], Unlit = true, ListBlend = blends[i] }, LightSource.None);
                Color pixel = i == 0 ? new(1, 1, 1, 128f / 255f) : i == 1 ? new(25f / 255f, 25f / 255f, 25f / 255f) : new(64f / 255f, 64f / 255f, 64f / 255f);
                _materials[i].SetShaderParameter("tex_diffuse", Pixel(pixel));
            }

            _materials[4] = (ShaderMaterial)compiler.Build(new MaterialEntry { Name = "fog-reflective-glass", Unlit = true,
                ListBlend = MaterialBlend.Translucent, EnvColor = Vector3.One, SpecMask = "fixture-mask" }, LightSource.None);
            _materials[4].SetShaderParameter("tex_diffuse", Pixel(new(64f / 255f, 64f / 255f, 64f / 255f, 128f / 255f)));
            _materials[4].SetShaderParameter("tex_spec_mask", Pixel(Colors.White));
            _materials[4].SetShaderParameter("env_color", Vector3.One * .1f);
            var body = (ShaderMaterial)compiler.Build(new MaterialEntry { Name = "fog-cloak-body", Unlit = true }, LightSource.None);
            body.SetShaderParameter("tex_diffuse", Pixel(new(64f / 255f, 64f / 255f, 64f / 255f)));
            var cloak = new CloakMaterials();
            cloak.SetAmount(.6f);
            _materials[5] = (ShaderMaterial)cloak.Get(body);

            for (int i = 0; i < 2; i++)
            {
                _views[i] = new SubViewport { Size = new(128, 128), OwnWorld3D = i == 0, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseDebanding = false };
                context.Tree.Root.AddChild(_views[i]);
                if (i != 0) _views[i].World3D = _views[0].FindWorld3D();
                _effects[i] = new DisplaySpaceEffect();
                _cameras[i] = new Camera3D { Current = true, CullMask = RenderLayers.MainView, Near = .01f, Far = 100,
                    Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
                    Compositor = new Compositor { CompositorEffects = [_effects[i]] } };
                _views[i].AddChild(_cameras[i]);
                _bindings[i] = new SurfaceFogRegistry(_cameras[i]);
            }

            var black = (ShaderMaterial)compiler.Build(new MaterialEntry { Name = "fog-background", Unlit = true }, LightSource.None);
            black.SetShaderParameter("tex_diffuse", Pixel(Colors.Black));
            _views[0].AddChild(new MeshInstance3D { Position = new(0, 0, -1), Mesh = new QuadMesh { Size = new(4, 4) }, MaterialOverride = black });
            _front = new MeshInstance3D { Mesh = new QuadMesh { Size = new(2, 2) } };
            _views[0].AddChild(_front);
        }

        int scenario = _phase % Scenarios;
        if (_frames == 0)
        {
            _front.Position = new(0, 0, scenario is 5 or 6 ? -.4f : -.2f);
            _front.MaterialOverride = _materials[scenario < 4 ? scenario : scenario >= 7 ? scenario - 3 : 0];
            for (int i = 0; i < 2; i++)
            {
                if (scenario == 4)
                {
                    // A new camera binding may reuse a slot from a fogged view; it must begin clear.
                    _bindings[i].Dispose();
                    _bindings[i] = new SurfaceFogRegistry(_cameras[i]);
                }
                _views[i].Msaa3D = _phase < Scenarios ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa4X;
                Color color = i == 0 ? Colors.Red : Colors.Green;
                PortalFogFrame? portal = null;
                if (scenario is 5 or 6)
                {
                    var lighting = new LightingDoc();
                    if (scenario == 5)
                    {
                        lighting.Zones.Add(new("Clear", 1, false, default, false, default, 0, 0, ""));
                        lighting.Zones.Add(new("Fog", 2, false, default, true, color, 0, 100, ""));
                        lighting.FogPortals.Add(new(new(0, -1, 0, 20), 1, 2,
                            [new(-100, -20, -100), new(100, -20, -100), new(100, -20, 100), new(-100, -20, 100)]));
                    }
                    else
                    {
                        lighting.FogVolumes.Add(new FogVolumePiece(new NVector3(color.R, color.G, color.B), .03f, 0, 0, 0, -1,
                            [new(0, 1, 0, 0), new(0, -1, 0, 100), new(1, 0, 0, 100), new(-1, 0, 0, 100), new(0, 0, 1, 100), new(0, 0, -1, 100)], 0));
                    }
                    portal = PortalFogFrame.Build(lighting, _cameras[i], FVector.Zero, 1, false, default, 0, 0);
                }
                var settings = new DisplayPostSettings { Fog = scenario is not (4 or 5 or 6), FogColor = color, FogStart = 0, FogEnd = 125,
                    PortalFog = portal, GlowGate = false, SurfaceFogApplied = true };
                _effects[i].Settings = settings;
                _bindings[i].Update(settings);
            }
        }

        if (++_frames < 12) return SmokeResult.Running;
        string output = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "b5-fixes", "surface-fog");
        Directory.CreateDirectory(output);
        float alpha = 128f / 255f, through = .84f;
        for (int i = 0; i < 2; i++)
        {
            if (_effects[i].Error is { } error) return Finish(SmokeResult.Fail(error));
            using Image shot = _views[i].GetTexture().GetImage();
            shot.SavePng(Path.Combine(output, $"phase-{_phase}-view-{i}.png"));
            Color pixel = shot.GetPixel(64, 64);
            Vector3 fog = i == 0 ? Vector3.Right : Vector3.Up;
            Vector3 background = fog * .8f;
            Vector3 expected = scenario switch
            {
                0 => background * (1 - alpha) + (Vector3.One * through + fog * (1 - through)) * alpha,
                1 => background + Vector3.One * (25f / 255f * through),
                2 => background * (254f / 255f * (1 - through) + (128f / 255f * through)),
                3 => background * (1 - through + ((1 - 64f / 255f) * through)),
                4 => Vector3.One * alpha,
                5 => background * (1 - alpha) + (Vector3.One * .8f + fog * .2f) * alpha,
                6 => fog * ((1 - MathF.Exp(-3)) * (1 - alpha)) + (Vector3.One * MathF.Exp(-1.2f) + fog * (1 - MathF.Exp(-1.2f))) * alpha,
                7 => background * (1 - alpha) + (Vector3.One * (64f / 255f * through) + fog * (1 - through)) * alpha + Vector3.One * (.1f * through),
                _ => (Vector3.One * (64f / 255f * through) + fog * (1 - through)) * .4f + background * .6f,
            };
            Vector3 errorRgb = (new Vector3(pixel.R, pixel.G, pixel.B) - expected).Abs();
            float worst = MathF.Max(errorRgb.X, MathF.Max(errorRgb.Y, errorRgb.Z));
            if (worst > .025f) return Finish(SmokeResult.Fail($"phase={_phase} view={i} expected={expected} pixel={pixel} error={worst}"));
        }
        _frames = 0;
        return ++_phase < Scenarios * 2 ? SmokeResult.Running : Finish(SmokeResult.Pass("36 GPU samples: two simultaneous views, alpha/add/modulate/darken, separate reflection, cloak, no fog, portal and volume, MSAA off/4x"));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        for (int i = 0; i < 2; i++) { _bindings[i]?.Dispose(); _effects[i]?.Release(); _views[i]?.QueueFree(); }
        return result;
    }

    private static ImageTexture Pixel(Color color)
    {
        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(color);
        return ImageTexture.CreateFromImage(image);
    }
}
