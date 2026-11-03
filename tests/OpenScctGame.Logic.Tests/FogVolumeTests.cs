using System;
using System.Numerics;
using System.Text.Json;
using OpenScctGame.Render.Post;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class FogVolumeTests
{
    private static FogVolumePiece Box(float width = 100f, float fade = 0f, int open = 0) => new(Vector3.UnitX, .03f, 0f, 0f, fade, -1,
        [new(-1, 0, 0, 0), new(1, 0, 0, width), new(0, 1, 0, 100), new(0, -1, 0, 100), new(0, 0, 1, 100), new(0, 0, -1, 100)], open);

    [Fact]
    public void UniformExtinctionUsesOnlyTheRayInsideTheVolumeIncludingAnInsideCamera()
    {
        Assert.Equal(3f, FogVolumeRules.OpticalDepth(Box(), new(-20, 0, 0), Vector3.UnitX, 200), 5);
        Assert.Equal(1.5f, FogVolumeRules.OpticalDepth(Box(), new(50, 0, 0), Vector3.UnitX, 200), 5);
        Assert.Equal(0f, FogVolumeRules.OpticalDepth(Box(), new(-20, 0, 0), Vector3.UnitZ, 200));
        Assert.Equal(0f, FogVolumeRules.OpticalDepth(Box(), new(0, 101, 0), Vector3.UnitX, 200));
        Vector4 fog = FogVolumeRules.Evaluate([Box()], [], 1, Vector3.Zero, new(100, 0, 0), new(0, 0, 0, 1));
        Assert.Equal(MathF.Exp(-3), fog.W, 6);
        Assert.Equal(1f - fog.W, fog.X, 6);
    }

    [Fact]
    public void HeightDensityHalvesAndIntegratesAcrossTheFloor()
    {
        var piece = Box() with { HeightFalloff = 10 };
        Assert.Equal(1.5f, FogVolumeRules.OpticalDepth(piece, new(0, 0, 10), Vector3.UnitX, 100), 5);
        Assert.Equal(3f, FogVolumeRules.OpticalDepth(piece, new(0, 0, -10), Vector3.UnitX, 100), 5);
        float expected = .03f * (10f + 10f / MathF.Log(2f) * .75f);
        Assert.Equal(expected, FogVolumeRules.OpticalDepth(piece, new(50, 0, -10), Vector3.UnitZ, 30), 5);
    }

    [Theory]
    [InlineData(0, 3f)]
    [InlineData(1, 2.7f)]
    [InlineData(2, 2.4f)]
    public void OnlyOpenFacesFadeAndTheRampIntegratesItsFullAndPartialStretches(int open, float expected)
    {
        Assert.Equal(expected, FogVolumeRules.OpticalDepth(Box(fade: 20, open: open), Vector3.Zero, Vector3.UnitX, 100), 5);
        if (open == 2)
            Assert.Equal(.0375f, FogVolumeRules.OpticalDepth(Box(10, 20, 2), Vector3.Zero, Vector3.UnitX, 10), 6);
    }

    [Fact]
    public void ParallelOpenFaceUsesItsConstantFadeWithoutNan()
    {
        var piece = Box(fade: 20, open: 1);
        Assert.Equal(.375f, FogVolumeRules.OpticalDepth(piece, new(5, 0, 0), Vector3.UnitY, 50), 6);
    }

    [Fact]
    public void ZoneLimitedVolumeCountsTheCorrectSegmentsAndDuplicateCrossingsOnlyOnce()
    {
        var portal = new FogPortal(new(1, 0, 0, 50), 1, 2,
            [new(50, -100, -100), new(50, 100, -100), new(50, 100, 100), new(50, -100, 100)]);
        var piece = Box() with { Zone = 2 };
        Vector4 fog = FogVolumeRules.Evaluate([piece], [portal, portal], 1, Vector3.Zero, new(100, 0, 0), new(0, 0, 0, 1));
        Assert.Equal(MathF.Exp(-1.5f), fog.W, 6);
        Vector4 reverse = FogVolumeRules.Evaluate([piece], [portal], 2, new(100, 0, 0), Vector3.Zero, new(0, 0, 0, 1));
        Assert.Equal(fog, reverse);
    }

    [Fact]
    public void OverlappingVolumeColorsUseOpticalDepthAndComposeWithZoneOpacity()
    {
        FogVolumePiece red = Box(), blue = Box() with { Color = Vector3.UnitZ, Extinction = .01f };
        Vector4 fog = FogVolumeRules.Evaluate([red, blue], [], 1, Vector3.Zero, new(100, 0, 0), new(0, .5f, 0, .5f));
        float opacity = 1f - MathF.Exp(-4f), through = .5f * (1f - opacity), scale = (1f - through) / (.5f + opacity);
        Assert.Equal(new Vector4(.75f * opacity * scale, .5f * scale, .25f * opacity * scale, through), fog);
    }

    [Fact]
    public void PortalQuarterUnitExpansionIsIncludedInCrossingAndCandidateBounds()
    {
        var portal = new FogPortal(new(1, 0, 0, 50), 1, 2,
            [new(50, -10, -10), new(50, 10, -10), new(50, 10, 10), new(50, -10, 10)]);
        Assert.True(portal.Crossing(new(0, 10.2f, 0), Vector3.UnitX, 100, out _, out _, out _));
        Assert.False(portal.Crossing(new(0, 10.3f, 0), Vector3.UnitX, 100, out _, out _, out _));
        Assert.Contains(portal.CandidateVertices, p => p.Y >= 10.25f && p.Z >= 10.25f);
    }

    [Fact]
    public void ConvertedBoundsRemainSeparateFromTheOriginalPlaneShape()
    {
        using JsonDocument json = JsonDocument.Parse("""
            {"color":[1,0,0],"extinction":0.03,"floor":1,"heightFalloff":10,"edgeFade":20,"zone":2,
             "openPlanes":1,"planes":[[-1,0,0,0],[1,0,0,100],[0,1,0,100],[0,-1,0,100],[0,0,1,100],[0,0,-1,100]],
             "min":[0,-100,-100],"max":[100,100,50]}
            """);
        FogVolumePiece piece = FogVolumePiece.Read(json.RootElement);
        Assert.Equal(50f, piece.Maximum!.Value.Z);
        Assert.Equal(100f, piece.Planes[4].W);
        Assert.True(FogVolumeRules.OpticalDepth(piece, new(0, 0, 60), Vector3.UnitX, 100) > 0f);
        Assert.False((piece with { Planes = [new(0, 0, 0, 10), ..piece.Planes[1..]] }).IsValid);
    }
}
