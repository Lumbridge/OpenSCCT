using OpenScct.Provenance;

namespace OpenScctGame.Hud;

/// <summary>
/// The 640 x 480 frame the motion tracker's boxes live in (spec/equipment/motion-tracking.md, "Screen box"), on the
/// screen: a centred area at the HUD's 4:3 scale (H / 480) on the laid-out picture (at most 16:9, stretched across a
/// wider screen: <see cref="HudLayout"/>), or stretched over the whole screen when the HUD is stretched (Reloaded's
/// widescreen off). Godot-free so it is unit tested.
/// </summary>
public readonly record struct MotionHudFrame(float ScaleX, float ScaleY, float OffsetX)
{
    public const float Width = 640f;
    public const float Height = 480f;
    public const float CentreX = 320f;

    /// <summary>The frame on a screen of <paramref name="width"/> x <paramref name="height"/> pixels.</summary>
    [Provenance(ProvenanceKind.Measured, "local/motion-capture HUD frames at 1280 x 720: zoom box x 160..1120, clean view and corners 1.5 px a unit")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded widescreen: picture laid out at most 16:9 and stretched wider (HudLayout); tracker frame centred at x 320 (Reloaded.Core 0x10361004)")]
    public static MotionHudFrame For(float width, float height, bool stretch)
    {
        float sy = HudLayout.PageScale(height);
        if (stretch)
        {
            return new MotionHudFrame(width / Width, sy, 0f);
        }

        float sx = sy * HudLayout.StretchX(width, height);
        return new MotionHudFrame(sx, sy, (width * 0.5f) - (CentreX * sx));
    }

    public (float X, float Y) ToScreen(float x, float y) => (OffsetX + (x * ScaleX), y * ScaleY);

    public (float X, float Y) FromScreen(float x, float y) => ((x - OffsetX) / ScaleX, y / ScaleY);
}
