using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Options;

namespace OpenScctGame.Hud;

/// <summary>One solid quad of the custom crosshair, in screen pixels (clockwise from the top left).</summary>
public readonly record struct CrosshairQuad(Vector2 A, Vector2 B, Vector2 C, Vector2 D, Color Colour);

/// <summary>
/// The custom crosshair's geometry (<see cref="CrosshairSettings"/>, CS2's styles): the quads to draw around a centre,
/// outlines first. Sizes are pixels at the settings' screen height, scaled to the real one and snapped to whole pixels
/// so thin lines stay sharp. The HUD and the options preview both draw from it.
/// </summary>
public static class CrosshairShape
{
    private const int CircleSegments = 40;

    /// <summary>The colour the original reticle turns for 0.4 s after a hit.</summary>
    public static readonly Color HitColour = new(1f, 0f, 0f, 1f);

    /// <param name="c">The settings.</param>
    /// <param name="centre">The crosshair's centre in screen pixels.</param>
    /// <param name="screenHeight">The screen's height in pixels.</param>
    /// <param name="spreadPx">How far the weapon's spread opens a dynamic style, in screen pixels.</param>
    /// <param name="hit">Draw in the hit colour (when the settings keep the hit flash).</param>
    public static List<CrosshairQuad> Build(CrosshairSettings c, Vector2 centre, float screenHeight, float spreadPx, bool hit)
    {
        ArgumentNullException.ThrowIfNull(c);
        var fills = new List<(Rect2 R, float A)>();
        var rings = new List<(float Inner, float Outer, float A)>();
        float k = screenHeight / Math.Max(240, c.ScreenHeight);
        int t = Math.Max(1, Px(c.Thickness * k));
        int len = Math.Max(0, Px(c.Length * k));
        int gap = Px(c.Gap * k);
        float spread = c.Style switch
        {
            0 or 1 or 2 or 7 => Math.Clamp(spreadPx, 0f, c.DynamicSpreadLimit * k),
            // Shot feedback: a short kick of the static cross on each shot, not the whole spread.
            5 => Math.Clamp(spreadPx, 0f, 4f * k),
            _ => 0f,
        };
        int s = Px(spread);
        int half = t / 2;
        var o = new Vector2(Mathf.Round(centre.X), Mathf.Round(centre.Y));

        void Arms(int offset, int length, float alpha)
        {
            if (length <= 0)
            {
                return;
            }

            int near = t - half + offset;
            fills.Add((new Rect2(o.X + near, o.Y - half, length, t), alpha));
            fills.Add((new Rect2(o.X - half - offset - length, o.Y - half, length, t), alpha));
            fills.Add((new Rect2(o.X - half, o.Y + near, t, length), alpha));
            if (!c.TStyle)
            {
                fills.Add((new Rect2(o.X - half, o.Y - half - offset - length, t, length), alpha));
            }
        }

        switch (c.Style)
        {
            case 0 or 4 or 5:
                Arms(gap + s, len, 1f);
                break;
            case 2:
            {
                // CS:GO's split cross: the inner pieces follow the spread, the outer ones stop at the split distance.
                int outer = Px(len * c.SplitSizeRatio);
                int inner = len - outer;
                Arms(gap + s, inner, c.InnerSplitAlpha);
                Arms(gap + Math.Min(s, Px(c.SplitDistance * k)) + inner, outer, c.OuterSplitAlpha);
                break;
            }
            case 1 or 3:
            {
                float r = Math.Max(t, gap + s + len);
                rings.Add((r - t / 2f, r + t / 2f, 1f));
                break;
            }
            case 8:
            {
                int h = Math.Max(t, gap + len);
                fills.Add((new Rect2(o.X - h, o.Y - h, 2 * h, t), 1f));
                fills.Add((new Rect2(o.X - h, o.Y + h - t, 2 * h, t), 1f));
                fills.Add((new Rect2(o.X - h, o.Y - h + t, t, 2 * h - 2 * t), 1f));
                fills.Add((new Rect2(o.X + h - t, o.Y - h + t, t, 2 * h - 2 * t), 1f));
                break;
            }
            case 7 or 9:
            {
                // Four corner brackets.
                int h = Math.Max(t, gap + s + len);
                int q = Math.Max(t, Px(len * 0.6f));
                foreach ((int sx, int sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    float x = sx < 0 ? o.X - h : o.X + h - q;
                    float y = sy < 0 ? o.Y - h : o.Y + h - t;
                    fills.Add((new Rect2(x, y, q, t), 1f));
                    float vx = sx < 0 ? o.X - h : o.X + h - t;
                    float vy = sy < 0 ? o.Y - h + t : o.Y + h - q;
                    fills.Add((new Rect2(vx, vy, t, q - t), 1f));
                }

                break;
            }
        }

        if (c.CenterDot || c.Style == 6)
        {
            int d = c.Style == 6 ? Math.Max(2, t) : t;
            int dh = d / 2;
            fills.Add((new Rect2(o.X - dh, o.Y - dh, d, d), 1f));
        }

        Color fill = hit && c.HitFlash ? HitColour with { A = c.Alpha / 255f } : Rgba(c.Red, c.Green, c.Blue, c.Alpha);
        Color line = Rgba(c.OutlineRed, c.OutlineGreen, c.OutlineBlue, c.OutlineAlpha);
        int w = Math.Max(1, Px(k));
        var quads = new List<CrosshairQuad>(fills.Count * 2 + rings.Count * CircleSegments * 2);
        if (c.OutlineMode != 0)
        {
            foreach ((Rect2 r, float a) in fills)
            {
                Rect2 back = c.OutlineMode == 1 ? r.Grow(w) : new Rect2(r.Position + new Vector2(w, w), r.Size);
                quads.Add(Quad(back, line with { A = line.A * a }));
            }

            foreach ((float inner, float outer, float a) in rings)
            {
                Vector2 shift = c.OutlineMode == 1 ? Vector2.Zero : new Vector2(w, w);
                float grow = c.OutlineMode == 1 ? w : 0f;
                Ring(quads, o + shift, Math.Max(0f, inner - grow), outer + grow, line with { A = line.A * a });
            }
        }

        foreach ((Rect2 r, float a) in fills)
        {
            quads.Add(Quad(r, fill with { A = fill.A * a }));
        }

        foreach ((float inner, float outer, float a) in rings)
        {
            Ring(quads, o, inner, outer, fill with { A = fill.A * a });
        }

        return quads;
    }

    private static int Px(float v) => (int)MathF.Round(v);

    private static Color Rgba(int r, int g, int b, int a) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    private static CrosshairQuad Quad(Rect2 r, Color colour) =>
        new(r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), colour);

    private static void Ring(List<CrosshairQuad> into, Vector2 centre, float inner, float outer, Color colour)
    {
        for (int i = 0; i < CircleSegments; i++)
        {
            float a0 = Mathf.Tau * i / CircleSegments;
            float a1 = Mathf.Tau * (i + 1) / CircleSegments;
            var d0 = new Vector2(MathF.Cos(a0), MathF.Sin(a0));
            var d1 = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
            into.Add(new CrosshairQuad(centre + d0 * inner, centre + d0 * outer, centre + d1 * outer, centre + d1 * inner, colour));
        }
    }
}
