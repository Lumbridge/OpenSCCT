using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class AiSearchTests
{
    private static readonly AiDifficulty Normal = AiDifficulty.For(AiSkill.Normal) with { Teamwork = 0f };

    private static (PawnRig Rig, AiWorld World, AiContact Contact) Scene(float ambient = 0.6f, bool wall = false)
    {
        var floor = Floor();
        if (wall)
        {
            floor.AddBox(V(-20f, -1000f, 0f), V(20f, 1000f, 400f));
        }

        PawnRig rig = Rig(floor, null, V(-320f, 0f, 0f), PawnTeam.Merc, yaw: 0);
        var light = new AiLightField([], new Dictionary<int, AiRgb> { [0] = new(ambient, ambient, ambient) }, _ => 0);
        AiWorld world = World(rig, V(-640f, -640f, -100f), V(960f, 640f, 300f), light: light);
        var memory = new AiMemory();
        memory.Report(new SPawnAttaque(), V(320f, 0f, 89f), 10f, 0.8f, AiContactSource.Heard);
        return (rig, world, memory.Contacts[0]);
    }

    [Fact]
    public void HiddenEnemyChangesCannotChangeTheSearchPlan()
    {
        var (rig, world, contact) = Scene();
        var first = new AiSearch(world, new object(), 1);
        Assert.True(first.Next(contact, rig.Pawn, rig.Controller, Normal, 10f, out FVector a));
        contact.Pawn.Location = V(9000f, 8000f, 7000f);
        contact.Pawn.Velocity = V(1000f, 1000f, 0f);
        contact.Pawn.Health = 0;
        var second = new AiSearch(world, new object(), 1);
        Assert.True(second.Next(contact, rig.Pawn, rig.Controller, Normal, 10f, out FVector b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void SearchInspectsLitLocationsButCannotClearDarknessOrAWall()
    {
        foreach (var setting in new[] { (Light: 0.6f, Wall: false), (Light: 0f, Wall: false), (Light: 0.6f, Wall: true) })
        {
            var (rig, world, contact) = Scene(setting.Light, setting.Wall);
            var search = new AiSearch(world, new object(), 1);
            for (int i = 0; i < 9; i++)
            {
                search.Next(contact, rig.Pawn, rig.Controller, Normal, 10f + i * 0.21f, out _);
            }

            if (setting.Light > 0f && !setting.Wall)
            {
                Assert.True(search.Inspections > 0);
            }
            else
            {
                Assert.Equal(0, search.Inspections);
            }
        }
    }

    [Fact]
    public void TeammatesAssignDifferentSearchLocations()
    {
        var (rig, world, contact) = Scene();
        AiDifficulty team = Normal with { Teamwork = 1f };
        var first = new AiSearch(world, new object(), 1);
        var second = new AiSearch(world, new object(), 1);
        Assert.True(first.Next(contact, rig.Pawn, rig.Controller, team, 10f, out FVector a));
        Assert.True(second.Next(contact, rig.Pawn, rig.Controller, team, 10f, out FVector b));
        Assert.True((a - b).Size() >= 180f, $"both teammates chose the same area: {a}, {b}");
    }

    [Fact]
    public void FailedRoutesAdvanceAtABoundedRate()
    {
        var (rig, world, contact) = Scene(0f);
        var search = new AiSearch(world, new object(), 1);
        Assert.True(search.Next(contact, rig.Pawn, rig.Controller, Normal, 10f, out FVector first));
        search.SkipGoal();
        Assert.False(search.Next(contact, rig.Pawn, rig.Controller, Normal, 10.1f, out _));
        Assert.True(search.Next(contact, rig.Pawn, rig.Controller, Normal, 10.5f, out FVector next));
        Assert.NotEqual(first, next);
        Assert.Equal(2, search.Plans);
    }

    [Fact]
    public void AnExhaustedSearchDiscountsItsEvidenceOnce()
    {
        var (rig, world, contact) = Scene(0f);
        var search = new AiSearch(world, new object(), 1);
        Assert.True(search.Next(contact, rig.Pawn, rig.Controller, Normal, 10f, out _));
        Assert.False(search.Next(contact, rig.Pawn, rig.Controller, Normal, 21f, out _));
        float confidence = contact.Confidence;
        Assert.InRange(confidence, 0.01f, 0.79f);
        search.Next(contact, rig.Pawn, rig.Controller, Normal, 22f, out _);
        Assert.Equal(confidence, contact.Confidence);
    }

    [Fact]
    public void RepeatedCalloutsAtTheSameSpotCannotRestartTheSearchDeadline()
    {
        var (rig, world, contact) = Scene(0f);
        var search = new AiSearch(world, new object(), 1);
        Assert.True(search.Next(contact, rig.Pawn, rig.Controller, Normal, 10f, out _));
        for (int i = 1; i < 12; i++)
        {
            contact.Time = 10f + i;
            search.Next(contact, rig.Pawn, rig.Controller, Normal, 10f + i, out _);
        }

        Assert.True(search.Finished);
        Assert.False(search.CanSearch(contact, 21f));
        contact.Time = 25f;
        Assert.True(search.CanSearch(contact, 25f));
    }
}
