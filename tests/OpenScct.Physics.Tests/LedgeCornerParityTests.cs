using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

public sealed class LedgeCornerParityTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 3)]
    public void CornerClipMatchesTheWallShapeInEitherDirection(bool left, bool outside, int expectedType)
    {
        // Facing +Y, the pawn's left is +X. An outside corner continues away from the pawn (+Y);
        // an inside corner returns toward it (-Y). Mirror the same shape across X for the right turn.
        float endX = left ? 400f : -400f;
        GERecord current = GE(0, GEKind.LG, V(-400f, 0f, 224f), V(400f, 0f, 224f));
        GERecord other = GE(1, GEKind.LG, V(endX, 0f, 224f), V(endX, outside ? 400f : -400f, 224f));
        Scene scene = Scene.Create(Floor().Build(), ges: [current, other]);
        var controller = new TestController();
        TestSBasePawn pawn = scene.SpawnSpy(V(left ? 330f : -330f, -39f, 107f), EPhysics.PHYS_Ledge,
            controller, new FRotator(0, 16384, 0));
        pawn.ZipP1 = current.P0;
        pawn.ZipP2 = current.P1;
        pawn.m_CurrentArmsZone = pawn.m_LedgeGrabArmsZone;
        pawn.m_CurrentArmsRadius = pawn.m_LedgeGrabArmsRadius;

        for (int i = 0; i < 120 && controller.ChangeLedges == 0; i++)
        {
            pawn.Acceleration = V(left ? 80f : -80f, 0f, 0f);
            scene.Step(1);
        }

        Assert.Equal(1, controller.ChangeLedges);
        Assert.Equal(expectedType, controller.LastChangeLedgeType);
    }
}
