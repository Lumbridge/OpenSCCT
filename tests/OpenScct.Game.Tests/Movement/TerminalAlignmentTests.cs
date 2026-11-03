using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class TerminalAlignmentTests
{
    [Fact]
    public void Blocked_alignment_returns_control_without_snapping_through_the_wall()
    {
        var geometry = Floor();
        geometry.AddBox(V(60f, -100f, 0f), V(80f, 100f, 300f));
        PawnRig rig = Rig(PawnTeam.Spy, geometry, yaw: 0);
        AddTerminal(rig, V(180f, 0f, rig.Pawn.Location.Z));
        rig.Controller.GotoState(K.s_Computer_GoTo);

        rig.Hold(default, 240);

        Assert.Equal(MovementStateNames.s_PlayerWalkingSt0H, rig.Controller.GetStateName());
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.InRange(rig.Pawn.Location.X, -1f, 60f - rig.Pawn.CollisionRadius);
        FVector stopped = rig.Pawn.Location;
        rig.Hold(new PlayerInputFrame { BT_Down = 1 }, 30);
        Assert.True((rig.Pawn.Location - stopped).Size2D() > 10f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unblocked_alignment_still_reaches_the_terminal_and_starts_hacking(bool crouched)
    {
        PawnRig rig = Rig(PawnTeam.Spy, yaw: 0, crouched: crouched);
        AddTerminal(rig, V(180f, 0f, rig.Pawn.Location.Z));
        rig.Controller.GotoState(K.s_Computer_GoTo);

        rig.Hold(default, 300);

        Assert.Equal(K.s_Computer_Use, rig.Controller.GetStateName());
        Assert.InRange(rig.Pawn.Location.X, 115f, 117f);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
    }

    [Fact]
    public void Physics_before_the_first_input_tick_uses_the_terminal_instead_of_an_old_traversal_target()
    {
        PawnRig rig = Rig(PawnTeam.Spy, yaw: 0);
        AddTerminal(rig, V(180f, 0f, rig.Pawn.Location.Z));
        rig.Pawn.vVandammePos = V(-1000f, 0f, rig.Pawn.Location.Z);
        rig.Controller.GotoState(K.s_Computer_GoTo);

        rig.Driver.performPhysics(rig.Pawn, Sim.DeltaTime);

        Assert.InRange(rig.Pawn.Location.X, 0.1f, 4f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Interrupted_or_stalled_alignment_returns_control_and_allows_a_retry(bool interrupted)
    {
        PawnRig rig = Rig(PawnTeam.Spy, yaw: 0);
        AddTerminal(rig, V(180f, 0f, rig.Pawn.Location.Z));
        rig.Controller.GotoState(K.s_Computer_GoTo);
        rig.Step(default);
        if (interrupted)
        {
            // A stale authoritative move can restore walking physics while the use state remains active.
            rig.Pawn.Physics = EPhysics.PHYS_Walking;
            rig.Pawn.Velocity = default;
        }
        else
        {
            // No arrival or bump notification: for example an obstructed spot or a lost setup action.
            rig.Pawn.fPhysReposLinearSpeed = 0f;
        }

        rig.Hold(default, 300);

        Assert.Equal(MovementStateNames.s_PlayerWalkingSt0H, rig.Controller.GetStateName());
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.InRange(rig.Pawn.Location.X, -1f, 10f);
        Assert.False(rig.Pawn.bNoReposAtEnd);
        rig.Controller.GotoState(K.s_Computer_GoTo);
        rig.Hold(default, 180);
        Assert.Equal(K.s_Computer_Use, rig.Controller.GetStateName());
    }

    [Fact]
    public void Losing_the_terminal_during_alignment_does_not_snap_to_its_old_spot()
    {
        PawnRig rig = Rig(PawnTeam.Spy, yaw: 0);
        SComputerObjectiveTrigger terminal = AddTerminal(rig, V(180f, 0f, rig.Pawn.Location.Z));
        rig.Controller.GotoState(K.s_Computer_GoTo);
        rig.Step(default);
        terminal.Destroy();

        rig.Hold(default, 10);

        Assert.Equal(MovementStateNames.s_PlayerWalkingSt0H, rig.Controller.GetStateName());
        Assert.InRange(rig.Pawn.Location.X, -1f, 15f);
    }

    private static SComputerObjectiveTrigger AddTerminal(PawnRig rig, FVector at)
    {
        var terminal = rig.Level.SpawnActor(new SComputerObjectiveTrigger { bStatic = false, bNoDelete = false, CaptureTime = 30 }, at,
            new FRotator(0, 32768, 0))!;
        Assert.NotNull(terminal);
        return terminal;
    }
}
