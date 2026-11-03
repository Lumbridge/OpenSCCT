using System;
using Godot;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Options;

namespace OpenScctGame.Hud;

/// <summary>Shared placement, opacity and graph geometry for the independent telemetry overlays.</summary>
public static class HudTelemetryVisual
{
    /// <summary>A value snapshot, so mutating the editor's shared settings invalidates retained commands.</summary>
    public readonly record struct EditorKey(bool Present, float X, float Y, float Scale, float Opacity, bool Visible, bool Preview)
    {
        public static EditorKey Of(HudElementSettings? layout, bool preview) => layout is null
            ? new(false, 0f, 0f, 1f, 1f, true, preview)
            : new(true, layout.X, layout.Y, layout.Scale, layout.Opacity, layout.Visible, preview);
    }

    /// <summary>Reserves the normal two-row FPS panel, independently of current samples, graphs or visibility.</summary>
    public static float NetworkEditorTop(Vector2 screen)
    {
        float h = Mathf.Clamp(Mathf.Round(HudK.FpsTextHeight * screen.Y / MagmaRenderer.PageHeight), 11f, 28f);
        return 12f + 2f * Mathf.Round(h * 0.4f) + 2f * (h + Mathf.Round(h * 0.2f));
    }

    /// <summary>Builds final commands from fresh local geometry, applying legacy appearance and editor changes once.</summary>
    public static Rect2 PlaceEdited(MagmaDrawList list, Vector2 screen, Rect2 source, Rect2 native,
        float legacyOpacity, HudElementSettings? layout, Vector2? referenceSize = null)
    {
        Rect2 target = layout is null ? native : HudEditorTransform.Transform(native, layout, screen);
        if (layout?.Visible == false) list.Clear();
        else
        {
            // A reserved footprint anchors dynamic readouts without stretching their current rows to fill it.
            Vector2 reference = referenceSize ?? source.Size;
            Vector2 scale = target.Size / reference.Max(new Vector2(0.001f, 0.001f));
            Transform(list, source, new Rect2(target.Position, source.Size * scale), legacyOpacity * (layout?.Opacity ?? 1f));
        }
        return target;
    }

    /// <summary>Positions are percentages of the available travel, keeping every size on screen.</summary>
    public static Rect2 Place(Vector2 screen, Vector2 baseSize, TelemetrySettings settings, float defaultTop = 8f)
    {
        float margin = Math.Min(8f, Math.Max(0f, Math.Min(screen.X, screen.Y) / 4f));
        Vector2 available = new(Math.Max(1f, screen.X - margin * 2f), Math.Max(1f, screen.Y - margin * 2f));
        float scale = Math.Clamp(Finite(settings.Scale, 100f), 50f, 200f) / 100f;
        scale = Math.Min(scale, Math.Min(available.X / Math.Max(1f, baseSize.X), available.Y / Math.Max(1f, baseSize.Y)));
        Vector2 size = baseSize * scale;
        float x = margin + Math.Max(0f, available.X - size.X) * Math.Clamp(Finite(settings.PositionX, 100f), 0f, 100f) / 100f;
        float top = settings.PositionX == 100f && settings.PositionY == 0f ? Math.Max(margin, Finite(defaultTop, margin)) : margin;
        top = Math.Min(top, Math.Max(margin, screen.Y - margin - size.Y));
        float y = top + Math.Max(0f, screen.Y - margin - size.Y - top) * Math.Clamp(Finite(settings.PositionY, 0f), 0f, 100f) / 100f;
        return new Rect2(new Vector2(x, y), size);
    }

    /// <summary>Transforms the whole element, including its panel, graph, text and shadows exactly once.</summary>
    public static void Transform(MagmaDrawList list, Rect2 source, Rect2 target, float opacityPercent)
    {
        Vector2 scale = new(target.Size.X / Math.Max(0.001f, source.Size.X), target.Size.Y / Math.Max(0.001f, source.Size.Y));
        float alpha = Math.Clamp(Finite(opacityPercent, 100f), 0f, 100f) / 100f;
        foreach (MagmaDrawItem item in list.Items)
        {
            if (item.Kind == MagmaWidgetKind.Text)
            {
                item.Rect = new Rect2(target.Position + (item.Rect.Position - source.Position) * scale, item.Rect.Size * scale);
                item.HeightPx *= scale.Y;
                item.XScale *= scale.X / Math.Max(0.001f, scale.Y);
                item.TrackingPx *= scale.X;
                item.ShadowOffsetPx *= scale;
                item.Color = item.Color with { A = item.Color.A * alpha };
                item.Shadow = item.Shadow with { A = item.Shadow.A * alpha };
            }
            else
            {
                for (int i = 0; i < item.Points.Length; i++)
                    item.Points[i] = target.Position + (item.Points[i] - source.Position) * scale;
                for (int i = 0; i < item.Colors.Length; i++)
                    item.Colors[i] = item.Colors[i] with { A = item.Colors[i].A * alpha };
            }
        }
    }

    /// <summary>Oldest to newest samples, with values clipped to the graph's range. Uses pooled quads.</summary>
    public static void Graph(MagmaDrawList list, WhiteRegion white, string path, Rect2 rect,
        ReadOnlySpan<float> values, float maxValue, string style, Color color)
    {
        if (values.Length == 0 || style == "Off" || rect.Size.X <= 0f || rect.Size.Y <= 0f) return;
        float range = Math.Max(0.001f, Finite(maxValue, 1f));
        float halfStroke = Math.Min(0.65f, rect.Size.Y * 0.5f);
        float bottom = rect.End.Y - halfStroke;
        float height = Math.Max(0f, rect.Size.Y - halfStroke * 2f);
        float Y(float value) => bottom - Math.Clamp(Finite(value, 0f) / range, 0f, 1f) * height;
        if (style == "Bars")
        {
            float pitch = rect.Size.X / values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                float x = rect.Position.X + i * pitch;
                float y = Y(values[i]);
                Quad(list, white, path, new Vector2(x, y), new Vector2(x + Math.Max(pitch * 0.8f, Math.Min(0.5f, pitch)), y),
                    new Vector2(x + Math.Max(pitch * 0.8f, Math.Min(0.5f, pitch)), rect.End.Y), new Vector2(x, rect.End.Y), color);
            }
            return;
        }

        // Perpendicular thickness keeps steep frame-time spikes visible. Clip the stroke to the plot.
        Vector2 previous = new(rect.Position.X, Y(values[0]));
        for (int i = 1; i < values.Length; i++)
        {
            Vector2 next = new(rect.Position.X + rect.Size.X * i / (values.Length - 1), Y(values[i]));
            Vector2 direction = next - previous;
            Vector2 offset = new Vector2(-direction.Y, direction.X).Normalized() * halfStroke;
            Quad(list, white, path, Clip(previous - offset), Clip(next - offset), Clip(next + offset), Clip(previous + offset), color);
            previous = next;
        }
        Vector2 Clip(Vector2 point) => new(Math.Clamp(point.X, rect.Position.X, rect.End.X), Math.Clamp(point.Y, rect.Position.Y, rect.End.Y));
    }

    private static void Quad(MagmaDrawList list, WhiteRegion white, string path, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        MagmaDrawItem item = list.Add(MagmaWidgetKind.Image);
        item.Path = path;
        item.Package = white.Package;
        item.Texture = white.Texture;
        Span<Vector2> points = stackalloc Vector2[] { a, b, c, d };
        Span<Vector2> uvs = stackalloc Vector2[] { white.Uv, white.Uv, white.Uv, white.Uv };
        Span<Color> colors = stackalloc Color[] { color, color, color, color };
        item.SetGeometry(points, uvs, colors);
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
}
