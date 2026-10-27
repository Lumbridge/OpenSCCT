using System.IO;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Content.Collision;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>
/// A box whose face lies exactly on a world triangle's bounds (a touching contact, zero overlap) is not blocked by
/// that triangle while it moves parallel to the contact. Network moves carry locations rounded to whole units, so the
/// server's copy of a pawn that walks along a wall on the unit grid can sit exactly on the wall's plane (SteeE: a
/// spy walking along the wall at x = -1664 is placed at x = -1699 with radius 35); geometry behind the wall must not
/// stop it.
/// </summary>
public sealed class TouchingContactTests
{
    private const ETraceFlags World = ETraceFlags.TRACE_World;
    private static readonly FVector Pawn = new(35f, 35f, 87.5f);

    /// <summary>A wall filling x 0..100 and, behind it, a ceiling slope whose lower edge lies on the wall's plane.</summary>
    private static CollisionWorld WallWithASlopeBehind()
    {
        var b = new CollisionDataBuilder();
        b.AddBox(V(0f, -500f, -200f), V(100f, 500f, 400f));
        // Facing -Y and down (normal (0, -0.447, -0.894), as at SteeE), x 0..60, beginning at the wall's plane.
        b.AddTriangle(V(0f, 0f, 150f), V(60f, 0f, 150f), V(0f, 100f, 100f));
        return CollisionWorld.Build(b.Build());
    }

    [Fact]
    public void ABoxTouchingAWallRisesPastGeometryBehindIt()
    {
        CollisionWorld w = WallWithASlopeBehind();
        Assert.False(w.LineCheck(out FCheckResult hit, null, V(-35f, 30f, 60f), V(-35f, 30f, -40f), Pawn, World),
            $"blocked at {hit.Location} by normal {hit.Normal}");
    }

    [Fact]
    public void ABoxTouchingAWallSlidesAlongIt()
    {
        CollisionWorld w = WallWithASlopeBehind();
        Assert.False(w.LineCheck(out FCheckResult hit, null, V(-35f, 40f, 0f), V(-35f, -40f, 0f), Pawn, World),
            $"blocked at {hit.Location} by normal {hit.Normal}");
    }

    [Fact]
    public void ABoxTouchingAWallIsStillStoppedByIt()
    {
        CollisionWorld w = WallWithASlopeBehind();
        Assert.True(w.LineCheck(out FCheckResult hit, null, V(-25f, 0f, 0f), V(-35f, 0f, 0f), Pawn, World));
        Assert.Equal(0f, hit.Time);
        Near(V(-1f, 0f, 0f), hit.Normal);
    }

    [Fact]
    public void ABoxAHairIntoTheWallIsStoppedByTheSlope()
    {
        // Overlapping the wall by 0.01: the slope behind the wall is reached as before.
        CollisionWorld w = WallWithASlopeBehind();
        Assert.True(w.LineCheck(out _, null, V(-34.99f, 30f, 60f), V(-34.99f, 30f, -40f), Pawn, World));
    }

    /// <summary>A floor at z = 0 and a wall whose face is the plane y = 100.</summary>
    private static Scene WallScene()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-2000f, 100f, 0f), V(2000f, 200f, 300f));
        return Scene.Create(b.Build());
    }

    [Fact]
    public void APawnStandingExactlyAgainstAWallWalksAlongIt()
    {
        // The walking pre-probe's side boxes (R - 0.2, 0.2 to the side) reach exactly the pawn's radius: on the wall's
        // plane the second probe met the wall itself and stopped the pawn dead.
        Scene s = WallScene();
        TestPawn p = s.SpawnPawn(V(0f, 0f, StandZ() + 1f));
        s.Settle(p);
        p.Location = V(0f, 100f - 35f, p.Location.Z);
        s.Collision.NotifyCollisionChanged();
        p.Acceleration = V(3000f, 3000f, 0f);
        s.Step(60);
        Assert.True(p.Location.X > 200f, $"stuck at {p.Location}");
        Assert.InRange(p.Location.Y, 100f - 35f - 1f, 100f - 35f);
    }

    [Fact]
    public void APawnJumpingExactlyAgainstAWallKeepsItsAirControl()
    {
        Scene s = WallScene();
        TestPawn p = s.SpawnPawn(V(0f, 0f, StandZ() + 1f));
        s.Settle(p);
        p.Location = V(0f, 100f - 35f, p.Location.Z);
        s.Collision.NotifyCollisionChanged();
        p.Acceleration = V(3000f, 3000f, 0f);
        s.Driver.MyDoJump(p);
        s.Step(20);
        Assert.Equal(EPhysics.PHYS_Falling, p.Physics);
        Assert.True(p.Location.X > 10f, $"no air control along the wall: {p.Location} v {p.Velocity}");
    }

    /// <summary>The place in SteeE where a server copy on the rounded location stopped rising (local only).</summary>
    [Fact]
    public void TheSteeEWallAtXMinus1664LetsATouchingPawnRise()
    {
        string root = TestPaths.SteeE;
        if (!Directory.Exists(root))
        {
            Assert.Skip("SteeE is not built (local only).");
        }

        CollisionWorld w = CollisionWorld.Build(SvMap.Load(root).Collision);
        Assert.False(w.LineCheck(out FCheckResult hit, null, V(-1699f, 1631f, -41.6f), V(-1699f, 1631f, -50.33f), Pawn, World),
            $"blocked at {hit.Location} by normal {hit.Normal}");
        Assert.True(w.LineCheck(out FCheckResult wall, null, V(-1698f, 1631f, -50.33f), V(-1699f, 1631f, -50.33f), Pawn, World));
        Assert.Equal(0f, wall.Time);
    }
}
