using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

public sealed class FactoryDiggerTests
{
    [Fact]
    public void DiggerBreachRemovesTheIntactWallCollision()
    {
        string folder = Path.Combine(Movement.MovementTestKit.RepoRoot, ".content", "svmap", "FactE");
        Assert.SkipWhen(!Directory.Exists(folder), "FactE has not been converted into .content/svmap.");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, folder);
        var wall = Assert.IsType<SGamePlayObject>(rig.Map!.FindActor("SGamePlayObject294"));
        rig.Pawn.Physics = EPhysics.PHYS_None;
        rig.Controller.GotoState("TestNoState");
        rig.Pawn.Location = new FVector(-1400, -1152, -719);
        var colliders = new List<int>();
        rig.Collision!.GetActorColliders(wall, colliders);
        int[] intact = colliders.Where(ci => rig.Collision.GetCollider(ci).Enabled).ToArray();
        Assert.NotEmpty(intact);
        FVector start = new(-4500, -6700, 600);
        FVector end = new(-4500, -7100, 600);
        FVector extent = new(35, 35, 87.5f);
        Assert.True(rig.Collision.LineCheck(out var hit, rig.Pawn, end, start, extent, ETraceFlags.TRACE_AllBlocking));
        Assert.Same(wall, hit.Actor);
        // The digger's wall_out event swaps this wall to its breached mesh.
        wall.eventTrigger(rig.Pawn, rig.Pawn);
        Assert.Equal(new AssetRef("FAC_STM.mur_apres"), wall.StaticMesh);
        Assert.All(intact, ci => Assert.False(rig.Collision.GetCollider(ci).Enabled,
            $"intact collider {ci} still enabled: {rig.Collision.GetCollider(ci)}"));
        Assert.False(rig.Collision.LineCheck(out hit, rig.Pawn, end, start, extent, ETraceFlags.TRACE_AllBlocking),
            $"breach blocked by {hit.Actor} at {hit.Location}");
        wall.eventUnTrigger(rig.Pawn, rig.Pawn);
        Assert.True(rig.Collision.LineCheck(out hit, rig.Pawn, end, start, extent, ETraceFlags.TRACE_AllBlocking));
        Assert.Same(wall, hit.Actor);
    }
}
