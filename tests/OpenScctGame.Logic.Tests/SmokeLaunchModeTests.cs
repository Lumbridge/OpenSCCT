using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SmokeLaunchModeTests
{
    [Fact]
    public void OlderEnableFlagsRemainRecognisedWithTheNewDefaults()
    {
        Assert.True(LaunchOptions.Parse([]).ResponsiveSmoke);
        var options = LaunchOptions.Parse(["--svm-play=AquaE", "--svm-rifles", "--svm-sidearms", "--svm-practice"]);
        Assert.True(options.Practice);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void PracticeIsAnExplicitLocalLaunchOption()
    {
        Assert.False(LaunchOptions.Parse([]).Practice);
        var options = LaunchOptions.Parse(["--svm-play=AquaE", "--svm-practice"]);
        Assert.True(options.Practice);
        Assert.Empty(options.Unknown);
    }

    [Theory]
    [InlineData("original", false)]
    [InlineData("fast", true)]
    [InlineData("full", true)]
    public void ExplicitModeSelectsTheSmokeRule(string mode, bool enabled)
    {
        LaunchOptions options = LaunchOptions.Parse(["--svm-play=TestRange", "--svm-smoke-mode=" + mode]);
        Assert.Equal(mode, options.SmokeMode);
        Assert.Equal(enabled, options.ResponsiveSmoke);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void OriginalSelectionOverridesLegacyEnableFlagInEitherOrder()
    {
        Assert.False(LaunchOptions.Parse(["--svm-responsive-smoke", "--svm-smoke-mode=original"]).ResponsiveSmoke);
        Assert.False(LaunchOptions.Parse(["--svm-smoke-mode=original", "--svm-responsive-smoke"]).ResponsiveSmoke);
        Assert.True(LaunchOptions.Parse(["--svm-responsive-smoke"]).ResponsiveSmoke);
        Assert.Null(LaunchOptions.Parse(["--svm-responsive-smoke"]).SmokeMode);
    }

    [Fact]
    public void InvalidSmokeModesStayVisibleAsUnknownArguments()
    {
        LaunchOptions options = LaunchOptions.Parse(["--svm-smoke-mode=ultra"]);
        Assert.True(options.ResponsiveSmoke);
        Assert.Null(options.SmokeMode);
        Assert.Contains("--svm-smoke-mode=ultra", options.Unknown);
    }
}
