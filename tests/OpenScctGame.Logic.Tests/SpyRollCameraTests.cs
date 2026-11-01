using OpenScct.Core;
using OpenScct.Game.Camera;
using OpenScct.Game.Movement;
using OpenScctGame.Cameras;
using OpenScctGame.Play.Session;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SpyRollCameraTests
{
    [Fact]
    public void PendingMouseDoesNotTurnANewLockedRoll()
    {
        PawnRig rig = ReadySpy();
        var look = new LookAhead();
        var mouse = new MouseLook();
        StepView(rig, look, mouse, 12f, roll: false);
        Assert.True(look.Active);

        StepView(rig, look, mouse, 0f, roll: true);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerRollingUp));
        Assert.Equal(default, look.Offset(rig.Controller, mouse, 12f, 0f, 0.5f));
        Assert.Equal(0f, look.OrbitDistance(rig.Controller));
    }

    [Fact]
    public void DisablingRollFreeLookStopsPendingMouseBeforeTheNextMouseTick()
    {
        PawnRig rig = ReadySpy();
        rig.Controller.SpyRollFreeLook = true;
        var look = new LookAhead();
        var mouse = new MouseLook();
        StepView(rig, look, mouse, 12f, roll: true);
        StepView(rig, look, mouse, 12f, roll: false);
        Assert.True(look.Active);
        rig.Controller.SpyRollFreeLook = false;

        StepView(rig, look, mouse, 0f, roll: false);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerRollingUp));
        Assert.Equal(default, look.Offset(rig.Controller, mouse, 12f, 0f, 0.5f));
        Assert.Equal(0f, look.OrbitDistance(rig.Controller));
    }

    [Fact]
    public void PerspectiveSwitchesDuringARollKeepTheCameraNearTheSpyAndTheRollStraight()
    {
        PawnRig rig = ReadySpy();
        var pc = rig.Controller;
        pc.SpyRollFreeLook = true;
        var look = new LookAhead();
        var mouse = new MouseLook();
        StepView(rig, look, mouse, 0f, roll: true);
        float initialX = rig.Pawn.Location.X;
        float initialY = rig.Pawn.Location.Y;
        pc.m_camera!.ActivateSmooth(0.1f);

        for (int i = 0; i < 3; i++)
        {
            pc.SetSpyFirstPerson(true);
            ViewSample eyes = StepView(rig, look, mouse, 100f, roll: false);
            Assert.True(eyes.SpyFirstPerson);
            Assert.True(look.Active);
            Assert.Equal(0f, look.OrbitDistance(pc));

            pc.SetSpyFirstPerson(false);
            ViewSample behind = StepView(rig, look, mouse, 100f, roll: false);
            Assert.False(behind.SpyFirstPerson);
            Assert.True(look.Active);
            Assert.True(look.OrbitDistance(pc) > 0f);
            Assert.InRange((behind.Location - rig.Pawn.Location).Size(), 1f, 500f);
            Assert.Equal(pc.Rotation.Yaw, behind.Rotation.Yaw);
            Assert.True(pc.IsInState(MovementStateNames.s_PlayerRollingUp));
        }

        Assert.InRange(rig.Pawn.Location.X - initialX, -5f, 5f);
        Assert.True(rig.Pawn.Location.Y > initialY);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RollingLookAheadTracksTheOptionalCameraTurn(bool crouched, bool freeLook)
    {
        var floor = new CollisionDataBuilder();
        floor.AddBox(new FVector(-6000f, -6000f, -64f), new FVector(6000f, 6000f, 0f));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, floor.Build(), options: new PawnRigOptions
        {
            Hooks = new CameraMovementHooks(),
        });
        var pc = rig.Controller;
        var facing = new FRotator(0, 16384, 0);
        rig.Place(new FVector(0f, 0f, (crouched ? rig.Pawn.CrouchHeight : 87.5f) + 2f), facing, facing, crouched);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        pc.SpyRollFreeLook = freeLook;
        for (int i = 0; i < 40; i++) rig.Step(new PlayerInputFrame { BT_Up = 1 });
        rig.Step(new PlayerInputFrame { BT_Up = 1, bRoll = 1 });
        Assert.True(pc.IsInState(crouched ? MovementStateNames.s_PlayerRolling : MovementStateNames.s_PlayerRollingUp));

        var look = new LookAhead();
        var mouse = new MouseLook();
        PlayerCalcView.FrameView(pc, out _, out _, out _);
        int initialYaw = pc.Rotation.Yaw;
        for (int i = 0; i < 3; i++)
        {
            mouse.Apply(pc, 12f, 0f);
            look.BeforeTick(pc, mouse, 12f, 0f);
            rig.Step(new PlayerInputFrame { MouseX = pc.aTurn, MouseY = pc.aLookUp });
            PlayerCalcView.FrameView(pc, out _, out FVector location, out FRotator rotation);
            look.AfterTick(pc, new ViewSample(rig.Frame, location, rotation, pc.FovAngle, true));
            Assert.Equal(freeLook, look.Active);
        }

        FRotator pending = look.Offset(pc, mouse, 2f, 0f, 0.5f);
        if (freeLook)
        {
            Assert.NotEqual(initialYaw, pc.Rotation.Yaw);
            Assert.True(pending.Yaw > 0);
            Assert.True(look.OrbitDistance(pc) > 0f);
        }
        else
        {
            Assert.Equal(initialYaw, pc.Rotation.Yaw);
            Assert.Equal(default, pending);
            Assert.Equal(0f, look.OrbitDistance(pc));
        }
    }

    private static PawnRig ReadySpy()
    {
        var floor = new CollisionDataBuilder();
        floor.AddBox(new FVector(-6000f, -6000f, -64f), new FVector(6000f, 6000f, 0f));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, floor.Build(), options: new PawnRigOptions
        {
            Hooks = new CameraMovementHooks(),
        });
        var facing = new FRotator(0, 16384, 0);
        rig.Place(new FVector(0f, 0f, 89.5f), facing, facing, crouched: false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        for (int i = 0; i < 40; i++) rig.Step(new PlayerInputFrame { BT_Up = 1 });
        return rig;
    }

    private static ViewSample StepView(PawnRig rig, LookAhead look, MouseLook mouse, float counts, bool roll)
    {
        var pc = rig.Controller;
        mouse.Apply(pc, counts, 0f);
        look.BeforeTick(pc, mouse, counts, 0f);
        rig.Step(new PlayerInputFrame { BT_Up = 1, bRoll = roll ? (byte)1 : (byte)0, MouseX = pc.aTurn });
        PlayerCalcView.FrameView(pc, out _, out FVector location, out FRotator rotation);
        var view = new ViewSample(rig.Frame, location, rotation, pc.FovAngle, true, pc.SpyFirstPersonActive);
        look.AfterTick(pc, view);
        return view;
    }
}
