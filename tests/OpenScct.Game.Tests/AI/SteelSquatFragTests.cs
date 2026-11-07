using System;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Game.Tests.AI;

[Collection(AiMatchCollection.Name)]
public sealed class SteelSquatFragTests
{
    [Fact]
    public void CourtyardMercCanLaunchIntoTheSecondFloorOffice()
    {
        MapCatalog maps = MapCatalog.CreateDefault();
        Assert.SkipWhen(maps.FindFolder("SteeE") is null, "Steel Squat content is required.");
        AiDifficulty quiet = AiDifficulty.For(AiSkill.Hard) with { SightRange = 1f, HearingScale = 0f, Teamwork = 0f };
        using var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = "SteeE", Maps = maps, Seed = 24,
            Bots = [new ServerBotSpec("Spy", 0, quiet), new ServerBotSpec("Merc", 1, quiet)],
        });

        match.Step();
        AiController merc = Assert.Single(match.Bots.Bots, b => b.Team == 1);
        for (int f = 0; f < 20 * Sim.Hz; f++)
        {
            match.Step();
            foreach (AiController bot in match.Bots.Bots)
                if (bot.Pawn is not null) bot.Held = true;
            if (merc.Pawn is not null &&
                merc.Controller.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal)) break;
        }

        SBasePawn pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(merc.Pawn);
        FVector courtyard = new(-3080f, 310.123f, -1127f + pawn.CollisionHeight + 2f);
        Assert.True(pawn.SetLocation(courtyard));
        pawn.Velocity = default;
        for (int f = 0; f < Sim.Hz; f++) match.Step();
        var office = match.Host.Map.MapActors.OfType<SComputerObjectiveTrigger>()
            .Single(t => t.TriggerName == "Office (2nd floor)");
        Assert.True(office.Location.Z - pawn.Location.Z > 250f);
        merc.Held = false;

        bool inside = false;
        for (int f = 0; f < 6 * Sim.Hz; f++)
        {
            office.bTriggering = true;
            match.Step();
            foreach (SProjFrag frag in merc.World.Level.DynamicActors<SProjFrag>())
                inside |= frag.Location.Z > -800f && (frag.Location - office.Location).SizeSquared() < 340f * 340f;
            if (inside && Assert.IsType<MercAi>(merc.Brain).FragsThrown == 1) break;
        }

        Assert.True(inside, merc.Status);
        Assert.Equal(1, Assert.IsType<MercAi>(merc.Brain).FragsThrown);
    }
}
