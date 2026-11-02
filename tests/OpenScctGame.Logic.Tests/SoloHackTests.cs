using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// Offline play from the main menu (the listen server and the local client of <see cref="PlaySession"/>, SteeE Story
/// with the map's rules): the spy walks to a terminal, presses Use, hacks it to the end and walks away
/// (spec/objectives/terminal-hack.md rules 9 to 27), with and without idle server bots. Combat is covered by the AI
/// match tests; here neither enemy fire nor a teammate's hack may decide the result. Skipped without converted SteeE.
/// </summary>
public sealed class SoloHackTests
{
    private const string Terminal = "Office";

    private readonly ITestOutputHelper _output;

    public SoloHackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static PlaySession? Start(VirtualClock clock, int bots, bool thread)
    {
        try
        {
            return new PlaySession(new PlaySettings
            {
                Map = "SteeE",
                Side = PlaySide.Spy,
                Mode = "Story",
                HostMode = thread ? PlayHostMode.Thread : PlayHostMode.Inline,
                Bots = bots,
            }, clock);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static (FVector Spot, FVector Face)? UseSpot(SPlayerController pc, string terminal, float dx, float dy)
    {
        if (pc.Pawn is not { } pawn)
        {
            return null;
        }

        foreach (SComputerObjectiveTrigger t in pc.XLevel.AllActors<SComputerObjectiveTrigger>())
        {
            if (t.TriggerName.StartsWith(terminal, StringComparison.OrdinalIgnoreCase))
            {
                t.GetUseSpot(pawn, out FVector spot, out _);
                t.Rotation.GetAxes(out FVector x, out FVector y, out _);
                spot += x * dx + y * dy;
                spot.Z = t.Location.Z;
                return (spot, t.Location);
            }
        }

        return null;
    }

    // Whether the terminal shows as hacked (objective taken, hacked skin) and its percent while a hack runs (-1 otherwise).
    private static (bool Taken, int Percent) Read(Level level, string terminal)
    {
        foreach (SComputerObjectiveTrigger t in level.AllActors<SComputerObjectiveTrigger>())
        {
            if (t.TriggerName.StartsWith(terminal, StringComparison.OrdinalIgnoreCase))
            {
                return (t.Objective is { ObjectiveTaken: true } && t.SkinState == SComputerObjectiveTrigger.SKIN_Hacked,
                    t.bTriggering ? t.GetHackPercent() : -1);
            }
        }

        return (false, -1);
    }

    [Theory]
    [InlineData(Terminal, 0, 0f, 0f, false)]
    [InlineData(Terminal, 3, 0f, 0f, false)]
    [InlineData(Terminal, 0, 30f, 0f, false)]
    [InlineData(Terminal, 0, 0f, -50f, false)]
    [InlineData(Terminal, 0, 0f, 0f, true)]
    [InlineData(Terminal, 3, 0f, 0f, true)]
    [InlineData(Terminal, 0, 0f, 0f, false, 3)]
    [InlineData(Terminal, 0, 0f, 0f, false, 90)]
    [InlineData(Terminal, 0, 0f, 0f, false, 400)]
    public void The_spy_hacks_a_terminal_to_the_end_and_walks_away(string terminal, int bots, float dx, float dy, bool thread, int useHold = 20)
    {
        var clock = new VirtualClock();
        PlaySession? started = Start(clock, bots, thread);
        Assert.SkipWhen(started is null, "SteeE has not been converted into .content/svmap.");
        using PlaySession session = started!;
        SComputerObjectiveTrigger? serverTerminal = null;
        int localHackPercent = -1;
        session.Host.BeforeWorldTick += host =>
        {
            // Keep the extra players and their replication, but isolate the local player's interaction from
            // autonomous combat/objective choices. This callback also runs on the host thread in Thread mode.
            foreach (AiController bot in session.Bots?.Bots ?? [])
            {
                bot.Held = true;
            }

            if (serverTerminal is null)
            {
                foreach (SComputerObjectiveTrigger candidate in host.World.Level.AllActors<SComputerObjectiveTrigger>())
                {
                    if (candidate.TriggerName.StartsWith(terminal, StringComparison.OrdinalIgnoreCase))
                    {
                        serverTerminal = candidate;
                        break;
                    }
                }
            }

            if (serverTerminal is { bTriggering: true, ControllerWhoTakesIt: not null } &&
                ReferenceEquals(serverTerminal.ControllerWhoTakesIt, session.ServerController))
            {
                localHackPercent = Math.Max(localHackPercent, serverTerminal.GetHackPercent());
            }
        };
        var pilot = new ScriptedPilot(pc => UseSpot(pc, terminal, dx, dy)) { UseHoldTicks = useHold };
        bool walkAway = false;
        session.BeforeClientTick = (s, _) =>
        {
            if (walkAway)
            {
                s.Input.Set("BT_Up", true);
                return;
            }

            PilotPhase before = pilot.Phase;
            pilot.Tick(s);
            if (pilot.Phase != before && pilot.Phase is PilotPhase.Face or PilotPhase.PressUse or PilotPhase.ReleaseUse or PilotPhase.Done)
            {
                _output.WriteLine($"pilot {pilot.Phase}: client {s.LocalController?.Pawn?.Location} " +
                    $"usable {s.LocalController?.IManager?.HasUsableInteractions()} view {s.LocalController?.Rotation}");
            }
        };

        var seen = new List<string>();
        string last = "";
        bool taken = false;
        int maxPercent = -1;
        long takenAt = -1;
        FVector walkFrom = default;
        bool walked = false;
        bool usedByLocalPlayer = false;
        for (int i = 0; i < 200 * Sim.Hz && !walked; i++)
        {
            clock.Advance(1_000_000 / Sim.Hz);
            if (thread)
            {
                System.Threading.Thread.Sleep(2);
            }

            if (thread)
            {
                session.Update();
            }
            else
            {
                session.Step(clock.NowMicroseconds);
            }
            Assert.Null(session.ServerFailure);
            Assert.True(pilot.Phase != PilotPhase.Failed,
                $"pilot failed at tick {i}: {pilot.Failure}; client {session.LocalController?.GetStateName()}; " + string.Join(" | ", seen));
            SPlayerController? pc = session.LocalController;
            string state = pc?.GetStateName().ToString() ?? "-";
            string server = thread ? "<threaded>" : session.ServerController?.GetStateName().ToString() ?? "-";
            usedByLocalPlayer |= pc?.IsInState("s_Computer_Use") == true;
            string now = state + "/" + server;
            if (now != last)
            {
                seen.Add(string.Create(CultureInfo.InvariantCulture, $"{i}: client {state} server {server}"));
                last = now;
            }

            // The server copy inline; with the host thread, the local client's copy (what the player sees).
            Level level = thread ? session.Client.World.Level : session.ServerSnapshot(h => h.World.Level);
            (bool isTaken, int percent) = Read(level, terminal);
            maxPercent = Math.Max(maxPercent, percent);
            if (isTaken && !taken)
            {
                taken = true;
                takenAt = i;
            }

            // After the leave animation the spy must walk again, then walk away on the forward key.
            if (taken && !walkAway && state.StartsWith("s_PlayerWalking", StringComparison.Ordinal) && pc?.Pawn is { } p)
            {
                walkAway = true;
                walkFrom = p.Location;
            }

            if (walkAway && pc?.Pawn is { } q && (q.Location - walkFrom).Size2D() > 150f)
            {
                walked = true;
            }
        }

        // Join the host before reading its captured progress or bot roster in the threaded case.
        session.Stop();
        Assert.Equal(bots, session.Bots?.Bots.Count ?? 0);
        foreach (AiController bot in session.Bots?.Bots ?? [])
        {
            Assert.True(bot.Held);
            Assert.NotNull(bot.Controller.Pawn);
        }

        foreach (string line in seen)
        {
            _output.WriteLine(line);
        }

        _output.WriteLine($"pilot {pilot.Phase} taken {taken} at {takenAt} max percent {maxPercent} walked {walked}");
        Assert.True(taken, "the terminal was not hacked: " + string.Join(" | ", seen));
        Assert.True(usedByLocalPlayer, "the local spy never entered the hacking state");
        Assert.True(localHackPercent >= 90, $"the local spy's authoritative hack percent only reached {localHackPercent}");
        Assert.True(maxPercent >= 90, $"the hack percent only reached {maxPercent}");
        Assert.True(walkAway, "the spy never walked again after the hack: " + string.Join(" | ", seen));
        Assert.True(walked, "the spy did not walk away from the terminal: " + string.Join(" | ", seen));
    }
}
