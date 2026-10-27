using OpenScctGame.Hud;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// The picture and HUD at 4:3, 16:10, 16:9 and 21:9 under Reloaded's widescreen rules (Hud/HudLayout, Options/ViewFov):
/// Hor+ up to 16:9 and a stretched 16:9 picture past it, the 4:3 HUD scale with areas kept at their side, the Enhanced
/// Reality extents and the ws_fov cap. Screen positions are checked against the original's HUD captured in the
/// sandbox at 1024 x 768 and 1280 x 720 (the spy's gadget box, the merc's gadget box).
/// </summary>
public sealed class LayoutTests
{
    public static TheoryData<int, int, float, float, float> Screens => new()
    {
        // width, height, layout aspect, horizontal stretch, page edge (page units)
        { 1024, 768, 4f / 3f, 1f, 0f },
        { 1680, 1050, 1.6f, 1f, 64f },
        { 1920, 1080, 16f / 9f, 1f, 107f },
        { 2560, 1080, 16f / 9f, 2560f / 1920f, 107f },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public void WidescreenStopsAtSixteenByNine(int width, int height, float aspect, float stretch, float edge)
    {
        Assert.Equal(aspect, HudLayout.LayoutAspect(width, height), 4);
        Assert.Equal(stretch, HudLayout.StretchX(width, height), 4);
        Assert.Equal(edge, HudLayout.PageEdge(HudLayout.LayoutAspect(width, height)));
        Assert.Equal(height * 4f / 3f * (aspect * 3f / 4f), HudLayout.LayoutWidth(width, height), 1);
    }

    [Fact]
    public void WidescreenOffLaysOutOverTheWholeScreen()
    {
        Assert.Equal(2560f / 1080f, HudLayout.LayoutAspect(2560, 1080, widescreen: false), 4);
        Assert.Equal(1f, HudLayout.StretchX(2560, 1080, widescreen: false));
    }

    [Theory]
    [InlineData(4f / 3f, 640f, 480f, 640f, 480f)]
    [InlineData(1.6f, 704f, 480f, 640f, 440f)]
    [InlineData(16f / 9f, 746.667f, 480f, 640f, 420f)]
    public void EnhancedRealityExtentsFollowWsErSmall(float aspect, float smallRight, float smallBottom, float bigRight, float bigBottom)
    {
        (float r, float b) = HudLayout.HudScreen(aspect, widescreen: true, widescreenHud: true, erSmall: true);
        Assert.Equal(smallRight, r, 2);
        Assert.Equal(smallBottom, b, 2);
        (r, b) = HudLayout.HudScreen(aspect, widescreen: true, widescreenHud: true, erSmall: false);
        Assert.Equal(bigRight, r, 2);
        Assert.Equal(bigBottom, b, 2);
        Assert.Equal((640f, 480f), HudLayout.HudScreen(aspect, widescreen: false, widescreenHud: false, erSmall: true));
    }

    [Fact]
    public void EnhancedRealityIconsKeepTheirSizeWithWsErSmallAndGrowWithout()
    {
        Assert.Equal(1.5f, HudLayout.ErScale(1280f, 720f, true, true, erSmall: true), 4);
        Assert.Equal(720f / 420f, HudLayout.ErScale(1280f, 720f, true, true, erSmall: false), 3);
        Assert.Equal(1.6f, HudLayout.ErScale(1024f, 768f, true, true, erSmall: false), 4);
    }

    /// <summary>
    /// Where the original drew the HUD: the spy's gadget box spans x 742 to 939 at 1024 x 768 and 1015 to 1200 at
    /// 1280 x 720, the merc's starts at x 82 at 1024 x 768 and 78 at 1280 x 720 (sandbox captures of Play Level SteeE).
    /// The right-hand box keeps its distance from the right edge at the 4:3 scale, the left-hand one from the left.
    /// </summary>
    [Fact]
    public void AreasKeepTheirSideAsInTheOriginal()
    {
        float boxLeft = 742f / 1.6f;
        float boxRight = 939f / 1.6f;
        Assert.InRange(HudLayout.PageToScreenX(boxLeft, HudAnchor.Right, 1280f, 720f), 1014f, 1017f);
        Assert.InRange(HudLayout.PageToScreenX(boxRight, HudAnchor.Right, 1280f, 720f), 1199f, 1202f);
        float mercLeft = 82f / 1.6f;
        Assert.InRange(HudLayout.PageToScreenX(mercLeft, HudAnchor.Left, 1280f, 720f), 76f, 80f);

        // 16:10 and 21:9 (laid out at 16:9 and stretched): the box keeps its right margin in page units.
        Assert.Equal(1680f - ((640f - boxRight) * 1050f / 480f), HudLayout.PageToScreenX(boxRight, HudAnchor.Right, 1680f, 1050f), 2);
        float layout = HudLayout.LayoutWidth(2560f, 1080f);
        Assert.Equal(1920f, layout);
        float onScreen = HudLayout.PageToScreenX(boxRight, HudAnchor.Right, layout, 1080f) * HudLayout.StretchX(2560f, 1080f);
        Assert.Equal(2560f - ((640f - boxRight) * 2.25f * 4f / 3f), onScreen, 1);
    }

    [Theory]
    [InlineData(4f / 3f, 95f)]
    [InlineData(1.6f, 105.269f)]
    [InlineData(16f / 9f, 111.003f)]
    [InlineData(21f / 9f, 111.003f)]
    public void MercFovIsHorPlusToSixteenByNine(float aspect, float horizontal)
    {
        // The merc's FovAngle is 95 (read live); at 16:9 it opens to 111 degrees, under the default ws_fov of 112.
        Assert.Equal(horizontal, ViewFov.ScreenHorizontal(95f, aspect, widescreen: true, cap: 112f), 2);
        var settings = new GameSettings();
        Assert.Equal(ViewFov.VerticalOf(95f, 4f / 3f), ViewFov.Vertical(95f, aspect, settings, capped: true), 2);
    }

    [Fact]
    public void WsFovCapsEveryViewButTheSpyCameras()
    {
        var settings = new GameSettings { MercFovCap = 100f };
        Assert.Equal(ViewFov.VerticalOf(100f, 16f / 9f), ViewFov.Vertical(95f, 16f / 9f, settings, capped: true), 3);
        Assert.Equal(ViewFov.VerticalOf(95f, 4f / 3f), ViewFov.Vertical(95f, 16f / 9f, settings, capped: false), 3);
        Assert.True(ViewFov.CapApplies("s_PlayerWalkingSt2H"));
        Assert.True(ViewFov.CapApplies(null));
        Assert.False(ViewFov.CapApplies("s_UseObject_StickyCam"));
        Assert.False(ViewFov.CapApplies("s_UseObject_CamNetwork"));
    }
}
