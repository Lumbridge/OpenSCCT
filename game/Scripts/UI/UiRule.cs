using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// A thin horizontal rule of the modern look: one pixel high, fading out at both ends, with a short bright segment and
/// two small ticks in the middle. Drawn across the control's width, centred on its height.
/// </summary>
public partial class UiRule : Control
{
    public override void _Draw()
    {
        float w = Size.X, y = Mathf.Floor(Size.Y * 0.5f);
        if (w < 8f)
        {
            return;
        }

        Color edge = UiArt.Accent with { A = 0f };
        Color mid = UiArt.Accent with { A = 0.28f };
        Vector2[] points = { new(0f, y), new(w * 0.5f, y), new(w * 0.5f, y + 1f), new(0f, y + 1f) };
        DrawPolygon(points, new[] { edge, mid, mid, edge });
        points = new Vector2[] { new(w * 0.5f, y), new(w, y), new(w, y + 1f), new(w * 0.5f, y + 1f) };
        DrawPolygon(points, new[] { mid, edge, edge, mid });
        Color bright = UiArt.Accent with { A = 0.8f };
        DrawRect(new Rect2(w * 0.5f - 40f, y - 1f, 80f, 2f), bright);
        DrawRect(new Rect2(w * 0.5f - 52f, y - 3f, 2f, 6f), bright);
        DrawRect(new Rect2(w * 0.5f + 50f, y - 3f, 2f, 6f), bright);
    }
}
