using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The widescreen field of view: Hor+ up to 16:9, the ws_fov cap and the stretched 4:3 picture.</summary>
public sealed class ViewFovTests
{
    [Fact]
    public void FourThreeScreenKeepsTheCameraAngle()
    {
        Assert.Equal(90f, ViewFov.ScreenHorizontal(90f, 4f / 3f, widescreen: true, cap: null), 3);
    }

    [Fact]
    public void WidescreenAddsWidthAndKeepsTheVerticalAngle()
    {
        float h = ViewFov.ScreenHorizontal(75f, 16f / 9f, widescreen: true, cap: null);
        Assert.InRange(h, 91f, 92f);
        Assert.Equal(ViewFov.VerticalOf(75f, 4f / 3f), ViewFov.VerticalOf(h, 16f / 9f), 3);
    }

    [Fact]
    public void ViewsAreCappedAtWsFov()
    {
        var s = new GameSettings { MercFovCap = 100f };
        float uncapped = ViewFov.ScreenHorizontal(90f, 16f / 9f, widescreen: true, cap: null);
        Assert.True(uncapped > 100f);
        Assert.Equal(ViewFov.VerticalOf(100f, 16f / 9f), ViewFov.Vertical(90f, 16f / 9f, s, capped: true), 3);
        Assert.Equal(ViewFov.VerticalOf(uncapped, 16f / 9f), ViewFov.Vertical(90f, 16f / 9f, s, capped: false), 3);
    }

    [Fact]
    public void WiderThanSixteenByNineKeepsTheSixteenByNineAngle()
    {
        Assert.Equal(ViewFov.ScreenHorizontal(90f, 16f / 9f, widescreen: true, cap: null), ViewFov.ScreenHorizontal(90f, 21f / 9f, widescreen: true, cap: null), 3);
    }

    [Fact]
    public void WidescreenOffStretchesTheFourThreeAngle()
    {
        var s = new GameSettings { Widescreen = false, MercFovCap = 95f };
        Assert.Equal(ViewFov.VerticalOf(90f, 16f / 9f), ViewFov.Vertical(90f, 16f / 9f, s, capped: true), 3);
    }
}
