using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Game.Tests.Combat;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The stick up and down bits of a spy that climbs (spec/native/input.md rule 3.2): in the climbing modes the stick's
/// direction is compared on the screen after it is normalised, so the speed mode does not matter. A slow spy's key
/// gives a stick of 2400, below the 3000 the walking modes compare aForward with, and must still climb a ladder.
/// </summary>
public sealed class SlowClimbTests
{
    [Theory]
    [InlineData(0, 0, true, true)]
    [InlineData(15000, 16384, true, true)]
    [InlineData(16384, 16384, true, false)]
    [InlineData(16384, 16384, false, true)]
    public void ProjectionUsesTheNativesIntegerWeightAtVerticalPitch(int pitch, int pawnYaw, bool forwardKey, bool climbsUp)
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(true, default, pawnYaw);
        pawn.Physics = EPhysics.PHYS_OnLadder;
        pc.Rotation = new FRotator(pitch, 0, 0);
        pc.BT_Up = (byte)(forwardKey ? 1 : 0);
        pc.BT_Left = (byte)(forwardKey ? 0 : 1);
        bool previousUse = false;
        ControlInput.Compute(pc, Sim.DeltaTime, ref previousUse);
        Assert.Equal(climbsUp, (pc.iMove & SPlayerController.cMoveUp) != 0);
    }
    private const float Top = 400f;

    private static PawnRig SlowSpyAtLadder()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 105f, 0f), V(300f, 900f, Top));
        TraversalDoc ges = Doc(Ge(0, GEKind.LADDER, V(0f, 100f, 0f), V(0f, 100f, Top), V(0f, 90f, 0f)));
        PawnRig rig = GeRig(PawnTeam.Spy, b, ges, V(0f, 20f, 0f));
        rig.SetFastSpeed(false);
        rig.WaitStill(30);
        return rig;
    }

    [Fact]
    public void A_slow_spy_climbs_a_ladder_with_the_forward_key()
    {
        PawnRig rig = SlowSpyAtLadder();
        Assert.Equal(MovementK.SpeedModeSlow, rig.Controller.GetSpeedMode());
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        float z = rig.Pawn.Location.Z;
        rig.Hold(Up, 60);
        Assert.Equal(SPlayerController.cMoveUp, rig.Controller.iMove & SPlayerController.cMoveUp);
        Assert.True(rig.Pawn.Location.Z - z > 100f, $"climbed {rig.Pawn.Location.Z - z:F0} uu in 1 s");
    }

    [Fact]
    public void A_slow_spy_climbs_down_a_ladder_with_the_back_key()
    {
        PawnRig rig = SlowSpyAtLadder();
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 90);
        float z = rig.Pawn.Location.Z;
        rig.Hold(Down, 45);
        Assert.Equal(SPlayerController.cMoveDown, rig.Controller.iMove & SPlayerController.cMoveDown);
        Assert.True(z - rig.Pawn.Location.Z > 60f, $"went down {z - rig.Pawn.Location.Z:F0} uu in 0.75 s");
    }

    [Fact]
    public void Walking_keeps_the_3000_threshold_so_a_slow_spy_sets_no_up_bit()
    {
        CollisionDataBuilder b = Floor();
        PawnRig rig = GeRig(PawnTeam.Spy, b, null, V(0f, 0f, 0f));
        rig.SetFastSpeed(false);
        rig.Hold(Up, 10);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.Equal(0, rig.Controller.iMove & SPlayerController.cMoveUp);
    }
}
