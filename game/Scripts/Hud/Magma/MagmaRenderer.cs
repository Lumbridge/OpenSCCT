using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Hud.Magma;

/// <summary>How a top-level instance maps page x to the screen (docs/HUD.md): by where its contents sit on the page.</summary>
public enum MagmaAnchor
{
    /// <summary>Contents left of the page centre: x * s.</summary>
    Left,

    /// <summary>Contents right of the page centre: W - (640 - x) * s.</summary>
    Right,

    /// <summary>Contents across the centre: W / 2 + (x - 320) * s.</summary>
    Center,

    /// <summary>The page stretched over the screen width: x * W / 640.</summary>
    Stretch,
}

/// <summary>One thing to draw, in screen pixels. Images are polygons (quads, or pieces left by a mask); texts are runs.</summary>
public sealed class MagmaDrawItem
{
    /// <summary>Instance path and widget name, for example <c>Chrono/Chrono</c> or <c>Vie/jaugeinterne</c>.</summary>
    public string Path { get; set; } = "";

    public MagmaWidgetKind Kind { get; set; }

    public MagmaAnchor Anchor { get; set; }

    /// <summary>The widget's rect in page units after its area origins (before the anchor and the screen scale).</summary>
    public Rect4 PageRect { get; set; }

    // Images
    public string Package { get; set; } = "";

    public string Texture { get; set; } = "";

    public Vector2[] Points { get; set; } = [];

    /// <summary>Texture coordinates normalised to the texture (0..1).</summary>
    public Vector2[] Uvs { get; set; } = [];

    /// <summary>Per-vertex modulate: for Normal and Burn the colour the texture is multiplied by; for Dodge the
    /// vertex colour vc that the dodge shader turns into S = saturate(2 tex vc).</summary>
    public Color[] Colors { get; set; } = [];

    public MagmaBlend Blend { get; set; }

    /// <summary>
    /// Sample the texture clamped to its edges instead of repeating it: set for images whose texture coordinates stay
    /// inside the texture (a nudge of a few hundredths aside), so the far edge does not wrap round to the near one.
    /// </summary>
    public bool Clamp { get; set; }

    // Texts
    public string Text { get; set; } = "";

    public string Font { get; set; } = "";

    public Rect2 Rect { get; set; }

    public float HeightPx { get; set; }

    public MagmaAlign Align { get; set; }

    public Color Color { get; set; }

    public Color Shadow { get; set; }

    public Vector2 ShadowOffsetPx { get; set; }

    public float XScale { get; set; } = 1f;

    public float TrackingPx { get; set; }

    /// <summary>Exact draw-command equality; never round moving/fading HUD values to suppress a redraw.</summary>
    internal bool SameDraw(MagmaDrawItem other) => Kind == other.Kind &&
        (Kind == MagmaWidgetKind.Text
            ? Text == other.Text && Font == other.Font && Rect == other.Rect && HeightPx == other.HeightPx &&
              Align == other.Align && Color == other.Color && Shadow == other.Shadow &&
              ShadowOffsetPx == other.ShadowOffsetPx && XScale == other.XScale && TrackingPx == other.TrackingPx
            : Package == other.Package && Texture == other.Texture && Blend == other.Blend && Clamp == other.Clamp &&
              Points.AsSpan().SequenceEqual(other.Points) && Uvs.AsSpan().SequenceEqual(other.Uvs) &&
              Colors.AsSpan().SequenceEqual(other.Colors));

    /// <summary>A canvas owns its own snapshot: producers may reuse their item and arrays before deferred _Draw.</summary>
    internal void CopyFrom(MagmaDrawItem source)
    {
        Path = source.Path;
        Kind = source.Kind;
        Anchor = source.Anchor;
        PageRect = source.PageRect;
        Package = source.Package;
        Texture = source.Texture;
        Blend = source.Blend;
        Clamp = source.Clamp;
        Text = source.Text;
        Font = source.Font;
        Rect = source.Rect;
        HeightPx = source.HeightPx;
        Align = source.Align;
        Color = source.Color;
        Shadow = source.Shadow;
        ShadowOffsetPx = source.ShadowOffsetPx;
        XScale = source.XScale;
        TrackingPx = source.TrackingPx;
        if (source.Kind == MagmaWidgetKind.Image) SetGeometry(source.Points, source.Uvs, source.Colors);
    }

    internal void SetGeometry(ReadOnlySpan<Vector2> points, ReadOnlySpan<Vector2> uvs, ReadOnlySpan<Color> colors)
    {
        if (Points.Length != points.Length) Points = new Vector2[points.Length];
        if (Uvs.Length != uvs.Length) Uvs = new Vector2[uvs.Length];
        if (Colors.Length != colors.Length) Colors = new Color[colors.Length];
        points.CopyTo(Points);
        uvs.CopyTo(Uvs);
        colors.CopyTo(Colors);
    }

    internal void Reset(MagmaWidgetKind kind)
    {
        Path = Package = Texture = Text = Font = "";
        Kind = kind;
        Anchor = default;
        PageRect = default;
        Blend = default;
        Clamp = false;
        Rect = default;
        HeightPx = TrackingPx = 0f;
        Align = default;
        Color = Shadow = default;
        ShadowOffsetPx = default;
        XScale = 1f;
        // Geometry storage remains available for reuse. Text never reads these arrays.
    }

    public override string ToString() => Kind == MagmaWidgetKind.Text ? $"{Path} '{Text}'" : $"{Path} {Texture}";
}

/// <summary>What one page draw produced, in draw order.</summary>
public sealed class MagmaDrawList
{
    private readonly List<MagmaDrawItem> _pool = new();
    private int _used;

    public List<MagmaDrawItem> Items { get; } = new();

    public void Clear()
    {
        Items.Clear();
        _used = 0;
    }

    /// <summary>Mutable until this list's next Clear. A consumer that retains commands must copy them.</summary>
    public MagmaDrawItem Add(MagmaWidgetKind kind)
    {
        if (_used == _pool.Count) _pool.Add(new MagmaDrawItem());
        MagmaDrawItem item = _pool[_used++];
        item.Reset(kind);
        Items.Add(item);
        return item;
    }

    /// <summary>The last item whose path is <paramref name="path"/> (ignoring case), or null.</summary>
    public MagmaDrawItem? Find(string path)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Items[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return Items[i];
            }
        }

        return null;
    }

    public string? TextOf(string path) => Find(path) is { Kind: MagmaWidgetKind.Text } item ? item.Text : null;

    /// <summary>Whether anything under <paramref name="pathPrefix"/> was drawn.</summary>
    public bool Any(string pathPrefix)
    {
        foreach (MagmaDrawItem item in Items)
        {
            if (item.Path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Rows a driver puts into a LIST widget (the interaction choice list).</summary>
public sealed class MagmaListRows
{
    public List<string> Rows { get; } = new();

    public int Selected { get; set; } = -1;

    public bool Focus { get; set; }

    /// <summary>Row pitch in page units (inferred: the item height).</summary>
    public float Pitch { get; set; } = 16f;

    /// <summary>Widgets of the row area the page's init hides (console button art).</summary>
    public List<string> HiddenWidgets { get; } = new();
}

/// <summary>
/// Turns a page instance tree into a <see cref="MagmaDrawList"/>: keyframes evaluated at each instance's frame,
/// overrides applied, state colours multiplied down the instance chain, images rotated about their rect centre, the
/// 640x480 page mapped to the screen with s = H / 480 and a per-instance anchor, and SETMASK / USEMASK applied by
/// clipping on the CPU (docs/HUD.md).
/// </summary>
public sealed class MagmaRenderer
{
    public const float PageWidth = 640f;
    public const float PageHeight = 480f;

    /// <summary>Page units from the 4:3 border within which a centre-anchored image edge still reaches the screen border.</summary>
    private const float PageEdgeSlack = 2f;

    /// <summary>How far texture coordinates may leave 0..1 and still sample clamped (the HUD's edge nudges are 0.01 to 0.02).</summary>
    private const float UvNudge = 0.05f;

    private readonly Dictionary<MagmaNode, Dictionary<string, MagmaListRows>> _lists = new();
    private readonly Dictionary<(MagmaNode, string), List<MagmaNode>> _rowNodes = new();
    private readonly List<Vector2[]> _maskGroup = new();
    private readonly List<Vector2[]> _maskPool = new();
    private readonly Vector2[] _points = new Vector2[4];
    private readonly Vector2[] _uvs = new Vector2[4];
    private readonly Color[] _colors = new Color[4];
    private readonly Dictionary<(MagmaNode Node, string Widget), string> _paths = new();
    private MagmaMask _mode;
    private bool _lastWasSet;

    /// <summary>Pixel size of a texture of a package, to normalise texel regions (null: use the region's own extent).</summary>
    public Func<string, string, Vector2I?>? TextureSize { get; set; }

    /// <summary>Width in pixels of a text in a font at a height with a tracking (for texts kept inside the screen).</summary>
    public Func<string, string, float, float, float>? MeasureText { get; set; }

    /// <summary>Every top-level instance stretched over the screen width (full-screen view pages).</summary>
    public bool StretchAll { get; set; }

    /// <summary>The key token pass every drawn text with a backslash goes through (KeyTokens.Expand), or null.</summary>
    public Func<string, string>? ExpandText { get; set; }

    public Vector2 Screen { get; private set; } = new(PageWidth, PageHeight);

    public float S { get; private set; } = 1f;

    /// <summary>The rows of LIST widget <paramref name="widget"/> in <paramref name="node"/>'s area.</summary>
    public MagmaListRows ListRows(MagmaNode node, string widget)
    {
        if (!_lists.TryGetValue(node, out Dictionary<string, MagmaListRows>? byName))
        {
            byName = new Dictionary<string, MagmaListRows>(StringComparer.OrdinalIgnoreCase);
            _lists[node] = byName;
        }

        if (!byName.TryGetValue(widget, out MagmaListRows? rows))
        {
            rows = new MagmaListRows();
            byName[widget] = rows;
        }

        return rows;
    }

    public void DrawPage(MagmaNode page, Vector2 screen, MagmaDrawList into)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(into);
        Screen = screen;
        S = screen.Y / PageHeight;
        _maskGroup.Clear();
        _mode = MagmaMask.None;
        _lastWasSet = false;
        if (!page.Visible)
        {
            return;
        }

        MagmaArea area = page.Area;
        float f = page.FrameF;
        for (int i = 0; i < area.Widgets.Count; i++)
        {
            MagmaWidget w = area.Widgets[i];
            if (page.IsWidgetHidden(i) || !MagmaEval.Evaluate(w, f, out MagmaKey k))
            {
                continue;
            }

            if (w.Kind == MagmaWidgetKind.Instance)
            {
                // Indexed: foreach over the read-only list boxed an enumerator per widget per frame.
                IReadOnlyList<MagmaNode> children = page.Children;
                for (int ci = 0; ci < children.Count; ci++)
                {
                    MagmaNode c = children[ci];
                    if (c.WidgetIndex != i || !c.Visible)
                    {
                        continue;
                    }

                    Vector2 p = c.PositionOverride ?? k.Pos;
                    MagmaAnchor anchor = StretchAll ? MagmaAnchor.Stretch : AnchorOf(c.ChildBounds(), p);
                    SetMode(w.Mask);
                    DrawNode(c, p, c.StateOverride ?? k.State, anchor, into);
                }
            }
            else
            {
                // Lists sit at their position; images and texts at their rect.
                Rect4 at = w.Kind == MagmaWidgetKind.List ? new Rect4(k.Pos.X, k.Pos.Y, k.Pos.X, k.Pos.Y) : k.Rect;
                MagmaAnchor anchor = StretchAll ? MagmaAnchor.Stretch : AnchorOf(at, Vector2.Zero);
                DrawWidget(page, w, k, Vector2.Zero, Colors4.White, anchor, into);
            }
        }
    }

    private static MagmaAnchor AnchorOf(Rect4? bounds, Vector2 origin)
    {
        if (bounds is not { } b)
        {
            return MagmaAnchor.Center;
        }

        float l = Math.Min(b.L, b.R) + origin.X, r = Math.Max(b.L, b.R) + origin.X;
        return r <= PageWidth / 2f ? MagmaAnchor.Left : l >= PageWidth / 2f ? MagmaAnchor.Right : MagmaAnchor.Center;
    }

    public Vector2 ToScreen(Vector2 p, MagmaAnchor anchor)
    {
        float x = anchor switch
        {
            MagmaAnchor.Right => Screen.X - (PageWidth - p.X) * S,
            MagmaAnchor.Center => Screen.X * 0.5f + (p.X - PageWidth / 2f) * S,
            MagmaAnchor.Stretch => p.X * Screen.X / PageWidth,
            _ => p.X * S,
        };
        return new Vector2(x, p.Y * S);
    }

    private void DrawNode(MagmaNode node, Vector2 origin, Color tint, MagmaAnchor anchor, MagmaDrawList into)
    {
        if (!node.Visible)
        {
            return;
        }

        MagmaMask saved = _mode;
        MagmaArea area = node.Area;
        float f = node.FrameF;
        for (int i = 0; i < area.Widgets.Count; i++)
        {
            MagmaWidget w = area.Widgets[i];
            if (node.IsWidgetHidden(i) || !MagmaEval.Evaluate(w, f, out MagmaKey k))
            {
                continue;
            }

            if (w.Kind == MagmaWidgetKind.Instance)
            {
                IReadOnlyList<MagmaNode> children = node.Children;
                for (int ci = 0; ci < children.Count; ci++)
                {
                    MagmaNode c = children[ci];
                    if (c.WidgetIndex == i)
                    {
                        SetMode(w.Mask);
                        MagmaMask before = _mode;
                        DrawNode(c, origin + (c.PositionOverride ?? k.Pos), tint * (c.StateOverride ?? k.State), anchor, into);
                        _mode = before;
                    }
                }

                continue;
            }

            DrawWidget(node, w, k, origin, tint, anchor, into);
        }

        _mode = saved;
    }

    private void SetMode(MagmaMask mask)
    {
        // A USEMASK request while no mask is active is ignored by the native mask stack: the widget draws unmasked.
        if (mask == MagmaMask.Use && _mode == MagmaMask.None)
        {
            return;
        }

        _mode = mask;
    }

    private void DrawWidget(MagmaNode node, MagmaWidget w, MagmaKey k, Vector2 origin, Color tint, MagmaAnchor anchor,
        MagmaDrawList into)
    {
        MagmaOverride? o = node.FindOverride(w.Name);
        Rect4 r = k.Rect;
        if (o?.Pos is { } pos)
        {
            r = r.Offset(pos - new Vector2(r.L, r.T));
        }

        if (o?.MoveTop is { } moveTop)
        {
            r = r.Offset(new Vector2(0f, moveTop - r.T));
        }

        if (o?.Top is { } top)
        {
            r = r with { T = top };
        }

        if (!_paths.TryGetValue((node, w.Name), out string? path))
        {
            path = node.Parent is null ? w.Name : node.Path + "/" + w.Name;
            _paths[(node, w.Name)] = path;
        }
        switch (w.Kind)
        {
            case MagmaWidgetKind.Text:
                _lastWasSet = false;
                SetMode(w.Mask);
                DrawText(path, w, k, o, r.Offset(origin), tint, anchor, into);
                return;
            case MagmaWidgetKind.List:
                _lastWasSet = false;
                DrawList(node, w, k, origin + k.Pos, tint, anchor, into);
                return;
            case MagmaWidgetKind.Image:
                DrawImage(node.Package, path, w, k, o, r, origin, tint, anchor, into);
                return;
            default:
                return;
        }
    }

    private void DrawText(string path, MagmaWidget w, MagmaKey k, MagmaOverride? o, Rect4 r, Color tint, MagmaAnchor anchor,
        MagmaDrawList into)
    {
        string text = o?.Text ?? w.Text;
        if (text.Length == 0)
        {
            return;
        }

        if (ExpandText is not null && text.Contains('\\', StringComparison.Ordinal))
        {
            text = ExpandText(text);
        }

        Vector2 p0 = ToScreen(new Vector2(r.L, r.T), anchor);
        Vector2 p1 = ToScreen(new Vector2(r.R, r.B), anchor);
        float xs = anchor == MagmaAnchor.Stretch ? Screen.X / PageWidth / S : 1f;
        float height = k.Height * S;
        MagmaAlign align = w.Align;
        if (w.AutoSized && MeasureText is not null)
        {
            // AUTOSIZED: the box shrinks to the text from its left edge (the spy's timer is placed at x 568 with a
            // 135 wide box; centred in it, the text would run past the page's right edge at 640).
            float width = MeasureText(w.Font, text, height * xs, k.Tracking * 0.001f * height * xs);
            float left = Math.Min(p0.X, p1.X);
            p0.X = left;
            p1.X = left + width;
            align = MagmaAlign.Left;
        }

        if (o?.KeepInside == true && MeasureText is not null)
        {
            float width = MeasureText(w.Font, text, height * xs, k.Tracking * 0.001f * height * xs);
            float left = Math.Min(p0.X, p1.X), right = Math.Max(p0.X, p1.X);
            float end = align switch
            {
                MagmaAlign.Center => (left + right + width) * 0.5f,
                MagmaAlign.Right => right,
                _ => left + width,
            };
            if (end > Screen.X)
            {
                p0.X -= end - Screen.X;
                p1.X -= end - Screen.X;
            }
        }

        MagmaDrawItem item = into.Add(MagmaWidgetKind.Text);
        item.Path = path;
        item.Anchor = anchor;
        item.PageRect = r;
        item.Text = text;
        item.Font = w.Font;
        item.Rect = new Rect2(Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y), Math.Abs(p1.X - p0.X), Math.Abs(p1.Y - p0.Y));
        item.HeightPx = height;
        item.Align = align;
        item.Color = Clamp01((o?.Color ?? k.TextColor) * k.State * tint);
        item.Shadow = Clamp01(k.Shadow * k.State * tint);
        // Magma's shadow offset counts y upwards: (1, -1) puts the shadow one unit right and one unit down.
        item.ShadowOffsetPx = new Vector2(k.ShadowOffset.X, -k.ShadowOffset.Y) * S;
        item.XScale = xs;
        item.TrackingPx = k.Tracking * 0.001f * height * xs;
    }

    private void DrawList(MagmaNode node, MagmaWidget w, MagmaKey k, Vector2 at, Color tint, MagmaAnchor anchor,
        MagmaDrawList into)
    {
        if (w.Area < 0 || !_lists.TryGetValue(node, out Dictionary<string, MagmaListRows>? byName) ||
            !byName.TryGetValue(w.Name, out MagmaListRows? rows))
        {
            return;
        }

        if (!_rowNodes.TryGetValue((node, w.Name), out List<MagmaNode>? pool))
        {
            pool = new List<MagmaNode>();
            _rowNodes[(node, w.Name)] = pool;
        }

        MagmaArea item = node.Package.Areas[w.Area];
        int textIndex = -1;
        for (int i = 0; i < item.Widgets.Count; i++)
        {
            if (item.Widgets[i].Kind == MagmaWidgetKind.Text)
            {
                textIndex = i;
                break;
            }
        }

        int count = w.Rows > 0 ? Math.Min(rows.Rows.Count, w.Rows) : rows.Rows.Count;
        int first = rows.Selected >= count ? rows.Selected - count + 1 : 0;
        for (int row = 0; row < count; row++)
        {
            while (pool.Count <= row)
            {
                pool.Add(new MagmaNode(node.Package, w.Area, node, w.Name + "#" + pool.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            MagmaNode rowNode = pool[row];
            int entry = first + row;
            bool selected = rows.Focus && entry == rows.Selected;
            rowNode.SetFrame(selected ? Timing(item, "selected", 30) : Timing(item, "enabled", 0));
            if (textIndex >= 0)
            {
                rowNode.Widget(item.Widgets[textIndex].Name).Text = rows.Rows[entry];
            }

            foreach (string hidden in rows.HiddenWidgets)
            {
                rowNode.Widget(hidden).Hidden = true;
            }

            DrawNode(rowNode, new Vector2(at.X, at.Y + rows.Pitch * row), tint * k.State, anchor, into);
        }
    }

    private static int Timing(MagmaArea area, string state, int fallback) =>
        area.Timings.TryGetValue(state, out int f) ? f : fallback;

    private void DrawImage(MagmaPackage package, string path, MagmaWidget w, MagmaKey k, MagmaOverride? o, Rect4 r, Vector2 origin,
        Color tint, MagmaAnchor anchor, MagmaDrawList into)
    {
        MagmaMaterial? material = w.Material >= 0 && w.Material < package.Materials.Count ? package.Materials[w.Material] : null;
        // Magma draws an image rect inclusive of its last unit (x2 - x1 + 1 wide; measured on the merc reticle).
        r = new Rect4(r.L, r.T, r.R + 1f, r.B + 1f);
        float rot = o?.Rotation ?? k.Rot;
        var c0 = new Vector2((r.L + r.R) * 0.5f, (r.T + r.B) * 0.5f);
        ReadOnlySpan<Vector2> corners = [new(r.L, r.T), new(r.R, r.T), new(r.R, r.B), new(r.L, r.B)];
        float rad = Mathf.DegToRad(rot);
        float cs = MathF.Cos(rad), sn = MathF.Sin(rad);
        Vector2[] points = _points;
        for (int c = 0; c < 4; c++)
        {
            Vector2 d = corners[c] - c0;
            Vector2 p = c0 + new Vector2(d.X * cs - d.Y * sn, d.X * sn + d.Y * cs) + origin;
            points[c] = ToScreen(p, anchor);
            // Centre-anchored areas on a wide screen: edges at or past the 4:3 page border reach the screen border.
            // Full-frame layers are often placed a unit or two in (the gas mask's tint starts at x 1): they count too.
            if (anchor == MagmaAnchor.Center && rot == 0f)
            {
                if (p.X <= PageEdgeSlack)
                {
                    points[c].X = Math.Min(points[c].X, 0f);
                }

                if (p.X >= PageWidth - PageEdgeSlack)
                {
                    points[c].X = Math.Max(points[c].X, Screen.X);
                }
            }
        }

        if (w.Mask == MagmaMask.Set)
        {
            // Noise occluders (the perturbation dissolve) cut per pixel in the original; they are not reproduced.
            if (material is not null && !material.Texture.StartsWith("perturbe", StringComparison.OrdinalIgnoreCase))
            {
                if (!_lastWasSet)
                {
                    _maskGroup.Clear();
                }

                if (_maskGroup.Count == _maskPool.Count) _maskPool.Add(new Vector2[4]);
                Vector2[] mask = _maskPool[_maskGroup.Count];
                points.CopyTo(mask, 0);
                _maskGroup.Add(mask);
            }

            _lastWasSet = true;
            _mode = MagmaMask.Set;
            return;
        }

        _lastWasSet = false;
        SetMode(w.Mask);
        if (material is null)
        {
            return;
        }

        Rect4 region = o?.Region ?? material.Region;
        Vector2I size = TextureSize?.Invoke(package.Name, material.Texture) ??
            new Vector2I((int)Math.Max(1f, Math.Max(region.L, region.R)), (int)Math.Max(1f, Math.Max(region.T, region.B)));
        ReadOnlySpan<Vector2> st = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
        Vector2[] uvs = _uvs;
        Color[] colors = _colors;
        bool clamp = true;
        float scale = w.Blend switch
        {
            MagmaBlend.Dodge2X or MagmaBlend.Burn2X => 1f,
            MagmaBlend.Dodge4X or MagmaBlend.Burn4X => 2f,
            _ => 0.5f,
        };
        bool dodge = w.Blend is MagmaBlend.Dodge or MagmaBlend.Dodge2X or MagmaBlend.Dodge4X;
        for (int c = 0; c < 4; c++)
        {
            float su = (k.Tiling.X >= 0f ? st[c].X * k.Tiling.X : 1f + st[c].X * k.Tiling.X) + k.Offset.X;
            float sv = (k.Tiling.Y >= 0f ? st[c].Y * k.Tiling.Y : 1f + st[c].Y * k.Tiling.Y) + k.Offset.Y;
            // Tiled or scrolled images (shock edges, noise) need the repeat; the rest (the gas mask's quarters,
            // offset 0.01) would wrap their far edge round to the opposite one and show it as a strip.
            clamp &= su >= -UvNudge && su <= 1f + UvNudge && sv >= -UvNudge && sv <= 1f + UvNudge;
            uvs[c] = new Vector2((region.L + su * (region.R - region.L)) / Math.Max(1, size.X),
                (region.T + sv * (region.B - region.T)) / Math.Max(1, size.Y));
            Color keyed = o?.Color ?? k.Colors[c];
            if (o?.AlphaGain is { } gain)
            {
                keyed.A = Math.Min(1f, keyed.A * gain);
            }

            Color col = Clamp01(keyed * tint);
            // vc = min(255, trunc(C x scale)) per channel, alpha unscaled; the stage doubles it (MODULATE2X).
            var vc = new Color(Math.Min(1f, col.R * scale), Math.Min(1f, col.G * scale), Math.Min(1f, col.B * scale), col.A);
            colors[c] = dodge ? new Color(vc.R, vc.G, vc.B, 1f) : new Color(vc.R * 2f, vc.G * 2f, vc.B * 2f, vc.A);
        }

        var pageRect = new Rect4(r.L + origin.X, r.T + origin.Y, r.R + origin.X, r.B + origin.Y);
        if (_mode == MagmaMask.Use && _maskGroup.Count > 0)
        {
            foreach ((Vector2[] p, Vector2[] uv, Color[] col) in Clip(points, uvs, colors))
            {
                Add(into, path, anchor, pageRect, package.Name, material.Texture, p, uv, col, w.Blend, clamp);
            }

            return;
        }

        Add(into, path, anchor, pageRect, package.Name, material.Texture, points, uvs, colors, w.Blend, clamp);
    }

    private static void Add(MagmaDrawList into, string path, MagmaAnchor anchor, Rect4 pageRect, string package, string texture,
        Vector2[] points, Vector2[] uvs, Color[] colors, MagmaBlend blend, bool clamp)
    {
        MagmaDrawItem item = into.Add(MagmaWidgetKind.Image);
        item.Path = path;
        item.Anchor = anchor;
        item.PageRect = pageRect;
        item.Package = package;
        item.Texture = texture;
        item.SetGeometry(points, uvs, colors);
        item.Blend = blend;
        item.Clamp = clamp;
    }

    private static Color Clamp01(Color c) =>
        new(Math.Clamp(c.R, 0f, 1f), Math.Clamp(c.G, 0f, 1f), Math.Clamp(c.B, 0f, 1f), Math.Clamp(c.A, 0f, 1f));

    /// <summary>The parts of a convex polygon outside every quad of the current mask group.</summary>
    private List<(Vector2[], Vector2[], Color[])> Clip(Vector2[] points, Vector2[] uvs, Color[] colors)
    {
        var pieces = new List<List<Vert>> { Verts(points, uvs, colors) };
        foreach (Vector2[] m in _maskGroup)
        {
            float area2 = 0f;
            for (int i = 0; i < m.Length; i++)
            {
                Vector2 a = m[i], b = m[(i + 1) % m.Length];
                area2 += a.X * b.Y - b.X * a.Y;
            }

            if (Math.Abs(area2) < 1e-3f)
            {
                continue;
            }

            float sign = area2 > 0f ? 1f : -1f;
            var next = new List<List<Vert>>();
            foreach (List<Vert> piece in pieces)
            {
                List<Vert> rest = piece;
                for (int e = 0; e < m.Length && rest.Count >= 3; e++)
                {
                    SplitByLine(rest, m[e], m[(e + 1) % m.Length], sign, out List<Vert> inside, out List<Vert> outside);
                    if (outside.Count >= 3)
                    {
                        next.Add(outside);
                    }

                    rest = inside;
                }
            }

            pieces = next;
        }

        var result = new List<(Vector2[], Vector2[], Color[])>();
        foreach (List<Vert> piece in pieces)
        {
            var p = new Vector2[piece.Count];
            var uv = new Vector2[piece.Count];
            var col = new Color[piece.Count];
            for (int i = 0; i < piece.Count; i++)
            {
                (p[i], uv[i], col[i]) = (piece[i].P, piece[i].Uv, piece[i].C);
            }

            result.Add((p, uv, col));
        }

        return result;
    }

    private readonly record struct Vert(Vector2 P, Vector2 Uv, Color C);

    private static List<Vert> Verts(Vector2[] p, Vector2[] uv, Color[] c)
    {
        var list = new List<Vert>(p.Length);
        for (int i = 0; i < p.Length; i++)
        {
            list.Add(new Vert(p[i], uv[i], c[i]));
        }

        return list;
    }

    private static void SplitByLine(List<Vert> poly, Vector2 a, Vector2 b, float sign, out List<Vert> inside, out List<Vert> outside)
    {
        inside = new List<Vert>();
        outside = new List<Vert>();
        Vector2 e = b - a;
        float Side(Vector2 p)
        {
            Vector2 q = p - a;
            return (e.X * q.Y - e.Y * q.X) * sign;
        }

        for (int i = 0; i < poly.Count; i++)
        {
            Vert cur = poly[i], nxt = poly[(i + 1) % poly.Count];
            float sc = Side(cur.P), sn = Side(nxt.P);
            if (sc >= 0f)
            {
                inside.Add(cur);
            }
            else
            {
                outside.Add(cur);
            }

            if (sc >= 0f != sn >= 0f)
            {
                float t = sc / (sc - sn);
                var x = new Vert(cur.P.Lerp(nxt.P, t), cur.Uv.Lerp(nxt.Uv, t), cur.C.Lerp(nxt.C, t));
                inside.Add(x);
                outside.Add(x);
            }
        }
    }
}
