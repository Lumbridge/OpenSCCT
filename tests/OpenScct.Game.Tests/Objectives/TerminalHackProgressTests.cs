using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

// spec/objectives/terminal-hack.md rules 31 to 35: only the server advances DeltaCaptureTime.
public sealed class TerminalHackProgressTests
{
    private static (ObjWorld W, SComputerObjectiveTrigger Terminal, SObjective Objective) Make()
    {
        var w = new ObjWorld();
        SComputerObjectiveTrigger? terminal = null;
        SObjective? objective = null;
        w.Load(x =>
        {
            terminal = x.Place(new SComputerObjectiveTrigger { DominationCaptureTime = 10, TriggerName = "Laboratory" },
                new FVector(0f, 0f, 0f), new FRotator(0, 0, 0));
            objective = new SObjective { ObjectiveName = "Laboratory (2nd floor)" };
            objective.Triggers.Add(terminal);
            x.Place(objective, default);
        });
        objective!.Reset();
        return (w, terminal!, objective!);
    }

    [Fact]
    public void A_client_terminal_keeps_the_replicated_progress()
    {
        var (w, terminal, _) = Make();
        w.Run(100f);
        terminal.Role = ENetRole.ROLE_SimulatedProxy;
        terminal.bActive = true;
        terminal.bTriggering = true;
        terminal.CaptureTime = 10;
        terminal.DeltaCaptureTime = 2.5f;

        w.Run(0.5f);

        Assert.Equal(2.5f, terminal.DeltaCaptureTime);
        Assert.Equal(25, terminal.GetHackPercent());
    }

    [Fact]
    public void A_client_clears_its_triggering_flag_on_a_locked_terminal()
    {
        var (w, terminal, _) = Make();
        terminal.Role = ENetRole.ROLE_SimulatedProxy;
        terminal.bTriggering = true;
        terminal.bLocked = true;

        w.Run(0.1f);

        Assert.False(terminal.bTriggering);
    }

    [Fact]
    public void Server_progress_counts_from_the_hack_start_after_a_long_level_time()
    {
        var (w, terminal, _) = Make();
        w.Run(30f);
        terminal.eventTriggering(true);
        w.Run(5f);

        Assert.InRange(terminal.DeltaCaptureTime, 4.9f, 5.1f);
        Assert.InRange(terminal.GetHackPercent(), 49, 51);
    }

    [Fact]
    public void Server_progress_stops_when_locked()
    {
        var (w, terminal, _) = Make();
        terminal.eventTriggering(true);
        w.Run(2f);
        terminal.bLocked = true;
        w.Run(2f);

        Assert.InRange(terminal.DeltaCaptureTime, 1.9f, 2.1f);
    }

    [Fact]
    public void Server_progress_stops_when_the_objective_is_taken()
    {
        var (w, terminal, objective) = Make();
        terminal.eventTriggering(true);
        w.Run(2f);
        objective.ObjectiveTaken = true;
        w.Run(2f);

        Assert.InRange(terminal.DeltaCaptureTime, 1.9f, 2.1f);
    }

    [Fact]
    public void Server_progress_stops_when_inactive()
    {
        var (w, terminal, _) = Make();
        terminal.eventTriggering(true);
        w.Run(2f);
        terminal.bActive = false;
        w.Run(2f);

        Assert.InRange(terminal.DeltaCaptureTime, 1.9f, 2.1f);
    }

    [Fact]
    public void The_sim_percent_is_not_clamped()
    {
        var (w, terminal, _) = Make();
        terminal.eventTriggering(true);
        w.Run(10.5f);

        Assert.True(terminal.GetHackPercent() >= 100);
    }
}
