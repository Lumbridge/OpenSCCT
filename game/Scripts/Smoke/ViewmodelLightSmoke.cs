using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Golden;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>Rendered regression for the unlit Blue Room gun and the rifle's three emissive ROF screens.</summary>
public sealed class ViewmodelLightSmoke : SmokeTest
{
    public const string TestName = "viewmodel-light";
    public override string Name => TestName;
    private OffscreenView? _view;
    private SkeletalModel? _gun;
    private int _frame;
    private string? _failure;
    private readonly string _output = ProjectSettings.GlobalizePath("res://../local/viewmodel-light");

    public override SmokeResult Step(SmokeContext context)
    {
        if (_frame++ == 0)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer");
            string? folder = MapBuilder.FindMapFolder("AquaE", context.App.Options.MapRoots);
            if (folder is null) return SmokeResult.Fail("AquaE content missing");
            var map = MapBuilder.Build(folder, new RenderOptions());
            _view = new OffscreenView(context.Tree.Root, map, new Vector2I(1280, 720));
            var eye = new FVector(7152, 1036, 272.50626f);
            var rotation = new FRotator(0, 16384, 0);
            _view.View.SetUeCamera(eye, rotation, 95, cut: true);
            _view.View.SquintingFactor = 0.4f;
            if (MathF.Abs(RenderK.SquintSceneBoost(0.4f) - 10f / 255f) > 0.000001f
                || RenderK.SquintSceneBoost(0f) != 0f)
                _failure = "eye adaptation does not match the original's quantized ambient";
            _gun = SkeletalModel.Load(SkeletalModel.FindGlb("SWeaponFirst.1DEF_01")!);
            _gun.SplitPart(FirstPersonView.ArmsMaterial);
            var mask = new ShaderMaterial { Shader = GD.Load<Shader>(FirstPersonView.MaskShaderPath),
                RenderPriority = (int)Material.RenderPriorityMin };
            foreach (MeshInstance3D mesh in _gun.Meshes)
            {
                mesh.MaterialOverlay = mask;
                mesh.Layers = RenderLayers.FirstPerson;
            }
            _view.Viewport.AddChild(_gun.Root);
            _gun.Place(eye, rotation, default);
            _gun.Sampler.Play("FirstWaitStFd2");
            _gun.Pose();
            _gun.SetSlotSkin(3, "SWeaponTex.SI_rof_auto");
            var lights = FirstPersonView.LightAtEye(map, eye);
            _gun.Light(lights);
            GD.Print($"VIEWMODEL lights={string.Join(',', lights.Names)} ambient={lights.Ambient}");
            if (lights.Count != 0 || map.AmbientAt(eye, 0) != Vector3.Zero)
                _failure = "Blue Room fixture no longer exercises a zero-light position";
        }
        if (_frame == 40)
        {
            Directory.CreateDirectory(_output);
            using Image shot = _view!.Capture();
            shot.SavePng(Path.Combine(_output, "map.png"));
            int detail = 0;
            // Interior of the gun, away from the screen and the world behind it.
            for (int y = 600; y < 700; y++)
                for (int x = 750; x < 950; x++)
                    if (shot.GetPixel(x, y).R > 0.04f) detail++;
            if (detail < 1000) _failure = $"weapon remains a silhouette ({detail} visible detail pixels)";
            _gun!.Light(new ActorLightSet());
            _view.View.SquintingFactor = 0f;
            _gun.SetSlotSkin(3, "SWeaponTex.SI_rof_single");
        }
        if (_frame is 60 or 80 or 100)
        {
            string mode = _frame == 60 ? "single" : _frame == 80 ? "burst" : "auto";
            using Image shot = _view!.Capture();
            shot.SavePng(Path.Combine(_output, mode + "-zero-light.png"));
            if (_frame == 60)
            {
                int highlights = 0;
                for (int y = 600; y < 700; y++)
                    for (int x = 750; x < 950; x++)
                        if (shot.GetPixel(x, y).R > 0.04f) highlights++;
                if (highlights < 1000)
                    _failure = $"original reflection highlights missing at zero ambient ({highlights} pixels)";
                GD.Print($"VIEWMODEL zero-light reflection pixels={highlights}");
            }
            int yellow = 0;
            for (int y = 550; y < 650; y++)
                for (int x = 1010; x < 1140; x++)
                {
                    Color c = shot.GetPixel(x, y);
                    if (c.R > 0.5f && c.G > 0.3f && c.B < 0.2f) yellow++;
                }
            if (yellow < 50) _failure = $"{mode} screen lost self-illumination ({yellow} yellow pixels)";
            GD.Print($"VIEWMODEL {mode} zero-light screen pixels={yellow}");
            if (_frame == 60) _gun!.SetSlotSkin(3, "SWeaponTex.SI_rof_burst");
            if (_frame == 80) _gun!.SetSlotSkin(3, "SWeaponTex.SI_rof_auto");
            if (_frame == 100)
            {
                _view.View.RendMap = RenderK.RendMapEmf;
                _gun!.SetVision(new Vector4(0f, 1f, 0f, 1f));
                bool rifleMask = false;
                foreach (MeshInstance3D mesh in _gun.Meshes)
                    for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                        if (mesh.Mesh.SurfaceGetMaterial(surface)?.ResourceName == "SWeaponTex.M160" &&
                            mesh.GetSurfaceOverrideMaterial(surface) is ShaderMaterial material)
                        {
                            rifleMask = material.GetShaderParameter("emf_mode").AsInt32() == 1 &&
                                material.GetShaderParameter("tex_emf").AsGodotObject() is Texture2D;
                            if (material.GetShaderParameter("tex_diffuse").AsGodotObject() is Texture2D diffuse &&
                                diffuse.GetImage() is { } diffuseImage)
                            {
                                using (diffuseImage)
                                    if (diffuseImage.HasMipmaps()) _failure = "vision-source metadata changed rifle diffuse mip policy";
                            }
                        }
                if (!rifleMask) _failure = "rifle lost the EMF modifier behind its normal-view diffuse alias";
            }
        }
        if (_frame == 130)
        {
            using Image shot = _view!.Capture();
            shot.SavePng(Path.Combine(_output, "emf.png"));
            int emfDetail = 0;
            for (int y = 580; y < 700; y++)
                for (int x = 800; x < 1100; x++)
                {
                    Color pixel = shot.GetPixel(x, y);
                    if (pixel.R > 0.66f && pixel.G > 0.66f && pixel.B > 0.66f) emfDetail++;
                }
            if (emfDetail < 1000) _failure = $"rifle EMF detail disappeared ({emfDetail} bright pixels)";
            GD.Print($"VIEWMODEL EMF detail pixels={emfDetail}");
            float edge = 0f, interior = 0f;
            for (int y = 300; y < 450; y++)
            {
                for (int x = 1080; x < 1105; x++) edge += shot.GetPixel(x, y).R;
                for (int x = 1140; x < 1220; x++) interior += shot.GetPixel(x, y).R;
            }
            edge /= 150 * 25;
            interior /= 150 * 80;
            if (edge < interior + 0.1f) _failure = $"EMF kiosk lost its exterior halo ({edge:F3} edge, {interior:F3} fill)";
            GD.Print($"VIEWMODEL EMF kiosk edge={edge:F3} interior={interior:F3}");
            _view.View.RendMap = RenderK.RendMapNormal;
        }
        if (_frame == 155)
        {
            using Image shot = _view!.Capture();
            shot.SavePng(Path.Combine(_output, "normal-after-emf.png"));
            using Image baseline = Image.LoadFromFile(Path.Combine(_output, "auto-zero-light.png"));
            float error = 0f;
            for (int y = 600; y < 700; y++)
                for (int x = 750; x < 950; x++)
                {
                    Color a = shot.GetPixel(x, y), b = baseline.GetPixel(x, y);
                    error += MathF.Abs(a.R-b.R) + MathF.Abs(a.G-b.G) + MathF.Abs(a.B-b.B);
                }
            error /= 100 * 200 * 3;
            if (error > 0.015f) _failure = $"EMF halo leaked into normal weapon rendering ({error:F4} mean error)";
            GD.Print($"VIEWMODEL EMF-to-normal error={error:F4}");
            _view.Dispose();
        }
        return _frame <= 161 ? SmokeResult.Running : _failure is { } failure ? SmokeResult.Fail(failure)
            : SmokeResult.Pass("Blue Room weapon detail, single/burst/auto self-illumination EMF mask/outline and return to normal; captures in " + _output);
    }
}
