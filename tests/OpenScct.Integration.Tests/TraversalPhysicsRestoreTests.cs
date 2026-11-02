using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class TraversalPhysicsRestoreTests
{
    [Theory]
    [InlineData(EPhysics.PHYS_Walking)]
    [InlineData(EPhysics.PHYS_Falling)]
    public void RestoringPhysicsAfterARejectedRootMoveReleasesThePreviousLock(EPhysics restored)
    {
        var floor = new CollisionDataBuilder();
        floor.AddBox(new FVector(-4000, -4000, -100), new FVector(4000, 4000, 0));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, floor.Build());
        rig.Place(new FVector(0, 0, 400), default, default, false);
        var moves = new SBasePawnMoves(rig.Controller);
        rig.Pawn.ExecuteAction(SBasePawn.SA_TOPLEDGECROUCH);
        moves.MoveAutonomous(Sim.DeltaTime, default, false);
        Assert.Equal(EPhysics.PHYS_RootMotion, rig.Pawn.Physics);
        Assert.NotEqual(0, rig.Pawn.NetworkRootMotion.RootLockType);

        // ServerMoves.ProcessBigMove restores the preceding physics when a received root move is
        // too far from the authority. This must run the same exit bookkeeping as any other root exit.
        moves.SetPhysics(restored);
        Assert.Equal(restored, rig.Pawn.Physics);
        Assert.Equal(0, rig.Pawn.NetworkRootMotion.RootLockType);

        // A later climb must establish a new world anchor, rather than reuse the rejected move's lock.
        FVector nextStart = rig.Pawn.Location + new FVector(200, 0, 0);
        rig.Pawn.Location = nextStart;
        rig.Pawn.ExecuteAction(SBasePawn.SA_TOPLEDGECROUCH);
        moves.MoveAutonomous(Sim.DeltaTime, default, false);
        Assert.Equal(nextStart, rig.Pawn.vLocationBeforeRM);
    }
}
