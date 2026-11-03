using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Camera;

public sealed class TraversalFollowingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollingOnLevelGroundEasesStanceHeightWithoutAVerticalCameraBump(bool crouched)
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: crouched,
            options: new PawnRigOptions { Hooks = new CameraMovementHooks() });
        SPlayerController pc = rig.Controller;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        FVector previous = default;
        for (int i = 0; i < 40; i++)
        {
            rig.Step(Forward);
            PlayerCalcView.FrameView(pc, out _, out previous, out _);
        }

        bool rolled = false;
        float maxStep = CamParamTable.Get(ECamMode.ECM_Walking).targetZSpeed * Sim.DeltaTime * PlayerCalcView.ViewSolvesPerFrame;
        for (int i = 0; i < 150; i++)
        {
            rig.Step(Forward with { bRoll = i == 0 ? (byte)1 : (byte)0 });
            rolled |= rig.Pawn.bIsRolling;
            PlayerCalcView.FrameView(pc, out _, out FVector view, out _);
            Assert.InRange(System.MathF.Abs(view.Z - previous.Z), 0f, maxStep + 0.01f);
            previous = view;
        }

        Assert.True(rolled);
        Assert.False(rig.Pawn.bIsRolling);
        Assert.Equal(crouched, rig.Pawn.bIsCrouched);
    }

    [Fact]
    public void StandingUpDuringVerticalTravelStillTracksTheTravel()
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: true,
            options: new PawnRigOptions { Hooks = new CameraMovementHooks() });
        SPlayerController pc = rig.Controller;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        FVector before = default;
        for (int i = 0; i < 40; i++) PlayerCalcView.FrameView(pc, out _, out before, out _);

        Assert.True(rig.Pawn.UnCrouch(false));
        // A traversal snap or mover carry need not contribute to the gameplay velocity.
        rig.Pawn.Location += V(0, 0, 100);
        rig.Pawn.Velocity = default;
        PlayerCalcView.FrameView(pc, out _, out FVector after, out _);

        Assert.InRange(after.Z - before.Z, 100f, 115.01f);
    }

    [Fact]
    public void TurningDuringTraversalWithUnderReportedVelocityDoesNotAccumulateFollowLag()
    {
        PawnRig rig = Rig(PawnTeam.Spy, options: new PawnRigOptions { Hooks = new CameraMovementHooks() });
        SPlayerController pc = rig.Controller;
        SPlayerCam camera = PlayerCamera.Of(pc)!;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        rig.Pawn.Location = V(0, 0, 300);
        FVector initial = rig.Pawn.Location;
        PlayerCalcView.FrameView(pc, out _, out _, out _);
        camera.ActivateSmooth(MovementK.HohSmooth);
        rig.Pawn.vVandammePos = initial + V(0, 0, 1000);
        rig.Pawn.vWallJumpDir = V(0, 1, 0);
        float maxAnchorGap = 0;
        float maxCameraDistance = 0;

        // This real traversal native reports displacement * dt in Velocity. The camera must still
        // follow the actual 87 uu/s travel while the player turns, including all three solves per tick.
        for (int frame = 0; frame < 10 * Sim.Hz; frame++)
        {
            rig.Driver.physVandamme(rig.Pawn, Sim.DeltaTime);
            pc.Rotation = new FRotator(0, frame * 400, 0);
            PlayerCalcView.FrameView(pc, out _, out FVector view, out _);
            FVector focus = rig.Pawn.Location + V(0, 0, rig.Pawn.CollisionHeight - camera.m_collisionHeight);
            maxAnchorGap = System.MathF.Max(maxAnchorGap, FVector.Dist(camera.m_prevStartPos, focus));
            maxCameraDistance = System.MathF.Max(maxCameraDistance, FVector.Dist(view, rig.Pawn.Location));
        }

        Assert.True(FVector.Dist(initial, rig.Pawn.Location) > 800);
        Assert.True(rig.Pawn.Velocity.Size() < 0.1f);
        Assert.True(maxAnchorGap < 0.01f, $"Follow anchor fell {maxAnchorGap} uu behind during traversal; camera reached {maxCameraDistance} uu from spy");
        Assert.InRange(maxCameraDistance, 100f, 400f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LookingDuringLedgePullUpKeepsFollowingTheSpy(bool crouched)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-300, 100, 0), V(300, 600, 250));
        if (crouched) level.AddBox(V(-300, 130, 400), V(300, 600, 450));
        PawnRig rig = Create(level, Doc(Ge(0, GEKind.LG, V(-300, 100, 250), V(300, 100, 250))), V(0, 60, 0));
        Until(rig, JumpPress, MovementStateNames.OnLedge, 120);
        for (int i = 0; i < 20; i++) Step(rig, default, look: true);
        Until(rig, Forward, crouched ? MovementStateNames.s_PlayerWalkingCr0H : MovementStateNames.s_PlayerWalkingSt0H,
            200, look: true);
        Assert.True(rig.Pawn.Location.Y > 100);
        for (int i = 0; i < 180; i++) Step(rig, Forward, look: true);
    }

    [Fact]
    public void LookingDuringPipeClimbAndExitKeepsFollowingTheSpy()
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-300, 230, 0), V(300, 900, 600));
        PawnRig rig = Create(level, Doc(Ge(0, GEKind.PIPE, V(0, 200, 0), V(0, 200, 600), V(0, 190, 0))), default);
        rig.Pawn.NewZipP1 = V(0, 200, 0);
        rig.Pawn.NewZipP2 = V(0, 200, 600);
        Until(rig, Forward, MovementStateNames.s_OnPipe, 150);
        Until(rig, Forward, MovementStateNames.s_PlayerWalkingSt0H, 600, look: true);
        Assert.True(rig.Pawn.Location.Y > 230);
        for (int i = 0; i < 180; i++) Step(rig, Forward, look: true);
    }

    [Fact]
    public void LookingDuringHandOverHandTransitionsKeepsFollowingTheSpy()
    {
        PawnRig rig = Create(Floor(), Doc(Ge(0, GEKind.HOH, V(-600, 0, 300), V(600, 0, 300))), default, yaw: 0);
        Until(rig, JumpPress, MovementStateNames.s_OnPipeH, 200);
        for (int i = 0; i < 20; i++) Step(rig, default, look: true);
        Until(rig, JumpPress, MovementStateNames.s_OnPipeH_4, 120, look: true);
        for (int i = 0; i < 60; i++) Step(rig, Forward, look: true);
        Until(rig, JumpPress, MovementStateNames.s_OnPipeH, 120, look: true);
        Until(rig, Crouch, MovementStateNames.s_PlayerWalkingSt0H, 300, look: true, hold: default(PlayerInputFrame));
        for (int i = 0; i < 180; i++) Step(rig, Forward, look: true);
    }

    private static PawnRig Create(CollisionDataBuilder level, TraversalDoc ges, FVector at, int yaw = 16384)
    {
        PawnRigOptions options = (ContentOptions() ?? new PawnRigOptions()) with { Hooks = new CameraMovementHooks() };
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, options);
        var facing = new FRotator(0, yaw, 0);
        rig.Place(at + V(0, 0, 89.5f), facing, facing, crouched: false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        rig.Controller.bBehindView = true;
        rig.Controller.ViewTarget = rig.Pawn;
        PlayerCalcView.FrameView(rig.Controller, out _, out _, out _);
        return rig;
    }

    private static void Until(PawnRig rig, PlayerInputFrame first, Name state, int frames, bool look = false,
        PlayerInputFrame? hold = null)
    {
        float? previousCenteredGap = null;
        for (int i = 0; i < frames; i++)
        {
            Step(rig, i == 0 ? first : hold ?? first with { Jump = false }, look);
            SPlayerCam camera = PlayerCamera.Of(rig.Controller)!;
            if (camera.m_camMode == ECamMode.ECM_SmoothTransitionCentered)
            {
                // The original's follow step includes the pawn's movement speed. Once the centred
                // transition has begun, root motion must not leave its camera further behind each tick.
                float gap = FVector.Dist(camera.m_prevStartPos, CameraBones.CenterOf(rig.Pawn) + V(0, 0, CameraK.CenteredStartLift));
                if (previousCenteredGap is { } previous)
                    Assert.True(gap <= previous + 0.01f,
                        $"Centred follow falls behind at frame {rig.Frame}, {rig.Controller.GetStateName()}: {previous} -> {gap}, velocity {rig.Pawn.Velocity}, teleported {rig.Pawn.bJustTeleported}");
                previousCenteredGap = gap;
            }
            else previousCenteredGap = null;
            if (rig.Controller.GetStateName() == state) return;
        }

        Assert.Fail($"Did not reach {state}; ended in {rig.Controller.GetStateName()}");
    }

    private static void Step(PawnRig rig, PlayerInputFrame input, bool look)
    {
        if (look)
        {
            float direction = rig.Frame % 40 < 20 ? 1f : -1f;
            input = input with { MouseX = direction * 50, MouseY = direction * 30 };
        }
        rig.Step(input);
        PlayerCalcView.FrameView(rig.Controller, out _, out FVector view, out _);
        SPlayerCam camera = PlayerCamera.Of(rig.Controller)!;
        float distance = FVector.Dist(view, rig.Pawn.Location);
        float anchor = FVector.Dist(camera.m_prevStartPos, rig.Pawn.Location);
        Assert.True(float.IsFinite(distance) && distance < 450,
            $"Camera detached at frame {rig.Frame}, {rig.Controller.GetStateName()}: camera distance {distance}, anchor {anchor}, velocity {rig.Pawn.Velocity}");
        Assert.True(anchor < 200,
            $"Follow anchor detached at frame {rig.Frame}, {rig.Controller.GetStateName()}: anchor {anchor}, velocity {rig.Pawn.Velocity}");
    }
}
