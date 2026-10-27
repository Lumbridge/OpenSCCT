using System;
using System.Collections.Generic;
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

/// <summary>The camera natives (spec/native/camera.md): the mode table, SetMode, UpdateView, the following and first-person views and the calc view dispatch.</summary>
public sealed class CameraTests
{
    // ---- mode table -----------------------------------------------------------------------------------------

    [Fact]
    public void TheTableHasEveryModeWithTheWindowsTheMovementUses()
    {
        var table = new FECamParam[CameraK.ModeCount];
        CamParamTable.Fill(table);
        for (int m = 0; m < CameraK.ModeCount; m++)
        {
            CameraWindow window = CameraWindows.WindowOf((ECamMode)m);
            Assert.Equal(window.MinYaw, table[m].minYaw);
            Assert.Equal(window.MaxYaw, table[m].maxYaw);
            Assert.Equal(window.MinPitch, table[m].minPitch);
            Assert.Equal(window.MaxPitch, table[m].maxPitch);
        }
    }

    [Fact]
    public void WalkingFollowsAt250WithCollisionAndTheFirstPersonModeHasNoDistance()
    {
        FECamParam walking = CamParamTable.Get(ECamMode.ECM_Walking);
        Assert.Equal(250f, walking.Distance[0]);
        Assert.Equal(400f, walking.interSpeed);
        Assert.Equal(1, walking.useColl);
        Assert.Equal(1, walking.useCylColl);
        Assert.Equal(1, walking.usePitchCurve);
        Assert.Equal(1, walking.useTwig);
        Assert.Equal(0, walking.useCollTarget);

        FECamParam first = CamParamTable.Get(ECamMode.ECM_FirstPerson);
        Assert.Equal(0f, first.Distance[0]);
        Assert.Equal(1, first.alignPawn);
        Assert.Equal(0, first.useCylColl);
    }

    [Fact]
    public void EveryVolumeEntryRepeatsEntryZero()
    {
        for (int m = 0; m < CameraK.ModeCount; m++)
        {
            FECamParam p = CamParamTable.Get((ECamMode)m);
            for (int v = 1; v < 4; v++)
            {
                Assert.Equal(p.Distance[0], p.Distance[v]);
                Assert.Equal(p.offset[0], p.offset[v]);
            }
        }
    }

    // ---- math -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.2f)]
    [InlineData(0.5f)]
    [InlineData(0.8f)]
    public void BiasMapsOneHalfToTheBias(float b)
    {
        Assert.Equal(b, CameraMath.Bias(0.5f, b), 4);
        Assert.Equal(0f, CameraMath.Bias(0f, b), 4);
        Assert.Equal(1f, CameraMath.Bias(1f, b), 4);
    }

    [Fact]
    public void ApproachMovesAtMostTheStep()
    {
        Assert.Equal(10f, CameraMath.Approach(0f, 100f, 10f));
        Assert.Equal(100f, CameraMath.Approach(95f, 100f, 10f));
        Assert.Equal(-10f, CameraMath.Approach(0f, -100f, 10f));
        FVector v = CameraMath.Approach(default, new FVector(300f, 400f, 0f), 50f);
        Assert.Equal(50f, v.Size(), 3);
    }

    // ---- SetMode (rule 7) -----------------------------------------------------------------------------------

    [Fact]
    public void SetModeToTheCurrentModeChangesNothing()
    {
        SPlayerCam cam = Camera(PawnTeam.Spy).Cam;
        cam.m_camMode = ECamMode.ECM_Walking;
        cam.m_prevIdealDistance = 123f;
        cam.SetMode(ECamMode.ECM_Walking);
        Assert.Equal(123f, cam.m_prevIdealDistance);
    }

    [Fact]
    public void WalkingToRunningOnlySwitches()
    {
        SPlayerCam cam = Camera(PawnTeam.Spy).Cam;
        cam.m_camMode = ECamMode.ECM_WalkingCr;
        cam.m_prevIdealDistance = 123f;
        cam.SetMode(ECamMode.ECM_RunningCr);
        Assert.Equal(ECamMode.ECM_RunningCr, cam.m_camMode);
        Assert.Equal(123f, cam.m_prevIdealDistance);
    }

    [Fact]
    public void LeavingCollisionKeepsThePulledInDistance()
    {
        SPlayerCam cam = Camera(PawnTeam.Spy).Cam;
        cam.m_camMode = ECamMode.ECM_Walking;
        cam.m_prevDistance = 77f;
        cam.m_prevIdealDistance = 250f;
        cam.SetMode(ECamMode.ECM_HOHFP);
        Assert.Equal(77f, cam.m_prevIdealDistance);
    }

    [Fact]
    public void OtherChangesJumpToTheNewModesDistance()
    {
        SPlayerCam cam = Camera(PawnTeam.Spy).Cam;
        cam.m_camMode = ECamMode.ECM_Walking;
        cam.m_prevDistance = 77f;
        cam.SetMode(ECamMode.ECM_BTW);
        Assert.Equal(150f, cam.m_prevIdealDistance);
        Assert.Equal(ECamMode.ECM_BTW, cam.m_camMode);
    }

    [Fact]
    public void LeavingTheCenteredTransitionTurnsSmoothingOn()
    {
        SPlayerCam cam = Camera(PawnTeam.Spy).Cam;
        cam.m_camMode = ECamMode.ECM_SmoothTransitionCentered;
        cam.m_SmoothDisplacement = false;
        cam.SetMode(ECamMode.ECM_Walking);
        Assert.True(cam.m_SmoothDisplacement);
    }

    [Fact]
    public void ACameraStartsAt200WithTheTableFilled()
    {
        SPlayerCam cam = Camera(PawnTeam.Spy).Cam;
        Assert.Equal(CameraK.StartDistance, cam.m_prevDistance);
        Assert.Equal(CameraK.StartDistance, cam.m_prevIdealDistance);
        Assert.Equal(250f, cam.m_camParam[(int)ECamMode.ECM_Walking].Distance[0]);
    }

    // ---- UpdateView (rule 6) --------------------------------------------------------------------------------

    [Fact]
    public void HitRollFadesAndShakeGoesOutAndBack()
    {
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Merc);
        float dt = rig.Level.Info.m_dT;
        cam.m_hitRoll = 1000;
        cam.m_hitFadeOut = 600;
        cam.m_shakeRoll = 0;
        cam.m_shakeTargetRoll = 500;
        cam.m_shakeSpeed = 6000;
        cam.m_shakeFadeOut = 3000;
        FRotator view = cam.UpdateView(new FRotator(100, 200, 0));
        Assert.InRange(cam.m_hitRoll, 1000 - (int)(600 * dt) - 1, 1000 - (int)(600 * dt) + 1);
        Assert.InRange(cam.m_shakeRoll, (int)(6000 * dt) - 1, (int)(6000 * dt) + 1);
        Assert.Equal(100, view.Pitch);
        Assert.Equal(200, view.Yaw);
        // The replace rule: a live shake replaces the hit roll, with its value from before the step (0 here).
        Assert.Equal(0, view.Roll);

        bool swungBack = false;
        for (int i = 0; i < 600 && (cam.m_shakeRoll != 0 || cam.m_shakeTargetRoll != 0 || cam.m_hitRoll != 0); i++)
        {
            cam.UpdateView(default);
            swungBack |= cam.m_shakeRoll < 0;
        }

        Assert.True(swungBack, "the shake never swung to the other side");
        Assert.Equal(0, cam.m_shakeRoll);
        Assert.Equal(0, cam.m_shakeTargetRoll);
        Assert.Equal(0, cam.m_hitRoll);
    }

    [Fact]
    public void AReachedShakeTargetShrinksAndFlipsSign()
    {
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Merc);
        float dt = rig.Level.Info.m_dT;
        cam.m_hitRoll = 700;
        cam.m_hitFadeOut = 100;
        cam.m_shakeRoll = 500;
        cam.m_shakeTargetRoll = 500;
        cam.m_shakeSpeed = 6000;
        cam.m_shakeFadeOut = 3000;
        FRotator view = cam.UpdateView(new FRotator(0, 0, 0));
        int flipped = -CameraMath.ApproachInt(500, 0, 3000 * dt);
        Assert.True(flipped < 0);
        Assert.Equal(flipped, cam.m_shakeTargetRoll);
        Assert.Equal(CameraMath.ApproachInt(500, flipped, 6000 * dt), cam.m_shakeRoll);
        Assert.Equal(500, view.Roll);
    }

    [Fact]
    public void AReachedTiltTargetShrinksAndFlipsSign()
    {
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Merc);
        float dt = rig.Level.Info.m_dT;
        cam.m_tiltPitch = 800;
        cam.m_tiltTargetPitch = 800;
        cam.m_tiltSpeed = 10000;
        cam.m_tiltFadeOut = 2000;
        FRotator view = cam.UpdateView(new FRotator(100, 0, 0));
        int flipped = -CameraMath.ApproachInt(800, 0, 2000 * dt);
        Assert.Equal(flipped, cam.m_tiltTargetPitch);
        Assert.Equal(CameraMath.ApproachInt(800, flipped, 10000 * dt), cam.m_tiltPitch);
        Assert.Equal(900, view.Pitch);
    }

    // ---- following solver sweeps (rule 3) -------------------------------------------------------------------

    private readonly record struct SweepCall(FVector Start, FVector End, FVector Extent, ETraceFlags Flags);

    private static List<SweepCall> RecordSweeps(SPlayerCam cam, Func<SweepCall, FCheckResult?>? hits = null)
    {
        var calls = new List<SweepCall>();
        cam.SweepOverride = (FVector start, FVector end, FVector extent, ETraceFlags flags, out FCheckResult hit) =>
        {
            var call = new SweepCall(start, end, extent, flags);
            calls.Add(call);
            FCheckResult? h = hits?.Invoke(call);
            hit = h ?? FCheckResult.NoHit(end);
            return h is not null;
        };
        return calls;
    }

    private static void Solve(PawnRig rig, SPlayerCam cam)
    {
        rig.Controller.Rotation = new FRotator(0, rig.Controller.Rotation.Yaw, 0);
        cam.SolveFollowing(rig.Controller, rig.Pawn, rig.Level.Info.m_dT, out _, out _);
    }

    private static bool IsCollisionBox(SPlayerCam cam, FVector extent) =>
        extent == new FVector(cam.m_collisionRadius, cam.m_collisionRadius, cam.m_collisionHeight);

    [Fact]
    public void TheTargetSweepDoesNotStopAtPawns()
    {
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Spy);
        cam.m_camParam[(int)cam.m_camMode].useCollTarget = 1;
        List<SweepCall> calls = RecordSweeps(cam);
        Solve(rig, cam);
        Assert.Equal((ETraceFlags)0xde, calls[0].Flags);
        Assert.False(calls[0].Flags.HasFlag(ETraceFlags.TRACE_Pawns));
    }

    [Fact]
    public void TheCollisionSweepEndIsLoweredByTheCloseUpLiftToo()
    {
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Spy);
        Assert.Equal(1, cam.CurrentParam.useColl);
        Assert.Equal(1, cam.CurrentParam.useCylColl);
        cam.m_closeupDist = 100f;
        cam.m_closeupHeight = 40f;
        cam.m_closeupDamping = 1f;
        cam.m_prevDistance = cam.m_absoluteMinDist;
        List<SweepCall> calls = RecordSweeps(cam);
        Solve(rig, cam);
        SweepCall collision = calls.Find(c => IsCollisionBox(cam, c.Extent));
        SweepCall clearance = calls.Find(c => c.Extent == new FVector(CameraK.ClearanceBox, CameraK.ClearanceBox, CameraK.ClearanceBox));
        // Level view: both ends of the collision sweep sit 40 uu below the target the clearance sweep starts from.
        Assert.Equal(clearance.Start.Z - 40f, collision.Start.Z, 2);
        Assert.Equal(collision.Start.Z, collision.End.Z, 2);
    }

    [Fact]
    public void AFixedTargetIgnoresACollisionHitThatStartsInside()
    {
        float Distance(float time)
        {
            (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Spy);
            cam.SetFixeTarget(rig.Pawn.Location, 10000f);
            cam.m_prevDistance = 200f;
            RecordSweeps(cam, c => IsCollisionBox(cam, c.Extent) && time >= 0f
                ? new FCheckResult { Time = time, Location = c.Start }
                : null);
            Solve(rig, cam);
            return cam.m_prevDistance;
        }

        float noHit = Distance(-1f);
        Assert.Equal(noHit, Distance(0f), 3);
        Assert.True(Distance(0.5f) < noHit - 1f, "a real hit no longer pulls the camera in");
    }

    [Fact]
    public void TheMode33HeadRoomProbeUsesTheCollisionFlags()
    {
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Spy);
        cam.m_camMode = ECamMode.ECM_NoTargetCol;
        List<SweepCall> calls = RecordSweeps(cam);
        Solve(rig, cam);
        SweepCall probe = calls.Find(c => c.End - c.Start == new FVector(0f, 0f, CameraK.NoTargetColProbe));
        ETraceFlags expected = CameraK.CameraTraceFlags |
            (cam.CurrentParam.collCBInterSpeed > 0f ? CameraK.CameraBlockingFlag : ETraceFlags.None);
        Assert.Equal(expected, probe.Flags);
        Assert.False(probe.Flags.HasFlag(ETraceFlags.TRACE_Pawns));
    }

    [Theory]
    [InlineData(-100, 3030f, -49)]
    [InlineData(100, 3030f, 150)]
    [InlineData(0, 100f, 1)]
    public void TheMode33PitchPushTruncatesAfterTheAdd(int pitch, float speed, int expected)
    {
        Assert.Equal(expected, SPlayerCam.PushPitch(pitch, speed, 1f / 60f));
    }

    [Fact]
    public void TheFirstPersonViewNeverRolls()
    {
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Merc);
        SPlayerController pc = rig.Controller;
        pc.bBehindView = false;
        rig.Step(default);
        cam.m_hitRoll = 1000;
        cam.m_hitFadeOut = 100;
        cam.m_shakeRoll = 400;
        cam.m_shakeTargetRoll = 400;
        cam.m_shakeSpeed = 1000;
        cam.m_shakeFadeOut = 100;
        cam.m_tiltPitch = 300;
        cam.m_tiltTargetPitch = 300;
        cam.m_tiltSpeed = 1000;
        cam.m_tiltFadeOut = 100;
        PlayerCalcView.NativeGlobalPlayerCalcView(pc, out _, out _, out FRotator rotation);
        Assert.Equal(0, rotation.Roll);
        Assert.Equal(pc.Rotation.Pitch + 300, rotation.Pitch);
    }

    // ---- views ----------------------------------------------------------------------------------------------

    [Fact]
    public void TheFollowingCameraSettlesBehindThePawnOnOpenFloor()
    {
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        pc.bBehindView = true;

        // Standing still the ideal distance does not grow (rule 3.8: it backs out only as the pawn walks away from
        // the camera), so the camera stays at the start distance.
        FVector camera = Run(rig, 240);
        FVector pawn = rig.Pawn.Location;
        Assert.Equal(CameraK.StartDistance, pawn.Y - camera.Y, 1f);
        Assert.Equal(pawn.X, camera.X, 1f);

        // At the mode's ideal distance it settles there.
        cam.m_prevIdealDistance = cam.CurrentParam.Distance[0];
        camera = Run(rig, 240);
        pawn = rig.Pawn.Location;
        Assert.Equal(cam.CurrentParam.Distance[0], pawn.Y - camera.Y, 1f);
        Assert.Equal(pawn.X, camera.X, 1f);
    }

    [Fact]
    public void TheAimingCameraSettlesOverTheRightShoulderOutsideThePawn()
    {
        // SecondPerson (2H aim, the sticky camera launcher): target 27 uu right of the start point, 30 uu back along
        // the view (camera.md mode 4), so the camera is about 40 uu from the pawn's axis, never inside the head.
        (PawnRig rig, SPlayerCam cam) = Camera(PawnTeam.Spy);
        rig.Controller.bBehindView = true;
        Run(rig, 10);
        cam.SetMode(ECamMode.ECM_SecondPerson);
        FVector camera = Run(rig, 240);
        FVector pawn = rig.Pawn.Location;
        FVector right = CameraMath.Rotate(new FVector(0f, 1f, 0f), new FRotator(0, rig.Pawn.Rotation.Yaw, 0));
        FVector rel = camera - pawn;
        float side = rel | right;
        float horizontal = MathF.Sqrt((rel.X * rel.X) + (rel.Y * rel.Y));
        Assert.True(MathF.Abs(side - 27f) < 1.5f, $"camera {camera} is {side} uu to the side of the pawn at {pawn}, not 27");
        Assert.True(horizontal >= 30f, $"camera {camera} is {horizontal} uu from the pawn's axis at {pawn}");
    }

    [Fact]
    public void AWallBehindThePawnPullsTheCameraIn()
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-600f, -160f, 0f), V(600f, -120f, 600f));
        PawnRig rig = Rig(PawnTeam.Spy, level);
        rig.Controller.Player = new SimViewport();
        SPlayerCam cam = PlayerCamera.Of(rig.Controller)!;
        rig.Controller.bBehindView = true;
        FVector camera = Run(rig, 240);
        FVector pawn = rig.Pawn.Location;
        Assert.True(camera.Y > -120f, $"camera at {camera} went through the wall at Y -120");
        Assert.True(pawn.Y - camera.Y >= cam.m_absoluteMinDist - 1f, $"camera at {camera} closer than the minimum distance");
        Assert.True(pawn.Y - camera.Y < 120f, $"camera at {camera} not pulled in");
    }

    [Fact]
    public void TheFirstPersonViewIsAtTheEyes()
    {
        (PawnRig rig, SPlayerCam _) = Camera(PawnTeam.Merc);
        SPlayerController pc = rig.Controller;
        pc.bBehindView = false;
        rig.Step(default);
        PlayerCalcView.NativeGlobalPlayerCalcView(pc, out Actor? viewActor, out FVector location, out FRotator rotation);
        SBasePawn pawn = rig.Pawn;
        Assert.Same(pawn, viewActor);
        FVector eyes = pawn.Location + pawn.WalkBob + new FVector(0f, 0f, pawn.EyeHeight) + pc.ShakeOffset;
        Assert.Equal(eyes.X, location.X, 2);
        Assert.Equal(eyes.Y, location.Y, 2);
        Assert.Equal(eyes.Z, location.Z, 2);
        Assert.Equal(pc.Rotation.Yaw, rotation.Yaw);
        Assert.Equal(location, pc.vCamLocation);
        Assert.Equal(rotation, pc.rCamRotation);
    }

    [Fact]
    public void AWalkingControllerUsesTheDefaultView()
    {
        (PawnRig rig, SPlayerCam _) = Camera(PawnTeam.Spy);
        Assert.Equal(PlayerCalcView.EViewSource.Default, PlayerCalcView.SourceOf(rig.Controller));
    }

    [Fact]
    public void ARemoteControllerGetsNoCamera()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Controller.Player = new SimNetConnection();
        Assert.Null(PlayerCamera.Of(rig.Controller));
    }

    [Fact]
    public void TheMovementCameraCallsReachTheCamera()
    {
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, Floor().Build(), null, new PawnRigOptions { Hooks = new CameraMovementHooks() });
        rig.Controller.Player = new SimViewport();
        SPlayerCam cam = PlayerCamera.Of(rig.Controller)!;
        var hooks = new CameraMovementHooks();
        hooks.SetCameraMode(rig.Controller, ECamMode.ECM_BTW);
        Assert.Equal(ECamMode.ECM_BTW, cam.m_camMode);
        hooks.CameraSmooth(rig.Controller, 0f);
        Assert.True(cam.m_SmoothDisplacement);
        Assert.Equal(CameraK.SpySmoothDefault, cam.m_Smoothvalue);
    }

    private static (PawnRig Rig, SPlayerCam Cam) Camera(PawnTeam team)
    {
        PawnRig rig = Rig(team);
        rig.Controller.Player = new SimViewport();
        SPlayerCam cam = PlayerCamera.Of(rig.Controller) ?? throw new InvalidOperationException("no camera for a local viewport player");
        return (rig, cam);
    }

    /// <summary>Steps standing still for <paramref name="ticks"/> ticks with the calc view after each; the last camera location.</summary>
    private static FVector Run(PawnRig rig, int ticks)
    {
        FVector location = default;
        for (int i = 0; i < ticks; i++)
        {
            rig.Step(default);
            PlayerCalcView.NativeGlobalPlayerCalcView(rig.Controller, out _, out location, out _);
        }

        return location;
    }
}
