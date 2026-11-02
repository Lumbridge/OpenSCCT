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
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Game.Tests.AI;

/// <summary>The actual Aquarius waterproof-door relay, collision and normal player interaction path.</summary>
[Collection(AiMatchCollection.Name)]
public sealed class AquariusDoorAiTests
{
    private static readonly MapCatalog Maps = MapCatalog.CreateDefault();
    private static readonly AiNavCache Nav = new();

    private sealed class EscapeBrain(AiController bot, FVector goal) : AiBrain(bot)
    {
        protected override bool Spy => Bot.Team == 0;
        public override string Status => Doors.Status;
        protected override int Choose(float now) => 0;
        protected override void Act(float now, ref AiPlan plan) => MoveTo(goal, now, ref plan, sneak: false);
    }

    [Theory]
    [InlineData("AquaE", 2284f, -1f, 1)]
    [InlineData("AquaE", 2284f, -1f, 0)]
    [InlineData("AquaE", 3416f, 1f, 1)]
    [InlineData("AquaE", 3416f, 1f, 0)]
    [InlineData("AquaD", 2284f, -1f, 1)]
    [InlineData("AquaD", 2284f, -1f, 0)]
    public void TrappedBotUsesInteriorButtonOnceAndWaitsForTheDelayedEscape(string map, float doorY, float direction, int team)
    {
        Assert.SkipWhen(Maps.FindFolder(map) is null, $"{map} content is required.");
        AiDifficulty quiet = AiDifficulty.For(AiSkill.Normal) with { SightRange = 1f, HearingScale = 0f, GadgetUse = 0f };
        using var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = map,
            Bots = [new ServerBotSpec("Spy", 0, quiet), new ServerBotSpec("Merc", 1, quiet)],
            Seed = 52,
            Classes = NetClasses.Create(),
            Maps = Maps,
            Nav = Nav,
        });
        AiController? walker = null;
        for (int frame = 0; frame < 20 * Sim.Hz; frame++)
        {
            match.Step();
            foreach (AiController bot in match.Bots.Bots)
            {
                // Allow the first pawn tick to create its brain before holding it.
                if (bot.Pawn is not null)
                {
                    bot.Held = true;
                }
            }

            walker = match.Bots.Bots.FirstOrDefault(b => b.Pawn is not null && b.Team == team);
            if (walker?.Controller.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) == true)
            {
                break;
            }
        }

        Assert.NotNull(walker?.Pawn);
        SBasePawn pawn = walker!.Pawn!;
        Mover door = match.Host.Map.MapActors.OfType<Mover>().Single(a => a.Tag == "PorteEtanche" && a.BasePos.Y == doorY);
        SMagicEvent relay = match.Host.Map.MapActors.OfType<SMagicEvent>().Single(a => a.Tag == "PorteEtancheInt");
        SInterrupteur exterior = match.Host.Map.MapActors.OfType<SInterrupteur>().First(a => a.Event == "PorteEtanche");
        SInterrupteur[] interior = match.Host.Map.MapActors.OfType<SInterrupteur>().Where(a => a.Event == "PorteEtancheInt").ToArray();
        Assert.Equal(2, interior.Length);
        Assert.All(interior, s => Assert.Equal(0, s.iReusableTime));

        FVector inside = new(3040f, doorY - direction * 180f, -38f);
        FVector outside = new(3040f, doorY + direction * 180f, -38f);
        Assert.True(pawn.SetLocation(inside));
        pawn.Velocity = default;
        walker.Controller.SetViewRotation(new FRotator(0, direction > 0f ? 16384 : 49152, 0));
        Step(match, 0.5f);

        // Aquarius authors the raised, passable position as key zero. Closing the event lowers the doors to key one.
        Assert.Equal(0, door.KeyNum);
        Assert.False(walker.World.Collision.LineCheck(out _, pawn, outside, inside, default, ETraceFlags.TRACE_Movers));
        exterior.TriggerInteraction!.HandleEventInfo();
        Step(match, 4f);
        Assert.Equal(1, door.KeyNum);
        Assert.False(door.bInterpolating);
        Assert.True(walker.World.Collision.LineCheck(out FCheckResult closed, pawn, outside, inside, default, ETraceFlags.TRACE_Movers));
        Assert.Same(door, closed.Actor);

        Assert.True(walker.World.Collision.LineCheck(out FCheckResult floorHit, pawn,
            outside - new FVector(0f, 0f, 300f), outside + new FVector(0f, 0f, 100f), default,
            ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Blocking));
        var brain = new EscapeBrain(walker, floorHit.Location);
        var trace = new StringBuilder();
        trace.AppendLine($"goal={floorHit.Location}");
        var queuedReleases = new HashSet<float>();
        float firstPress = -1f;
        float crossedAt = -1f;
        float crouchAt = -1f;
        string previous = string.Empty;
        match.Host.BeforeWorldTick += _ =>
        {
            if (crossedAt >= 0f)
            {
                return;
            }

            var plan = new AiPlan { Tolerance = 9000 };
            brain.Think(walker.World.Now, ref plan);
            AiInput input = plan.Keys;
            if (plan.Yaw is { } yaw)
            {
                input.View = new FRotator(plan.Pitch ?? 0, yaw, 0);
                input.Aim = input.View;
            }

            if (plan.Crouched is { } crouched && crouched != pawn.bIsCrouched && walker.World.Now - crouchAt > 0.6f)
            {
                input.bCrouch = 1;
                crouchAt = walker.World.Now;
            }

            input.ApplyTo(walker.Controller);
        };

        for (int frame = 0; frame < 45 * Sim.Hz; frame++)
        {
            match.Step();
            foreach (FWaitingEvent pending in relay.EventQueue)
            {
                if (pending.Event.Event == door.Tag && queuedReleases.Add(pending.Time) && firstPress < 0f)
                {
                    firstPress = walker.World.Now;
                }
            }

            string state = $"{brain.Status} {walker.Controller.GetStateName()} {pawn.Physics} door={door.KeyNum}/{door.bInterpolating} queue={relay.EventQueue.Count} route={brain.Follower.NextIndex}/{brain.Follower.Path.Count} failed={brain.Follower.Failed} link={brain.Follower.ActiveLink} bad={walker.World.BadEdges(team == 0).Count}";
            if (state != previous || frame % Sim.Hz == 0)
            {
                trace.AppendLine($"{walker.World.Now:F2} {state} at {pawn.Location}");
                previous = state;
            }

            // Either interior button opens both doors; a route may choose the other exit when its first
            // approach is obstructed. Require a crossing at the actual portals, not merely a far-away Y value.
            if (MathF.Abs(pawn.Location.X - 3040f) < 160f && (pawn.Location.Y < 2224f || pawn.Location.Y > 3476f))
            {
                crossedAt = walker.World.Now;
                break;
            }
        }

        AiTestKit.SaveReport($"waterproof-{map}-{doorY}-team{team}", trace.ToString());
        Assert.True(firstPress >= 0f, "The bot never pressed an interior button.\n" + trace);
        Assert.True(crossedAt >= firstPress + 9.9f, "The bot did not escape through a delayed door.\n" + trace);
        Assert.Single(queuedReleases);
        Assert.Equal(0, door.KeyNum);

        // Extra presses would queue another toggle and lower the doors again after the first release.
        Step(match, 14f);
        Assert.Empty(relay.EventQueue);
        Assert.Equal(0, door.KeyNum);
    }

    private static void Step(AiBotMatch match, float seconds)
    {
        for (int frame = 0; frame < seconds * Sim.Hz; frame++)
        {
            match.Step();
        }
    }
}
