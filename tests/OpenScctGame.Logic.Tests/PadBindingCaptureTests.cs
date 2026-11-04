using System.Collections.Generic;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class PadBindingCaptureTests
{
    private static HashSet<string> Down(params string[] controls) => new(controls, System.StringComparer.Ordinal);

    [Fact]
    public void OpenerMustReleaseBeforeNewInputCanBeCaptured()
    {
        var capture = new PadBindingCapture(3);
        Assert.Equal(3, capture.Device);
        Assert.False(capture.Armed);
        Assert.Null(capture.Update(Down("A"), 1));
        Assert.False(capture.Armed);
        Assert.Null(capture.Update(Down(), 0));
        Assert.True(capture.Armed);
        Assert.Null(capture.Update(Down("A"), 0));
        Assert.Equal("A", capture.Preview);
        Assert.Equal("A", capture.Update(Down(), .1));
        Assert.Null(capture.Update(Down("B"), 1));
    }

    [Theory]
    [InlineData("B")]
    [InlineData("Start")]
    [InlineData("Back")]
    [InlineData("LT")]
    [InlineData("RT")]
    public void MenuButtonsAndTriggersAreBindableOnRelease(string button)
    {
        var capture = new PadBindingCapture(0);
        capture.Update(Down(), 0);
        Assert.Null(capture.Update(Down(button), 0));
        Assert.Equal(button, capture.Update(Down(), .05));
    }

    [Theory]
    [InlineData(.199, "B")]
    [InlineData(.2, "B:Hold")]
    [InlineData(.5, "B:Hold")]
    public void BUsesTheExistingTapHoldThreshold(double elapsed, string expected)
    {
        var capture = new PadBindingCapture(0);
        capture.Update(Down(), 0);
        capture.Update(Down("B"), 0);
        Assert.Equal(expected, capture.Update(Down(), elapsed));
    }

    [Theory]
    [InlineData("LS", "A", "LS+A")]
    [InlineData("LS", "B", "LS+B")]
    [InlineData("LB", "DpadDown", "LB+DpadDown")]
    [InlineData("LB", "Back", "LB+Back")]
    [InlineData("LB", "RB", "LB+RB")]
    public void ExistingCombinationsCaptureBeforeEitherStandaloneInput(string modifier, string button, string expected)
    {
        var capture = new PadBindingCapture(0);
        capture.Update(Down(), 0);
        capture.Update(Down(modifier), 0);
        Assert.Null(capture.Update(Down(modifier, button), .1));
        Assert.Equal(expected, capture.Preview);
        Assert.Equal(expected, capture.Update(Down(modifier), .05));
    }

    [Fact]
    public void ReleasingTheModifierFirstStillCapturesTheCombination()
    {
        var capture = new PadBindingCapture(0);
        capture.Update(Down(), 0);
        capture.Update(Down("LB", "Y"), 0);
        Assert.Equal("LB+Y", capture.Update(Down("Y"), .05));
    }

    [Fact]
    public void AnUnsupportedCombinationCannotAccidentallyCaptureItsRemainingButton()
    {
        var capture = new PadBindingCapture(0);
        capture.Update(Down(), 0);
        capture.Update(Down("RB"), 0);
        Assert.Null(capture.Update(Down("RB", "A"), 0));
        Assert.NotNull(capture.Error);
        Assert.False(capture.Armed);
        Assert.Null(capture.Update(Down("A"), .1));
        Assert.Null(capture.Update(Down(), .1));
        Assert.True(capture.Armed);
        capture.Update(Down("X"), 0);
        Assert.Null(capture.Error);
        Assert.Equal("X", capture.Update(Down(), .1));
    }

    [Fact]
    public void ThreeButtonCombinationIsRejectedAndStickMotionIsIgnored()
    {
        var capture = new PadBindingCapture(0);
        capture.Update(Down("LeftStickUp"), 0);
        Assert.True(capture.Armed);
        Assert.Null(capture.Update(Down("LS", "LB", "A"), 0));
        Assert.NotNull(capture.Error);
        Assert.False(capture.Armed);
    }

    [Fact]
    public void HeldBPreviewDoesNotRequireAnotherPressAndAChordStillTakesPriority()
    {
        var capture = new PadBindingCapture(0);
        capture.Update(Down(), 0);
        capture.Update(Down("B"), 0);
        capture.Update(Down("B"), .2);
        Assert.Equal("B:Hold", capture.Preview);
        capture.Update(Down("LS", "B"), .1);
        Assert.Equal("LS+B", capture.Preview);
        Assert.Equal("LS+B", capture.Update(Down(), .05));
    }
}
