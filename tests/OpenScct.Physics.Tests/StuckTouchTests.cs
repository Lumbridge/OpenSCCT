using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>
/// The touch pass of MoveActor (docs/PHYSICS.md) found by the stalled-frame bot matches: a spy walking past a terminal's
/// interaction cylinder through the corners of the box round it touched and untouched the interaction on every move
/// (the swept test took the box, the end-of-move overlap test the cylinder), and each pair sent two reliable calls to
/// its client (AddObjetOnClient, RemoveInteractionObjOnClient) until a stalled frame overflowed the connection's
/// reliable buffer and the server dropped the player.
/// </summary>
public sealed class StuckTouchTests
{
    [Fact]
    public void WalkingPastATriggersCornerTouchesItOnlyWhereTheCylindersMeet()
    {
        Scene s = Scene.Create(Floor().Build());
        TestTrigger trigger = s.Level.SpawnActor(new TestTrigger(), V(300f, 0f, 64f), default)!;
        s.Collision.NotifyCollisionChanged();

        // 89 uu to the side of the trigger's axis: the cylinders (35 + 64 = 99) meet only while |x - 300| < 43.4, the
        // boxes round them over |x - 300| < 99.
        TestPawn p = s.Spawn(new TestPawn(), V(0f, 89f, StandZ() + 1f), EPhysics.PHYS_Falling);
        s.Settle(p);
        Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
        p.Acceleration = V(3000f, 0f, 0f);
        s.Step(90);
        Assert.True(p.Location.X > 500f, $"x {p.Location.X}");
        Assert.Equal(1, trigger.Touches);
        Assert.Equal(1, trigger.UnTouches);
    }
}
