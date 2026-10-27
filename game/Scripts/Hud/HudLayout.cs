using System;
using OpenScct.Provenance;

namespace OpenScctGame.Hud;

/// <summary>
/// Reloaded's widescreen rules for the picture and the HUD (spec/reloaded/FEATURES.md rl:gfx.widescreen and rl:gfx.hud,
/// spec/native/reloaded-features-from-code.md rules 20 and 21), Godot-free so they are unit tested.
/// <list type="bullet">
/// <item>The aspect the game lays the 3D view and the HUD out for is the screen's, capped at 16:9 (Reloaded.Core
/// 0x100a4ac0: the layout width is min(W, 16/9 H)). A wider screen shows that 16:9 picture stretched across it, 3D and
/// HUD alike (the projection patch 0x100a6260 builds the matrices for the capped width); the options call such
/// modes "(21:9 stretched)" and Reloaded's own notes say it supports up to 16:9 without stretching.</item>
/// <item>With the widescreen HUD on, the 640 x 480 page keeps the 4:3 scale (H / 480) and its virtual edges move out by
/// ceil((aspect / (4/3) - 1) x 320) page units on each side (0x100a4ac0: the left edge -d, the right edge 640 + d);
/// left-hand areas stay at the left edge, right-hand areas follow the right one.</item>
/// <item>The Enhanced Reality projection extents (HudScreenRight, HudScreenBottom; 0x10079880): 640 x 480 without the
/// widescreen settings; with them and ws_er_small, HudScreenRight = 320 + 240 x aspect (icons keep their 4:3 size);
/// without ws_er_small, HudScreenBottom = 240 + 320 / aspect (the 640 wide layout is kept and the icons grow).</item>
/// </list>
/// </summary>
public static class HudLayout
{
    public const float PageWidth = 640f;
    public const float PageHeight = 480f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a4ac0 and 0x100a6260: width capped at height x 1.7777777777777777")]
    public const float WidestAspect = 16f / 9f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a4ac0 and 0x100a7330: aspect / 1.3333334")]
    public const float ReferenceAspect = 4f / 3f;

    /// <summary>The aspect the picture and the HUD are laid out for: the screen's, at most 16:9 (with widescreen on).</summary>
    public static float LayoutAspect(float width, float height, bool widescreen = true)
    {
        if (!(width > 0f) || !(height > 0f))
        {
            return ReferenceAspect;
        }

        float aspect = width / height;
        return widescreen ? MathF.Min(aspect, WidestAspect) : aspect;
    }

    /// <summary>The width the picture is laid out over: min(W, 16/9 H) with widescreen on.</summary>
    public static float LayoutWidth(float width, float height, bool widescreen = true) =>
        widescreen ? MathF.Min(width, height * WidestAspect) : width;

    /// <summary>
    /// How much a screen wider than 16:9 stretches the laid-out picture horizontally (1 up to 16:9; 21/16 at 21:9).
    /// </summary>
    public static float StretchX(float width, float height, bool widescreen = true)
    {
        float layout = LayoutWidth(width, height, widescreen);
        return layout > 0f ? width / layout : 1f;
    }

    /// <summary>
    /// The page units the widescreen HUD's virtual edges move out by on each side: ceil((aspect / (4/3) - 1) x 320); 0 at
    /// 4:3 and below, 64 at 16:10, 107 at 16:9 and wider.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a4ac0: ceil((aspect / 1.3333 - 1) x 0.5 x 640); left edge -d (0x1036b05c), right edge 640 + d (0x10360fd0)")]
    public static float PageEdge(float layoutAspect)
    {
        float d = (layoutAspect / ReferenceAspect - 1f) * 0.5f * PageWidth;
        return d > 0f ? MathF.Ceiling(d) : 0f;
    }

    /// <summary>Screen pixels a page unit is drawn at vertically (and horizontally, before a stretch past 16:9).</summary>
    public static float PageScale(float height) => height / PageHeight;

    /// <summary>
    /// The Enhanced Reality projection extents (HudScreenRight, HudScreenBottom) for the settings.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x10079880: 640 and 480; with widescreen or widescreen_hud: ws_er_small -> Right = aspect x 240 + 320, else Bottom = 320 / aspect + 240")]
    public static (float Right, float Bottom) HudScreen(float layoutAspect, bool widescreen, bool widescreenHud, bool erSmall)
    {
        if (!widescreen && !widescreenHud)
        {
            return (PageWidth, PageHeight);
        }

        return erSmall ? ((layoutAspect * 240f) + 320f, PageHeight) : (PageWidth, (320f / layoutAspect) + 240f);
    }

    /// <summary>
    /// Screen pixels per Enhanced Reality canvas unit over a picture of <paramref name="layoutWidth"/> x
    /// <paramref name="height"/>: the ER canvas spans HudScreenRight x HudScreenBottom.
    /// </summary>
    public static float ErScale(float layoutWidth, float height, bool widescreen, bool widescreenHud, bool erSmall)
    {
        if (!(layoutWidth > 0f) || !(height > 0f))
        {
            return 1f;
        }

        (float right, float bottom) = HudScreen(layoutWidth / height, widescreen, widescreenHud, erSmall);
        return MathF.Min(layoutWidth / right, height / bottom);
    }

    /// <summary>
    /// Where a page x lands on a laid-out picture <paramref name="layoutWidth"/> wide and <paramref name="height"/> high:
    /// left-hand areas keep their offset from the left edge, right-hand ones from the right edge, centred ones from the
    /// middle, all at H / 480.
    /// </summary>
    public static float PageToScreenX(float x, HudAnchor anchor, float layoutWidth, float height)
    {
        float s = PageScale(height);
        return anchor switch
        {
            HudAnchor.Right => layoutWidth - ((PageWidth - x) * s),
            HudAnchor.Center => (layoutWidth * 0.5f) + ((x - (PageWidth * 0.5f)) * s),
            HudAnchor.Stretch => x * layoutWidth / PageWidth,
            _ => x * s,
        };
    }
}

/// <summary>How a top-level HUD area is placed on a wide picture.</summary>
public enum HudAnchor
{
    Left,
    Right,
    Center,
    Stretch,
}
