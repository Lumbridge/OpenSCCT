using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PredictionRootTests
{
    private static void RequireMap(string scenario)
    {
        string map = RubberBandRun.MapOf(scenario);
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(map) is null,
            $"{map} has not been converted into .content/svmap.");
    }

    [Fact]
    public void FenceRootHandoffKeepsTheCompletedCommandAligned()
    {
        RequireMap("moveset/spy-fence-climb");
        RubberBandRun run = RubberBandRun.Play("moveset/spy-fence-climb", SimLinkProfile.Ping200);
        Assert.True(run.LargestCorrection() <= 32f, run.Describe());
        Assert.Empty(run.Reinits);
        Assert.True(run.FinalError < 2f, run.Describe());
    }

    [Theory]
    [InlineData("150")]
    [InlineData("200")]
    public void WallJumpHandsRootMotionBackToFalling(string profile)
    {
        RequireMap("moveset/spy-wall-jump");
        RubberBandRun run = RubberBandRun.Play("moveset/spy-wall-jump", SimLinkProfile.Get(profile));
        Assert.True(run.LargestCorrection() <= 32f, run.Describe());
        Assert.Empty(run.Reinits);
        Assert.True(run.FinalError < 2f, run.Describe());
    }

    [Fact]
    public void LadderEntryDoesNotAccumulateRootCorrections()
    {
        RequireMap("moveset/spy-ladder-down");
        RubberBandRun run = RubberBandRun.Play("moveset/spy-ladder-down", SimLinkProfile.C);
        Assert.True(run.LargestCorrection() <= 32f, run.Describe());
        Assert.Empty(run.Reinits);
        Assert.True(run.FinalError < 2f, run.Describe());
    }
}
