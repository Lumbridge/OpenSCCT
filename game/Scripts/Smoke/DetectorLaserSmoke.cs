using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Golden;

namespace OpenScctGame.Smoke;

/// <summary>Pixel regression: detector and mine beams stay dark and visible in EMF, and retain their own thermal heat.</summary>
public sealed class DetectorLaserSmoke : SmokeTest
{
    public const string TestName = "detector-laser";
    public override string Name => TestName;
    private OffscreenView? _view;
    private Lasers? _lasers;
    private int _frames, _phase, _teardown;
    private Color _emfDetector, _emfBackground, _thermalBackground, _thermalMine;
    private float _detectorDarkening, _mineDarkening, _mineThermalChange;
    private SmokeResult? _result;
    private string _folder = "";

    public override SmokeResult Step(SmokeContext context)
    {
        if (_result is { } result) return ++_teardown < 4 ? SmokeResult.Running : result;
        if (_view is null)
        {
            if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires rendered pixels");
            string? folder = MapBuilder.FindMapFolder("TestRange", context.App.Options.MapRoots);
            if (folder is null) return SmokeResult.Fail("TestRange content missing");
            var map = MapBuilder.Build(folder);
            foreach (Node child in map.Root.GetChildren()) if (child is Node3D node) node.Visible = false;
            _view = new OffscreenView(context.Tree.Root, map, new Vector2I(640, 480));
            // Raw red becomes a bright cyan EMF backdrop after inversion. Red=1 also excludes this fixture
            // from the EMF glow mask, so a full-screen halo cannot wash out the thin non-glowing beam.
            // Against the usual dark-blue clear, a discarded beam could resemble the correct dark beam.
            map.Root.AddChild(new MeshInstance3D
            {
                Name = "EmfContrastBackdrop",
                Position = UeSpace.ToGodot(new FVector(100, 0, 0)),
                Mesh = new BoxMesh { Size = new Vector3(0.01f, 20f, 20f) },
                MaterialOverride = new ShaderMaterial
                {
                    Shader = new Shader
                    {
                        Code = "shader_type spatial;\nrender_mode unshaded, fog_disabled;\nvoid fragment() { ALBEDO = vec3(1.0, 0.0, 0.0); }\n",
                    },
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            _lasers = new Lasers(map);
            if (!_lasers.Available) return Finish(SmokeResult.Fail("detector mesh missing"));
            SetBeam(1f);
            _view.View.SetUeCamera(new FVector(-200, 0, 0), default, 75, cut: true);
            _view.View.RendMap = RenderK.RendMapEmf;
            _folder = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "detector-laser");
            Directory.CreateDirectory(_folder);
            return SmokeResult.Running;
        }
        if (++_frames < 12) return SmokeResult.Running;
        _frames = 0;
        using Image shot = _view.Capture();
        shot.SavePng(Path.Combine(_folder, $"{_phase}.png"));
        Color color = Center(shot);
        switch (_phase++)
        {
            case 0:
                _emfDetector = color;
                _view.Map.ShowVisionOnly(false);
                break;
            case 1:
                _emfBackground = color;
                if (Luma(color) < 0.3f) return Finish(SmokeResult.Fail("EMF contrast backdrop did not render brightly"));
                _detectorDarkening = Luma(color) - Luma(_emfDetector);
                if (_detectorDarkening < 0.04f)
                    return Finish(SmokeResult.Fail($"EMF detector beam missing or bright instead of dark: darkening={_detectorDarkening:F3}"));
                SetBeam(0.4f);
                _view.Map.ShowVisionOnly(true);
                break;
            case 2:
                _mineDarkening = Luma(_emfBackground) - Luma(color);
                if (_mineDarkening < 0.04f)
                    return Finish(SmokeResult.Fail($"EMF mine beam missing or bright instead of dark: darkening={_mineDarkening:F3}"));
                float emfHeatDifference = Difference(_emfDetector, color);
                if (emfHeatDifference > 0.025f)
                    return Finish(SmokeResult.Fail($"thermal heat changed the EMF beam: difference={emfHeatDifference:F3}"));
                _view.View.RendMap = RenderK.RendMapThermal;
                _view.Map.ShowVisionOnly(false);
                break;
            case 3:
                _thermalBackground = color;
                _view.Map.ShowVisionOnly(true);
                break;
            case 4:
                _thermalMine = color;
                _mineThermalChange = Difference(_thermalBackground, color);
                if (_mineThermalChange < 0.04f)
                    return Finish(SmokeResult.Fail($"thermal mine beam missing: difference={_mineThermalChange:F3}"));
                SetBeam(1f);
                break;
            default:
                float thermalChange = Difference(_thermalBackground, color);
                if (thermalChange < 0.04f) return Finish(SmokeResult.Fail($"thermal detector beam missing: difference={thermalChange:F3}"));
                float heatDifference = Difference(_thermalMine, color);
                return Finish(heatDifference > 0.015f
                    ? SmokeResult.Pass($"EMF detector/mine beams visible and dark (darkening={_detectorDarkening:F3}/{_mineDarkening:F3}); thermal heat 1/0.4 visible (difference={thermalChange:F3}/{_mineThermalChange:F3}, heat separation={heatDifference:F3}); {_folder}")
                    : SmokeResult.Fail($"detector/mine thermal heat did not change the beam: difference={heatDifference:F3}"));
        }
        return SmokeResult.Running;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _view?.Dispose();
        _view = null;
        _result = result;
        return SmokeResult.Running;
    }

    private void SetBeam(float heat) => _lasers!.Set("detector", new FVector(0, -100, 0), new FVector(0, 100, 0), hideInNormalVision: true, heat: heat);

    private static float Difference(Color a, Color b) => MathF.Max(MathF.Abs(a.R - b.R), MathF.Max(MathF.Abs(a.G - b.G), MathF.Abs(a.B - b.B)));

    private static float Luma(Color color) => (0.3f * color.R) + (0.59f * color.G) + (0.11f * color.B);

    private static Color Center(Image shot)
    {
        var sum = new Color(0, 0, 0, 0);
        for (int y = 236; y < 244; y++)
            for (int x = 288; x < 352; x++) sum += shot.GetPixel(x, y);
        return sum / 512f;
    }
}
