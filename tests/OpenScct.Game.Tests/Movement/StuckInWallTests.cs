using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// Stuck states the stalled-frame bot matches found on TestBox: a client correction (MovePawn, net-moves.md rule 16)
/// whose sweep stops more than 50 uu short teleports the pawn to the target, and the original checks that teleport
/// (FarMoveActor with FindSpot and the encroachment test, editor 0x111fe4a0). Unchecked, it put a merc 22 uu into the
/// south wall by the door to room C, where the floor probe met the wall's triangles at time 0 and lifted him 1.4 uu a
/// frame until he fell and hung in PHYS_Falling (3 to 170 s, the server copy meanwhile falling on the floor with a
/// growing fall speed that turned his eventual landing into a drop attack), or 0.47 uu into a corridor wall where the
/// walking pre-probe's side box held him against it whatever way he walked along it.
/// </summary>
public sealed class StuckInWallTests
{
    private static string TestBoxDir => Path.Combine(RepoRoot, "fixtures", "testbox");

    private static readonly FVector MercExtent = V(35f, 35f, 87.5f);

    /// <summary>True when the pawn's cylinder (as box) overlaps the world.</summary>
    private static bool InWorldGeometry(PawnRig rig) =>
        rig.Collision!.PointCheck(out _, rig.Pawn, rig.Pawn.Location, MercExtent, (ETraceFlags)0x86);

    [Fact]
    public void ACorrectionBlockedByTheDoorJambDoesNotPutTheMercInTheWall()
    {
        // Seed 2016: the client walks down the corridor to room C while his server copy is held against the south
        // wall of room A beside the door; the correction's diff points from the corridor through the jamb.
        PawnRig rig = PawnRig.ForMap(PawnTeam.Merc, TestBoxDir);
        rig.Place(V(500.67734f, -42.94922f, 89.005f), new FRotator(0, -17808, 0), new FRotator(0, -17808, 0), crouched: false);
        Assert.True(rig.WaitStill(60));
        new SBasePawnMoves(rig.Controller).MovePawn(V(625.60596f, 12.577866f, 89.005f) - rig.Pawn.Location);
        Assert.False(InWorldGeometry(rig), $"the merc was put into the wall at {rig.Pawn.Location}");

        // Walking on into the wall and along it: he stays walking on the floor.
        for (int i = 0; i < 180; i++)
        {
            rig.Step(new PlayerInputFrame { BT_Up = 1, View = new FRotator(0, i < 90 ? -17808 : 0, 0) });
            Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
            Assert.InRange(rig.Pawn.Location.Z, 87.5f, 90.5f);
        }
    }

    [Fact]
    public void ACorrectionBlockedByAnotherPawnDoesNotPinTheMercToTheWall()
    {
        // Seed 2041: the correction's sweep meets the other merc almost at once and the target is 0.47 uu into the
        // corridor's east wall.
        PawnRig rig = PawnRig.ForMap(PawnTeam.Merc, TestBoxDir);
        rig.Place(V(483.47824f, -120.89978f, 89.005f), new FRotator(0, -10113, 0), new FRotator(0, -10113, 0), crouched: false);
        rig.AddMate(PawnTeam.Merc, V(540.95f, -79.54f, 0f), 0);
        Assert.True(rig.WaitStill(60));
        FVector from = rig.Pawn.Location;
        new SBasePawnMoves(rig.Controller).MovePawn(V(541.46704f, -155.84348f, 89.005f) - from);
        Assert.False(InWorldGeometry(rig), $"the merc was put into the wall at {rig.Pawn.Location}");

        // Walking along the wall (into it at 30 degrees): he slides down the corridor.
        FVector start = rig.Pawn.Location;
        for (int i = 0; i < 60; i++)
        {
            rig.Step(new PlayerInputFrame { BT_Up = 1, View = new FRotator(0, -10113, 0) });
        }

        Assert.True((rig.Pawn.Location - start).Size2D() > 100f, $"the merc moved {(rig.Pawn.Location - start).Size2D()} uu from {start}");
    }
}
