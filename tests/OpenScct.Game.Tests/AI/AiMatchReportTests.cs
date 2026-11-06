using System;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Game.Tests.AI;

[Collection(AiMatchCollection.Name)]
public sealed class AiMatchReportTests
{
    private static readonly MapCatalog Maps = MapCatalog.CreateDefault();
    private static readonly Lazy<NetClassRegistry> Classes = new(() => NetClasses.Create());

    private static AiBotMatch Create(out SMission mission, bool chained, int minimum, int random = 0, bool nested = false)
    {
        Assert.SkipWhen(Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = Maps, Classes = Classes.Value, Bots = [],
        });
        var level = match.Host.World.Level;
        foreach (SMission old in level.AllActors<SMission>())
        {
            old.Desactivate();
            old.TotalObjectivesLeft = 0;
            old.GameMode = EGameMode.GM_Undefined;
        }

        level.BeginLoad();
        mission = level.SpawnActor(new SMission
        {
            GameMode = EGameMode.GM_Multi, bChained = chained, MinimumObjectives = minimum, RandomObjectives = random,
        }, default, default)!;
        for (int i = 0; i < 3; i++)
        {
            mission.Objectives.Add(level.SpawnActor(new SObjective(), default, default)!);
        }

        if (nested)
        {
            var sub = level.SpawnActor(new SMission { GameMode = EGameMode.GM_Undefined, bChained = true }, default, default)!;
            sub.Objectives.Add(mission.Objectives[1]);
            sub.Objectives.Add(mission.Objectives[2]);
            mission.Objectives.RemoveAt(2);
            mission.Objectives[1] = sub;
        }

        level.EndLoad();
        match.Host.Game!.GameReplicationInfo!.NumObjectives = 0;
        mission.Reset();
        return match;
    }

    [Fact]
    public void ChainedObjectivesCountOnlyCompletedStages()
    {
        using var match = Create(out SMission mission, chained: true, minimum: 3);
        // Later stages deliberately carry ObjectiveTaken=true while deactivated.
        Assert.True(mission.Objectives[1]!.ObjectiveTaken);
        for (int completed = 0; completed <= 3; completed++)
        {
            AiBotMatchReport report = match.Report();
            Assert.Equal(3, report.ObjectivesNeeded);
            Assert.Equal(completed, report.ObjectivesTaken);
            if (completed < 3)
            {
                mission.Objectives[completed]!.NotifyTaken(null, null);
            }
        }
    }

    [Fact]
    public void MinimumCompletionDoesNotCountDeactivatedUnusedObjectives()
    {
        using var match = Create(out SMission mission, chained: false, minimum: 2);
        mission.Objectives[0]!.NotifyTaken(null, null);
        mission.Objectives[1]!.NotifyTaken(null, null);
        Assert.True(mission.Objectives[2]!.ObjectiveTaken);
        Assert.Equal(2, match.Report().ObjectivesTaken);
        Assert.Equal(2, match.Report().ObjectivesNeeded);
    }

    [Fact]
    public void RandomSubsetStartsAtZeroOfItsActualRequiredCount()
    {
        using var match = Create(out _, chained: false, minimum: 0, random: 2);
        Assert.Equal(0, match.Report().ObjectivesTaken);
        Assert.Equal(2, match.Report().ObjectivesNeeded);
    }

    [Fact]
    public void NestedMissionCountsLeafObjectives()
    {
        using var match = Create(out SMission mission, chained: true, minimum: 2, nested: true);
        Assert.Equal(3, match.Report().ObjectivesNeeded);
        Assert.Equal(0, match.Report().ObjectivesTaken);
        mission.Objectives[0]!.NotifyTaken(null, null);
        var sub = Assert.IsType<SMission>(mission.Objectives[1]);
        sub.Objectives[0]!.NotifyTaken(null, null);
        Assert.Equal(2, match.Report().ObjectivesTaken);
        sub.Objectives[1]!.NotifyTaken(null, null);
        Assert.Equal(3, match.Report().ObjectivesTaken);
    }
}
