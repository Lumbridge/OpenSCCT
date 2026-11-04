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
    public void ReturningAfterTwoMinutesInFirstPersonReanchorsTheSmoothedFollowingCamera()
    {
        PawnRig rig = Rig(PawnTeam.Spy, Floor(100000f), options: new PawnRigOptions
        {
            Hooks = new CameraMovementHooks(),
        });
        SPlayerController pc = rig.Controller;
        SPlayerCam camera = PlayerCamera.Of(pc)!;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        PlayerCalcView.FrameView(pc, out _, out _, out _);
        FVector initialPawn = rig.Pawn.Location;
        pc.SetSpyFirstPerson(true);
        // Pipe traversal can leave this slow smoothing active while the eye view bypasses the follow solver.
        camera.ActivateSmooth(MovementK.HohSmooth);
        for (int frame = 0; frame < 120 * Sim.Hz; frame++)
        {
            rig.Step(frame < 90 * Sim.Hz ? Forward : default);
            PlayerCalcView.FrameView(pc, out _, out _, out _);
        }

        Assert.True(pc.SpyFirstPersonActive);
        Assert.True(FVector.Dist(initialPawn, rig.Pawn.Location) > 10000f);
        pc.SetSpyFirstPerson(false);
        PlayerCalcView.FrameView(pc, out _, out FVector returned, out _);
        Assert.InRange(FVector.Dist(returned, rig.Pawn.Location), 100f, 400f);

        FVector beforeMove = rig.Pawn.Location;
        FVector following = returned;
        for (int frame = 0; frame < Sim.Hz; frame++)
        {
            rig.Step(Forward);
            PlayerCalcView.FrameView(pc, out _, out following, out _);
        }

        FVector pawnMove = rig.Pawn.Location - beforeMove;
        FVector cameraMove = following - returned;
        Assert.True(pawnMove.Size() > 100f);
        Assert.InRange((cameraMove - pawnMove).Size(), 0f, 60f);
        Assert.InRange(FVector.Dist(following, rig.Pawn.Location), 100f, 400f);
    }

    [Fact]
    public void CrouchedEyeAndStaleInterpolatedStandingEyeStayBelowVentRoof()
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: true);
        var spy = (SPawnAttaque)rig.Pawn;
        spy.EyeHeight = 1000f; // The old standing eye may survive until its ordinary height smoothing catches up.
        FVector eye = SpyPerspectiveCamera.EyeLocation(spy);
        Assert.Equal(spy.Location.Z + System.MathF.Min(spy.BaseEyeHeight,
            spy.CollisionHeight - SpyPerspectiveCamera.DefaultClearance), eye.Z);
        Assert.True(SpyPerspectiveCamera.TryConstrain(spy, spy.Location + V(1000, 1000, 1000), out FVector safe));
        FVector offset = safe - spy.Location;
        Assert.True(offset.Z + SpyPerspectiveCamera.DefaultClearance <= spy.CollisionHeight);
        float cornerX = System.MathF.Abs(offset.X) + SpyPerspectiveCamera.DefaultClearance;
        float cornerY = System.MathF.Abs(offset.Y) + SpyPerspectiveCamera.DefaultClearance;
        Assert.True(cornerX * cornerX + cornerY * cornerY <= spy.CollisionRadius * spy.CollisionRadius + 0.01f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArmedEyeViewDoesNotInheritThePreviousHeightAfterAClimb(bool crouched)
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: crouched);
        var spy = (SPawnAttaque)rig.Pawn;
        SPlayerController pc = rig.Controller;
        spy.WeaponLoaded = 2;
        pc.GotoState(crouched ? MovementStateNames.s_PlayerWalkingCr2H : MovementStateNames.s_PlayerWalkingSt2H);
        // The original UpdateEyeHeight deliberately preserves the previous world height after a
        // vertical move. Its lower clamp is below the body's centre, unsuitable as a new eye-view anchor.
        spy.OldZ = spy.Location.Z - 200f;
        spy.UpdateEyeHeight(Sim.DeltaTime);
        Assert.True(spy.EyeHeight < 0f);
        float originalEyeHeight = spy.EyeHeight;
        pc.bBehindView = true;
        pc.ViewTarget = spy;
        pc.SetSpyFirstPerson(true);

        PlayerCalcView.FrameView(pc, out _, out FVector eye, out _);
        Assert.True(pc.SpyFirstPersonActive);
        Assert.Equal(spy.Location.Z + System.MathF.Min(spy.BaseEyeHeight,
            spy.CollisionHeight - SpyPerspectiveCamera.DefaultClearance), eye.Z);
        Assert.True(eye.Z > spy.Location.Z);
        Assert.Equal(originalEyeHeight, spy.EyeHeight); // Local perspective must not change combat's eye offset.
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
    public void BackToWallInputSuspendsAnActiveEyeViewUntilNormalMovementResumes()
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-200f, -100f, 0f), V(200f, -40f, 300f));
        PawnRig rig = Rig(PawnTeam.Spy, level);
        SPlayerController pc = rig.Controller;
        pc.SetSpyFirstPerson(true);
        Assert.True(pc.SpyFirstPersonActive);

        rig.Step(new PlayerInputFrame { bUseBackToWall = 1 });
        Assert.True(pc.IsInState(MovementStateNames.s_EnterBTW));
        Assert.False(pc.SpyFirstPersonActive);
        for (int frame = 0; frame < 120 && !pc.IsInState(MovementStateNames.s_BTW_up); frame++)
            rig.Step(default);
        Assert.True(pc.IsInState(MovementStateNames.s_BTW_up));
        Assert.False(pc.SpyFirstPersonActive);
        Assert.True(pc.SpyFirstPersonRequested);

        rig.Step(new PlayerInputFrame { bUseBackToWall = 1 });
        Assert.True(pc.IsInState(MovementStateNames.s_SortieBTW));
        Assert.False(pc.SpyFirstPersonActive);
        for (int frame = 0; frame < 120 && !pc.IsInState(MovementStateNames.s_PlayerWalkingSt0H); frame++)
            rig.Step(default);
        Assert.True(pc.IsInState(MovementStateNames.s_PlayerWalkingSt0H));
        Assert.True(pc.SpyFirstPersonActive);
    }

    [Theory]
    [InlineData("s_EnterBTW")]
    [InlineData("s_BTW_up")]
    [InlineData("s_BTW_down")]
    [InlineData("s_BTW_StandLeft")]
    [InlineData("s_BTW_StandRight")]
    [InlineData("s_BTW_CrouchLeft")]
    [InlineData("s_BTW_CrouchRight")]
    [InlineData("s_BTWCorner")]
    [InlineData("s_BTWFireStandLeft")]
    [InlineData("s_BTWFireStandRight")]
    [InlineData("s_BTWFireCrouchLeft")]
    [InlineData("s_BTWFireCrouchRight")]
    [InlineData("s_SortieBTW")]
    public void EveryWallStanceRejectsPerspectiveEntryAndPreservesItsAuthoredCamera(string state)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        SPlayerCam camera = PlayerCamera.Of(pc)!;
        pc.GotoState(new Name(state));
        ECamMode mode = camera.m_camMode;

        pc.SwitchSpyPerspective();
        pc.SetSpyFirstPerson(true);
        Assert.False(pc.SpyFirstPersonRequested);
        Assert.False(pc.SpyFirstPersonActive);
        Assert.Equal(mode, camera.m_camMode);

        pc.GotoState(MovementStateNames.s_PlayerWalkingSt0H);
        pc.SetSpyFirstPerson(true);
        pc.GotoState(new Name(state));
        Assert.False(pc.SpyFirstPersonActive);
        pc.SwitchSpyPerspective();
        Assert.True(pc.SpyFirstPersonRequested);
        pc.SetSpyFirstPerson(false); // Releasing a held eye-view binding still clears it while on the wall.
        Assert.False(pc.SpyFirstPersonRequested);
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
