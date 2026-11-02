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
/// Text uses Magma bitmap fonts by default (top-aligned, the pen line one height below its top). A draw item can
/// opt into a native font for a measured, single-line modern HUD label while retaining the same effect ordering.
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
    private HudAssets? _drawnAssets;
    private readonly Dictionary<TextLayoutKey, TextLine[]> _textLayouts = new();
    private readonly Queue<TextLayoutKey> _textLayoutOrder = new();
    private const int MaxTextLayouts = 256;
    private readonly record struct TextLayoutKey(MagmaFont Font, string Text, Rect2 Rect, float Height, float XScale, float Tracking);
    internal readonly record struct TextLine(string Text, float Top, float Width);

    public HudAssets? Assets { get; set; }

    /// <summary>Items submitted last, for checks.</summary>
    public int ItemCount { get; private set; }

    /// <summary>Number of changed command batches submitted; used by the focused performance check.</summary>
    internal long RedrawCount { get; private set; }
    internal long TextLayoutMisses { get; private set; }
    internal int RegionalCopyCount { get; private set; }

    /// <summary>Diagnostic override for pixel comparisons against full-screen back-buffer copies.</summary>
    internal bool FullViewportCopies { get; set; }

    public void Submit(IReadOnlyList<MagmaDrawItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ItemCount = items.Count;
        RegionalCopyCount = 0;
        bool assetsChanged = !ReferenceEquals(_drawnAssets, Assets);
        if (assetsChanged)
        {
            _drawnAssets = Assets;
            _textLayouts.Clear();
            _textLayoutOrder.Clear();
        }
        // The normal HUD is untransformed. Other callers retain full copies if their canvas transforms it.
        bool regionalCopies = !FullViewportCopies && IsInsideTree() && GetGlobalTransformWithCanvas() == Transform2D.Identity;
        Rect2 viewport = regionalCopies ? GetViewportRect() : default;
        int batch = 0;
        int start = 0;
        while (start < items.Count)
        {
            bool dodge = IsDodge(items[start]);
            bool? clamp = ClampOf(items[start]);
            int end = start + 1;
            while (end < items.Count && IsDodge(items[end]) == dodge &&
                   (clamp is null || ClampOf(items[end]) is null || ClampOf(items[end]) == clamp))
            {
                clamp ??= ClampOf(items[end]);
                end++;
            }

            MagmaBatch b = Batch(batch++);
            bool changed = b.SetItems(items, start, end);
            Material? material = dodge ? DodgeMaterial() : null;
            if (b.Material != material) b.Material = material;
            // Repeat is per canvas item, so a run shares it: clamped images must not wrap their far edge round.
            TextureRepeatEnum repeat = clamp == true ? TextureRepeatEnum.Disabled : TextureRepeatEnum.Enabled;
            if (b.TextureRepeat != repeat) b.TextureRepeat = repeat;
            if (!b.Visible) b.Visible = true;
            // A dodge run reads the screen as drawn so far, HUD included: copy the back buffer just before it.
            BackBufferCopy copy = _copies[batch - 1];
            if (copy.Visible != dodge) copy.Visible = dodge;
            if (dodge)
            {
                Rect2 bounds = default;
                bool bounded = regionalCopies && TryCopyRect(items, start, end, viewport, out bounds);
                if (bounded) RegionalCopyCount++;
                BackBufferCopy.CopyModeEnum mode = bounded ? BackBufferCopy.CopyModeEnum.Rect : BackBufferCopy.CopyModeEnum.Viewport;
                if (copy.CopyMode != mode) copy.CopyMode = mode;
                if (bounded && copy.Rect != bounds) copy.Rect = bounds;
            }
            if (changed || assetsChanged)
            {
                b.QueueRedraw();
                RedrawCount++;
            }
            start = end;
        }

        for (int i = batch; i < _batches.Count; i++)
        {
            if (_batches[i].Items.Count > 0)
            {
                _batches[i].ClearItems();
                _batches[i].QueueRedraw();
                RedrawCount++;
            }

            _batches[i].Visible = false;
            _copies[i].Visible = false;
        }
    }

    /// <summary>
    /// The dodge shader reads only SCREEN_UV with nearest filtering. A padded union covers every sampled pixel;
    /// unknown/non-finite geometry and full-screen runs keep the original full-viewport copy.
    /// </summary>
    internal static bool TryCopyRect(IReadOnlyList<MagmaDrawItem> items, int start, int end, Rect2 viewport, out Rect2 bounds)
    {
        bounds = default;
        if (!(viewport.Size.X > 0f && viewport.Size.Y > 0f)) return false;
        float left = float.PositiveInfinity, top = float.PositiveInfinity;
        float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        for (int i = start; i < end; i++)
        {
            MagmaDrawItem item = items[i];
            if (!IsDodge(item) || item.Points.Length < 3) return false;
            foreach (Vector2 point in item.Points)
            {
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return false;
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }
        if (!float.IsFinite(left)) return false;
        left = Math.Max(viewport.Position.X, MathF.Floor(left) - 1f);
        top = Math.Max(viewport.Position.Y, MathF.Floor(top) - 1f);
        right = Math.Min(viewport.End.X, MathF.Ceiling(right) + 1f);
        bottom = Math.Min(viewport.End.Y, MathF.Ceiling(bottom) + 1f);
        if (right <= left || bottom <= top) return false;
        bounds = new Rect2(left, top, right - left, bottom - top);
        return bounds.Size.X * bounds.Size.Y < viewport.Size.X * viewport.Size.Y;
    }

    private static bool IsDodge(MagmaDrawItem item) =>
        item.Kind == MagmaWidgetKind.Image && item.Blend is MagmaBlend.Dodge or MagmaBlend.Dodge2X or MagmaBlend.Dodge4X;

    /// <summary>An image's sampling (clamped or repeated); null for texts, which sit inside their font page either way.</summary>
    private static bool? ClampOf(MagmaDrawItem item) => item.Kind == MagmaWidgetKind.Image ? item.Clamp : null;

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
        if (item.NativeFont is { } native)
        {
            // Native runs are measured and truncated by their owner. Keeping them in the same command stream
            // lets flash, sleep and other screen effects cover modern HUD text exactly like bitmap text.
            int size = Math.Max(1, Mathf.RoundToInt(item.HeightPx));
            float width = native.GetStringSize(item.Text, HorizontalAlignment.Left, -1f, size).X;
            float x = item.Align switch
            {
                MagmaAlign.Center => item.Rect.Position.X + (item.Rect.Size.X - width) * .5f,
                MagmaAlign.Right => item.Rect.End.X - width,
                _ => item.Rect.Position.X,
            };
            Vector2 baseline = new(x, item.Rect.Position.Y + native.GetAscent(size));
            if (item.Shadow.A > 0f)
                target.DrawString(native, baseline + item.ShadowOffsetPx, item.Text, HorizontalAlignment.Left, -1f, size, item.Shadow);
            target.DrawString(native, baseline, item.Text, HorizontalAlignment.Left, -1f, size, item.Color);
            return;
        }

        MagmaFont? font = Assets?.Font(item.Font);
        if (font is null)
        {
            return;
        }

        font.LoadPages(file => Assets!.LoadTexture(file));
        foreach (TextLine line in CachedLayout(font, item))
        {
            float x = item.Align switch
            {
                MagmaAlign.Center => item.Rect.Position.X + (item.Rect.Size.X - line.Width) * 0.5f,
                MagmaAlign.Right => item.Rect.End.X - line.Width,
                _ => item.Rect.Position.X,
            };
            if (item.Shadow.A > 0f)
            {
                DrawRun(target, font, line.Text, new Vector2(x, line.Top) + item.ShadowOffsetPx, item, item.Shadow);
            }

            DrawRun(target, font, line.Text, new Vector2(x, line.Top), item, item.Color);
        }
    }

    internal TextLine[] CachedLayout(MagmaFont font, MagmaDrawItem item)
    {
        var key = new TextLayoutKey(font, item.Text, item.Rect, item.HeightPx, item.XScale, item.TrackingPx);
        if (_textLayouts.TryGetValue(key, out TextLine[]? cached)) return cached;
        List<(string Line, float Top)> layout = Layout(font, item);
        var lines = new TextLine[layout.Count];
        for (int i = 0; i < lines.Length; i++)
        {
            (string line, float top) = layout[i];
            lines[i] = new TextLine(line, top, font.Measure(line, item.HeightPx, item.XScale, item.TrackingPx));
        }
        if (_textLayouts.Count == MaxTextLayouts) _textLayouts.Remove(_textLayoutOrder.Dequeue());
        _textLayouts.Add(key, lines);
        _textLayoutOrder.Enqueue(key);
        TextLayoutMisses++;
        return lines;
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
    private readonly MagmaDrawList _snapshot = new();

    public MagmaBatch()
    {
    }

    public MagmaBatch(MagmaCanvas owner)
    {
        _owner = owner;
    }

    public List<MagmaDrawItem> Items => _snapshot.Items;

    internal bool SetItems(IReadOnlyList<MagmaDrawItem> source, int start, int end)
    {
        bool changed = Items.Count != end - start;
        for (int i = start; !changed && i < end; i++)
            changed = !Items[i - start].SameDraw(source[i]);
        if (!changed) return false;

        _snapshot.Clear();
        for (int i = start; i < end; i++) _snapshot.Add(source[i].Kind).CopyFrom(source[i]);
        return true;
    }

    internal void ClearItems() => _snapshot.Clear();

    public override void _Draw() => _owner?.DrawItems(this, Items);
}
