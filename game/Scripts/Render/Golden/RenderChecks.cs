using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Render.Golden;

/// <summary>
/// Frame checks of the vision passes, the spot shadow light, laser beams and emitters, run one after another on an
/// <see cref="OffscreenView"/> from one camera (the render smoke test calls <see cref="Step"/> once a frame until it
/// returns a result). Each check renders a few frames and reads the picture back: thermal must lead in blue over green
/// (the table's cold end), EMF must be blue, motion must carry the ramp's red, the torch must light the frame it
/// points at, a laser beam must show in thermal only, and the map's always-on emitters must have live particles.
/// docs/RENDERING.md#smoke-checks.
/// </summary>
public sealed class RenderChecks
{
    private const int SettleFrames = 4;

    private readonly OffscreenView _view;
    private readonly (FVector Location, FRotator Rotation, float Fov) _camera;
    private readonly List<string> _results = [];
    private readonly List<string> _failures = [];
    private int _stage;
    private int _waited;
    private double _baseline;
    private SpotShadowLight? _torch;
    private Lasers? _lasers;

    public RenderChecks(OffscreenView view, FVector location, FRotator rotation, float fov)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        _camera = (location, rotation, fov);
    }

    /// <summary>True once every check failed none.</summary>
    public bool Passed => _failures.Count == 0;

    /// <summary>One frame of the checks: null while running, else the summary ("checks=ok(...)" or "checks=FAIL(...)").</summary>
    public string? Step()
    {
        switch (_stage)
        {
            case 0:
                Begin(RenderK.RendMapNormal);
                return Next();
            case 1:
                return Wait(() => _baseline = Luma(_view.Capture()), RenderK.RendMapThermal);
            case 2:
                return Wait(() => Check("thermal", Mean(_view.Capture()), c => c.B > c.G && c.B > 0.01f), RenderK.RendMapEmf);
            case 3:
                return Wait(() => Check("emf", Mean(_view.Capture()), c => c.B > c.R && c.B > c.G), RenderK.RendMapMotion);
            case 4:
                return Wait(() => Check("motion", Mean(_view.Capture()), c => c.R > 0.005f || c.B > 0.005f), RenderK.RendMapNormal, TorchOn);
            case 5:
                return Wait(TorchCheck, RenderK.RendMapNormal, LaserOn);
            case 6:
                return Wait(() => LaserCheck(false), RenderK.RendMapThermal);
            case 7:
                return Wait(() => LaserCheck(true), RenderK.RendMapNormal, EmitterCheck);
            default:
                _torch?.Dispose();
                return (Passed ? "checks=ok(" : "checks=FAIL(") + string.Join(',', Passed ? _results : _failures) + ")";
        }
    }

    private string? Next()
    {
        _stage++;
        _waited = 0;
        return null;
    }

    private void Begin(int rendMap)
    {
        _view.View.RendMap = rendMap;
        _view.View.SetUeCamera(_camera.Location, _camera.Rotation, _camera.Fov, cut: true);
    }

    private string? Wait(Action check, int nextMode, Action? then = null)
    {
        if (++_waited <= SettleFrames)
        {
            return null;
        }

        check();
        then?.Invoke();
        Begin(nextMode);
        return Next();
    }

    private void Check(string name, Color mean, Func<Color, bool> rule)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{name}({mean.R:F3},{mean.G:F3},{mean.B:F3})");
        (rule(mean) ? _results : _failures).Add(line);
    }

    private void TorchOn()
    {
        _torch = new SpotShadowLight(_view.Viewport, _view.Map.Pack, SpotShadowShape.Torch);
        _torch.Place(_camera.Location, _camera.Rotation);
        _torch.On = true;
    }

    private void TorchCheck()
    {
        double lit = Luma(_view.Capture());
        string line = string.Create(CultureInfo.InvariantCulture, $"torch({_baseline:F4}->{lit:F4})");
        (lit > _baseline * 1.02 + 0.0005 ? _results : _failures).Add(line);
        if (_torch is not null)
        {
            _torch.On = false;
        }
    }

    private void LaserOn()
    {
        _lasers = new Lasers(_view.Map);
        FVector forward = _camera.Rotation.Vector();
        _camera.Rotation.GetAxes(out _, out FVector right, out _);
        FVector mid = _camera.Location + (forward * 150f);
        _lasers.Set("check", mid - (right * 200f), mid + (right * 200f));
    }

    private void LaserCheck(bool thermal)
    {
        bool shown = _lasers is { Available: true } && _view.Map.VisionOnlyShown;
        string line = string.Create(CultureInfo.InvariantCulture, $"laser{(thermal ? "36" : "5")}={(shown ? "shown" : "hidden")}");
        bool ok = _lasers is not { Available: true } || shown == thermal;
        (ok ? _results : _failures).Add(line);
    }

    private void EmitterCheck()
    {
        if (_view.Map.Emitters is not { } emitters)
        {
            _results.Add("emitters=none");
            return;
        }

        emitters.SetTime(_view.Map.Time + 5f);
        string line = string.Create(CultureInfo.InvariantCulture, $"emitters={emitters.Count}/particles={emitters.Particles}");
        (emitters.Count == 0 || emitters.Particles > 0 ? _results : _failures).Add(line);
    }

    /// <summary>Mean colour of a picture, every fourth pixel.</summary>
    public static Color Mean(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = 0; y < image.GetHeight(); y += 4)
        {
            for (int x = 0; x < image.GetWidth(); x += 4)
            {
                Color c = image.GetPixel(x, y);
                r += c.R;
                g += c.G;
                b += c.B;
                n++;
            }
        }

        return n == 0 ? new Color(0f, 0f, 0f) : new Color((float)(r / n), (float)(g / n), (float)(b / n));
    }

    private static double Luma(Image image)
    {
        Color m = Mean(image);
        return (0.299 * m.R) + (0.587 * m.G) + (0.114 * m.B);
    }
}
