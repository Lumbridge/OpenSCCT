using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Game.Tests.AI;

[Collection(AiMatchCollection.Name)]
public sealed class MercMapTrapTests
{
    private static readonly MapCatalog Maps = MapCatalog.CreateDefault();
    private static readonly AiNavCache Nav = new();

    [Theory]
    [InlineData("Greek Temple")]
    [InlineData("Pirates' Room")]
    public void AquariusGuardPlacesACarriedTrapNearAnActiveObjective(string objectiveName)
    {
        Assert.SkipWhen(Maps.FindFolder("AquaE") is null, "AquaE content is required.");
        AiDifficulty quiet = AiDifficulty.For(AiSkill.Normal) with
        {
            SightRange = 1f, HearingScale = 0f, Teamwork = 0f, GadgetUse = 1f,
        };
        using var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = "AquaE",
            Bots = [new ServerBotSpec("Spy", 0, quiet), new ServerBotSpec("Merc", 1, quiet)],
            Seed = 54,
            Classes = NetClasses.Create(),
            Maps = Maps,
            Nav = Nav,
        });
        AiController? merc = null;
        for (int frame = 0; frame < 20 * Sim.Hz; frame++)
        {
            match.Step();
            foreach (AiController bot in match.Bots.Bots)
                if (bot.Pawn is not null) bot.Held = true;
            merc = match.Bots.Bots.FirstOrDefault(b => b.Pawn is not null && b.Team == 1);
            if (merc?.Controller.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) == true) break;
        }

        Assert.NotNull(merc?.Pawn);
        SBasePawn pawn = merc!.Pawn!;
        SComputerObjectiveTrigger terminal = match.Host.Map.MapActors.OfType<SComputerObjectiveTrigger>()
            .Single(t => t.TriggerName == objectiveName);
        NavGraph graph = merc.World.MercNav;
        var candidates = new List<int>();
        graph.NodesNear(terminal.Location, 300f, candidates);
        bool placed = false;
        foreach (int node in candidates.OrderBy(n => (graph.Node(n).Floor - terminal.Location).SizeSquared()))
        {
            if (graph.Node(node).Standing &&
                pawn.SetLocation(graph.Node(node).Floor + new FVector(0f, 0f, pawn.CollisionHeight + 2f)))
            {
                placed = true;
                break;
            }
        }
        Assert.True(placed);
        pawn.Velocity = default;
        for (int frame = 0; frame < Sim.Hz; frame++) match.Step();
        merc.Brain!.Reset();
        merc.Held = false;

        var trace = new StringBuilder();
        var launchers = new List<SGadMultiMinesLauncher>();
        trace.AppendLine($"objective={objectiveName} at {terminal.Location}, merc={pawn.Location}, active={terminal.bActive} complete={terminal.complete}");
        for (SGadget? gadget = pawn.MyGadget; gadget is not null; gadget = gadget.MyGadget)
        {
            trace.AppendLine($"carried={gadget.GetUcClass().Path} charge={gadget.Charge}");
            if (gadget is SGadMultiMinesLauncher launcher) launchers.Add(launcher);
        }

        int initialCharges = launchers.Sum(launcher => launcher.Charge);
        Assert.True(initialCharges > 0);
        for (int frame = 0; frame < 45 * Sim.Hz; frame++)
        {
            match.Step();
            if (frame % Sim.Hz == 0)
            {
                trace.AppendLine($"{merc.World.Now:F2} {merc.Status} state={merc.Controller.GetStateName()} at={pawn.Location} selected={pawn.SelectedGadget?.GetUcClass().Path} mines={merc.Controller.NbBasicMineInMapIOwn}/{merc.Controller.NbTrapMineInMapIOwn}");
            }

            if (merc.Controller.NbBasicMineInMapIOwn + merc.Controller.NbTrapMineInMapIOwn > 0) break;
        }

        AiTestKit.SaveReport($"traps-AquaE-{objectiveName.Replace(' ', '-')}", trace.ToString());
        Assert.True(merc.Controller.NbBasicMineInMapIOwn + merc.Controller.NbTrapMineInMapIOwn > 0, trace.ToString());
        Assert.Equal(initialCharges - 1, launchers.Sum(launcher => launcher.Charge));
    }
}
