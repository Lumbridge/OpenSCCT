using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

public sealed class WaistLedgeHoldTests
{
    [Fact]
    public void FloorLimitedHoldWaitsUntilThePawnFacesTheWall()
    {
        Scene scene = Scene.Create(Floor().Build());
        var controller = new TestController();
        TestSBasePawn pawn = scene.SpawnSpy(V(0f, -37f, 89f), EPhysics.PHYS_Ledge, controller);
        pawn.TypeLedge = 1;
        pawn.ZipP1 = V(-400f, 0f, 100f);
        pawn.ZipP2 = V(400f, 0f, 100f);

        scene.Step(1);
        Assert.Equal(0, controller.TopSynchros);
        Assert.Equal(0, controller.NoGEs);
        scene.Step(10);

        Assert.True(controller.TopSynchros > 0);
        Assert.Equal(0, controller.NoGEs);
        Assert.Equal(16384, pawn.Rotation.Yaw);
        Assert.Equal(-37f, pawn.Location.Y);
        Assert.InRange(pawn.Location.Z, pawn.CollisionHeight, pawn.CollisionHeight + 1f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABlockedWaistHoldStillRaisesNoGE(bool steepSurface)
    {
        CollisionDataBuilder level = Floor();
        if (steepSurface)
        {
            level.AddRamp(-200f, 200f, -400f, 400f, -400f, 800f);
        }
        else
        {
            level.AddBox(V(-500f, -500f, 200f), V(500f, 500f, 240f));
        }

        Scene scene = Scene.Create(level.Build());
        var controller = new TestController();
        TestSBasePawn pawn = scene.SpawnSpy(V(0f, -37f, steepSurface ? 190f : 100f), EPhysics.PHYS_Ledge,
            controller, new FRotator(0, 16384, 0));
        pawn.TypeLedge = 1;
        float top = steepSurface ? 100f : 200f;
        pawn.ZipP1 = V(-400f, 0f, top);
        pawn.ZipP2 = V(400f, 0f, top);

        scene.Step(30);

        Assert.True(controller.NoGEs > 0);
        Assert.Equal(0, controller.TopSynchros);
        Assert.Equal(-37f, pawn.Location.Y);
    }
}
