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
}
