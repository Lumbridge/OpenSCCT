using OpenScctGame.Hud;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// The motion tracker's 640 x 480 frame on the screen at the aspects the game supports (spec/equipment/motion-tracking.md,
/// "Screen box"): centred at the HUD's 4:3 scale, the 16:9 picture stretched across 21:9, the whole screen when the HUD
/// is stretched.
/// </summary>
public sealed class MotionHudFrameTests
{
    [Theory]
    [InlineData(1024f, 768f, 0f, 1024f)]
    [InlineData(1152f, 720f, 96f, 1056f)]
    [InlineData(1280f, 720f, 160f, 1120f)]
    [InlineData(1680f, 720f, 210f, 1470f)]
    public void TheFrameIsTheCentredFourByThreeAreaOfThePicture(float w, float h, float left, float right)
    {
        MotionHudFrame f = MotionHudFrame.For(w, h, stretch: false);
        Assert.Equal(left, f.ToScreen(0f, 0f).X, 2);
        Assert.Equal(right, f.ToScreen(640f, 480f).X, 2);
        Assert.Equal(h, f.ToScreen(640f, 480f).Y, 2);
        Assert.Equal(w / 2f, f.ToScreen(320f, 240f).X, 2);
    }

    [Fact]
    public void AtSixteenByNineAUnitIsOneAndAHalfPixelsBothWays()
    {
        // Capture (1280 x 720): a steady 64 x 64 box drawn about 95 pixels square.
        MotionHudFrame f = MotionHudFrame.For(1280f, 720f, stretch: false);
        Assert.Equal(1.5f, f.ScaleX, 4);
        Assert.Equal(1.5f, f.ScaleY, 4);
        (float x, float y) = f.FromScreen(640f, 360f);
        Assert.Equal(320f, x, 3);
        Assert.Equal(240f, y, 3);
    }

    [Fact]
    public void AStretchedHudStretchesTheFrameOverTheScreen()
    {
        MotionHudFrame f = MotionHudFrame.For(1280f, 720f, stretch: true);
        Assert.Equal(0f, f.ToScreen(0f, 0f).X);
        Assert.Equal(1280f, f.ToScreen(640f, 0f).X, 2);
    }
}
