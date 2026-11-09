using System;
using System.IO;
using Godot;
using OpenScctGame.Fx;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Smoke;

/// <summary>Rendered surface projection, including the oblique-wall clipping regression and real occlusion.</summary>
public sealed class LaserDotSmoke : SmokeTest
{
    public const string TestName = "laser-dot";
    public override string Name => TestName;

    private enum Surface { Flat, Angled, Step, Occluder, Empty }
    private readonly record struct Case(string Name, Surface Surface, float Scale = .25f,
        float CameraDistance = 2f, int Vision = 0, bool Legacy = false);

    private static readonly Case[] Cases =
    [
        new("front", Surface.Flat),
        new("oblique-60-degrees", Surface.Angled),
        new("stepped-surface", Surface.Step),
        new("intervening-wall", Surface.Occluder),
        new("no-surface", Surface.Empty),
        new("minimum-scale", Surface.Flat, .05f),
        new("large-scale", Surface.Flat, 2f),
        new("near-camera", Surface.Flat, CameraDistance: .3f),
        new("far-camera", Surface.Flat, CameraDistance: 40f),
        new("thermal-material", Surface.Flat, Vision: 36),
        new("emf-material", Surface.Flat, Vision: 38),
        new("legacy-quad-control", Surface.Angled, Legacy: true),
    ];

    private SubViewport? _viewport;
    private Node3D? _scene;
    private Texture2D? _texture;
    private string? _output;
    private int _case, _frames, _referencePixels, _teardown;
    private SmokeResult? _result;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_result is { } result) return ++_teardown < 4 ? SmokeResult.Running : result;
        try
        {
            if (_viewport is null)
            {
                if (DisplayServer.GetName() == "headless") return SmokeResult.Fail("requires a renderer");
                MaterialCompiler.EnsureGlobals();
                _viewport = new SubViewport { Size = new Vector2I(256, 256), OwnWorld3D = true,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always, UseDebanding = false };
                context.Tree.Root.AddChild(_viewport);
                _texture = DotTexture();
                if (System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") is { Length: > 0 } output)
                {
                    _output = Path.Combine(Path.GetFullPath(output), TestName);
                    Directory.CreateDirectory(_output);
                }
                Configure();
            }
            if (++_frames < 12) return SmokeResult.Running;
            using Image image = _viewport!.GetTexture().GetImage();
            Case test = Cases[_case];
            if (_output is not null && image.SavePng(Path.Combine(_output, test.Name + ".png")) != Error.Ok)
                return Finish(SmokeResult.Fail(test.Name + ": could not save rendered evidence"));
            (int pixels, float balance, float aspect) = Measure(image);
            GD.Print($"SVM_LASER_DOT {test.Name}: pixels={pixels}, half-balance={balance:F3}, aspect={aspect:F3}");
            if (test.Surface is Surface.Empty or Surface.Occluder)
            {
                if (pixels != 0) return Finish(SmokeResult.Fail($"{test.Name}: {pixels} laser pixels without a visible receiving surface"));
            }
            else if (test.Legacy)
            {
                // This is the previous beam-facing quad. The same gate must reject its missing half, proving
                // that a visible centre pixel or a node count cannot accidentally pass this regression.
                if (pixels >= _referencePixels * .8f || balance >= .75f)
                    return Finish(SmokeResult.Fail($"legacy control did not reproduce clipped coverage ({pixels}/{_referencePixels}, balance={balance:F3})"));
            }
            else
            {
                if (pixels < 500 || balance < .88f || aspect < .9f || aspect > 1.1f)
                    return Finish(SmokeResult.Fail($"{test.Name}: dot clipped or missing ({pixels} pixels, balance={balance:F3}, aspect={aspect:F3})"));
                if (_case == 0) _referencePixels = pixels;
                // The 60-degree wall and both sides of the step fit within the original 20 uu projection
                // depth. Viewed along the beam, they must retain the front wall's full circular footprint.
                if (test.Scale == .25f && (pixels < _referencePixels * .85f || pixels > _referencePixels * 1.15f))
                    return Finish(SmokeResult.Fail($"{test.Name}: coverage changed from {_referencePixels} to {pixels} pixels"));
            }
            if (++_case == Cases.Length)
                return Finish(SmokeResult.Pass("full dot on frontal/60-degree/stepped surfaces; occluder and empty space reject it; min/large scales, near/far camera, thermal/EMF; legacy quad fails coverage" +
                    (_output is null ? "" : "; images=" + _output)));
            _frames = 0;
            Configure();
            return SmokeResult.Running;
        }
        catch (Exception error) { return Finish(SmokeResult.Fail(error.ToString())); }
    }

    private void Configure()
    {
        _scene?.Free();
        _scene = new Node3D();
        _viewport!.AddChild(_scene);
        Case test = Cases[_case];
        OpenScctGame.Render.ShaderGlobals.Set("svm_vision", test.Vision);
        float width = UeSpace.LengthToGodot(SpyLaserView.DotTexels * test.Scale);
        float size = width * 3f;
        _scene.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
        } });
        _scene.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = size,
            Position = new Vector3(0, 0, test.CameraDistance), Near = .01f, Far = 100f, Current = true });
        var wall = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Colors.Black, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        if (test.Surface == Surface.Step)
        {
            AddWall(new Vector2(size, size * 2f), new Vector3(-size / 2f, 0, -.035f), wall);
            AddWall(new Vector2(size, size * 2f), new Vector3(size / 2f, 0, .035f), wall);
        }
        else if (test.Surface != Surface.Empty)
        {
            MeshInstance3D receiver = AddWall(new Vector2(size * 3f, size * 3f), Vector3.Zero, wall);
            if (test.Surface == Surface.Angled) receiver.Rotation = new Vector3(0, Mathf.DegToRad(60f), 0);
            if (test.Surface == Surface.Occluder)
                AddWall(new Vector2(size * 2f, size * 2f), new Vector3(0, 0, .25f), wall);
        }
        MeshInstance3D dot;
        if (test.Legacy)
        {
            dot = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(width, width) },
                Position = new Vector3(0, 0, UeSpace.LengthToGodot(.5f)),
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoTexture = _texture,
                } };
            _scene.AddChild(dot);
        }
        else
        {
            dot = SpyLaserView.CreateDot(_texture!);
            _scene.AddChild(dot);
            SpyLaserView.PlaceDot(dot, default, UeSpace.DirectionToUe(Vector3.Forward), test.Scale, 20f);
        }
        dot.Visible = true;
    }

    private MeshInstance3D AddWall(Vector2 size, Vector3 position, Material material)
    {
        var wall = new MeshInstance3D { Mesh = new QuadMesh { Size = size }, Position = position, MaterialOverride = material };
        _scene!.AddChild(wall);
        return wall;
    }

    private static Texture2D DotTexture()
    {
        using Image image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float radius = new Vector2(x - 31.5f, y - 31.5f).Length() / 32f;
            image.SetPixel(x, y, new Color(1f, .025f, .005f, Math.Clamp(1f - radius, 0f, 1f)));
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static (int Pixels, float Balance, float Aspect) Measure(Image image)
    {
        int count = 0, left = 0, right = 0, top = 0, bottom = 0;
        int minX = 256, minY = 256, maxX = -1, maxY = -1;
        for (int y = 0; y < 256; y++)
        for (int x = 0; x < 256; x++)
        {
            Color color = image.GetPixel(x, y);
            if (color.R < .2f || color.R < color.G * 4f || color.R < color.B * 4f) continue;
            count++;
            if (x < 128) left++; else right++;
            if (y < 128) top++; else bottom++;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        float balance = Math.Min((float)Math.Min(left, right) / Math.Max(1, Math.Max(left, right)),
            (float)Math.Min(top, bottom) / Math.Max(1, Math.Max(top, bottom)));
        float aspect = count == 0 ? 0f : (float)(maxX - minX + 1) / (maxY - minY + 1);
        return (count, balance, aspect);
    }

    private SmokeResult Finish(SmokeResult result)
    {
        OpenScctGame.Render.ShaderGlobals.Set("svm_vision", 0);
        _viewport?.QueueFree();
        _viewport = null;
        _scene = null;
        _result = result;
        return SmokeResult.Running;
    }
}
