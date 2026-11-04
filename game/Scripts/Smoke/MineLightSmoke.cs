using OpenScctGame.Render.Materials;
using Godot;
using OpenScct.Server;
using OpenScctGame.Fx;
using System;
using System.IO;
using OpenScctGame.Render;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

public sealed class MineLightSmoke : SmokeTest
{
    public const string TestName = "mine-light";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private DisplaySpaceEffect? _display;
    private ShaderMaterial? _laserLight;
    private int _frame, _phase;
    private float _firstRed;
    private string _folder = "";

    public override SmokeResult Step(SmokeContext context)
    {
        if (_viewport is not null) return CheckPixels();
        if (ContentPaths.Shared is not { } root) return SmokeResult.Fail("no shared content");
        var library = FxLibrary.Load(root);
        var view = new EffectViews { Library = library };
        context.Tree.Root.AddChild(view);
        const string on = "SGameplayObjectsTex.Object.proxy_mine_On";
        const string off = "SGameplayObjectsTex.Object.proxy_mine_Off_final";
        if (library.Mesh("SGameplayObjects.Mines.proxy_mine") is not { } mesh || mesh.GetSurfaceCount() < 2)
            return SmokeResult.Fail("mine light surface missing");
        if (view.SkinMaterial(on, 0) is not EffectMaterial material || material.AlbedoTexture is null)
            return SmokeResult.Fail("armed light material missing");
        Vector3 first = material.Uv1Offset;
        view.SkinMaterial(on, 0.5f);
        if (material.Uv1Offset == first) return SmokeResult.Fail("light did not blink after 0.5 seconds");
        view.SkinMaterial(on, 1f);
        if (material.Uv1Offset != first) return SmokeResult.Fail("light did not loop after one second");
        if (view.SkinMaterial(off, 0) is not EffectMaterial dark || dark == material || library.AnimationForSkin(off) is not null)
            return SmokeResult.Fail("chaffed light must remain off");
        // Laser mines keep the mesh's authored LED, rather than the proximity mine's run-time skin.
        if (mesh.SurfaceGetMaterial(1) is not ShaderMaterial laserLight)
            return SmokeResult.Fail("laser mine's authored LED material missing");
        _laserLight = laserLight;
        if (DisplayServer.GetName() == "headless")
            return laserLight.Shader.Code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal)
                ? SmokeResult.Pass("proximity blink/chaff skins and laser mine's transparent animated glow")
                : SmokeResult.Fail("laser mine LED still alpha-blends its opaque black animation background");
        _folder = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "mine-light-pixels");
        Directory.CreateDirectory(_folder);
        _viewport = new SubViewport { Size = new Vector2I(512, 256), OwnWorld3D = true, UseDebanding = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        _display = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
        _viewport.AddChild(new Camera3D { Current = true, Position = new Vector3(0, 0, 2),
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 1.2f, KeepAspect = Camera3D.KeepAspectEnum.Width,
            Near = 0.01f, Far = 10, CullMask = RenderLayers.MainView, Environment = FaithfulView.CreateEnvironment(),
            Attributes = FaithfulView.CreateAttributes(), Compositor = new Compositor { CompositorEffects = [_display] } });
        var background = new ShaderMaterial { Shader = new Shader { Code =
            "shader_type spatial; render_mode unshaded, fog_disabled; void fragment(){ALBEDO=vec3(0.2,0.3,0.4);}" } };
        _viewport.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(1.2f, 0.6f) },
            Position = new Vector3(0, 0, -0.1f), MaterialOverride = background });
        _viewport.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(-0.3f, 0, 0),
            Rotation = new Vector3(0, -MathF.PI / 2, 0) });
        _viewport.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.4f, 0.4f) },
            Position = new Vector3(0.3f, 0, 0), MaterialOverride = laserLight });
        MaterialCompiler.SetVision(RenderK.RendMapNormal);
        MaterialCompiler.SetTime(0);
        return SmokeResult.Running;
    }

    private SmokeResult CheckPixels()
    {
        if (++_frame < 12) return SmokeResult.Running;
        using Image shot = _viewport!.GetTexture().GetImage();
        shot.SavePng(Path.Combine(_folder, $"laser-blink-{_phase}.png"));
        Color background = shot.GetPixel(255, 128);
        // Both authored frames must leave the black corners of the glow card transparent.
        Color corner = shot.GetPixel(310, 55);
        float error = MathF.Max(MathF.Abs(corner.R - background.R),
            MathF.Max(MathF.Abs(corner.G - background.G), MathF.Abs(corner.B - background.B)));
        if (error > 3f / 255f) return Finish(SmokeResult.Fail($"laser glow has an opaque black border: phase={_phase} corner={corner} background={background}; {_folder}"));
        Color center = shot.GetPixel(384, 128);
        if (_phase == 0) _firstRed = center.R;
        else if (MathF.Abs(center.R - _firstRed) < 0.1f)
            return Finish(SmokeResult.Fail($"laser glow did not blink: first={_firstRed} second={center.R}"));
        if (_phase++ == 0)
        {
            _frame = 0;
            MaterialCompiler.SetTime(0.2f);
            return SmokeResult.Running;
        }
        if (!_laserLight!.Shader.Code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal))
            return Finish(SmokeResult.Fail("legacy glow did not recover additive blending"));
        return Finish(SmokeResult.Pass("proximity armed/chaff skins; laser's actual mesh/material blink with transparent glow-card corners in both frames"));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _display?.Release();
        _viewport?.QueueFree();
        return result;
    }
}
