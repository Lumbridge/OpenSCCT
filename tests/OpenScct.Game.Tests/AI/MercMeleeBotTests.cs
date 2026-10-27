using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Game.Tests.AI;

/// <summary>
/// A merc bot's Use key at close quarters (spec/combat/melee-elbow-charge-berserk.md rule 1): with a spy in front and
/// in charge reach it charges (forward held, Use pressed, the controller's cooldowns met); within elbow reach it
/// presses Use without forward, which gives the berserk spin when ready and the elbow while the spin cools. TestBox,
/// one merc bot and one spy bot held still.
/// </summary>
[Collection(AiMatchCollection.Name)]
public sealed class MercMeleeBotTests
{
    private static readonly MapCatalog Maps = MapCatalog.CreateDefault();

    private sealed class Scene : IDisposable
    {
        public required AiBotMatch Match;
        public required AiController Merc;
        public required AiController Spy;

        public void Dispose() => Match.Dispose();

        public void Step(float seconds)
        {
            long until = Match.Frame + (long)(seconds * Sim.Hz);
            while (Match.Frame < until)
            {
                Match.Step();
            }
        }
    }

    private static Scene? Start(float distance, AiDifficulty merc)
    {
        if (Maps.FindFolder(MapCatalog.TestBox) is null)
        {
            return null;
        }

        var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = MapCatalog.TestBox,
            Bots = [new ServerBotSpec("SpyBot1", 0, AiDifficulty.For(AiSkill.Normal)), new ServerBotSpec("MercBot1", 1, merc with { GadgetUse = 0.1f })],
            Seed = 9,
            Classes = NetClasses.Create(),
            Maps = Maps,
        });
        AiController? m = null;
        AiController? s = null;
        while (match.Frame < 20 * Sim.Hz && (m?.Pawn is null || s?.Pawn is null))
        {
            match.Step();
            m = match.Bots.Bots.FirstOrDefault(b => b.Pawn is SPAWNDEFENSE);
            s = match.Bots.Bots.FirstOrDefault(b => b.Pawn is SPawnAttaque);
        }

        Assert.NotNull(m);
        Assert.NotNull(s);
        var scene = new Scene { Match = match, Merc = m!, Spy = s! };
        m!.Held = true;
        s!.Held = true;
        for (int i = 0; i < 30 && !(Walking(scene.Merc) && Walking(scene.Spy)); i++)
        {
            scene.Step(0.5f);
        }

        Assert.True(Walking(scene.Merc) && Walking(scene.Spy),
            $"the players are not walking: {scene.Merc.Controller.GetStateName()} {scene.Spy.Controller.GetStateName()}");
        Place(scene, distance);

        // The spy spawns crouched: stand it up with the crouch key.
        if (scene.Spy.Pawn!.bIsCrouched)
        {
            int frames = 2;
            match.Host.BeforeWorldTick += _ =>
            {
                if (frames-- > 0)
                {
                    new AiInput { bCrouch = 1 }.ApplyTo(scene.Spy.Controller);
                }
            };
            scene.Step(1f);
        }

        Assert.False(scene.Spy.Pawn!.bIsCrouched, "the spy did not stand up");
        return scene;
    }

    /// <summary>The merc on an open floor spot, the spy <paramref name="distance"/> in front on a clear line, the merc looking at him.</summary>
    private static void Place(Scene s, float distance)
    {
        SBasePawn merc = s.Merc.Pawn!;
        SBasePawn spy = s.Spy.Pawn!;
        AiWorld world = s.Merc.World;
        FVector start = merc.Location;
        var spots = new List<FVector> { start };
        for (int gx = -4; gx <= 4; gx++)
        {
            for (int gy = -4; gy <= 4; gy++)
            {
                spots.Add(new FVector(start.X + gx * 256f, start.Y + gy * 256f, start.Z));
            }
        }

        var down = new FVector(0f, 0f, 200f);
        foreach (FVector spot in spots)
        {
            if (!merc.SetLocation(spot))
            {
                continue;
            }

            for (int k = 0; k < 16; k++)
            {
                FVector dir = new FRotator(0, k * 4096, 0).Vector();
                FVector at = spot + dir * distance;
                FVector beyond = spot + dir * (distance + 300f);
                if (!world.LineOpen(spot, beyond) || world.LineOpen(spot, spot - down) || world.LineOpen(at, at - down) ||
                    world.LineOpen(beyond, beyond - down) || !spy.SetLocation(at))
                {
                    continue;
                }

                s.Merc.Controller.SetViewRotation(new FRotator(0, k * 4096, 0));
                s.Spy.Controller.SetViewRotation(new FRotator(0, (k * 4096 + 16384) & 0xFFFF, 0));
                for (int i = 0; i < 20 && spy.Physics != EPhysics.PHYS_Walking; i++)
                {
                    s.Step(0.25f);
                }

                Assert.True(spy.Physics == EPhysics.PHYS_Walking, "the placed spy did not land");
                return;
            }
        }

        Assert.Fail($"no clear spot for the spy in front of the merc (from {start})");
    }

    private static bool Walking(AiController b) => b.Controller.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal);

    /// <summary>Lets the merc bot play for <paramref name="seconds"/>; the merc's and the spy's states seen on the way.</summary>
    private static (HashSet<string> Merc, HashSet<string> Spy, string Trace) Play(Scene s, float seconds)
    {
        var merc = new HashSet<string>();
        var spy = new HashSet<string>();
        var trace = new System.Text.StringBuilder();
        string last = string.Empty;
        s.Merc.Held = false;
        long until = s.Match.Frame + (long)(seconds * Sim.Hz);
        while (s.Match.Frame < until)
        {
            s.Match.Step();
            string ms = s.Merc.Controller.GetStateName().ToString();
            string ss = s.Spy.Controller.GetStateName().ToString();
            merc.Add(ms);
            spy.Add(ss);
            string line = $"{ms}/{ss} {s.Merc.Status}";
            if (line != last)
            {
                trace.Append(s.Match.Frame).Append(": ").Append(line).Append("; ");
                last = line;
            }
        }

        return (merc, spy, trace.ToString());
    }

    [Fact]
    public void AMercBotChargesASpyInFrontWithinChargeReach()
    {
        using Scene? s = Start(450f, AiDifficulty.For(AiSkill.Hard) with { MeleeUse = 1f });
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");
        (HashSet<string> merc, HashSet<string> spy, string trace) = Play(s!, 5f);
        Assert.True(merc.Contains("s_PlayerCharging"), "the merc bot did not charge: " + trace);
        Assert.True(spy.Contains("s_Charge_GoBackward"), "the charge did not hit the spy: " + trace);
        Assert.True(((MercAi)s!.Merc.Brain!).Charges > 0);
    }

    [Fact]
    public void AMercBotThatNeverMeleesShootsInstead()
    {
        using Scene? s = Start(450f, AiDifficulty.For(AiSkill.Hard) with { MeleeUse = 0f });
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");
        (HashSet<string> merc, _, string trace) = Play(s!, 3f);
        Assert.False(merc.Contains("s_PlayerCharging"), "the merc bot charged: " + trace);
        Assert.True(((MercAi)s!.Merc.Brain!).Bursts > 0, "the merc bot did not shoot: " + trace);
    }

    [Fact]
    public void AMercBotElbowsASpyInReachWhileItsSpinCoolsDown()
    {
        using Scene? s = Start(100f, AiDifficulty.For(AiSkill.Hard) with { MeleeUse = 1f });
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");

        // A spin just made: the Use key gives the elbow for the next 3 s (rule 1).
        s!.Merc.Controller.fLastBerserkTime = s.Merc.Controller.Level.TimeSeconds;
        (HashSet<string> merc, HashSet<string> spy, string trace) = Play(s, 2.5f);
        Assert.True(merc.Contains("s_ElbowShooting"), "the merc bot did not elbow: " + trace);
        Assert.True(spy.Contains("s_ElbowShot_GoBackward"), "the elbow did not knock the spy back: " + trace);
        Assert.False(merc.Contains("s_PlayerCharging"), "the merc bot charged at elbow reach: " + trace);
    }
}
