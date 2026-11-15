using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Golden;

namespace OpenScctGame.Smoke;

/// <summary>AquaE's tank keeps its layers as the camera turns and unrelated cells enter or leave the render list.</summary>
public sealed class CameraEdgeMapSmoke : SmokeTest
{
    public const string TestName = "camera-edge-map";
    public override string Name => TestName;
    private const int Angles = 17;
    private static readonly FVector CameraLocation = new(3425.3346f, 596.6384f, 306.8792f);
    private static readonly Vector3 TankPoint = UeSpace.ToGodot(new FVector(2320, 1150, 350));
    private readonly List<MeshInstance3D> _static = [];
    private readonly Color[,] _reference = new Color[2, Angles];
    private readonly bool[,] _sampled = new bool[2, Angles];
    private OffscreenView? _view;
    private MapSpotLights? _spots;
    private int _phase, _frames, _samples;
    private float _worst;

    public override SmokeResult Step(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires rendered pixels");
        if (_view is null)
        {
            string? folder = MapBuilder.FindMapFolder("AquaE", context.App.Options.MapRoots);
            if (folder is null) return SmokeResult.Fail("AquaE content missing");
            RenderMap map = MapBuilder.Build(folder);
            _view = new OffscreenView(context.Tree.Root, map, new(2072, 1152));
            AnimatedProps.Build(map, map.Root);
            GameplayObjects.ShowMode(map, "Story");
            _spots = new MapSpotLights(map, _view.Viewport);
            foreach (Node child in map.Root.GetNode<Node3D>("Static").GetChildren())
                if (child is MeshInstance3D node) _static.Add(node);
        }

        int angle = _phase % Angles, variant = _phase / Angles, msaa = variant / 2;
        if (_frames == 0)
        {
            _view.Viewport.Msaa3D = msaa == 0 ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa4X;
            // Perturb the visible draw list without changing any pixels: offscreen geometry must not reorder glass.
            foreach (MeshInstance3D node in _static) node.ExtraCullMargin = variant % 2 == 0 ? 0 : 10000;
            _view.Map.SetTime(10);
            _view.View.SetUeCamera(CameraLocation, new FRotator(-1987, 18838 + angle * 128, 0), 80.21489f, cut: true);
        }
        _spots!.Update(CameraLocation);
        _view.Map.LightBeams?.UpdateSlots();
        if (++_frames < 6) return SmokeResult.Running;

        Vector2 pixel = _view.View.Camera.UnprojectPosition(TankPoint);
        if (pixel.X >= 4 && pixel.X < 2068 && pixel.Y >= 4 && pixel.Y < 1148)
        {
            using Image image = _view.Capture();
            Color got = image.GetPixel(Mathf.RoundToInt(pixel.X), Mathf.RoundToInt(pixel.Y));
            float difference = 0;
            if (variant % 2 == 0)
            {
                if (angle > 0 && _sampled[msaa, angle - 1]) difference = Difference(got, _reference[msaa, angle - 1]);
                _reference[msaa, angle] = got;
                _sampled[msaa, angle] = true;
            }
            else if (_sampled[msaa, angle]) difference = Difference(got, _reference[msaa, angle]);
            _worst = MathF.Max(_worst, difference);
            _samples++;
            if (difference > .03f)
            {
                string file = SmokePaths.Local("camera-edge", $"tank-{_phase}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                image.SavePng(file);
                return Finish(SmokeResult.Fail($"tank colour jumped {difference * 255:F1}/255 at angle={angle} variant={variant}; {file}"));
            }
        }

        _frames = 0;
        if (++_phase < Angles * 4) return SmokeResult.Running;
        return Finish(_samples >= 40
            ? SmokeResult.Pass($"{_samples} tank samples across 17 camera angles, changing culling and 4x MSAA; worst RGB change={_worst * 255:F1}/255")
            : SmokeResult.Fail($"too few tank samples: {_samples}"));
    }

    private static float Difference(Color a, Color b) => MathF.Max(MathF.Abs(a.R - b.R), MathF.Max(MathF.Abs(a.G - b.G), MathF.Abs(a.B - b.B)));

    private SmokeResult Finish(SmokeResult result)
    {
        _spots?.Dispose();
        _view?.Dispose();
        return result;
    }
}
