using System.Collections.Generic;
using Godot;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Options;

namespace OpenScctGame.Hud;

public static class HudEditorTransform
{
    public static string Group(MagmaDrawItem item)
    {
        string path = item.Path;
        if (path is "Vie/poison/effet01" or "Vie/poison/effet02_dodge") return "PoisonOverlay";
        if (path.StartsWith("PCConnectedPlayers/", System.StringComparison.Ordinal) ||
            path.StartsWith("Extras/Board", System.StringComparison.Ordinal) ||
            path.StartsWith("Extras/Split", System.StringComparison.Ordinal) ||
            path.StartsWith("Extras/Ping", System.StringComparison.Ordinal) ||
            path.StartsWith("Extras/Rank", System.StringComparison.Ordinal)) return "Scoreboard";
        if (path.StartsWith("Console/", System.StringComparison.Ordinal)) return "Chat";
        int slash = path.IndexOf('/');
        return slash < 0 ? path : path[..slash];
    }
    public static Dictionary<string, Rect2> Bounds(IReadOnlyList<MagmaDrawItem> items)
    {
        var bounds = new Dictionary<string, Rect2>();
        foreach (var item in items)
        {
            string id = Group(item);
            if (!HudEditorSettings.Elements.ContainsKey(id)) continue;
            Rect2 rect = item.Rect;
            if (item.Kind != MagmaWidgetKind.Text)
            {
                if (item.Points.Length == 0) continue;
                rect = new Rect2(item.Points[0], Vector2.Zero);
                foreach (var p in item.Points) rect = rect.Expand(p);
            }
            bounds[id] = bounds.TryGetValue(id, out var prior) ? prior.Merge(rect) : rect;
        }
        return bounds;
    }
    public static Rect2 Transform(Rect2 rect, HudElementSettings e, Vector2 screen)
    {
        Vector2 size = rect.Size * e.Scale;
        Vector2 position = rect.Position + new Vector2(e.X, e.Y) * screen;
        // Preserve exact stock placement, but keep edited elements recoverable at smaller resolutions.
        if (e.X != 0 || e.Y != 0 || e.Scale != 1)
            position = position.Clamp(Vector2.Zero, (screen - size).Max(Vector2.Zero));
        return new Rect2(position, size);
    }

    public static void Apply(List<MagmaDrawItem> items, Dictionary<string, HudElementSettings> layout, Vector2 screen,
        IReadOnlyDictionary<string, Rect2>? layoutBounds = null)
    {
        if (layout.Count == 0) return;
        var bounds = Bounds(items);
        // Animated geometry must not redefine an element's scale pivot or edge clamp every frame.
        if (layoutBounds is not null)
            foreach (var entry in layoutBounds) bounds[entry.Key] = entry.Value;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i]; string id = Group(item);
            if (!layout.TryGetValue(id, out var e) || !bounds.TryGetValue(id, out var rect)) continue;
            if (!e.Visible) { items.RemoveAt(i); continue; }
            Vector2 origin = Transform(rect, e, screen).Position;
            Vector2 Move(Vector2 p) => origin + (p - rect.Position) * e.Scale;
            item.Rect = new Rect2(Move(item.Rect.Position), item.Rect.Size * e.Scale);
            item.HeightPx *= e.Scale; item.TrackingPx *= e.Scale; item.ShadowOffsetPx *= e.Scale;
            item.Color = Fade(item.Color, e.Opacity); item.Shadow = Fade(item.Shadow, e.Opacity);
            for (int p = 0; p < item.Points.Length; p++) item.Points[p] = Move(item.Points[p]);
            for (int c = 0; c < item.Colors.Length; c++)
            {
                // Additive Magma blends ignore alpha; scale their light contribution instead.
                var color = item.Colors[c];
                item.Colors[c] = item.Blend == MagmaBlend.Normal || item.Blend == MagmaBlend.Burn2X || item.Blend == MagmaBlend.Burn4X
                    ? Fade(color, e.Opacity) : new Color(color.R * e.Opacity, color.G * e.Opacity, color.B * e.Opacity, color.A);
            }
        }
    }
    /// <summary>Applies an element around a stable authored origin, without content-dependent clamping.</summary>
    public static void Apply(List<MagmaDrawItem> items, string element, HudElementSettings settings,
        Vector2 sourceOrigin, Vector2 targetOrigin)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (Group(item) != element) continue;
            if (!settings.Visible) { items.RemoveAt(i); continue; }
            Vector2 Move(Vector2 p) => targetOrigin + (p - sourceOrigin) * settings.Scale;
            item.Rect = new Rect2(Move(item.Rect.Position), item.Rect.Size * settings.Scale);
            item.HeightPx *= settings.Scale; item.TrackingPx *= settings.Scale; item.ShadowOffsetPx *= settings.Scale;
            item.Color = Fade(item.Color, settings.Opacity); item.Shadow = Fade(item.Shadow, settings.Opacity);
            for (int p = 0; p < item.Points.Length; p++) item.Points[p] = Move(item.Points[p]);
            for (int c = 0; c < item.Colors.Length; c++)
            {
                var color = item.Colors[c];
                item.Colors[c] = item.Blend == MagmaBlend.Normal || item.Blend == MagmaBlend.Burn2X || item.Blend == MagmaBlend.Burn4X
                    ? Fade(color, settings.Opacity) : new Color(color.R * settings.Opacity, color.G * settings.Opacity, color.B * settings.Opacity, color.A);
            }
        }
    }
    private static Color Fade(Color c, float opacity) => new(c.R, c.G, c.B, c.A * opacity);
}

