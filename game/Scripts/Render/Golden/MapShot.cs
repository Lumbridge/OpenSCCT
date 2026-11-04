using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Render.Golden;

/// <summary>
/// <c>--svm-mapshot=&lt;request.json&gt;</c>: renders a map's pictures (menu, loading screens) with the game's own
/// renderer and quits (docs/MAPS.md "Map pictures"). The request is a golden camera file (<see cref="GoldenShots"/>)
/// without pawns, plus <c>map</c> (the map id, found like any map), an optional <c>folder</c> (the SVMap folder to use
/// instead), <c>out</c> (where the PNGs go; the request's folder when absent) and <c>mode</c> (whose objective props
/// show; Story when absent). Each shot is drawn in an <see cref="OffscreenView"/> at twice its size (as far as 4096
/// pixels allow) and scaled down to it, so edges are smooth, with its look (plain, night vision or thermal), and saved
/// as its <c>image</c>. Prints one <c>SVM_MAPSHOT</c> JSON line per shot
/// and quits with 0 when every shot was saved, 1 otherwise. Needs a real renderer: start it with --svm-background,
/// never --headless.
/// </summary>
public partial class MapShot : Node
{
    /// <summary>Frames drawn before the first picture, so streaming and shader compilation have settled.</summary>
    private const int WarmUpFrames = 30;

    /// <summary>Frames drawn after a camera move or a look change before the picture is read.</summary>
    private const int SettleFrames = 6;

    /// <summary>Frames between freeing the view and quitting, so no render-thread callback runs into the shutdown.</summary>
    private const int TeardownFrames = 4;

    /// <summary>The largest side drawn; a shot is supersampled only as far as this allows.</summary>
    private const int MaxRenderSide = 4096;

    private readonly string _request;
    private readonly List<GoldenShot> _shots = [];
    private RenderMap? _map;
    private OffscreenView? _view;
    private MapSpotLights? _spots;
    private string _out = "";
    private int _index;
    private int _waited;
    private int _saved;
    private bool _tearingDown;

    public MapShot(string request)
    {
        _request = request;
        Name = "MapShot";
    }

    public override void _Ready() =>
        // The app adds this node from its own _Ready, while the root is still setting up its children: the view can
        // only join the tree a moment later.
        Callable.From(Begin).CallDeferred();

    private void Begin()
    {
        if (Start() is { } problem)
        {
            Report(new Dictionary<string, object> { ["error"] = problem });
            Finish();
        }
    }

    /// <summary>Reads the request and builds the map's view; the reason when it cannot.</summary>
    private string? Start()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return "map pictures need a real renderer; run with --svm-background instead of --headless";
        }

        string map;
        string? folder;
        string mode;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(_request));
            JsonElement root = doc.RootElement;
            map = Text(root, "map") ?? string.Empty;
            folder = Text(root, "folder");
            mode = Text(root, "mode") ?? "Story";
            _out = Text(root, "out") ?? Path.GetDirectoryName(Path.GetFullPath(_request)) ?? ".";
            _shots.AddRange(GoldenShots.Load(_request));
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            return $"cannot read {_request}: {e.Message}";
        }

        if (_shots.Count == 0)
        {
            return "the request has no shots";
        }

        foreach (GoldenShot shot in _shots)
        {
            if (GoldenShots.RendMapOf(shot.Look) is null)
            {
                return $"shot {shot.Id}: unknown look '{shot.Look}' (plain, nightvision or thermal)";
            }

            if (shot.Width is < 16 or > 4096 || shot.Height is < 16 or > 4096)
            {
                return $"shot {shot.Id}: size {shot.Width}x{shot.Height} is outside 16..4096";
            }
        }

        folder ??= map.Length > 0 ? MapBuilder.FindMapFolder(map, App.From(this).Options.MapRoots) : null;
        if (folder is null || !File.Exists(Path.Combine(folder, MapBuilder.WorldFile)))
        {
            return $"map '{map}' has no render folder";
        }

        Directory.CreateDirectory(_out);
        _map = MapBuilder.Build(folder);
        _view = new OffscreenView(GetTree().Root, _map, RenderSize(_shots[0]));
        _spots = new MapSpotLights(_map, _view.Viewport);
        AnimatedProps.Build(_map, _map.Root);
        GameplayObjects.ShowMode(_map, mode);
        Place(_shots[0]);
        _waited = -WarmUpFrames;
        return null;
    }

    public override void _Process(double delta)
    {
        if (_tearingDown)
        {
            if (++_waited >= TeardownFrames)
            {
                GetTree().Quit(_saved == _shots.Count && _shots.Count > 0 ? 0 : 1);
            }

            return;
        }

        if (_view is null || _index >= _shots.Count)
        {
            return;
        }

        GoldenShot shot = _shots[_index];
        if (_waited == 0 && _index > 0)
        {
            Place(shot);
        }

        _spots?.Update(shot.CameraLocation);
        _map!.LightBeams?.UpdateSlots();
        if (++_waited <= SettleFrames)
        {
            return;
        }

        string path = Path.Combine(_out, shot.Image);
        Image image = _view.Capture();
        if (image.GetWidth() != shot.Width || image.GetHeight() != shot.Height)
        {
            // OffscreenView draws without anti-aliasing (golden comparisons need that); a menu picture is smoothed by
            // drawing it larger and scaling it down.
            image.Resize(shot.Width, shot.Height, Image.Interpolation.Lanczos);
        }

        Error saved = image.SavePng(path);
        if (saved == Error.Ok)
        {
            _saved++;
        }

        Report(new Dictionary<string, object>
        {
            ["id"] = shot.Id,
            ["image"] = path,
            ["width"] = image.GetWidth(),
            ["height"] = image.GetHeight(),
            ["look"] = shot.Look,
            ["saved"] = saved == Error.Ok,
        });
        _index++;
        _waited = 0;
        if (_index == _shots.Count)
        {
            Finish();
        }
    }

    private void Place(GoldenShot shot)
    {
        _map!.SetTime(shot.TimeSeconds);
        Vector2I size = RenderSize(shot);
        if (_view!.Viewport.Size != size)
        {
            _view.Viewport.Size = size;
        }

        _view.View.RendMap = GoldenShots.RendMapOf(shot.Look) ?? RenderK.RendMapNormal;
        _view.View.SetUeCamera(shot.CameraLocation, shot.CameraRotation, shot.Fov, cut: true);
    }

    /// <summary>The size a shot is drawn at: twice its own, or less when that would pass <see cref="MaxRenderSide"/>.</summary>
    private static Vector2I RenderSize(GoldenShot shot)
    {
        float scale = Math.Clamp(MaxRenderSide / (float)Math.Max(shot.Width, shot.Height), 1f, 2f);
        return new Vector2I((int)(shot.Width * scale), (int)(shot.Height * scale));
    }

    private void Finish()
    {
        _spots?.Dispose();
        _spots = null;
        _view?.Dispose();
        _view = null;
        _tearingDown = true;
        _waited = 0;
    }

    private static void Report(Dictionary<string, object> line) =>
        GD.Print("SVM_MAPSHOT " + JsonSerializer.Serialize(line, new JsonSerializerOptions { WriteIndented = false }));

    private static string? Text(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
