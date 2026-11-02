using System;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PolarContainerNavigationTests(ITestOutputHelper output)
{
    private sealed class ProbeBrain(AiController bot) : AiBrain(bot)
    {
        protected override bool Spy => true;
        public override string Status => "probe";
        protected override int Choose(float now) => 0;
        protected override void Act(float now, ref AiPlan plan) { }
        public bool TerminalFloor(SComputerObjectiveTrigger terminal, out FVector floor) => UseSpot(terminal, out floor, out _);
    }

    [Fact]
    public void ContainerTerminalUseSpotAttachesToAWalkableRoute()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("PolaE") is null, "PolaE content is not installed.");
        using var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = "PolaE", Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = [new ServerBotSpec("ContainerProbe", 0, AiDifficulty.For(AiSkill.Normal))],
        });
        match.Bots.LoginAll();
        AiController bot = Assert.Single(match.Bots.Bots);
        for (int i = 0; i < 120; i++) match.Step();
        bot.Held = true;
        SBasePawn pawn = Assert.IsAssignableFrom<SBasePawn>(bot.Controller.Pawn);
        AiWorld world = Assert.IsType<AiWorld>(match.Bots.World);
        SComputerObjectiveTrigger terminal = Assert.Single(world.Terminals, t => t.TriggerName == "Container");
        Assert.True(new ProbeBrain(bot).TerminalFloor(terminal, out FVector goal));
        Assert.InRange(goal.Z, -112f, -108f); // floor inside the container, not the terminal's top at -31
        var graph = world.SpyNav;
        output.WriteLine($"terminal={terminal.Location} goal={goal}");
        int[] nearby = Enumerable.Range(0, graph.NodeCount)
            .Where(n => graph.Node(n).Floor.X > -6100f && graph.Node(n).Floor.Z < -120f && Math.Abs(graph.Node(n).Floor.Z - goal.Z) < 100f &&
                NavFollower.Horizontal(graph.Node(n).Floor - goal) is > 200f and < 700f)
            .OrderBy(n => (graph.Node(n).Floor - goal).SizeSquared()).Take(12).ToArray();
        Assert.NotEmpty(nearby);
        var follower = new NavFollower(world, true);
        int reached = 0;
        match.Host.BeforeWorldTick += _ =>
        {
            NavCommand command = follower.Steer(pawn, world.Now, bot.Controller.Rotation.Yaw & 0xffff, false);
            AiInput keys = command.Keys;
            if (command.Yaw is { } yaw) keys.View = keys.Aim = new FRotator(0, yaw, 0);
            if (command.Crouched is { } crouched && crouched != pawn.bIsCrouched)
                keys.bCrouch = 1;
            keys.FastSpeed = true;
            keys.ApplyTo(bot.Controller);
        };
        foreach (int node in nearby)
        {
            pawn.Location = graph.Node(node).Floor + new FVector(0f, 0f, pawn.CollisionHeight + 2f);
            pawn.Velocity = default;
            pawn.Physics = EPhysics.PHYS_Walking;
            bot.Controller.GotoState(new Name("s_PlayerWalkingSt0H"));
            bool planned = follower.SetGoal(pawn, goal);
            bool complete = planned && follower.Path.Count > 0 && (follower.Path[^1].Point - goal).Size() < 1f;
            output.WriteLine($"from={graph.Node(node).Floor} complete={complete} steps={follower.Path.Count}");
            if (!complete) continue;
            for (int frame = 0; frame < 15 * Sim.Hz && !follower.Arrived && !follower.Failed; frame++) match.Step();
            output.WriteLine($"arrived={follower.Arrived} failed={follower.Failed} location={pawn.Location} state={bot.Controller.GetStateName()}");
            if (follower.Arrived) { reached++; break; }
        }
        Assert.True(reached > 0, "No nearby walkable floor can physically reach the Container terminal use spot.");
    }
}
