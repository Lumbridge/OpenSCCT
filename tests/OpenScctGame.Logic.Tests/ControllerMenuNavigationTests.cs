using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ControllerMenuNavigationTests
{
    [Fact]
    public void RepeatedAxisEventsAndDpadInTheSameDirectionProduceOneStep()
    {
        var navigation = new PadMenuNavigation();
        Assert.Equal("Down", navigation.Axis(false, .7f, 0));
        Assert.Null(navigation.Axis(false, .9f, .01));
        Assert.Null(navigation.Axis(false, .5f, .02));
        Assert.Null(navigation.Button("Down", true, .03));
        Assert.Null(navigation.Button("Down", true, .04));
        Assert.Null(navigation.Advance(.39));
        Assert.Equal("Down", navigation.Advance(.4));
        Assert.Null(navigation.Advance(.41));
        Assert.Equal("Down", navigation.Advance(3));
        Assert.Null(navigation.Advance(3));
    }

    [Fact]
    public void ReleasingOrChangingDirectionRearmsOneImmediateStep()
    {
        var navigation = new PadMenuNavigation();
        Assert.Equal("Down", navigation.Axis(false, .8f, 0));
        Assert.Null(navigation.Axis(false, .4f, .01));
        Assert.Null(navigation.Axis(false, .3f, .02));
        Assert.Null(navigation.Advance(1));
        Assert.Equal("Down", navigation.Axis(false, .8f, 1));
        Assert.Equal("Up", navigation.Axis(false, -.8f, 1.01));
        navigation.Reset();
        Assert.Null(navigation.Advance(10));
        Assert.Equal("Down", navigation.Button("Down", true, 10));
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("Escape")]
    public void AcceptAndBackNeverRepeatWhileHeld(string key)
    {
        var navigation = new PadMenuNavigation();
        Assert.Equal(key, navigation.Button(key, true, 0));
        Assert.Null(navigation.Button(key, true, .1));
        Assert.Null(navigation.Advance(10));
        Assert.Null(navigation.Button(key, false, 10));
        Assert.Equal(key, navigation.Button(key, true, 10));
    }
}
