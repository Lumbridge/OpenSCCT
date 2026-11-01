using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Camera;

public sealed class SpyPerspectiveTests
{
    [Fact]
    public void SwitchChangesOnlyLocalViewAndReturnsToFollowingCamera()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        SPlayerCam camera = PlayerCamera.Of(pc)!;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        FRotator aim = pc.Rotation;
        ECamMode mode = camera.m_camMode;

        pc.SwitchSpyPerspective();
        Assert.True(pc.SpyFirstPersonActive);
        PlayerCalcView.FrameView(pc, out Actor? viewed, out FVector eye, out _);
        Assert.Same(rig.Pawn, viewed);
        Assert.Equal(rig.Pawn.Location.X, eye.X);
        Assert.Equal(rig.Pawn.Location.Y, eye.Y);
        Assert.True(pc.bBehindView);
        Assert.False(pc.bFirstPersonWithPawn);
        Assert.Equal(aim, pc.Rotation);
        Assert.Equal(mode, camera.m_camMode);

        pc.SwitchSpyPerspective();
        Assert.False(pc.SpyFirstPersonActive);
        PlayerCalcView.FrameView(pc, out _, out FVector following, out _);
        Assert.True(FVector.Dist(following, eye) > 20f);
    }

    [Fact]
    public void CrouchedEyeAndStaleInterpolatedStandingEyeStayBelowVentRoof()
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: true);
        var spy = (SPawnAttaque)rig.Pawn;
        spy.EyeHeight = 1000f; // The old standing eye may survive until its ordinary height smoothing catches up.
        FVector eye = SpyPerspectiveCamera.EyeLocation(spy);
        Assert.Equal(spy.Location.Z + spy.CollisionHeight - SpyPerspectiveCamera.DefaultClearance, eye.Z);
        Assert.True(SpyPerspectiveCamera.TryConstrain(spy, spy.Location + V(1000, 1000, 1000), out FVector safe));
        FVector offset = safe - spy.Location;
        Assert.True(offset.Z + SpyPerspectiveCamera.DefaultClearance <= spy.CollisionHeight);
        float cornerX = System.MathF.Abs(offset.X) + SpyPerspectiveCamera.DefaultClearance;
        float cornerY = System.MathF.Abs(offset.Y) + SpyPerspectiveCamera.DefaultClearance;
        Assert.True(cornerX * cornerX + cornerY * cornerY <= spy.CollisionRadius * spy.CollisionRadius + 0.01f);
    }

    [Theory]
    [InlineData(ColliderKind.MeshHull)]
    [InlineData(ColliderKind.Mover)]
    [InlineData(ColliderKind.Brush)]
    [InlineData(ColliderKind.Gpo)]
    public void ViewSweepStopsBeforeWallOrDoorWithoutCameraBlockingFlag(ColliderKind kind)
    {
        CollisionDataBuilder level = Floor();
        level.AddBoxHullCollider(kind, null, CollisionDataBuilder.WorldFlags & ~(ColliderFlags.BlockCamera | ColliderFlags.BlockActors),
            V(80, -500, 0), V(90, 500, 500));
        PawnRig rig = Rig(PawnTeam.Spy, level);
        var spy = (SPawnAttaque)rig.Pawn;
        FVector wanted = SpyPerspectiveCamera.EyeLocation(spy) + V(200, 0, 0);
        Assert.True(SpyPerspectiveCamera.TryConstrain(spy, wanted, out FVector safe, withinBody: false));
        Assert.True(safe.X + SpyPerspectiveCamera.DefaultClearance < 80f, $"Near plane crossed wall: {safe}");
        spy.bCollideWorld = false;
        spy.bBlockActors = false;
        spy.bBlockPlayers = false;
        Assert.True(SpyPerspectiveCamera.TryConstrain(spy, wanted, out FVector duringTraversal, withinBody: false));
        Assert.Equal(safe, duringTraversal);
    }

    [Fact]
    public void CeilingStopsEyeEvenWhenTraversalBodyExtendsAboveIt()
    {
        CollisionDataBuilder level = Floor();
        int ceiling = level.AddBoxHullCollider(ColliderKind.Mover, null, CollisionDataBuilder.WorldFlags,
            V(-500, -500, 0), V(500, 500, 20), V(0, 0, 400));
        PawnRig rig = Rig(PawnTeam.Spy, level);
        var spy = (SPawnAttaque)rig.Pawn;
        float ceilingZ = spy.Location.Z + 20f;
        rig.Collision!.SetColliderTransform(ceiling, V(0, 0, ceilingZ), default);
        spy.EyeHeight = spy.CollisionHeight;
        Assert.True(SpyPerspectiveCamera.TryConstrain(spy, SpyPerspectiveCamera.EyeLocation(spy), out FVector safe));
        Assert.True(safe.Z + SpyPerspectiveCamera.DefaultClearance < ceilingZ);
    }

    [Fact]
    public void CameraFailsClosedIfDoorClosesAcrossTheAnchorOrCollisionWorldIsMissing()
    {
        CollisionDataBuilder level = Floor();
        int door = level.AddBoxHullCollider(ColliderKind.Mover, null, CollisionDataBuilder.WorldFlags,
            V(-10, -100, -100), V(10, 100, 100), V(300, 0, 100));
        PawnRig rig = Rig(PawnTeam.Spy, level);
        var spy = (SPawnAttaque)rig.Pawn;
        rig.Collision!.SetColliderTransform(door, spy.Location, default);
        Assert.False(SpyPerspectiveCamera.TryConstrain(spy, SpyPerspectiveCamera.EyeLocation(spy), out _));
        rig.Level.PhysicsDriver = null;
        Assert.False(SpyPerspectiveCamera.TryConstrain(spy, SpyPerspectiveCamera.EyeLocation(spy), out _));
    }

    [Fact]
    public void RemoteAndForcedViewsSuspendThePreferenceWithoutChangingTheirFlags()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        SPlayerCam camera = PlayerCamera.Of(pc)!;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        pc.SetSpyFirstPerson(true);
        Assert.True(pc.SpyFirstPersonActive);
        pc.bBinocularVision = true;
        Assert.False(pc.SpyFirstPersonActive);
        pc.bBinocularVision = false;
        pc.FreeCameraFlying = 1;
        Assert.False(pc.SpyFirstPersonActive);
        pc.FreeCameraFlying = 0;
        pc.ViewTarget = pc;
        Assert.False(pc.SpyFirstPersonActive);
        pc.ViewTarget = rig.Pawn;
        camera.m_bAttachToBone = true;
        Assert.False(pc.SpyFirstPersonActive);
        camera.m_bAttachToBone = false;
        pc.bFirstPersonWithPawn = true;
        Assert.False(pc.SpyFirstPersonActive);
        pc.bFirstPersonWithPawn = false;
        Assert.True(pc.SpyFirstPersonActive);
        Assert.True(pc.SpyFirstPersonRequested);
        Assert.True(pc.bBehindView);
    }

    [Fact]
    public void DeathAndNewPossessionClearRequestAndMercsCannotEnableIt()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        pc.SetSpyFirstPerson(true);
        rig.Pawn.Health = 0;
        Assert.False(pc.SpyFirstPersonRequested);
        rig.Pawn.Health = 100;
        Assert.False(pc.SpyFirstPersonRequested);
        pc.SetSpyFirstPerson(true);
        pc.Pawn = null;
        Assert.False(pc.SpyFirstPersonRequested);
        pc.Pawn = rig.Pawn;
        Assert.False(pc.SpyFirstPersonRequested);

        PawnRig merc = Rig(PawnTeam.Merc);
        merc.Controller.SetSpyFirstPerson(true);
        Assert.False(merc.Controller.SpyFirstPersonRequested);
        Assert.False(merc.Controller.SpyFirstPersonActive);
    }
}
