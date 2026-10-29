using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Golden;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Smoke;

/// <summary>
/// Renders the views the capture tool recorded in the original (tools/golden/capture_game.py) with the Faithful
/// renderer: same camera location, rotation and horizontal FOV, same picture size, the spy at the recorded pawn
/// placement. Writes local/golden/svm/&lt;Map&gt;/&lt;pose&gt;.png and render.json; tools/golden/score.py compares
/// them. Also runs the display-space known-value check first. Needs a real renderer (not --headless).
/// </summary>
public sealed class GoldenSmoke : SmokeTest
{
    public const string TestName = "golden";

    private enum Phase
    {
        Start,
        DisplayCheck,
        Shot,
        Teardown,
        Done,
    }

    private Phase _phase = Phase.Start;
    private RenderSmokeArgs _args = null!;
    private RenderMap? _map;
    private OffscreenView? _view;
    private MapSpotLights? _spots;
    private DisplayCheck? _check;
    private List<GoldenShot> _shots = [];
    private int _index;
    private int _waited;
    private string _outDir = "";
    private readonly Dictionary<string, object> _report = [];
    private readonly List<Dictionary<string, object>> _shotReports = [];
    private int _displayError = -1;
    private SmokeResult _result;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        switch (_phase)
        {
            case Phase.Start:
                return Start(context);
            case Phase.DisplayCheck:
                return CheckDisplay();
            case Phase.Shot:
                return Shot();
            case Phase.Teardown:
                // Views and their compositor effects are freed a few frames before the quit, so no render-thread
                // callback runs into the engine's shutdown.
                return ++_waited < 4 ? SmokeResult.Running : _result;
            default:
                return SmokeResult.Running;
        }
    }

    private SmokeResult Start(SmokeContext context)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return SmokeResult.Fail("golden renders need a real renderer; run without --headless");
        }

        _args = RenderSmokeArgs.Parse();
        string? folder = MapBuilder.FindMapFolder(_args.Map, context.App.Options.MapRoots);
        if (folder is null)
        {
            return SmokeResult.Fail($"map {_args.Map} has no render folder (.content/svmap/{_args.Map})");
        }

        string cameras = Path.Combine(_args.GoldenIn, _args.Map, "cameras.json");
        if (!File.Exists(cameras))
        {
            return SmokeResult.Fail($"no {cameras}: run tools/golden/capture_game.py first");
        }

        _shots = GoldenShots.Load(cameras);
        if (_args.Only.Length > 0)
        {
            _shots = _shots.Where(s => _args.Only.Contains(s.Id, StringComparer.Ordinal)).ToList();
        }

        if (_shots.Count == 0)
        {
            return SmokeResult.Fail("no shots to render");
        }

        _map = MapBuilder.Build(folder, new RenderOptions
        {
            LightmapScale = _args.LightmapScale ?? RenderK.LightmapScale,
            MipDrop = _args.MipDrop ?? RenderK.WorldMipDrop,
        });
        _view = new OffscreenView(context.Tree.Root, _map, new Vector2I(_shots[0].Width, _shots[0].Height));
        _view.View.GlowGate = _args.GlowGate;
        _view.View.RendMap = _args.RendMap;
        _report["rendMap"] = _args.RendMap;
        if (_args.DynTune is { } tune)
        {
            WorldLights.SetTune(tune);
            _report["dynTune"] = tune.ToString();
        }

        // The map's LE_ESpotShadow lights (window and sun patches, cookies) as the game draws them each frame
        // (PlayRoot -> FlashLightView -> MapSpotLights), nearest the camera first.
        _spots = new MapSpotLights(_map, _view.Viewport);
        _report["spotLights"] = _spots.Count;
        List<AnimatedProp> props = AnimatedProps.Build(_map, _map.Root);
        _report["gameMode"] = _args.GameMode;
        _report["mipBias"] = _args.MipBias;
        Render.Materials.MaterialCompiler.SetMipBias(_args.MipBias);
        Render.Materials.MaterialCompiler.NightAmbientScale = _args.NightBoost;
        _view.View.RendMap = _args.RendMap;
        _report["modeProps"] = GameplayObjects.ShowMode(_map, _args.GameMode);
        string team = GoldenShots.TeamOf(cameras);
        bool spy = _view.AddPawn(spy: team != "merc");
        if (_args.PawnHeat is { } pawnHeat && _view.Pawn is { } pawn)
        {
            pawn.SetVision(new Vector4(pawnHeat, 1f, 0f, team == "merc" ? 1f : 0f));
            _report["pawnHeat"] = pawnHeat;
        }
        _report["team"] = team;
        _outDir = Path.Combine(_args.GoldenOut, _args.Map);
        Directory.CreateDirectory(_outDir);
        _report["map"] = _args.Map;
        _report["stats"] = _map.Stats.ToString();
        _report["lightmapScale"] = _args.LightmapScale ?? RenderK.LightmapScale;
        _report["glowGate"] = _args.GlowGate;
        _report["mipDrop"] = _args.MipDrop ?? RenderK.WorldMipDrop;
        _report["animatedProps"] = props.Count;
        _report["coronas"] = _view.View.Coronas?.Count ?? 0;
        _report["spy"] = spy;
        _report["warnings"] = _map.Warnings.Count;
        GD.Print($"SVM_GOLDEN map {_args.Map} {_map.Stats} props={props.Count} spy={spy} warnings={_map.Warnings.Count}");
        foreach (string w in _map.Warnings.Take(20))
        {
            GD.Print("SVM_GOLDEN warning " + w);
        }

        GD.Print("SVM_GOLDEN zone-triangles " + string.Join(' ', _map.Stats.ZoneTriangles.Select(kv => $"{kv.Key}:{kv.Value}")));
        _check = new DisplayCheck(context.Tree.Root);
        _phase = Phase.DisplayCheck;
        _waited = 0;
        return SmokeResult.Running;
    }

    private SmokeResult CheckDisplay()
    {
        if (++_waited < 6)
        {
            return SmokeResult.Running;
        }

        _displayError = _check!.MaxError(out string detail);
        _report["displayCheck"] = new Dictionary<string, object> { ["maxError"] = _displayError, ["values"] = detail };
        GD.Print($"SVM_GOLDEN display-check maxError={_displayError} [{detail}]");
        _check.Dispose();
        _check = null;
        _phase = Phase.Shot;
        _index = 0;
        _waited = 0;
        return SmokeResult.Running;
    }

    private SmokeResult Shot()
    {
        GoldenShot shot = _shots[_index];
        if (_waited == 0)
        {
            Setup(shot);
        }

        _spots?.Update(shot.CameraLocation);
        _map!.LightBeams?.UpdateSlots();

        if (++_waited <= _args.SettleFrames)
        {
            return SmokeResult.Running;
        }

        Image image = _view!.Capture();
        string path = Path.Combine(_outDir, shot.Id + ".png");
        image.SavePng(path);
        int drawCalls = DrawCalls();
        int primitives = (int)RenderingServer.ViewportGetRenderInfo(_view.Viewport.GetViewportRid(),
            RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.PrimitivesInFrame);
        _shotReports.Add(new Dictionary<string, object>
        {
            ["id"] = shot.Id,
            ["image"] = shot.Id + ".png",
            ["drawCalls"] = drawCalls,
            ["primitives"] = primitives,
            ["zone"] = _map!.ZoneAt(shot.CameraLocation)?.LocationName ?? "",
        });
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_GOLDEN shot {shot.Id} {image.GetWidth()}x{image.GetHeight()} drawCalls={drawCalls} primitives={primitives}"));
        _index++;
        _waited = 0;
        if (_index < _shots.Count)
        {
            return SmokeResult.Running;
        }

        return Finish();
    }

    private void Setup(GoldenShot shot)
    {
        _map!.SetTime(shot.TimeSeconds);
        if (_view!.Viewport.Size != new Vector2I(shot.Width, shot.Height))
        {
            _view.Viewport.Size = new Vector2I(shot.Width, shot.Height);
        }

        _view.View.SetUeCamera(shot.CameraLocation, shot.CameraRotation, shot.Fov, cut: true);
        if (_args.Debug.Contains("coronas", StringComparison.Ordinal) && _view.View.Coronas is { } coronas)
        {
            foreach (string line in coronas.Describe(shot.CameraLocation, shot.CameraRotation))
            {
                GD.Print($"SVM_GOLDEN corona {shot.Id} {line}");
            }
        }
        string sequence = shot.PawnAnim ?? (shot.PawnCollisionHeight < 70f ? "waitCrAlFd0" : "WaitStAlFd0");
        _view.PlacePawn(shot.PawnLocation, shot.PawnRotation, shot.PawnPrePivot, sequence, shot.PawnAnimFrame);
        if (_view.Pawn is { } pawn)
        {
            // First-person captures (the merc) do not show the pawn.
            pawn.Root.Visible = shot.BehindView;
        }
    }

    /// <summary>Draw calls of the frame in the offscreen view and its sky zone view together.</summary>
    private int DrawCalls()
    {
        long total = RenderingServer.ViewportGetRenderInfo(_view!.Viewport.GetViewportRid(),
            RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.DrawCallsInFrame);
        if (_view.View.SkyViewport is { } sky)
        {
            total += RenderingServer.ViewportGetRenderInfo(sky.GetViewportRid(),
                RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.DrawCallsInFrame);
        }

        return (int)total;
    }

    private SmokeResult Finish()
    {
        _result = Verdict();
        _phase = Phase.Teardown;
        _waited = 0;
        _spots?.Dispose();
        _spots = null;
        _view?.Dispose();
        _view = null;
        return SmokeResult.Running;
    }

    private SmokeResult Verdict()
    {
        _report["shots"] = _shotReports;
        string json = JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_outDir, "render.json"), json);
        int maxCalls = _shotReports.Max(r => (int)r["drawCalls"]);
        if (_displayError < 0 || _displayError > 1)
        {
            return SmokeResult.Fail($"display-space readback off by {_displayError} steps (limit 1)");
        }

        return SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture,
            $"shots={_shotReports.Count} out={_outDir} maxDrawCalls={maxCalls} displayError={_displayError} load={_map!.Stats.LoadSeconds:F2}s"));
    }
}
