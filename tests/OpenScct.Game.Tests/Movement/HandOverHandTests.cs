using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// Hand over hand on a synthetic bar (spec/movement/pipe-horizontal.md): an HOH bar 300 above the floor along X from
/// -600 to 600 at y = 0; the spy jumps into it from under it.
/// </summary>
public sealed class HandOverHandTests
{
    private const float BarZ = 300f;

    private static PawnRig BarRig(float x = 0f)
    {
        CollisionDataBuilder b = Floor();
        TraversalDoc ges = Doc(Ge(0, GEKind.HOH, V(-600f, 0f, BarZ), V(600f, 0f, BarZ)));
        return GeRig(PawnTeam.Spy, b, ges, V(x, 0f, 0f), yaw: 0);
    }

    private static PawnRig Hanging(float x = 0f)
    {
        PawnRig rig = BarRig(x);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_OnPipeH, 200, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_GrabPipH, seen);
        rig.Hold(NoInput, 20);
        return rig;
    }

    [Fact]
    public void JumpingIntoABarGrabsItAndHangsUnderIt()
    {
        PawnRig rig = Hanging();
        Assert.Equal(EPhysics.PHYS_OnPipeH, rig.Pawn.Physics);
        Assert.Equal(PipHFire_.NoPipHFire, rig.Pawn.PipHFire);
        // The hands on the bar: the pawn hangs m_HandOverHandArmsZone.Z (128) below it.
        Assert.InRange(rig.Pawn.Location.Z, BarZ - 128f - 1f, BarZ - 128f + 1f);
        Assert.InRange(rig.Pawn.Location.Y, -1f, 1f);
    }

    [Fact]
    public void ForwardMovesAlongTheBarAt126()
    {
        PawnRig rig = Hanging();
        float x0 = rig.Pawn.Location.X;
        rig.Hold(Up, 60);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_OnPipeH));
        Assert.Equal(MovementK.HohMoveRequest, rig.Pawn.Velocity.Size2D(), 1);
        Assert.InRange(SvmMath.Abs(rig.Pawn.Location.X - x0), 120f, 130f);
    }

    [Fact]
    public void JumpSwingsTheLegsOverAndBack()
    {
        PawnRig rig = Hanging();
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_OnPipeH_4, 120, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_PipH_TR2_4, seen);
        // The legs-over cylinder: half height Pipe4Height, the top kept, zone configuration 3.
        Assert.Equal(rig.Pawn.Pipe4Height, rig.Pawn.CollisionHeight);
        Assert.True(rig.Pawn.bIsPipe4Decal);
        Assert.Equal(3, rig.Pawn.ZoneConfig);
        rig.Hold(Up, 30);
        Assert.Equal(MovementK.HohLegsOverMoveRequest, rig.Pawn.Velocity.Size2D(), 1);
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_OnPipeH, 120, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_PipH_TR2_2, seen);
        Assert.Equal(87.5f, rig.Pawn.CollisionHeight);
        Assert.False(rig.Pawn.bIsPipe4Decal);
    }

    [Fact]
    public void TheLegsOverNeedsBarAhead()
    {
        // Facing +X 60 from the bar's end: the hands' point 95 ahead is past it.
        PawnRig rig = Hanging(x: 540f);
        rig.Step(JumpPress);
        rig.Hold(NoInput, 30);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_OnPipeH));
        Assert.False(rig.Pawn.bIsPipe4Decal);
    }

    [Fact]
    public void BackTurnsRound()
    {
        PawnRig rig = Hanging();
        int yaw = rig.Pawn.Rotation.Yaw & 65535;
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Down, MovementStateNames.s_PipH_Return, 10, seen), Seen(seen));
        Assert.Equal((yaw + 32768) & 65535, rig.Pawn.Rotation.Yaw & 65535);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_OnPipeH, 120, seen), Seen(seen));
    }

    [Fact]
    public void HoldingCrouchDropsFromTheBar()
    {
        PawnRig rig = Hanging();
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Crouch, MovementStateNames.s_PlayerWalkingSt0H, 300, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_PlayerJump, seen);
        Assert.InRange(rig.Pawn.Location.Z, 87f, 92f);
    }
}
