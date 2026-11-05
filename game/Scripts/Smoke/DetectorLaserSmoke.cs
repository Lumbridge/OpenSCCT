using System;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Golden;

namespace OpenScctGame.Smoke;

/// <summary>Pixel regression: detector beams show heat in thermal, but do not turn white in EMF.</summary>
public sealed class DetectorLaserSmoke : SmokeTest
{
    public const string TestName = "detector-laser";
    public override string Name => TestName;
    private OffscreenView? _view;
    private int _frames, _phase, _teardown;
    private Color _emfBeam, _thermalBackground;
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
            var lasers = new Lasers(map);
            if (!lasers.Available) return Finish(SmokeResult.Fail("detector mesh missing"));
            lasers.Set("detector", new FVector(0, -100, 0), new FVector(0, 100, 0));
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
                _emfBeam = color;
                _view.Map.ShowVisionOnly(false);
                break;
            case 1:
                float emfError = Difference(_emfBeam, color);
                if (emfError > 0.025f) return Finish(SmokeResult.Fail($"EMF beam glows: difference={emfError:F3}"));
                _view.View.RendMap = RenderK.RendMapThermal;
                _view.Map.ShowVisionOnly(false);
                break;
            case 2:
                _thermalBackground = color;
                _view.Map.ShowVisionOnly(true);
                break;
            default:
                float thermalChange = Difference(_thermalBackground, color);
                return Finish(thermalChange > 0.04f
                    ? SmokeResult.Pass($"EMF beam matches background; thermal beam remains visible (difference={thermalChange:F3}); {_folder}")
                    : SmokeResult.Fail($"thermal beam missing: difference={thermalChange:F3}"));
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

    private static float Difference(Color a, Color b) => MathF.Max(MathF.Abs(a.R - b.R), MathF.Max(MathF.Abs(a.G - b.G), MathF.Abs(a.B - b.B)));

    private static Color Center(Image shot)
    {
        var sum = new Color(0, 0, 0, 0);
        for (int y = 236; y < 244; y++)
            for (int x = 288; x < 352; x++) sum += shot.GetPixel(x, y);
        return sum / 512f;
    }
}
