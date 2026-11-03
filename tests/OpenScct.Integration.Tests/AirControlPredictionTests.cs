using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class AirControlPredictionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prediction_preserves_the_traversal_air_control_lock_only_until_walking(bool walkedBeforeJump)
    {
        var floor = new CollisionDataBuilder();
        floor.AddBox(new FVector(-2000, -2000, -100), new FVector(2000, 2000, 0));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, floor.Build());
        rig.Place(new FVector(0, 0, 89.5f), default, default, false);
        rig.WaitStill(120);
        rig.Controller.GotoState(MovementStateNames.s_OnPipe);
        Assert.True(rig.Controller.bBlockControlForJump);

        if (walkedBeforeJump)
        {
            // Finishing traversal plays the ordinary walking action before the next jump.
            rig.Pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H, true);
            rig.Controller.GotoState(MovementStateNames.s_PlayerWalkingSt0H);
            rig.Step(new PlayerInputFrame { Jump = true });
        }
        else
        {
            // A controlled traversal fall must retain its intentional air-control restriction.
            rig.Pawn.Location = new FVector(0, 0, 300);
            rig.Pawn.ChangeEnumAction(SBasePawn.SA_LONG_FALL_ST_0H, true);
            rig.Controller.GotoState(MovementStateNames.s_PlayerJump);
        }
        Assert.Equal(EPhysics.PHYS_Falling, rig.Pawn.Physics);

        var moves = new SBasePawnMoves(rig.Controller) { ModernCommands = true };
        FVector at = rig.Pawn.Location;
        FVector velocity = rig.Pawn.Velocity;
        moves.CapturePrediction(0);
        // A live state differs from the saved command when an older correction needs replay.
        rig.Controller.bBlockControlForJump = walkedBeforeJump;
        moves.BeginPredictionReplay();
        try
        {
            moves.RestorePrediction(0, at, velocity, EPhysics.PHYS_Falling, rig.Pawn.Base, rig.Pawn.ZoneConfig);
        }
        finally { moves.EndPredictionReplay(at); }

        rig.Step(new PlayerInputFrame { BT_Up = 1 });

        Assert.Equal(!walkedBeforeJump, rig.Controller.bBlockControlForJump);
        if (walkedBeforeJump)
        {
            Assert.True(rig.Pawn.Acceleration.X > 0f, "The next normal jump must accept forward steering after a correction.");
            Assert.True(rig.Pawn.Velocity.X > velocity.X);
        }
        else
        {
            Assert.Equal(0f, rig.Pawn.Acceleration.X);
            Assert.Equal(velocity.X, rig.Pawn.Velocity.X);
        }
    }
}
