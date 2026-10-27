using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The big move's snap to the client's location (net-moves.md rule 13, game 0x10aae660 and 0x10ab1670): a sweep, then
/// when blocked a horizontal sweep, then a sweep with the full remaining offset unless the base is a mover that moved
/// in the last 3 s (fLastTimeMoverMoves). The mover test is that window, not the mover's current velocity.
/// </summary>
public sealed class BigMoveSnapTests
{
    /// <summary>A floor and a low ceiling over x below 50, so a rising diagonal snap is blocked at once.</summary>
    private static PawnRig CeilingRig()
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-400f, -400f, 185f), V(50f, 400f, 300f));
        return Rig(PawnTeam.Merc, level);
    }

    private static FVector SnapTarget(PawnRig rig) => rig.Pawn.Location + V(100f, 0f, 50f);

    [Fact]
    public void ABlockedSnapEndsWithTheFullOffset()
    {
        PawnRig rig = CeilingRig();
        FVector target = SnapTarget(rig);
        new SBasePawnMoves(rig.Controller).SnapTo(target);
        Assert.Equal(target.X, rig.Pawn.Location.X, 1);
        Assert.Equal(target.Z, rig.Pawn.Location.Z, 1);
    }

    [Fact]
    public void ABlockedSnapOnARecentlyMovedMoverSkipsTheFullOffset()
    {
        PawnRig rig = CeilingRig();
        var mover = new Mover { fLastTimeMoverMoves = rig.Level.TimeSeconds - 1f };
        rig.Pawn.SetBase(mover, bNotifyActor: false);
        Assert.Equal(0f, mover.Velocity.Z);
        FVector target = SnapTarget(rig);
        new SBasePawnMoves(rig.Controller).SnapTo(target);

        // The horizontal sweep reached x; the last sweep (the rest of the rise) was not made.
        Assert.Equal(target.X, rig.Pawn.Location.X, 1);
        Assert.True(rig.Pawn.Location.Z < target.Z - 20f, $"the snap rose to {rig.Pawn.Location.Z} (target {target.Z})");
    }
}
