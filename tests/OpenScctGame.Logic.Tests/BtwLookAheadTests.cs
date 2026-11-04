using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScctGame.Cameras;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class BtwLookAheadTests
{
    [Theory]
    [InlineData(-3000, 0)]
    [InlineData(-400, 0)]
    [InlineData(4000, 13000)]
    public void PendingMouseDoesNotJumpAheadOfCornerRecentering(int yawOffset, int pitch)
    {
        var floor = new CollisionDataBuilder();
        floor.AddBox(new FVector(-1000, -1000, -64), new FVector(1000, 1000, 0));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, floor.Build());
        rig.WaitStill(60);
        var pc = rig.Controller;
        pc.GotoState(MovementStateNames.s_BTWFireStandRight);
        rig.Pawn.bWeaponTransit = true;
        pc.bBehindView = false;
        pc.ViewTarget = rig.Pawn;
        int reference = rig.Pawn.Rotation.Yaw + 32768;
        pc.Rotation = new FRotator(pitch, reference + yawOffset, 0);
        var mouse = new MouseLook();
        var look = new LookAhead();
        mouse.Apply(pc, 1f, 0f);
        look.BeforeTick(pc, mouse, 1f, 0f);
        rig.Step(new PlayerInputFrame { MouseX = pc.aTurn });
        Assert.Equal(MovementStateNames.s_BTWFireStandRight, pc.GetStateName());
        Assert.True(pc.TryGetBtwViewLimits(out _, out _));
        look.AfterTick(pc, new ViewSample(rig.Frame, rig.Pawn.Location, pc.Rotation, 90f, true));

        Assert.False(look.Active);
        Assert.Equal(default, look.Offset(pc, mouse, .01f, 0f, .5f));
        Assert.Equal(default, look.Pending);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PendingCornerAimStaysSmoothAndInsideTheViewWindow(bool left, bool crouched, bool firing)
    {
        var floor = new CollisionDataBuilder();
        floor.AddBox(new FVector(-1000, -1000, -64), new FVector(1000, 1000, 0));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, floor.Build());
        var pc = rig.Controller;
        var facing = new FRotator(0, 32768, 0);
        rig.Place(new FVector(0, 0, 90), facing, default, crouched);
        rig.WaitStill(60);
        Name state = (firing, left, crouched) switch
        {
            (true, true, true) => MovementStateNames.s_BTWFireCrouchLeft,
            (true, true, false) => MovementStateNames.s_BTWFireStandLeft,
            (true, false, true) => MovementStateNames.s_BTWFireCrouchRight,
            (true, false, false) => MovementStateNames.s_BTWFireStandRight,
            (false, true, true) => MovementStateNames.s_BTW_CrouchLeft,
            (false, true, false) => MovementStateNames.s_BTW_StandLeft,
            (false, false, true) => MovementStateNames.s_BTW_CrouchRight,
            (false, false, false) => MovementStateNames.s_BTW_StandRight,
        };
        pc.GotoState(state);
        // Keep the pose while exercising its real PlayerMove without an animation or corner-clearance fixture.
        rig.Pawn.bWeaponTransit = true;
        rig.Pawn.WeaponLoaded = (byte)(firing ? left ? 2 : 7 : 0);
        pc.bBehindView = false;
        pc.ViewTarget = rig.Pawn;
        pc.Rotation = new FRotator(0, left ? -4000 : 4000, 0);
        var mouse = new MouseLook();
        var look = new LookAhead();

        foreach (float direction in new[] { 1f, -1f })
        for (int tick = 0; tick < 65; tick++)
        {
            FRotator before = pc.Rotation;
            float counts = direction * 120f;
            mouse.Apply(pc, counts, 8f);
            look.BeforeTick(pc, mouse, counts, 8f);
            rig.Step(new PlayerInputFrame { MouseX = pc.aTurn, MouseY = pc.aLookUp });
            Assert.Equal(state, pc.GetStateName());
            var current = new ViewSample(rig.Frame, rig.Pawn.Location, pc.Rotation, 90f, true);
            look.AfterTick(pc, current);
            Assert.True(look.Active, $"look-ahead disabled in {state} at tick {tick}");
            Assert.True(pc.TryGetBtwViewLimits(out CameraWindow window, out int reference));

            var previous = current with { Rotation = before };
            FRotator blended = ViewBlend.Blend(previous, current, .5f).Rotation;
            FRotator offset = look.Offset(pc, mouse, direction * 100000f, 100000f, .5f);
            int yaw = FRotator.NormalizeAxis(blended.Yaw + offset.Yaw - reference);
            int pitch = FRotator.NormalizeAxis(blended.Pitch + offset.Pitch);
            Assert.InRange(yaw, window.MinYaw, window.MaxYaw);
            Assert.InRange(pitch, window.MinPitch, window.MaxPitch);

            // The rendered pending turn becomes the next simulated turn, including at either limit.
            FRotator predicted = LookAhead.Turn(pc, mouse, direction * 2f, -2f, Sim.DeltaTime);
            mouse.Apply(pc, direction * 2f, -2f);
            before = pc.Rotation;
            rig.Step(new PlayerInputFrame { MouseX = pc.aTurn, MouseY = pc.aLookUp });
            Assert.Equal(predicted.Yaw, FRotator.NormalizeAxis(pc.Rotation.Yaw - before.Yaw));
            Assert.Equal(predicted.Pitch, FRotator.NormalizeAxis(pc.Rotation.Pitch - before.Pitch));
        }
    }
}
