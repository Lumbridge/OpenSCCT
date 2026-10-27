using Godot;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>Text draw items in the HUD's bitmap font for the parts the HUD draws outside the Magma pages.</summary>
public static class HudText
{
    /// <summary>The Magma tracking of the HUD texts (45 thousandths of the height after each glyph).</summary>
    public const float Tracking = 0.045f;

    /// <summary>
    /// A text item in screen pixels: <paramref name="rect"/> is the box (top aligned, the pen line one height below its
    /// top), <paramref name="heightPx"/> the glyph height. Heights are rounded to whole pixels so the glyphs keep a
    /// steady scale (the bitmap font stays crisp).
    /// </summary>
    public static MagmaDrawItem Item(string path, string text, Rect2 rect, float heightPx, MagmaAlign align, Color colour,
        Color? shadow = null, string font = HudAssets.DefaultFont)
    {
        float h = Mathf.Max(1f, Mathf.Round(heightPx));
        Color s = shadow ?? new Color(0f, 0f, 0f, colour.A);
        return new MagmaDrawItem
        {
            Path = path,
            Kind = MagmaWidgetKind.Text,
            Text = text,
            Font = font,
            Rect = new Rect2(Mathf.Round(rect.Position.X), Mathf.Round(rect.Position.Y), rect.Size),
            HeightPx = h,
            Align = align,
            Color = colour,
            Shadow = s,
            ShadowOffsetPx = s.A > 0f ? new Vector2(1f, 1f) : Vector2.Zero,
            TrackingPx = Tracking * h,
        };
    }
}
