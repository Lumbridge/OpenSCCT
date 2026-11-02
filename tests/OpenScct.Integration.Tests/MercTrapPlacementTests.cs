using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MercTrapPlacementTests(ITestOutputHelper output)
{
    [Fact]
    public void MercPlacesATrapWhileGuardingPolarObjectivesWithItsSpawnLoadout()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("PolaE") is null, "PolaE content is not installed.");
        using var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = "PolaE", Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = [new ServerBotSpec("TrapGuard", 1, AiDifficulty.For(AiSkill.Normal))],
        });
        match.Bots.LoginAll();
        AiController bot = Assert.Single(match.Bots.Bots);
        for (int i = 0; i < 90 * Sim.Hz; i++)
        {
            match.Step();
            if (bot.Controller.NbBasicMineInMapIOwn > 0 && bot.Controller.NbTrapMineInMapIOwn > 0) return;
            if (i % (10 * Sim.Hz) == 0)
                output.WriteLine($"{i / Sim.Hz}s {bot.Status} at {bot.Pawn?.Location} state {bot.Controller.GetStateName()} gear {bot.Pawn?.SelectedGadget?.GetType().Name}");
        }
        Assert.Fail("The merc placed no traps during 90 seconds of uncontested objective defense on PolaE.");
    }
}
