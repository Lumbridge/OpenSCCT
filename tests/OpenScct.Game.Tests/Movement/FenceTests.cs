using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The fence climb on a synthetic fence (spec/movement/fence.md): a climbable panel 2 thick across y = 100, 300 high,
/// with a FENCE GE along its top; the spy runs into it facing +Y.
/// </summary>
public sealed class FenceTests
{
    private const float FenceTop = 300f;

    private static (CollisionDataBuilder Level, TraversalDoc Ges) FenceLevel(bool climbable = true)
    {
        CollisionDataBuilder b = Floor();
        ushort mat = b.AddMaterial(0, climbable ? CollisionMaterialFlags.Climbable : CollisionMaterialFlags.None);
        b.AddBox(V(-500f, 100f, 0f), V(500f, 102f, FenceTop), mat);
        return (b, Doc(Ge(0, GEKind.FENCE, V(-500f, 100f, FenceTop), V(500f, 100f, FenceTop))));
    }

    private static PawnRig FenceRig(PawnTeam team = PawnTeam.Spy, bool climbable = true)
    {
        (CollisionDataBuilder level, TraversalDoc ges) = FenceLevel(climbable);
        return GeRig(team, level, ges, V(0f, -100f, 0f));
    }

    [Fact]
    public void RunningIntoAClimbableFenceGrabsIt()
    {
        PawnRig rig = FenceRig();
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Up, MovementStateNames.OnFence, 200, seen), Seen(seen));
        Assert.Contains(MovementStateNames.GrabFence, seen);
        Assert.Equal(EPhysics.PHYS_OnVerticalPlane, rig.Pawn.Physics);
    }

    [Fact]
    public void AFaceWithoutTheClimbableFlagIsNotGrabbed()
    {
        PawnRig rig = FenceRig(climbable: false);
        var seen = new List<Name>();
        Assert.False(StepUntil(rig, Up, MovementStateNames.GrabFence, 120, seen), Seen(seen));
    }

    [Fact]
    public void TheMercCannotClimbFences()
    {
        PawnRig rig = FenceRig(PawnTeam.Merc);
        var seen = new List<Name>();
        Assert.False(StepUntil(rig, Up, MovementStateNames.GrabFence, 120, seen), Seen(seen));
    }

    [Fact]
    public void ForwardClimbsAt120AndSidewaysToo()
    {
        PawnRig rig = FenceRig();
        Assert.True(StepUntil(rig, Up, MovementStateNames.OnFence, 200));
        rig.Hold(Up, 20);
        Assert.Equal(MovementK.FenceClimbSpeed, rig.Pawn.Velocity.Z, 1);
        float x = rig.Pawn.Location.X;
        rig.Hold(new PlayerInputFrame { BT_Right = 1 }, 30);
        Assert.Equal(MovementK.FenceClimbSpeed, rig.Pawn.Velocity.Size(), 1);
        // Facing +Y the right axis is -X.
        Assert.True(x - rig.Pawn.Location.X > 50f, $"moved {x - rig.Pawn.Location.X}");
    }

    [Fact]
    public void ClimbingToTheTopCrossesToTheFarSide()
    {
        PawnRig rig = FenceRig();
        Assert.True(StepUntil(rig, Up, MovementStateNames.OnFence, 200));
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Up, MovementStateNames.OnTopFence, 400, seen), Seen(seen));
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.OnFence, 400, seen), Seen(seen));
        // Turned round on the far side of the fence.
        Assert.True(rig.Pawn.Location.Y > 102f, $"y {rig.Pawn.Location.Y}");
        Assert.Equal((16384 + 32768) & 65535, rig.Pawn.Rotation.Yaw & 65535);
    }

    [Fact]
    public void CrouchLetsGoAndClimbingDownLands()
    {
        PawnRig rig = FenceRig();
        Assert.True(StepUntil(rig, Up, MovementStateNames.OnFence, 200));
        rig.Hold(Up, 40);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Crouch, MovementStateNames.s_PlayerWalkingSt0H, 300, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_PlayerJump, seen);
    }
}
