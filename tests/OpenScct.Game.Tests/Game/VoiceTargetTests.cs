using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using Xunit;

namespace OpenScct.Game.Tests.Game;

public sealed class VoiceTargetTests
{
    private static (GameWorld World, SPlayerController Spy, SPlayerController Merc) Scene(bool wall = false)
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory,
            "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        var geometry = new CollisionDataBuilder();
        if (wall) geometry.AddBox(new FVector(400, -200, -200), new FVector(450, 200, 300));
        CollisionWorld collision = CollisionWorld.Build(geometry.Build());
        collision.BindLevel(world.Level);
        world.Level.PhysicsDriver = new PhysicsDriver(collision);
        players[0].Pawn!.SetLocation(new FVector(0, 0, 100));
        players[1].Pawn!.SetLocation(new FVector(1000, 0, 100));
        players[0].Pawn!.Health = players[1].Pawn!.Health = 100;
        players[0].Rotation = default;
        players[0].GotoState("s_Binoculars_Att");
        players[0].bBinocularVision = true;
        world.Gri.VoiceChatMode = EVoiceChatMode.VCM_Modern;
        return (world, players[0], players[1]);
    }

    [Fact]
    public void AScopedLivingEnemyOnTheAuthoritativeTraceCanBeHeard()
    {
        var (_, spy, merc) = Scene();
        spy.ServerSetVoiceChatTarget(merc.Pawn);
        Assert.Same(merc, spy.ValidatedVoiceChatTarget());
    }

    [Theory]
    [InlineData("wall")]
    [InlineData("unscoped")]
    [InlineData("away")]
    [InlineData("range")]
    [InlineData("dead-listener")]
    [InlineData("dead-target")]
    [InlineData("team-mode")]
    public void TheServerRejectsAnIneligibleCandidate(string reason)
    {
        var (world, spy, merc) = Scene(reason == "wall");
        switch (reason)
        {
            case "unscoped": spy.GotoState("s_PlayerWalkingSt0H"); break;
            case "away": spy.Rotation = new FRotator(0, 16384, 0); break;
            case "range": merc.Pawn!.SetLocation(new FVector(8000, 0, 100)); break;
            case "dead-listener": spy.Pawn!.Health = 0; break;
            case "dead-target": merc.Pawn!.Health = 0; break;
            case "team-mode": world.Gri.VoiceChatMode = EVoiceChatMode.VCM_Team; break;
        }
        spy.ServerSetVoiceChatTarget(merc.Pawn);
        Assert.Null(spy.VoiceChatTargetPC);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APreviouslyAcceptedTargetIsClearedWithoutAClientClear(bool leaveScope)
    {
        var (_, spy, merc) = Scene();
        spy.ServerSetVoiceChatTarget(merc.Pawn);
        Assert.Same(merc, spy.VoiceChatTargetPC);
        if (leaveScope) spy.GotoState("s_PlayerWalkingSt0H");
        else merc.Pawn!.SetLocation(new FVector(1000, 1000, 100));
        Assert.Null(spy.ValidatedVoiceChatTarget());
        Assert.Null(spy.VoiceChatTargetPC);
    }
}
