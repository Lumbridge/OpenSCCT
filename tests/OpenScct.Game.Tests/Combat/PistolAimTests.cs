using OpenScct.Core;
using OpenScct.Game.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The armed spy's estimated pistol aim (CombatNatives.TurnTowardCameraTarget): the pistol turns toward what the behind
/// camera looks at, but never back toward the camera. Playtest: a merc bot walked up to the spy, the camera line hit the
/// bot's near side between the camera and the estimated muzzle, and the laser pointed back past the camera and down to
/// the floor (beam rotation (-7964, -19560) against a view of (2590, 19186)).
/// </summary>
public sealed class PistolAimTests
{
    private static readonly FVector Camera = new(-1435.19f, 1711.59f, -69.12f);
    private static readonly FRotator Look = new(2590, 19186, 0);
    private static readonly FVector Muzzle = new(-1433.56f, 1807.44f, -8.76f);

    [Fact]
    public void ATargetBetweenTheCameraAndTheMuzzleKeepsTheCameraDirection()
    {
        // The playtest's numbers: the camera line hit the bot's cylinder 50 uu in front of the camera.
        var target = new FVector(-1448.49f, 1759.91f, -56.42f);
        Assert.Null(CombatNatives.TurnTowardCameraTarget(Muzzle, Look, target));
    }

    [Fact]
    public void AFarTargetTurnsThePistolTowardIt()
    {
        FVector target = Camera + Look.Vector() * 1000f + new FVector(0f, 0f, -40f);
        FRotator? aim = CombatNatives.TurnTowardCameraTarget(Muzzle, Look, target);
        Assert.NotNull(aim);
        Assert.True((aim!.Value.Vector() | Look.Vector()) > 0.95f, $"aim {aim} against the look {Look}");
    }

    [Fact]
    public void ATargetAtTheMuzzleKeepsTheCameraDirection()
    {
        Assert.Null(CombatNatives.TurnTowardCameraTarget(Muzzle, Look, Muzzle + Look.Vector() * 10f));
    }
}
