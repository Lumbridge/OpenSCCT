using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// spec/objectives/terminal-hack.md rules 31 to 35 over the wire: a SteeE terminal hacked well past 10 s of level time
/// shows the server's progress on a connected client (it used to read 99 % from the first frame, the client rebuilding
/// the progress from its own level time and an unreplicated start time). Skipped without the converted SteeE.
/// </summary>
public sealed class TerminalHackNetTests
{
    private const string MapName = "SteeE";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_late_client_can_resume_a_stopped_hack_as_soon_as_the_terminal_is_released(bool delayed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapName) is null, "SteeE has not been converted into .content/svmap.");
        using WalkerRig rig = WalkerRig.Create(delayed ? SimLinkProfile.B : null, 42UL, 0,
            new ServerSettings { Map = MapName, NetMode = ENetMode.NM_ListenServer });
        for (int n = 0; n < 30 * Sim.Hz; n++) rig.Step();
        ClientSession client = rig.AddClient();
        for (int n = 0; n < 3 * Sim.Hz; n++) rig.Step();
        Assert.Equal(EClientState.Joined, client.State);

        SComputerObjectiveTrigger? terminal = null;
        foreach (SComputerObjectiveTrigger candidate in rig.Server.World.Level.AllActors<SComputerObjectiveTrigger>())
        {
            terminal = candidate;
            break;
        }
        Assert.NotNull(terminal);
        var entry = Assert.IsType<SComputerInteraction>(terminal.InteractionObj);
        terminal.bActive = true;
        terminal.bLocked = false;
        terminal.CaptureTime = 10;
        if (terminal.Objective is { } objective) objective.ObjectiveTaken = false;
        for (int n = 0; n < Sim.Hz; n++) rig.Step();
        var copy = Assert.IsType<SComputerInteraction>(client.Driver.FindActorByNetGuid((uint)entry.ActorId));
        Assert.Equal(0, copy.iReusableTime);

        // Drive the authoritative use and cancel; both the use timestamp and availability reach the late
        // client through normal replication, while its own level clock is still thirty seconds younger.
        var spy = rig.Server.World.Level.Spawn<SPlayerController>()!;
        spy.Pawn = rig.Server.World.Level.Spawn<SPawnAttaque>(terminal.Location + new FVector(64, 0, 0))!;
        InteractionMenuRules.InteractionInit(spy, entry, 0);
        terminal.eventTriggering(true);
        for (int n = 0; n < Sim.Hz; n++) rig.Step();
        Assert.False(copy.bUsable);
        Assert.Equal(entry.iLastUsedTime, copy.iLastUsedTime);
        terminal.eventTriggering(false);
        Assert.True(entry.bUsable);
        for (int n = 0; n < Sim.Hz && !copy.bUsable; n++) rig.Step();
        Assert.True(copy.bUsable);
        Assert.True(copy.iLastUsedTime - copy.Level.TimeSeconds > 20f,
            "The replicated server use time must still be ahead of the late client's clock.");

        var localSpy = client.World.Level.Spawn<SPlayerController>()!;
        FVector at = copy.Location + copy.Rotation.Vector() * 30f;
        localSpy.Pawn = client.World.Level.Spawn<SPawnAttaque>(at, (copy.Location - at).Rotation())!;
        Assert.True(copy.CanUse(localSpy),
            "A released laptop with no reuse delay must be usable immediately, without waiting for client uptime to reach the server's last-use timestamp.");
    }

    [Fact]
    public void A_client_shows_the_server_hack_progress_after_thirty_seconds_of_level_time()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapName) is null, "SteeE has not been converted into .content/svmap.");
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.Get("B"), 41UL, 1,
            new ServerSettings { Map = MapName, NetMode = ENetMode.NM_ListenServer });
        SComputerObjectiveTrigger? terminal = null;
        foreach (Actor? actor in rig.Server.Map.MapActors)
        {
            if (actor is SComputerObjectiveTrigger { bDeleteMe: false } t)
            {
                terminal = t;
                break;
            }
        }

        Assert.NotNull(terminal);
        ClientSession client = rig.Clients[0];
        rig.WaitForAllWalkers(1, input: WalkPatterns.For);
        int index = rig.Server.Driver.MapBinding.IndexOf(terminal);
        var copy = (SComputerObjectiveTrigger)client.Map!.MapActors[index]!;
        for (int n = 0; n < 1800 && (client.Connection!.FindActorChannel(copy) is null || terminal.Level.TimeSeconds < 30f); n++)
        {
            rig.Step(WalkPatterns.For);
        }

        Assert.NotNull(client.Connection!.FindActorChannel(copy));
        Assert.True(terminal.Level.TimeSeconds >= 30f);
        Assert.Equal(ENetRole.ROLE_SimulatedProxy, copy.Role);

        terminal.bActive = true;
        terminal.bLocked = false;
        terminal.CaptureTime = terminal.DominationCaptureTime;
        if (terminal.Objective is { } objective)
        {
            objective.ObjectiveTaken = false;
        }

        terminal.eventTriggering(true);
        var samples = new List<(int Server, int Client)>();
        for (int n = 0; n < 5 * Sim.Hz; n++)
        {
            rig.Step(WalkPatterns.For);
            if (n % Sim.Hz == Sim.Hz - 1)
            {
                samples.Add((terminal.GetHackPercent(), copy.GetHackPercent()));
            }
        }

        // The server counts DeltaCaptureTime from the hack start; the client only shows what was sent.
        Assert.InRange(terminal.GetHackPercent(), 49, 51);
        Assert.InRange(copy.GetHackPercent(), 40, 55);
        foreach ((int server, int clientPercent) in samples)
        {
            // Within a net update or two of the server (5 Hz objective triggers, plus link latency).
            Assert.InRange(server - clientPercent, -1, 6);
        }

        for (int i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i].Client > samples[i - 1].Client, $"client percent did not rise: {samples[i - 1].Client} then {samples[i].Client}");
        }
    }
}
