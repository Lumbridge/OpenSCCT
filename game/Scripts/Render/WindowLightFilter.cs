using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Render;

/// <summary>Optical sheets for live lights. Geometry and tint change only when panes are edited, broken or reset;
/// the shader filters each light's current colour, so pulses and switches need no lightmap uploads.</summary>
public sealed class WindowLightFilter
{
    public sealed record Pane(string Id, Color Tint, Vector3[] Triangles);
    private const string TextureName = "svm_window_light_sheets", CountName = "svm_window_light_triangles";
    private static bool registered;
    private readonly ImageTexture texture;
    private IReadOnlyList<Pane> panes = Array.Empty<Pane>();

    public WindowLightFilter()
    {
        using var empty = Image.CreateEmpty(4, 1, false, Image.Format.Rgbaf);
        texture = ImageTexture.CreateFromImage(empty);
        EnsureGlobals();
        ShaderGlobals.Set(TextureName, texture);
        ShaderGlobals.Set(CountName, 0);
    }

    public static void EnsureGlobals()
    {
        if (!registered)
        {
            using var empty = Image.CreateEmpty(4, 1, false, Image.Format.Rgbaf);
            ShaderGlobals.Add(TextureName, RenderingServer.GlobalShaderParameterType.Sampler2D, ImageTexture.CreateFromImage(empty));
            ShaderGlobals.Add(CountName, RenderingServer.GlobalShaderParameterType.Int, 0);
            registered = true;
        }
    }

    public void SetPanes(IReadOnlyList<Pane> value)
    {
        panes = value;
        SetBroken(null);
    }

    public void SetBroken(IReadOnlySet<string>? broken)
    {
        int count = 0;
        foreach (var pane in panes) if (broken?.Contains(pane.Id) != true) count += pane.Triangles.Length / 3;
        using var image = Image.CreateEmpty(4, Math.Max(1, count), false, Image.Format.Rgbaf);
        int row = 0;
        foreach (var pane in panes)
        {
            if (broken?.Contains(pane.Id) == true) continue;
            int triangles = pane.Triangles.Length / 3;
            for (int i = 0; i < triangles; i++, row++)
            {
                Vector3 a = pane.Triangles[i * 3], b = pane.Triangles[i * 3 + 1] - a, c = pane.Triangles[i * 3 + 2] - a;
                image.SetPixel(0, row, new Color(a.X, a.Y, a.Z));
                image.SetPixel(1, row, new Color(b.X, b.Y, b.Z));
                image.SetPixel(2, row, new Color(c.X, c.Y, c.Z));
                image.SetPixel(3, row, new Color(pane.Tint.R, pane.Tint.G, pane.Tint.B, triangles));
            }
        }
        texture.SetImage(image);
        ShaderGlobals.Set(TextureName, texture);
        ShaderGlobals.Set(CountName, count);
    }
}
