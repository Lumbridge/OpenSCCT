using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Golden;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// Loads a converted map's render folder (SteeE by default, <c>--svm-render-map=</c>) with the Faithful
/// <see cref="MapBuilder"/> and reports load time, triangles, merged nodes and surfaces and the BSP handedness check.
/// With a real renderer it also draws the map from every player start (camera 250 uu behind, 40 up, like the spy
/// camera) and from the golden cameras when they exist, and reports draw calls per frame, which must stay at most
/// <see cref="MaxDrawCalls"/>. Headless runs check the build only.
/// </summary>
public sealed class SteeERenderSmoke : SmokeTest
{
    public const string TestName = "steee-render";

    /// <summary>Draw-call budget of a frame (plan performance target).</summary>
    public const int MaxDrawCalls = 600;

    private const int SettleFrames = 3;

    private RenderSmokeArgs _args = null!;
    private RenderMap? _map;
    private OffscreenView? _view;
    private readonly List<(string Name, FVector Location, FRotator Rotation, float Fov)> _cameras = [];
    private readonly List<int> _drawCalls = [];
    private readonly List<int> _primitives = [];
    private int _index = -1;
    private int _waited;
    private bool _tornDown;
    private int _night;
    private string _pawnDetail = "";
    private string _nightDetail = "";
    private RenderChecks? _checks;
    private string? _checksDetail;
    private SmokeResult _final;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_map is null)
        {
            return Load(context);
        }

        if (_tornDown)
        {
            return ++_waited < 4 ? SmokeResult.Running : _final;
        }

        if (_view is null)
        {
            // Headless: the map was never added to the tree, so free it here.
            _map.Root.Free();
            return Summary("renderer=headless");
        }

        if (_index < _cameras.Count)
        {
            if (_index >= 0 && ++_waited <= SettleFrames)
            {
                return SmokeResult.Running;
            }

            if (_index >= 0)
            {
                Measure();
            }

            _index++;
            _waited = 0;
            if (_index < _cameras.Count)
            {
                (string _, FVector location, FRotator rotation, float fov) = _cameras[_index];
                _view.View.SetUeCamera(location, rotation, fov);
                return SmokeResult.Running;
            }
        }

        // Last: the night vision pass (RendMap 34) on the first camera; its picture must turn green.
        if (_night == 0 && _cameras.Count > 0)
        {
            _night = 1;
            _waited = 0;
            (string _, FVector nl, FRotator nr, float nf) = _cameras[0];
            _view.View.SetUeCamera(nl, nr, nf, cut: true);
            _view.View.RendMap = RenderK.RendMapNight;
            return SmokeResult.Running;
        }

        if (_night == 1)
        {
            if (++_waited <= SettleFrames)
            {
                return SmokeResult.Running;
            }

            _night = 2;
            _nightDetail = NightCheck(_view.Capture());
            _view.View.RendMap = RenderK.RendMapNormal;
        }

        // Then the other vision passes, the torch, laser beams and emitters on the first camera.
        if (_checksDetail is null && _cameras.Count > 0)
        {
            (string _, FVector cl, FRotator cr, float cf) = _cameras[0];
            _checks ??= new RenderChecks(_view, cl, cr, cf);
            _checksDetail = _checks.Step();
            if (_checksDetail is null)
            {
                return SmokeResult.Running;
            }
        }

        int[] sorted = [.. _drawCalls.Order()];
        int max = sorted.Length > 0 ? sorted[^1] : 0;
        int median = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0;
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"cameras={sorted.Length} drawCalls max={max} median={median} primitives max={(_primitives.Count > 0 ? _primitives.Max() : 0)}");
        detail += " " + _nightDetail + " " + _checksDetail;
        _final = max > MaxDrawCalls
            ? SmokeResult.Fail($"{detail}; over the budget of {MaxDrawCalls} ({_map.Stats})")
            : !_nightDetail.StartsWith("night=green", StringComparison.Ordinal)
                ? SmokeResult.Fail($"night vision did not tint the frame: {_nightDetail}")
                : _checks is { Passed: false }
                    ? SmokeResult.Fail($"render checks failed: {_checksDetail}")
                    : Summary(detail);

        // Free the view and its compositor effects a few frames before the quit.
        _tornDown = true;
        _waited = 0;
        _view.Dispose();
        return SmokeResult.Running;
    }

    private SmokeResult Load(SmokeContext context)
    {
        _args = RenderSmokeArgs.Parse();
        string? folder = MapBuilder.FindMapFolder(_args.Map, context.App.Options.MapRoots);
        if (folder is null)
        {
            return SmokeResult.Fail($"map {_args.Map} has no render folder (.content/svmap/{_args.Map})");
        }

        _map = MapBuilder.Build(folder, new RenderOptions { LightmapScale = _args.LightmapScale ?? RenderK.LightmapScale });
        GD.Print($"SVM_RENDER map {_args.Map} {_map.Stats} warnings={_map.Warnings.Count}");
        foreach (string w in _map.Warnings.Take(20))
        {
            GD.Print("SVM_RENDER warning " + w);
        }

        if (_map.Stats.Triangles == 0)
        {
            return SmokeResult.Fail("the map built no triangles");
        }

        if (_map.Stats.HandednessChecked > 0 && _map.Stats.HandednessAgree * 100 < _map.Stats.HandednessChecked * 99)
        {
            return SmokeResult.Fail($"handedness: only {_map.Stats.HandednessAgree} of {_map.Stats.HandednessChecked} BSP triangles face their normals");
        }

        _pawnDetail = CheckPawns(_map);
        if (!_pawnDetail.StartsWith("pawns=ok", StringComparison.Ordinal))
        {
            return SmokeResult.Fail(_pawnDetail);
        }

        if (DisplayServer.GetName() == "headless")
        {
            return SmokeResult.Running;
        }

        _view = new OffscreenView(context.Tree.Root, _map, new Vector2I(1280, 720));
        AnimatedProps.Build(_map, _map.Root);
        AddPlayerStartCameras(folder);
        string golden = Path.Combine(_args.GoldenIn, _args.Map, "cameras.json");
        if (File.Exists(golden))
        {
            foreach (GoldenShot s in GoldenShots.Load(golden))
            {
                _cameras.Add(("golden:" + s.Id, s.CameraLocation, s.CameraRotation, s.Fov));
            }
        }

        return SmokeResult.Running;
    }

    private void Measure()
    {
        Rid rid = _view!.Viewport.GetViewportRid();
        long calls = RenderingServer.ViewportGetRenderInfo(rid, RenderingServer.ViewportRenderInfoType.Visible,
            RenderingServer.ViewportRenderInfo.DrawCallsInFrame);
        if (_view.View.SkyViewport is { } sky)
        {
            calls += RenderingServer.ViewportGetRenderInfo(sky.GetViewportRid(), RenderingServer.ViewportRenderInfoType.Visible,
                RenderingServer.ViewportRenderInfo.DrawCallsInFrame);
        }

        _drawCalls.Add((int)calls);
        _primitives.Add((int)RenderingServer.ViewportGetRenderInfo(rid, RenderingServer.ViewportRenderInfoType.Visible,
            RenderingServer.ViewportRenderInfo.PrimitivesInFrame));
        GD.Print($"SVM_RENDER camera {_cameras[_index].Name} drawCalls={calls} primitives={_primitives[^1]}");
    }

    /// <summary>
    /// Loads the spy and the merc, plays a run sequence on each through the sampler and the pose applier, and checks
    /// that the thigh bones move between two samples 0.2 s apart.
    /// </summary>
    private static string CheckPawns(RenderMap map)
    {
        var parts = new List<string>();
        foreach ((PawnMesh mesh, string sequence) in new[] { (PawnMesh.Spy, "JoggStAlFd0"), (PawnMesh.Merc, "JoggStAlFd2") })
        {
            var view = new PawnView { Map = map };
            try
            {
                if (!view.Load(mesh))
                {
                    return $"pawn {mesh}: mesh not in the shared packs";
                }

                if (!view.Play(sequence))
                {
                    return $"pawn {mesh}: no sequence {sequence}";
                }

                Skeleton3D skeleton = view.Model!.Skeleton;
                int bone = skeleton.FindBone("B L Thigh");
                view.Step(0);
                Quaternion a = skeleton.GetBonePoseRotation(bone);
                view.Step(0.2);
                Quaternion b = skeleton.GetBonePoseRotation(bone);
                float angle = Mathf.RadToDeg(a.AngleTo(b));
                if (bone < 0 || angle < 1f)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"pawn {mesh}: {sequence} did not move the thigh ({angle:F1} deg)");
                }

                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{mesh}:{sequence}:{angle:F0}deg"));
            }
            finally
            {
                view.Free();
            }
        }

        return "pawns=ok(" + string.Join(',', parts) + ")";
    }

    /// <summary>Mean colour of a night vision frame: green must lead (tint 0.718, 1, 0.667 over luminance).</summary>
    private static string NightCheck(Image image)
    {
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

        r /= n;
        g /= n;
        b /= n;
        bool green = g > 0.002 && g > r && g > b;
        return string.Create(CultureInfo.InvariantCulture, $"night={(green ? "green" : "off")}({r:F3},{g:F3},{b:F3})");
    }

    private SmokeResult Summary(string rendered) =>
        SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture,
            $"map={_args.Map} load={_map!.Stats.LoadSeconds:F2}s tris={_map.Stats.Triangles} static={_map.Stats.StaticNodes}/{_map.Stats.StaticSurfaces} gameplay={_map.Stats.GameplayNodes} handedness={_map.Stats.HandednessAgree}/{_map.Stats.HandednessChecked} {_pawnDetail} {rendered}"));

    /// <summary>A spy-like view from each player start: 250 uu behind and 40 uu above, looking along its yaw.</summary>
    private void AddPlayerStartCameras(string folder)
    {
        string path = Path.Combine(folder, "sim", "gameplay.json");
        if (!File.Exists(path))
        {
            return;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (JsonElement a in doc.RootElement.GetProperty("actors").EnumerateArray())
        {
            string cls = a.GetProperty("class").GetString() ?? "";
            if (!cls.EndsWith("PlayerStart", StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement p = a.GetProperty("props");
            if (!p.TryGetProperty("Location", out JsonElement l))
            {
                continue;
            }

            var location = new FVector(l.GetProperty("X").GetSingle(), l.GetProperty("Y").GetSingle(), l.GetProperty("Z").GetSingle());
            int yaw = p.TryGetProperty("Rotation", out JsonElement r) && r.TryGetProperty("Yaw", out JsonElement y) ? y.GetInt32() : 0;
            var rotation = new FRotator(0, yaw, 0);
            FVector back = rotation.Vector() * -250f;
            _cameras.Add((a.GetProperty("id").GetString() ?? cls, location + back + new FVector(0f, 0f, 40f), rotation, 75f));
        }
    }
}
