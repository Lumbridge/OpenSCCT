using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Hud.Magma;

/// <summary>
/// Draws <see cref="MagmaDrawList"/>s with Godot canvas items. Items are drawn in order; each run of items with the
/// same blend goes to one child canvas item, so Dodge runs can use their own material:
/// <list type="bullet">
/// <item>Normal and Burn: alpha blend of texture x vertex colour, in the 8-bit display space of the screen (as the
/// original's D3D blend).</item>
/// <item>Dodge, 2X, 4X: <c>dst x (1 + S)</c>, S = saturate(2 tex vc), widget alpha ignored, read from a copy of the
/// screen taken just before the run (a BackBufferCopy).</item>
/// </list>
/// Texts are the Magma bitmap fonts: top-aligned in their rect, the pen line one text height below its top.
/// </summary>
public partial class MagmaCanvas : Node2D
{
    private const string DodgeShader = """
        shader_type canvas_item;
        render_mode unshaded, blend_mix;
        uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
        varying vec4 vc;
        void vertex() { vc = COLOR; }
        void fragment() {
            vec3 s = clamp(2.0 * texture(TEXTURE, UV).rgb * vc.rgb, 0.0, 1.0);
            vec3 dst = texture(screen_tex, SCREEN_UV).rgb;
            COLOR = vec4(min(dst * (1.0 + s), vec3(1.0)), 1.0);
        }
        """;

    /// <summary>Multi-line text pitch, in text heights (the game's wrapped tip lines, measured by the Bridge HUD work).</summary>
    public const float LinePitch = 1.45f;

    private readonly List<MagmaBatch> _batches = new();
    private readonly List<BackBufferCopy> _copies = new();
    private ShaderMaterial? _dodge;

    public HudAssets? Assets { get; set; }

    /// <summary>Items submitted last, for checks.</summary>
    public int ItemCount { get; private set; }

    public void Submit(IReadOnlyList<MagmaDrawItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ItemCount = items.Count;
        int batch = 0;
        int start = 0;
        while (start < items.Count)
        {
            bool dodge = IsDodge(items[start]);
            int end = start + 1;
            while (end < items.Count && IsDodge(items[end]) == dodge)
            {
                end++;
            }

            MagmaBatch b = Batch(batch++);
            b.Items.Clear();
            for (int i = start; i < end; i++)
            {
                b.Items.Add(items[i]);
            }

            b.Material = dodge ? DodgeMaterial() : null;
            b.Visible = true;
            // A dodge run reads the screen as drawn so far, HUD included: copy the back buffer just before it.
            _copies[batch - 1].Visible = dodge;
            b.QueueRedraw();
            start = end;
        }

        for (int i = batch; i < _batches.Count; i++)
        {
            if (_batches[i].Items.Count > 0)
            {
                _batches[i].Items.Clear();
                _batches[i].QueueRedraw();
            }

            _batches[i].Visible = false;
            _copies[i].Visible = false;
        }
    }

    private static bool IsDodge(MagmaDrawItem item) =>
        item.Kind == MagmaWidgetKind.Image && item.Blend is MagmaBlend.Dodge or MagmaBlend.Dodge2X or MagmaBlend.Dodge4X;

    private ShaderMaterial DodgeMaterial() => _dodge ??= new ShaderMaterial { Shader = new Shader { Code = DodgeShader } };

    private MagmaBatch Batch(int i)
    {
        while (_batches.Count <= i)
        {
            var copy = new BackBufferCopy
            {
                Name = "Copy" + _batches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CopyMode = BackBufferCopy.CopyModeEnum.Viewport,
                Visible = false,
            };
            AddChild(copy);
            _copies.Add(copy);
            var b = new MagmaBatch(this)
            {
                Name = "Batch" + _batches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TextureFilter = TextureFilterEnum.Linear,
                TextureRepeat = TextureRepeatEnum.Enabled,
            };
            AddChild(b);
            _batches.Add(b);
        }

        return _batches[i];
    }

    internal void DrawItems(CanvasItem target, List<MagmaDrawItem> items)
    {
        foreach (MagmaDrawItem item in items)
        {
            if (item.Kind == MagmaWidgetKind.Text)
            {
                DrawText(target, item);
                continue;
            }

            Texture2D? texture = Assets?.Texture(item.Package, item.Texture);
            if (texture is null || item.Points.Length < 3)
            {
                continue;
            }

            target.DrawPolygon(item.Points, item.Colors, item.Uvs, texture);
        }
    }

    private void DrawText(CanvasItem target, MagmaDrawItem item)
    {
        MagmaFont? font = Assets?.Font(item.Font);
        if (font is null)
        {
            return;
        }

        font.LoadPages(file => Assets!.LoadTexture(file));
        foreach ((string line, float y) in Layout(font, item))
        {
            float width = font.Measure(line, item.HeightPx, item.XScale, item.TrackingPx);
            float x = item.Align switch
            {
                MagmaAlign.Center => item.Rect.Position.X + (item.Rect.Size.X - width) * 0.5f,
                MagmaAlign.Right => item.Rect.End.X - width,
                _ => item.Rect.Position.X,
            };
            if (item.Shadow.A > 0f)
            {
                DrawRun(target, font, line, new Vector2(x, y) + item.ShadowOffsetPx, item, item.Shadow);
            }

            DrawRun(target, font, line, new Vector2(x, y), item, item.Color);
        }
    }

    /// <summary>Lines of a text item with the top of each line: explicit line breaks, and word wrap in rects taller
    /// than two lines.</summary>
    public static List<(string Line, float Top)> Layout(MagmaFont font, MagmaDrawItem item)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(item);
        var lines = new List<(string, float)>();
        float pitch = item.HeightPx * LinePitch;
        float top = item.Rect.Position.Y;
        bool wrap = item.Rect.Size.Y > 2.2f * item.HeightPx;
        foreach (string paragraph in item.Text.Split('\n'))
        {
            if (!wrap || font.Measure(paragraph, item.HeightPx, item.XScale, item.TrackingPx) <= item.Rect.Size.X)
            {
                lines.Add((paragraph, top));
                top += pitch;
                continue;
            }

            string current = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string attempt = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && font.Measure(attempt, item.HeightPx, item.XScale, item.TrackingPx) > item.Rect.Size.X)
                {
                    lines.Add((current, top));
                    top += pitch;
                    current = word;
                }
                else
                {
                    current = attempt;
                }
            }

            if (current.Length > 0)
            {
                lines.Add((current, top));
                top += pitch;
            }
        }

        return lines;
    }

    private static void DrawRun(CanvasItem target, MagmaFont font, string text, Vector2 topLeft, MagmaDrawItem item, Color color)
    {
        float k = item.HeightPx / font.Size;
        float kx = k * item.XScale;
        float baseline = topLeft.Y + item.HeightPx;
        float x = topLeft.X;
        foreach (char c in text)
        {
            if (font.TryGlyph(c, out MagmaGlyph g) && g.W > 0 && g.Page >= 0 && g.Page < font.Pages.Length &&
                font.Pages[g.Page] is { } page)
            {
                var dest = new Rect2(x + g.XOff * kx, baseline - g.Base * k, g.W * kx, g.H * k);
                target.DrawTextureRectRegion(page, dest, font.SourceRect(g), color);
            }

            x += font.AdvanceOf(c) * kx + item.TrackingPx;
        }
    }
}

/// <summary>One run of same-blend items of a <see cref="MagmaCanvas"/>.</summary>
public partial class MagmaBatch : Node2D
{
    private readonly MagmaCanvas? _owner;

    public MagmaBatch()
    {
    }

    public MagmaBatch(MagmaCanvas owner)
    {
        _owner = owner;
    }

    public List<MagmaDrawItem> Items { get; } = new();

    public override void _Draw() => _owner?.DrawItems(this, Items);
}
