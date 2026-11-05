using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Smoke;

/// <summary>CPU cookie uploads, shared layers and complete map-light teardown, including extreme spot frusta.</summary>
public sealed class SpotShadowCookiesSmoke : SmokeTest
{
    public const string TestName = "spot-shadow-cookies";
    public override string Name => TestName;
    private readonly List<SpotShadowLight> _lights = [];
    private SubViewport? _viewport;
    private Image? _red;
    private Image? _blue;
    private Image? _empty;
    private ShaderMaterial? _probe;
    private int _phase;
    private int _waited;
    private int _previousMapSize;
    private bool _gpu;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_viewport is null)
            {
                Setup(context);
                CreateLights();
                if (_gpu) CreateProbe();
                _phase = 1;
                return SmokeResult.Running;
            }

            // Render-thread atlas creation/free and texture uploads must settle between simulated map visits.
            if (++_waited < 4) return SmokeResult.Running;
            _waited = 0;
            if (_phase == 1)
            {
                CheckSources();
                if (_gpu) CheckLayers(Colors.White, Colors.Red, Colors.Blue);
                ReleaseLights();
                _probe?.SetShaderParameter("expected_layers", 1);
                _phase = 2;
                return SmokeResult.Running;
            }

            if (_phase == 2)
            {
                if (_gpu) CheckLayers(Colors.White);
                // A new visit must start its cookie indices again rather than retain the previous map's layers.
                _lights.Add(new SpotShadowLight(_viewport, null, Shape(400f, .1f), _blue));
                _probe?.SetShaderParameter("expected_layers", 2);
                _phase = 3;
                return SmokeResult.Running;
            }

            if (_gpu) CheckLayers(Colors.White, Colors.Blue);
            CheckSources();
            return Finish(true, $"source images preserved; shared/missing cookies and map re-entry; narrow/regular/wide/parallel frusta; GPU layers={_gpu}");
        }
        catch (Exception ex)
        {
            return Finish(false, ex.Message);
        }
    }

    private void Setup(SmokeContext context)
    {
        _gpu = DisplayServer.GetName() != "headless";
        _previousMapSize = SpotShadowLight.MapSize;
        SpotShadowLight.MapSize = 128;
        _viewport = new SubViewport { Size = new Vector2I(64, 64), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);

        _red = Image.CreateEmpty(8, 4, false, Image.Format.Rgb8);
        _red.Fill(Colors.Red);
        _red.GenerateMipmaps();
        _empty = new Image();

        string folder = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "test-results", TestName);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "blue.png");
        using (Image source = Image.CreateEmpty(3, 9, false, Image.Format.Rgba8))
        {
            source.Fill(Colors.Blue);
            Require(source.SavePng(file) == Error.Ok, "could not write the synthetic cookie");
        }

        var cache = new TextureCache(folder);
        _blue = cache.GetImage("blue.png");
        Require(_blue is not null && ReferenceEquals(_blue, cache.GetImage("blue.png")), "CPU cookie cache did not reuse its image");
        Require(cache.GetImage("missing.png") is null && cache.GetImage(null) is null, "missing cookie did not fall back to white");
        Require(cache.Count == 0, "requesting CPU cookie pixels allocated a GPU texture");
        Require(cache.Get("blue.png", mipmaps: false) is not null, "CPU cookie could not subsequently be used as a texture");
        Require(ReferenceEquals(_blue, cache.GetImage("blue.png")), "texture creation replaced the retained CPU cookie");
    }

    private void CreateLights()
    {
        SpotShadowShape[] shapes = [Shape(50f, .998f), Shape(400f, .1f), Shape(500f, .05f) with { Range = 1f }, Shape(200f, 1f)];
        for (int i = 0; i < shapes.Length; i++)
        {
            var light = new SpotShadowLight(_viewport!, null, shapes[i], i == 1 ? _blue : _red, cacheStatic: true);
            _lights.Add(light);
            CheckProjection(light, shapes[i]);
        }

        _lights.Add(new SpotShadowLight(_viewport!, null, Shape(400f, .1f)));
        _lights.Add(new SpotShadowLight(_viewport!, null, Shape(400f, .1f), _empty));
    }

    private static SpotShadowShape Shape(float height, float ratio) =>
        SpotShadowShape.Torch with { Range = 6000f, SpotWidth = 700f, SpotHeight = height, NearPlaneRatio = ratio, Cookie = "" };

    private static void CheckProjection(SpotShadowLight light, SpotShadowShape shape)
    {
        Camera3D camera = light.Viewport.GetNode<Camera3D>("LightCamera");
        Camera3D fixedCamera = light.Viewport.GetNode<Camera3D>("StaticShadow/StaticLightCamera");
        bool parallel = shape.ApexDistance() is null;
        Require(camera.Projection == (parallel ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Frustum),
            "spot camera did not retain the required parallel/perspective projection");
        // The decoded far-plane top edge must actually project to the texture's top edge. Testing the resulting
        // projection matrix catches rejected FOV setters and aspect errors rather than repeating the size formula.
        var top = new Vector4(0f, UeSpace.LengthToGodot(shape.SpotHeight), -camera.Far, 1f);
        Vector4 clip = camera.GetCameraProjection() * top;
        Require(MathF.Abs(clip.Y / clip.W - 1f) < 1e-4f, "shadow projection changed the cookie's far-plane top edge");
        Vector4 fixedClip = fixedCamera.GetCameraProjection() * top;
        Require(MathF.Abs(fixedClip.Y / fixedClip.W - 1f) < 1e-4f, "static shadow projection changed the cookie's far-plane top edge");
        Require(fixedCamera.Projection == camera.Projection && fixedCamera.Size == camera.Size && fixedCamera.FrustumOffset == camera.FrustumOffset,
            "static and dynamic shadow frusta differ");
    }

    private void CheckSources()
    {
        Require(_red!.GetSize() == new Vector2I(8, 4) && _red.GetFormat() == Image.Format.Rgb8 && _red.HasMipmaps(),
            "normalizing a cookie mutated its source dimensions, format or mipmaps");
        Require(_blue!.GetSize() == new Vector2I(3, 9) && !_blue.HasMipmaps(), "normalizing a cached cookie mutated its source");
        Require(_red.GetPixel(0, 0) == Colors.Red && _blue.GetPixel(0, 0) == Colors.Blue, "normalizing a cookie changed its source pixels");
    }

    private void CreateProbe()
    {
        _probe = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = $$"""
                    shader_type canvas_item;
                    global uniform sampler2DArray svm_spot_cookies;
                    uniform int expected_layers = 3;
                    void fragment() {
                        ivec3 dimensions = textureSize(svm_spot_cookies, 0);
                        if (dimensions != ivec3({{RenderK.SpotCookieSize}}, {{RenderK.SpotCookieSize}}, expected_layers)) {
                            COLOR = vec4(1.0, 0.0, 1.0, 1.0);
                        } else {
                            int layer = clamp(int(UV.x * float(expected_layers)), 0, expected_layers - 1);
                            COLOR = texture(svm_spot_cookies, vec3(0.5, 0.5, float(layer)));
                        }
                    }
                    """,
            },
        };
        _viewport!.AddChild(new ColorRect { Size = new Vector2(64, 64), Material = _probe, Color = Colors.White });
    }

    private void CheckLayers(params Color[] colours)
    {
        // Deliberate test capture: the canvas shader samples the same global array used by world materials.
        // Its magenta failure colour also checks the uploaded array dimensions and number of layers.
        using Image image = _viewport!.GetTexture().GetImage();
        Require(image is not null && !image.IsEmpty(), "cookie probe returned no rendered pixels");
        for (int i = 0; i < colours.Length; i++)
        {
            int x = (int)((i + .5f) * image!.GetWidth() / colours.Length);
            Color pixel = image.GetPixel(x, image.GetHeight() / 2);
            Require(pixel == colours[i], $"cookie probe layer {i}: expected {colours[i]}, got {pixel} (magenta means wrong array dimensions/layers)");
        }
    }

    private void ReleaseLights()
    {
        foreach (SpotShadowLight light in _lights) light.Dispose();
        _lights.Clear();
    }

    private SmokeResult Finish(bool passed, string detail)
    {
        ReleaseLights();
        _viewport?.QueueFree();
        _red?.Dispose();
        _blue?.Dispose();
        _empty?.Dispose();
        SpotShadowLight.MapSize = _previousMapSize;
        return passed ? SmokeResult.Pass(detail) : SmokeResult.Fail(detail);
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
