using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>
/// physOnPipe details read from the game build (0x10ad10c0) with REA (spec/native/physics-traversal.md physOnPipe
/// rule 6).
/// </summary>
public sealed class PipeParityTests
{
    private const float Dt = 1f / 30f;

    private static (Scene Scene, TestSBasePawn Pawn, TestController Controller) ClimbingNearTheTop(params GERecord[] more)
    {
        GERecord pipe = GE(0, GEKind.PIPE, V(0f, 0f, 0f), V(0f, 0f, 400f), V(0f, -10f, 0f));
        var ges = new GERecord[more.Length + 1];
        ges[0] = pipe;
        more.CopyTo(ges, 1);
        Scene s = Scene.Create(Floor().Build(), ges: ges);
        var c = new TestController();
        // At the hold spot facing the pipe (+Y), the hands 4.7 under the top, climbing at 300.
        TestSBasePawn p = s.SpawnSpy(V(0f, -37f, 305f), EPhysics.PHYS_OnPipe, c, new FRotator(0, 16384, 0));
        p.ZipP1 = p.NewZipP1 = pipe.P0;
        p.ZipP2 = p.NewZipP2 = pipe.P1;
        p.m_geoNormal = V(0f, -1f, 0f);
        p.Acceleration = V(0f, 0f, 300f);
        return (s, p, c);
    }

    [Fact]
    public void ClimbingPastThePipeTopStepsOff()
    {
        (Scene s, TestSBasePawn p, TestController c) = ClimbingNearTheTop();
        s.Driver.physOnPipe(p, Dt);
        Assert.Equal(1, c.TopPipes);
    }

    [Fact]
    public void AHandOverHandBarAsTheSecondGESkipsTheTopTestEvenOutOfReach()
    {
        // A bar 100 above the climb zone point: found by the search, far too high to join.
        GERecord bar = GE(1, GEKind.HOH, V(-200f, 3f, 495f), V(200f, 3f, 495f));
        (Scene s, TestSBasePawn p, TestController c) = ClimbingNearTheTop(bar);
        s.Driver.physOnPipe(p, Dt);
        Assert.Equal(0, c.HandOverHands);
        Assert.Equal(0, c.TopPipes);
    }

    [Theory]
    [InlineData(-10f, false, -35f)]
    [InlineData(0f, true, 35f)]
    [InlineData(10f, true, 35f)]
    public void TopEntryStartsBesideThePipeToCancelTheDescentClipsSidewaysMotion(float x, bool left, float spotX)
    {
        GERecord pipe = GE(0, GEKind.PIPE, V(0f, -10f, 0f), V(0f, -10f, 300f), V(0f, -20f, 0f));
        var level = Floor();
        level.AddBox(V(-500f, 0f, 0f), V(500f, 400f, 300f));
        Scene s = Scene.Create(level.Build(), ges: [pipe]);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(x, 40f, WalkZ(300f)), EPhysics.PHYS_Walking, c, new FRotator(0, -16384, 0));
        p.Acceleration = V(0f, -3000f, 0f);
        s.Driver.CheckFeetZone(p);
        Assert.Equal(1, c.TopPipes);
        Assert.Equal(left, c.LastSide);
        Near(V(spotX, 40f, p.Location.Z + 2f), p.vVandammePos);
    }

    [Fact]
    public void TopEntryHorizontalProbeUsesTheControllersShortExtent()
    {
        GERecord pipe = GE(0, GEKind.PIPE, V(0f, -10f, 0f), V(0f, -10f, 300f), V(0f, -20f, 0f));
        var level = Floor();
        level.AddBox(V(-500f, 0f, 0f), V(500f, 400f, 300f));
        // Beside the descent path: the native's 22 uu horizontal probe fits below it, while using
        // the pawn's standing half-height incorrectly rejects the move. The final downward path is clear.
        level.AddBox(V(50f, -90f, 440f), V(60f, -35f, 460f));
        Scene s = Scene.Create(level.Build(), ges: [pipe]);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(10f, 40f, WalkZ(300f)), EPhysics.PHYS_Walking, c, new FRotator(0, -16384, 0));
        p.Acceleration = V(0f, -3000f, 0f);
        s.Driver.CheckFeetZone(p);
        Assert.Equal(1, c.TopPipes);
    }
}
