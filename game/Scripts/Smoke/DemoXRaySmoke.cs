using System;
using System.IO;
using Godot;
using OpenScctGame.Play.Demo;
using OpenScctGame.Render;

namespace OpenScctGame.Smoke;

/// <summary>Renders the demo mask over an exposed pawn proxy and a completely occluded one.</summary>
public sealed class DemoXRaySmoke : SmokeTest
{
    public const string TestName = "demo-xray";
    public override string Name => TestName;

    private static readonly Vector3 Spy = new(-1.25f, 0, 0);
    private static readonly Vector3 Merc = new(1.25f, 0, 0);
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private DemoXRayView? _xray;
    private Image? _baseline;
    private int _frames;
    private SmokeResult? _result;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_result is { } result) return ++_frames < 4 ? SmokeResult.Running : result;
        if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer");
        try
        {
            if (_viewport is null) SetUp(context);
            if (++_frames < 12) return SmokeResult.Running;
            using Image image = _viewport!.GetTexture().GetImage();
            if (_baseline is null)
            {
                Save(image, "off");
                _baseline = (Image)image.Duplicate();
                _xray!.DrawMask(_camera!, visible: true);
                _frames = 0;
                return SmokeResult.Running;
            }

            Save(image, "on");
            var spy = (Vector2I)_camera!.UnprojectPosition(Spy);
            var merc = (Vector2I)_camera.UnprojectPosition(Merc);
            Color wall = _baseline.GetPixelv(merc + new Vector2I(28, 0));
            if (Different(wall, _baseline.GetPixelv(merc)))
                return Finish(SmokeResult.Fail("the test wall did not fully obscure the merc"));
            if (Different(_baseline.GetPixelv(spy), image.GetPixelv(spy)) || Different(wall, image.GetPixelv(merc)))
                return Finish(SmokeResult.Fail("X-ray colored the interior of a silhouette"));

            int cool = CountOutline(_baseline, image, spy, warm: false);
            int warm = CountOutline(_baseline, image, merc, warm: true);
            return Finish(cool >= 24 && warm >= 24
                ? SmokeResult.Pass($"visible and occluded outlines render without filling their interiors ({cool} spy, {warm} merc pixels)")
                : SmokeResult.Fail($"missing colored outlines ({cool} spy, {warm} merc pixels)"));
        }
        catch (Exception error) { return Finish(SmokeResult.Fail(error.ToString())); }
    }

    private void SetUp(SmokeContext context)
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(256, 160), OwnWorld3D = true, UseDebanding = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        context.Tree.Root.AddChild(_viewport);
        _viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.025f, .025f, .025f),
        } });
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal, KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = 4f, Position = new Vector3(0, 0, 5), CullMask = RenderLayers.MainView, Current = true,
        };
        _viewport.AddChild(_camera);

        Vector3 pawnSize = new(.9f, 1.2f, .6f);
        Color body = new(.42f, .42f, .42f);
        AddBox(Spy, pawnSize, body, RenderLayers.World);
        AddBox(Merc, pawnSize, body, RenderLayers.World);
        AddBox(Merc + new Vector3(0, 0, .85f), new Vector3(1.8f, 1.9f, .2f), new Color(.18f, .18f, .18f), RenderLayers.World);
        AddBox(Spy, pawnSize, new Color(.08f, .55f, 1f), RenderLayers.DemoXRay);
        AddBox(Merc, pawnSize, new Color(1f, .26f, .08f), RenderLayers.DemoXRay);
        _xray = new DemoXRayView();
        _viewport.AddChild(_xray);
        _xray.Disable();
    }

    private void AddBox(Vector3 center, Vector3 size, Color color, uint layer)
    {
        _viewport!.AddChild(new MeshInstance3D
        {
            Position = center, Mesh = new BoxMesh { Size = size }, Layers = layer,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        });
    }

    private static int CountOutline(Image baseline, Image enabled, Vector2I center, bool warm)
    {
        int count = 0;
        for (int y = center.Y - 32; y <= center.Y + 32; y++)
        for (int x = center.X - 28; x <= center.X + 28; x++)
        {
            Color color = enabled.GetPixel(x, y);
            if (!Different(color, baseline.GetPixel(x, y))) continue;
            if (warm ? color.R > color.B + .15f : color.B > color.R + .15f) count++;
        }
        return count;
    }

    private static bool Different(Color a, Color b) =>
        MathF.Abs(a.R - b.R) > .08f || MathF.Abs(a.G - b.G) > .08f || MathF.Abs(a.B - b.B) > .08f;

    private static void Save(Image image, string state)
    {
        if (System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") is not { Length: > 0 } output) return;
        Directory.CreateDirectory(output);
        string path = Path.Combine(output, $"demo-xray-{state}.png");
        if (image.SavePng(path) != Error.Ok) throw new IOException("Could not save " + path);
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _baseline?.Dispose();
        _viewport?.QueueFree();
        _result = result;
        _frames = 0;
        return SmokeResult.Running;
    }
}
