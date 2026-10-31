using OpenScct.Core;
using OpenScct.Game.AI;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class AiMemoryRegressionTests
{
    private static readonly AiDifficulty Normal = AiDifficulty.For(AiSkill.Normal);
    private static readonly AiSighting Clear = new(true, 1f, 400f, 1f, true);

    [Fact]
    public void HiddenMovementFacingAndDeathDoNotRewriteOrRemoveRememberedContact()
    {
        var rig = Rig(Floor(), null, V(0f, 400f, 0f));
        var memory = new AiMemory();
        AiContact c = memory.Observe(rig.Pawn, Clear, 10f, 1f, Normal);
        AiObservedPose pose = c.Pose!.Value;
        memory.EndLook(10.1f);
        rig.Pawn.Location += V(500f, 200f, 0f);
        rig.Pawn.Rotation = new FRotator(0, 32768, 0);
        rig.Pawn.Health = 0;
        memory.Decay(0.5f, Normal);
        Assert.Same(c, Assert.Single(memory.Contacts));
        Assert.Equal(pose, c.Pose);
        Assert.Equal(pose.Position, c.Position);
        Assert.False(c.Visible);
        Assert.True(c.Confidence < 1f);
    }

    [Fact]
    public void BrieflyLosingAKnockedDownTargetDoesNotRestartIdentificationOrReaction()
    {
        var rig = Rig(Floor(), null, V(0f, 400f, 0f));
        var memory = new AiMemory();
        AiContact c = memory.Observe(rig.Pawn, Clear, 10f, 1f, Normal);
        rig.Pawn.bLyingOnGround = true;
        memory.Observe(rig.Pawn, Clear, 10.1f, 0.1f, Normal);
        memory.EndLook(10.2f);
        memory.Decay(0.1f, Normal);
        memory.Observe(rig.Pawn, Clear with { Strength = 0.05f }, 10.3f, 0.1f, Normal);
        Assert.True(c.Confirmed);
        Assert.Equal(10f, c.VisibleSince);
        Assert.True(c.Pose!.Value.Downed);
        Assert.True(c.BodyVisible);
    }

    [Fact]
    public void TrackerPositionCannotRevealAnOccludedTargetsNewPosture()
    {
        var rig = Rig(Floor(), null, V(0f, 400f, 0f));
        var memory = new AiMemory();
        AiContact c = memory.Observe(rig.Pawn, Clear, 10f, 1f, Normal);
        AiObservedPose seen = c.Pose!.Value;
        rig.Pawn.bLyingOnGround = true;
        rig.Pawn.Location += V(100f, 0f, 0f);
        memory.Observe(rig.Pawn, Clear, 10.1f, 0.1f, Normal, bodyVisible: false);
        Assert.Equal(rig.Pawn.Location, c.Position);
        Assert.Equal(seen, c.Pose);
        Assert.False(c.BodyVisible);
    }

    [Fact]
    public void RepeatedOrOlderCalloutsCannotRefreshConfidenceOrRewindAContact()
    {
        var rig = Rig(Floor(), null, default);
        var memory = new AiMemory();
        memory.Report(rig.Pawn, V(100f, 0f, 0f), 10f, 0.8f, AiContactSource.Told);
        memory.Decay(Normal.MemoryHalfLife, Normal);
        memory.Report(rig.Pawn, V(100f, 0f, 0f), 10f, 0.8f, AiContactSource.Told);
        memory.Report(rig.Pawn, V(999f, 0f, 0f), 9f, 1f, AiContactSource.Told);
        AiContact c = Assert.Single(memory.Contacts);
        Assert.Equal(0.4f, c.Confidence, 3);
        Assert.Equal(V(100f, 0f, 0f), c.Position);
    }

    [Fact]
    public void PartialSightSuppliesAnInspectionPointBeforeIdentification()
    {
        var rig = Rig(Floor(), null, V(0f, 400f, 0f));
        var memory = new AiMemory();
        AiContact c = memory.Observe(rig.Pawn, Clear with { Strength = 0.3f }, 10f, 0.1f, Normal);
        Assert.False(c.Confirmed);
        Assert.True(c.Confidence > 0f);
        Assert.InRange((c.Position - rig.Pawn.Location).Size(), 0f, 56f);
        Assert.Null(c.Pose);
        memory.EndLook(10.1f);
        Assert.False(c.Visible);
    }
}
