using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>
/// physOnPipeH details read from the game build (0x10acff50) with REA: when TopSynchro fires and when the chain to a
/// crossing bar is looked for (spec/native/physics-traversal.md physOnPipeH rules 3, 4 and 6).
/// </summary>
public sealed class HandOverHandParityTests
{
    private const float Dt = 1f / 30f;

    /// <summary>A bar along X at height 300 and the pawn hanging under it at <paramref name="x"/>, facing +X.</summary>
    private static (Scene Scene, TestSBasePawn Pawn, TestController Controller) OnBar(float x, int yaw, float end = 400f, params GERecord[] more)
    {
        GERecord bar = GE(0, GEKind.HOH, V(-400f, 0f, 300f), V(end, 0f, 300f));
        var ges = new GERecord[more.Length + 1];
        ges[0] = bar;
        more.CopyTo(ges, 1);
        Scene s = Scene.Create(Floor().Build(), ges: ges);
        var c = new TestController();
        TestSBasePawn p = s.SpawnSpy(V(x, 0f, 300f - 128f), EPhysics.PHYS_OnPipeH, c, new FRotator(0, yaw, 0));
        p.ZipP1 = p.NewZipP1 = bar.P0;
        p.ZipP2 = p.NewZipP2 = bar.P1;
        return (s, p, c);
    }

    [Fact]
    public void TopSynchroWaitsForTheYawOfTheLastCall()
    {
        // At the hold spot but 500 off the bar's yaw: this call turns onto it, TopSynchro comes on the next one.
        (Scene s, TestSBasePawn p, TestController c) = OnBar(0f, 500);
        s.Driver.physOnPipeH(p, Dt);
        Assert.Equal(0, p.Rotation.Yaw & 0xFFFF);
        Assert.Equal(0, c.TopSynchros);

        s.Driver.physOnPipeH(p, Dt);
        Assert.Equal(1, c.TopSynchros);
    }

    [Fact]
    public void ACrossingBarIsOnlyTakenWhenMovingTowardTheNearerEnd()
    {
        // A bar joined to the end of ours (x = 130), crossing it. Moving -X, away from that nearer end: no GE search,
        // so no chain to the crossing bar.
        GERecord cross = GE(1, GEKind.HOH, V(130f, 0f, 300f), V(130f, 400f, 300f));
        (Scene s, TestSBasePawn p, TestController c) = OnBar(100f, 32768, 130f, cross);
        p.Acceleration = V(200f, 0f, 0f);
        s.Driver.physOnPipeH(p, Dt);
        Assert.Equal(0, c.OrthoPipeHs);

        // Moving +X toward it with fast input: the crossing bar within 31 is taken.
        (s, p, c) = OnBar(100f, 0, 130f, cross);
        p.Acceleration = V(200f, 0f, 0f);
        s.Driver.physOnPipeH(p, Dt);
        Assert.Equal(1, c.OrthoPipeHs);
        Near(cross.P0, p.NewZipP1);
    }
}
