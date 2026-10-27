using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>The pill line check and TraceBone (spec/native/hit-zones.md).</summary>
public sealed class HitZoneTests
{
    [Fact]
    public void ATraceThroughASphereEntersAtItsSurface()
    {
        var s = new PillShape(5, new FVector(100f, 0f, 0f), new FVector(100f, 0f, 0f), 10f, 1, true);
        Assert.True(PillLineCheck.EntryTime(s, FVector.Zero, new FVector(200f, 0f, 0f), out float t));
        Assert.Equal(0.45f, t, 4);
    }

    [Fact]
    public void ATraceAcrossACapsuleSideEntersAtTheRadius()
    {
        var s = new PillShape(2, new FVector(100f, 0f, -20f), new FVector(100f, 0f, 20f), 10f, 1, false);
        Assert.True(PillLineCheck.EntryTime(s, FVector.Zero, new FVector(200f, 0f, 0f), out float t));
        Assert.Equal(0.45f, t, 3);
    }

    [Fact]
    public void ATraceThatPassesBesideMisses()
    {
        var s = new PillShape(2, new FVector(100f, 0f, -20f), new FVector(100f, 0f, 20f), 10f, 1, false);
        Assert.False(PillLineCheck.EntryTime(s, new FVector(0f, 30f, 0f), new FVector(200f, 30f, 0f), out _));
    }

    [Fact]
    public void TheNearestPillWinsAtEqualPriority()
    {
        var shapes = new List<PillShape>
        {
            new(9, new FVector(150f, 0f, -20f), new FVector(150f, 0f, 20f), 12.5f, 1, false),
            new(2, new FVector(100f, 0f, -20f), new FVector(100f, 0f, 20f), 12.5f, 1, false),
        };
        Assert.True(PillLineCheck.Check(shapes, FVector.Zero, new FVector(300f, 0f, 0f), out _, out FVector at, out FVector n, out int pill));
        Assert.Equal(2, pill);
        Assert.Equal(87.5f, at.X, 2);
        Assert.Equal(-1f, n.X, 3);
    }

    [Fact]
    public void TheEstimatedPoseHasTheFifteenPillsWithTheTableRadii()
    {
        var scene = new CombatScene();
        (_, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        var shapes = new List<PillShape>();
        EstimatedPillPose.Instance.GetPills(spy, shapes);
        Assert.Equal(15, shapes.Count);
        foreach (PillShape s in shapes)
        {
            Assert.Equal(PillTable.Rows[s.Pill - 1].Radius, s.Radius);
        }
    }

    [Fact]
    public void AHeadHighShotOnAPawnFindsTheHeadPill()
    {
        var scene = new CombatScene();
        (_, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        (_, SBasePawn merc) = scene.Player(false, new FVector(-500f, 0f, 100f));
        float headZ = spy.Location.Z + 0.87f * spy.CollisionHeight;
        BoneTraceResult hit = TraceHitZones.Instance.TraceBone(merc, new FVector(500f, 0f, headZ), new FVector(-400f, 0f, headZ), true);
        Assert.True(hit.bHit);
        Assert.Same(spy, hit.Actor);
        Assert.Equal(1, hit.PillTag);
    }

    [Fact]
    public void TheShooterIsNeverHitByHisOwnTrace()
    {
        var scene = new CombatScene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        BoneTraceResult hit = TraceHitZones.Instance.TraceBone(merc, new FVector(500f, 0f, 150f), new FVector(-50f, 0f, 150f), true);
        Assert.False(hit.bHit);
    }
}
