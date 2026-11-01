using OpenScct.Core;
using OpenScctGame.Cameras;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// Which tick-to-tick camera changes are drawn as a cut rather than blended (PlayCamera). The original draws each
/// frame's own view, so a change of view source (the spy's binoculars put away: from the eyes to the following camera
/// 250 uu behind) is instant; blending it drew a few frames from inside the spy's head.
/// </summary>
public sealed class ViewCutTests
{
    [Fact]
    public void FromTheEyesToTheFollowingCameraIsACut()
    {
        var eyes = new ViewSample(1, new FVector(0f, 0f, 56f), default, 75f, true);
        var behind = new ViewSample(2, new FVector(-250f, 0f, 56f), default, 75f, true);
        Assert.True(ViewBlend.IsCut(eyes, behind));
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(40f)]
    [InlineData(80f)]
    public void ATickOfMovementIsBlended(float step)
    {
        // A camera moving with a falling or running pawn, or a following camera closing in at its speed, moves well
        // under 100 uu in a tick.
        var a = new ViewSample(1, new FVector(0f, 0f, 0f), default, 75f, true);
        var b = new ViewSample(2, new FVector(step, 0f, 0f), default, 75f, true);
        Assert.False(ViewBlend.IsCut(a, b));
    }

    [Fact]
    public void NoPreviousViewIsACut()
    {
        var b = new ViewSample(2, default, default, 75f, true);
        Assert.True(ViewBlend.IsCut(default, b));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PerspectiveSwitchCutsEvenWhenAWallPushesTheFollowingCameraClose(bool previousFirst, bool currentFirst)
    {
        var previous = new ViewSample(1, new FVector(-5f, 0f, 20f), default, 75f, true, previousFirst);
        var current = new ViewSample(2, new FVector(0f, 0f, 20f), default, 75f, true, currentFirst);
        Assert.True(ViewBlend.IsCut(previous, current));
    }
}
