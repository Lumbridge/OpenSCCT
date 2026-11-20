using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class HospitalSewerRailNavigationTests
{
    private static readonly AiNavCache Navigation = new();

    [Theory]
    [InlineData(2600)] // Below the west railing.
    [InlineData(2890)] // Below the opening.
    [InlineData(3220)] // Below the east railing.
    public void SpyClimbsOutOfTheMainStreetTrench(int startX)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("MounE") is null, "MounE content is not installed.");
        using var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = "MounE", Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, Nav = Navigation,
            Bots = [new ServerBotSpec("SewerProbe", 0, AiDifficulty.For(AiSkill.Normal))],
        });
        match.Bots.LoginAll();
        AiController bot = Assert.Single(match.Bots.Bots);
        for (int i = 0; i < 100; i++)
        {
            match.Step();
        }

        bot.Held = true;
        SBasePawn pawn = Assert.IsAssignableFrom<SBasePawn>(bot.Controller.Pawn);
        AiWorld world = Assert.IsType<AiWorld>(match.Bots.World);
        var follower = new NavFollower(world, spy: true);
        pawn.Location = new FVector(startX, -1968f, -960f + pawn.CollisionHeight + 2f);
        pawn.Velocity = default;
        pawn.Acceleration = default;
        pawn.Physics = EPhysics.PHYS_Walking;
        pawn.Rotation = bot.Controller.Rotation = new FRotator(0, 16384, 0);
        bot.Controller.GotoState(MovementStateNames.s_PlayerWalkingSt0H);
        Assert.True(follower.SetGoal(pawn, new FVector(startX, -1775f, -704f)));

        match.Host.BeforeWorldTick += _ =>
        {
            NavCommand motion = follower.Steer(pawn, world.Now, bot.Controller.Rotation.Yaw & 0xffff, false);
            AiInput keys = motion.Keys;
            if (motion.Yaw is { } yaw)
            {
                keys.View = keys.Aim = new FRotator(0, yaw, 0);
            }
            if (motion.Crouched is { } crouched && crouched != pawn.bIsCrouched)
            {
                keys.bCrouch = 1;
            }
            keys.FastSpeed = true;
            keys.ApplyTo(bot.Controller);
        };

        int hangingFrames = 0;
        for (int frame = 0; frame < 15 * Sim.Hz && !follower.Arrived && !follower.Failed; frame++)
        {
            match.Step();
            hangingFrames = bot.Controller.GetStateName() == MovementStateNames.OnLedge ? hangingFrames + 1 : 0;
            Assert.True(hangingFrames < Sim.Hz, $"Spy hung at {pawn.Location} for a full second.");
        }

        Assert.True(follower.Arrived && pawn.Location.Y > -1840f && pawn.Location.Z > -704f,
            $"Spy from x={startX} ended at {pawn.Location} in {bot.Controller.GetStateName()}.");
    }
}
