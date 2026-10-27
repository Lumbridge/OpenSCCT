using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// The modern look's frame (<see cref="UiLook.Modern"/>): a box drawn with lines instead of a texture, so it stays sharp
/// at any size. A dark fill, a thin border with two cut corners (top left and bottom right), brighter corner ticks on
/// the square corners, optional faint scanlines inside and an optional accent bar on the left. With
/// <see cref="Expand"/> it draws around the control (the focus outline).
/// </summary>
public partial class UiBox : StyleBox
{
    [Export] public Color Fill { get; set; } = new(0f, 0f, 0f, 0f);

    [Export] public Color Border { get; set; } = new(0f, 0f, 0f, 0f);

    [Export] public Color Ticks { get; set; } = new(0f, 0f, 0f, 0f);

    [Export] public Color Bar { get; set; } = new(0f, 0f, 0f, 0f);

    /// <summary>The scanlines' colour (alpha 0 for none).</summary>
    [Export] public Color Scan { get; set; } = new(0f, 0f, 0f, 0f);

    [Export] public float BorderWidth { get; set; } = 1f;

    /// <summary>The size of the two cut corners (0 for a plain rectangle).</summary>
    [Export] public float Notch { get; set; }

    [Export] public float TickLength { get; set; } = 8f;

    [Export] public float TickWidth { get; set; } = 2f;

    [Export] public float BarWidth { get; set; } = 3f;

    /// <summary>How far the box draws outside the control's rectangle.</summary>
    [Export] public float Expand { get; set; }

    /// <summary>The scanlines' spacing in pixels.</summary>
    [Export] public float ScanSpacing { get; set; } = 3f;

    public override void _Draw(Rid canvasItem, Rect2 rect)
    {
        Rect2 r = rect.Grow(Expand);
        if (r.Size.X < 2f || r.Size.Y < 2f)
        {
            return;
        }

        float n = Math.Min(Notch, Math.Min(r.Size.X, r.Size.Y) * 0.4f);
        float l = r.Position.X, t = r.Position.Y, rr = r.End.X, b = r.End.Y;
        Vector2[] outline =
        {
            new(l + n, t), new(rr, t), new(rr, b - n), new(rr - n, b), new(l, b), new(l, t + n),
        };

        if (Fill.A > 0f)
        {
            RenderingServer.CanvasItemAddPolygon(canvasItem, outline, new[] { Fill });
        }

        if (Scan.A > 0f && ScanSpacing >= 1f)
        {
            for (float y = t + 2f; y < b - 1f; y += ScanSpacing)
            {
                float x0 = l + Math.Max(0f, n - (y - t)) + 1f;
                float x1 = rr - Math.Max(0f, n - (b - y)) - 1f;
                if (x1 > x0)
                {
                    RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(x0, y, x1 - x0, 1f), Scan);
                }
            }
        }

        if (Bar.A > 0f && BarWidth > 0f)
        {
            float inset = BorderWidth + 2f;
            float top = t + Math.Max(inset, n * 0.6f + 2f);
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(l + inset, top, BarWidth, Math.Max(0f, b - inset - top)), Bar);
        }

        if (Border.A > 0f && BorderWidth > 0f)
        {
            float w = BorderWidth;
            // Straight edges as rectangles (crisp on the pixel grid), the cut corners as smoothed lines.
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(l + n, t, rr - l - n, w), Border);
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(rr - w, t + w, w, b - n - t - w), Border);
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(l, b - w, rr - l - n, w), Border);
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(l, t + n, w, b - t - n - w), Border);
            if (n > 0f)
            {
                float h = w * 0.5f;
                RenderingServer.CanvasItemAddLine(canvasItem, new Vector2(l + h, t + n), new Vector2(l + n, t + h), Border, w, true);
                RenderingServer.CanvasItemAddLine(canvasItem, new Vector2(rr - h, b - n), new Vector2(rr - n, b - h), Border, w, true);
            }
        }

        if (Ticks.A > 0f && TickLength > 0f)
        {
            float k = Math.Min(TickLength, Math.Min(r.Size.X, r.Size.Y) * 0.45f);
            float tw = TickWidth;
            // Top right and bottom left: the square corners get an L.
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(rr - k, t, k, tw), Ticks);
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(rr - tw, t, tw, k), Ticks);
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(l, b - tw, k, tw), Ticks);
            RenderingServer.CanvasItemAddRect(canvasItem, new Rect2(l, b - k, tw, k), Ticks);
            if (n > 0f)
            {
                // The cut corners get a short bright stroke along the cut.
                float h = tw * 0.5f;
                RenderingServer.CanvasItemAddLine(canvasItem, new Vector2(l + h, t + n), new Vector2(l + n, t + h), Ticks, tw, true);
                RenderingServer.CanvasItemAddLine(canvasItem, new Vector2(rr - h, b - n), new Vector2(rr - n, b - h), Ticks, tw, true);
            }
        }
    }
}
