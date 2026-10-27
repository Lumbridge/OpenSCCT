using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// The OpenSCCT wordmark of the modern look: the mark of game/branding (a ring with a scan gap, a centre dot and four
/// aiming ticks, as tools/branding/make_branding.py draws it) followed by "OPEN" light and "SCCT" bold, drawn with
/// lines and the bundled font so it stays sharp at any size.
/// </summary>
public partial class UiWordmark : Control
{
    private const string Light = "OPEN";
    private const string Bold = "SCCT";

    public UiWordmark()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
    }

    /// <summary>The height of the mark and the letters' size, in pixels.</summary>
    public float MarkSize { get; set; } = 56f;

    public override Vector2 _GetMinimumSize()
    {
        float gap = MarkSize * 0.3f;
        return new Vector2(MarkSize + gap + TextWidth(), MarkSize + 4f);
    }

    public override void _Draw()
    {
        float h = MarkSize;
        float total = _GetMinimumSize().X;
        float x = Math.Max(0f, (Size.X - total) * 0.5f);
        float y = Math.Max(0f, (Size.Y - h) * 0.5f);
        DrawMark(this, new Vector2(x + h * 0.5f, y + h * 0.5f), h, UiArt.Accent, UiArt.AccentDim);

        int size = FontSize();
        float baseline = y + h * 0.5f + size * 0.36f;
        float tx = x + h * 1.3f;
        if (UiArt.WordLightFont is { } light)
        {
            DrawString(light, new Vector2(tx, baseline), Light, HorizontalAlignment.Left, -1f, size, UiArt.Text);
            tx += light.GetStringSize(Light, HorizontalAlignment.Left, -1f, size).X + size * 0.12f;
        }

        if (UiArt.WordBoldFont is { } bold)
        {
            DrawString(bold, new Vector2(tx, baseline), Bold, HorizontalAlignment.Left, -1f, size, UiArt.Accent);
        }
    }

    /// <summary>
    /// Draws the mark centred on <paramref name="centre"/> in a square of <paramref name="size"/> (the proportions of
    /// make_branding.py's draw_mark).
    /// </summary>
    public static void DrawMark(CanvasItem canvas, Vector2 centre, float size, Color ring, Color ticks)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        float rOut = size * 0.34f, rIn = size * 0.25f;
        float r = (rOut + rIn) * 0.5f, w = rOut - rIn;
        float gap = size * 0.035f;
        float a = MathF.Asin(Math.Clamp((gap + w * 0.5f) / r, 0f, 0.9f));
        canvas.DrawArc(centre, r, a, MathF.PI - a, 48, ring, w, true);
        canvas.DrawArc(centre, r, MathF.PI + a, MathF.Tau - a, 48, ring, w, true);
        canvas.DrawCircle(centre, size * 0.045f, ring, true, -1f, true);
        float o = rOut + size * 0.03f, len = size * 0.07f, tw = size * 0.022f;
        canvas.DrawRect(new Rect2(centre.X - tw, centre.Y - o - len, 2f * tw, len), ticks);
        canvas.DrawRect(new Rect2(centre.X - tw, centre.Y + o, 2f * tw, len), ticks);
        canvas.DrawRect(new Rect2(centre.X - o - len, centre.Y - tw, len, 2f * tw), ticks);
        canvas.DrawRect(new Rect2(centre.X + o, centre.Y - tw, len, 2f * tw), ticks);
    }

    private int FontSize() => Math.Max(8, (int)MathF.Round(MarkSize * 0.62f));

    private float TextWidth()
    {
        int size = FontSize();
        float width = 0f;
        if (UiArt.WordLightFont is { } light)
        {
            width += light.GetStringSize(Light, HorizontalAlignment.Left, -1f, size).X + size * 0.12f;
        }

        if (UiArt.WordBoldFont is { } bold)
        {
            width += bold.GetStringSize(Bold, HorizontalAlignment.Left, -1f, size).X;
        }

        return width;
    }
}
