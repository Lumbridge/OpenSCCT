using OpenScct.Core;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Animation;

public sealed class ArmAimTests
{
    [Theory]
    [InlineData(BTW_.BTWFireStandLeft, "B R UpperArm")]
    [InlineData(BTW_.BTWFireCrouchLeft, "B R UpperArm")]
    [InlineData(BTW_.BTWFireStandRight, "B L UpperArm")]
    [InlineData(BTW_.BTWFireCrouchRight, "B L UpperArm")]
    public void CornerFiringArmTracksRenderedAimWithoutRemovingItsSimulationLag(BTW_ corner, string bone)
    {
        var pawn = new SPawnAttaque { IK_Type = IK_Type_.IKT_BTW, BTW = corner };
        pawn.iYawPitchFromView = (-400 << 16) | 65200;
        // The held arm still trails its simulation target. A small view movement crosses the yaw wrap.
        var held = new BoneDirection(new Name(bone), new FRotator(-900, 64000, -3000), 1f);
        FRotator drawn = ArmAim.DrawnRotation(pawn, held, new FRotator(-100, 100, 0));

        Assert.Equal(new FRotator(-600, 64436, -3000), drawn);
        Assert.Equal(new FRotator(-900, 64000, -3000), held.Rotation);
    }

    [Theory]
    [InlineData(BTW_.BTWFireStandLeft, "B R UpperArm")]
    [InlineData(BTW_.BTWFireStandRight, "B L UpperArm")]
    public void HolsteringKeepsTheFixedArmPose(BTW_ corner, string bone)
    {
        var pawn = new SPawnAttaque { IK_Type = IK_Type_.IKT_BTW, BTW = corner };
        pawn.PawnAnim.Play(SBasePawn.DRAWCHANNEL, new PawnClip(new Name("CornerHolster"), 20, 30f),
            1f, 0f, loop: false, backward: true);
        var held = new BoneDirection(new Name(bone), new FRotator(-4000, 25000, -1000), 1f);

        Assert.Equal(held.Rotation, ArmAim.DrawnRotation(pawn, held, new FRotator(1000, 5000, 0)));
    }

    [Fact]
    public void RenderedCornerAimLeavesOtherBonesAndPeekPosesAlone()
    {
        var pawn = new SPawnAttaque { IK_Type = IK_Type_.IKT_BTW, BTW = BTW_.BTWFireStandRight };
        var otherArm = new BoneDirection(new Name("B R UpperArm"), new FRotator(100, 200, 300), 1f);
        Assert.Equal(otherArm.Rotation, ArmAim.DrawnRotation(pawn, otherArm, new FRotator(1000, 5000, 0)));

        pawn.BTW = BTW_.BTWStandRight;
        var leftArm = otherArm with { Bone = new Name("B L UpperArm") };
        Assert.Equal(leftArm.Rotation, ArmAim.DrawnRotation(pawn, leftArm, new FRotator(1000, 5000, 0)));
    }
}
