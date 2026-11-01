using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Golden;

namespace OpenScctGame.Smoke;

/// <summary>Actual additive flares, translucent sky lights and a masked moon: materials, placement and rendered pixels.</summary>
public sealed class MapLightSpritesSmoke : SmokeTest
{
    public const string TestName = "map-light-sprites";
    public override string Name => TestName;
    private static readonly (string Map, int Count, uint Layer, string Blend)[] Cases =
        [("SteeE", 8, RenderLayers.Effects, "BLEND_ADDITIVE"),
         ("ClarD", 545, RenderLayers.Sky, "BLEND_TRANSLUCENT"),
         ("StatE", 1, RenderLayers.Sky, "BLEND_MASKED")];
    private int _case;
    private int _frames;
    private OffscreenView? _view;
    private MeshInstance3D? _sprite;
    private Image? _shown;
    private int _pixels;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_case >= Cases.Length) return SmokeResult.Pass($"SteeE 8 + ClarD 545 + StatE 1 sprites: additive/translucent/masked materials, lighting, billboards and sky layers; rendered differing pixels={_pixels}");
        var test = Cases[_case];
        if (_view is null)
        {
            string? folder = MapBuilder.FindMapFolder(test.Map, context.App.Options.MapRoots);
            if (folder is null) return SmokeResult.Fail(test.Map + " content missing");
            RenderMap map = MapBuilder.Build(folder);
            if (map.LightSprites is not { } sprites || sprites.Nodes.Count != test.Count || sprites.Missing.Count != 0)
            {
                map.Root.Free();
                return SmokeResult.Fail($"{test.Map}: expected {test.Count} visible Light sprites, got {map.LightSprites?.Nodes.Count}; missing={map.LightSprites?.Missing.Count}");
            }

            _sprite = sprites.Nodes.Values.First();
            foreach (MeshInstance3D sprite in sprites.Nodes.Values)
            {
                if (!sprite.Visible || sprite.Layers != test.Layer || sprite.Mesh is not QuadMesh { Size.X: > 0, Size.Y: > 0 } ||
                    sprite.MaterialOverride is not ShaderMaterial { Shader: { } shader } material ||
                    !shader.Code.Contains("#define BILLBOARD", StringComparison.Ordinal) ||
                    !shader.Code.Contains("#define " + test.Blend, StringComparison.Ordinal) ||
                    shader.Code.Contains("#define LIGHT_ACTOR", StringComparison.Ordinal) ||
                    material.GetShaderParameter("tex_diffuse").AsGodotObject() is not Texture2D)
                {
                    map.Root.Free();
                    return SmokeResult.Fail(test.Map + " sprite material/layer missing: " + sprite.Name);
                }
            }

            if (DisplayServer.GetName() == "headless")
            {
                map.Root.Free();
                _case++;
                return SmokeResult.Running;
            }

            // Isolate an authored sprite without replacing its geometry, material, position or sky layer.
            foreach (Node child in map.Root.GetChildren())
                if (child is Node3D node && !ReferenceEquals(node, _sprite)) node.Visible = false;
            _view = new OffscreenView(context.Tree.Root, map, new Vector2I(640, 360));
            FVector target = UeSpace.ToUe(_sprite.Position);
            FVector camera = test.Layer == RenderLayers.Sky && map.Lighting.SkyLocation is { } sky
                ? sky : target + new FVector(300, -250, 100);
            _view.View.SetUeCamera(camera, (target - camera).Rotation(), 70f, cut: true);
            _frames = 0;
            return SmokeResult.Running;
        }

        if (++_frames < 5) return SmokeResult.Running;
        if (_shown is null)
        {
            _shown = _view.Capture();
            if (System.Environment.GetEnvironmentVariable("SVM_LIGHT_SPRITE_PREVIEW") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                _shown.SavePng(Path.Combine(output, test.Map + "-sprite.png"));
            }
            _sprite!.Visible = false;
            _frames = 0;
            return SmokeResult.Running;
        }

        using Image hidden = _view.Capture();
        int changed = 0;
        for (int y = 0; y < hidden.GetHeight(); y++)
        for (int x = 0; x < hidden.GetWidth(); x++)
        {
            Color a = _shown.GetPixel(x, y), b = hidden.GetPixel(x, y);
            if (MathF.Abs(a.R - b.R) + MathF.Abs(a.G - b.G) + MathF.Abs(a.B - b.B) > .025f) changed++;
        }
        _shown.Dispose();
        _shown = null;
        _view.Dispose();
        _view = null;
        if (changed < 5) return SmokeResult.Fail(test.Map + " sprite produced no visible pixels");
        _pixels += changed;
        _case++;
        return SmokeResult.Running;
    }
}
