using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The merc's mouse aim (input-and-rotation.md, UpdateRotationAttaquant_Mouse): the look and turn input move the
/// desired rotation, the one the rifle's shots are aimed with (merc-rifle.md rule 6), and the view follows it.
/// </summary>
public sealed class MercAimTests
{
    [Fact]
    public void TheMouseMovesTheAimTheRifleShootsAlongAndTheViewFollows()
    {
        var scene = new CombatScene();
        (SPlayerController pc, _) = scene.Player(false, new FVector(0f, 0f, 100f), yaw: 1000);
        pc.LocalMyDesiredRotation = new FRotator(0, 1000, 0);
        pc.Rotation = new FRotator(0, 1000, 0);
        pc.fSensibiliteSouris = 4f;
        pc.fLookUpValue = 100f;
        pc.fTurnValue = -50f;

        pc.UpdateRotation(0.1f, 0f);

        // int(100 x 1 x 4 x 0.1 x 50) = 2000 up, int(-50 x 4 x 0.1 x 50) = -1000 left.
        Assert.Equal(new FRotator(2000, 0, 0), pc.MyDesiredRotation);
        Assert.Equal(pc.MyDesiredRotation, pc.Rotation);
        Assert.Equal(pc.MyDesiredRotation, pc.LocalMyDesiredRotation);
    }

    [Fact]
    public void TheAimPitchIsClampedLikeTheView()
    {
        var scene = new CombatScene();
        (SPlayerController pc, _) = scene.Player(false, new FVector(0f, 0f, 100f));
        pc.fSensibiliteSouris = 4f;
        pc.fLookUpValue = -1400f;
        pc.UpdateRotation(0.1f, 0f);
        Assert.Equal(-12000, pc.MyDesiredRotation.Pitch);
        Assert.Equal(-12000, pc.Rotation.Pitch);
    }
}
