using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Movement;
using OpenScct.Server;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// ReinitBecauseOfDivergences (native 1670, net-moves.md rule 17) puts the pawn back to walking through setPhysics
/// (game 0x10ae5000 calls the pawn's setPhysics slot 0xfc with PHYS_Walking), and AActor::setPhysics leaving
/// PHYS_RootMotion unlocks the mesh's root. Written straight into the field, the reinit left the root locked: the
/// play-team smoke's spy, reinitialised while he still played the helper's boost (lock 3, facing the wall), was then
/// boosted by his bot mate with that old lock, since physRootMotion only locks an unlocked root. His boost went
/// backwards along the helper's yaw and 90 uu too high, missed the ledge and dropped him on his mate.
/// </summary>
public sealed class ReinitRootMotionTests
{
    private static IRootMotionMesh Mesh(PawnRig rig) => ((ISBasePawnPhysics)rig.Pawn).RootMotionMesh!;

    private static void Steps(PawnRig rig, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Step(NoInput);
        }
    }

    [Fact]
    public void AReinitDuringARootMotionMoveUnlocksTheRoot()
    {
        PawnRig rig = Rig(PawnTeam.Spy, yaw: 49152, options: ContentOptions());
        Steps(rig, 10);
        rig.Pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_PERFORM);
        Steps(rig, 5);
        Assert.Equal(EPhysics.PHYS_RootMotion, rig.Pawn.Physics);
        Assert.Equal(3, Mesh(rig).RootLockType);

        new SBasePawnMoves(rig.Controller).ReinitBecauseOfDivergences();

        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.Equal(0, Mesh(rig).RootLockType);
    }

    [Fact]
    public void TheBoostAfterAReinitGoesUpTheClimbersFacing()
    {
        Assert.SkipWhen(ContentOptions() is null, "the converted characters (root tracks) are not in .content");
        // The helper's boost (lock 3, facing -Y) is cut by a reinit, as the smoke's placement does.
        PawnRig rig = Rig(PawnTeam.Spy, yaw: 49152, options: ContentOptions());
        Steps(rig, 10);
        rig.Pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_PERFORM);
        Steps(rig, 5);
        new SBasePawnMoves(rig.Controller).ReinitBecauseOfDivergences();
        Steps(rig, 30);

        // Later he climbs facing +Y: the boost clip's root carries him 42.8 forward and 335.7 up (lock 1).
        rig.Place(rig.Pawn.Location, new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), crouched: false);
        Steps(rig, 30);
        FVector start = rig.Pawn.Location;
        rig.Pawn.ChangeEnumAction(SBasePawn.SA_SHL_S_JUMP);
        FVector top = start;
        for (int i = 0; i < 200 && rig.Pawn.PawnAnim[0].Sequence == new Name("CoopBtwAlHpEdS"); i++)
        {
            rig.Step(NoInput);
            if (rig.Pawn.Location.Z > top.Z)
            {
                top = rig.Pawn.Location;
            }
        }

        FVector moved = top - start;
        Assert.True(moved.Y > 30f, $"the boost went {moved.Y} along his facing (expected about +42.8), moved {moved}");
        Assert.InRange(moved.Z, 300f, 345f);
    }
}
