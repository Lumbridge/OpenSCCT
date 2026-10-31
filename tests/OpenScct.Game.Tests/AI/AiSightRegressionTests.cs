using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class AiSightRegressionTests
{
    private static readonly AiDifficulty Normal = AiDifficulty.For(AiSkill.Normal);

    private static (PawnRig Rig, AiWorld World) Scene(float ambient = 0.6f)
    {
        PawnRig rig = Rig(Floor(), null, V(0f, 700f, 0f));
        var light = new AiLightField([], new Dictionary<int, AiRgb> { [0] = new(ambient, ambient, ambient) }, _ => 0);
        return (rig, World(rig, V(-128f, -128f, -100f), V(128f, 900f, 300f), light: light));
    }

    [Fact]
    public void MotionVisionStillShowsAStationaryLitBodyWithoutATrackerLock()
    {
        var (rig, world) = Scene();
        AiSighting sight = AiSight.Look(world, V(0f, 0f, 150f), new FRotator(0, 16384, 0), 85f,
            AiVisionMode.Motion, rig.Pawn, Normal);
        Assert.True(sight.Visible, "Motion vision draws the normal scene underneath its blurred colour pass.");
    }

    [Fact]
    public void DimlyLitTargetsAccumulateEvidenceWhileCompleteDarknessDoesNot()
    {
        var (rig, world) = Scene(0.04f);
        AiSighting sight = AiSight.Look(world, V(0f, 0f, 150f), new FRotator(0, 16384, 0), 85f,
            AiVisionMode.Normal, rig.Pawn, Normal);
        Assert.True(sight.Visible, $"A dim exposed body should take longer to spot, not disappear: {sight}");
        Assert.InRange(sight.Strength, 0.02f, 0.5f);
        Assert.Equal(0f, AiSight.LitStrength(0f, 700f, Normal));
    }

    [Fact]
    public void ADownedBodyCanBeSeenWhenItsCollisionCentreIsOutsideTheView()
    {
        var (rig, world) = Scene();
        rig.Pawn.bLyingOnGround = true;
        rig.Pawn.Location = V(0f, 140f, 89.5f);
        FVector eye = V(0f, 0f, 150f);
        FVector body = rig.Pawn.Location - V(0f, 0f, rig.Pawn.CollisionHeight * MercAiK.LyingAimDrop);
        AiSighting sight = AiSight.Look(world, eye, (body - eye).Rotation(), 20f, AiVisionMode.Normal, rig.Pawn, Normal);
        Assert.True(sight.Visible, "The visible prone body, rather than the upright collision centre, must determine sight.");
    }

    [Fact]
    public void FullCloakHidesTheBodyInMotionVision()
    {
        var (rig, world) = Scene();
        rig.Pawn.GlassEffect = byte.MaxValue;
        Assert.False(AiSight.Look(world, V(0f, 0f, 150f), new FRotator(0, 16384, 0), 85f,
            AiVisionMode.Motion, rig.Pawn, Normal).Visible);
    }

    [Fact]
    public void RectangularViewIncludesItsCornersAndClipsAboveThePicture()
    {
        Assert.True(AiSight.InView(V(1000f, 1200f, 700f), default, 90f));
        Assert.False(AiSight.InView(V(1000f, 0f, 900f), default, 90f));
        Assert.False(AiSight.InView(V(-1000f, 0f, 0f), default, 90f));
    }

    [Fact]
    public void NightVisionSeesTheOpaqueBodyThatTheRendererDrawsThroughCloak()
    {
        var (rig, world) = Scene();
        rig.Pawn.GlassEffect = byte.MaxValue;
        AiSighting night = AiSight.Look(world, V(0f, 0f, 150f), new FRotator(0, 16384, 0), 85f,
            AiVisionMode.Night, rig.Pawn, Normal);
        Assert.True(night.Visible);
        Assert.True(night.Strength > 0.5f);
    }
}
