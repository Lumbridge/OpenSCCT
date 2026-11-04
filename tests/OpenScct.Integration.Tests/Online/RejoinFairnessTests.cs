using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class RejoinFairnessTests
{
    [Theory]
    [InlineData(false, false, 3, 2)]
    [InlineData(false, true, 3, 3)]
    [InlineData(true, false, 3, 3)]
    [InlineData(false, false, 1, 0)]
    public void ACompetitiveReturnSpendsAnAbandonedLifeAndWaitsForRespawn(bool practice, bool alreadyDead, int lives, int expectedLives)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with { AutoLaunch = false, EndScreenSeconds = 30f };
        settings = settings with { Setup = settings.Setup with { SpyLives = 3, MercLives = 3, RespawnSeconds = 5, Practice = practice } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 851, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
        int attempt = 0;
        OnlineClient Join(string name, int team) => rig.Add(new OnlineClient(
            JoinTarget.Custom("Rejoin server", () => network.Bind(name + ++attempt), "server"),
            OnlineRig.ClientSettings(name, team), rig.Now));
        OnlineClient returning = Join("Returning", 0);
        OnlineClient teammate = Join("Teammate", 0);
        OnlineClient merc = Join("Merc", 1);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == 3), 10));
        foreach (OnlineClient client in rig.Clients) client.SetReady(true);
        Assert.True(rig.RunUntil(() => returning.Lobby.Players.All(p => p.Ready), 10));
        returning.Launch();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match && c.Session?.LocalPlayer?.Pawn is not null), 20));
        SPlayerController Player() => (SPlayerController)rig.Server.Host.Players.Single(p => p.Controller?.PlayerReplicationInfo?.PlayerName == "Returning").Controller!;
        SPlayerController old = Player();
        var before = old.PlayerReplicationInfo!;
        before.NumLives = lives;
        before.Deaths = 2;
        before.Score = 123;
        before.Kills = 1;
        before.iNbObjectivesTaken = 2;
        before.Stats!.Spy_AlarmsTriggered = 4;
        before.Stats.AddFeat(FeatKeys.SpyShockerStuns, 3);
        int participantId = rig.Server.Host.Scoreboards.ParticipantId(before);
        old.Pawn!.Health = alreadyDead ? 0 : 25;
        returning.Leave();
        Assert.True(rig.RunUntil(() => rig.Server.Players.All(p => p.Name != "Returning"), 10));
        Assert.False(rig.Server.Host.Game!.bGameEnded);
        OnlineClient rejoined = Join("Returning", 1); // the reserved side, rather than the new preference, wins
        Assert.True(rig.RunUntil(() => rejoined.State == OnlineClientState.Match && rig.Server.Host.Players.Any(p =>
            p.Controller?.PlayerReplicationInfo?.PlayerName == "Returning"), 10), rejoined.Error);
        SPlayerController current = Player();
        PlayerReplicationInfo after = current.PlayerReplicationInfo!;
        Assert.NotSame(before, after);
        Assert.Same(before.Stats, after.Stats); // Delayed effects still credit the original retained stats object.
        Assert.Equal(0, after.Team!.TeamIndex);
        Assert.Equal(expectedLives, after.NumLives);
        Assert.Equal(2 + (!practice && !alreadyDead ? 1 : 0), after.Deaths);
        Assert.Equal(123, after.Score);
        Assert.Equal(1, after.Kills);
        Assert.Equal(2, after.iNbObjectivesTaken);
        Assert.Equal(4, after.Stats!.Spy_AlarmsTriggered);
        if (!practice)
        {
            Assert.Null(current.Pawn);
            Assert.True(current.IsInState("s_MultiSpectating_WhenKilled"));
            rig.RunUntil(() => false, 2);
            Assert.Null(current.Pawn);
        }
        if (expectedLives > 0)
        {
            Assert.True(rig.RunUntil(() => current.Pawn is { Health: > 0 }, 8));
            Assert.Equal(100, current.Pawn!.Health);
            Assert.Equal(expectedLives, after.NumLives);
        }
        else
        {
            rig.RunUntil(() => false, 8);
            Assert.Null(current.Pawn);
        }
        var row = rig.Server.Host.Scoreboards.Finish()!.Rows.Single(p => p.Name == "Returning");
        Assert.Equal(participantId, row.ParticipantId);
        Assert.False(row.LeftEarly);
        Assert.Equal(2, row.Objectives);
        Assert.Equal(2, row.Stats![FeatKeys.SpyObjectives]);
        Assert.Equal(3, row.Stats[FeatKeys.SpyShockerStuns]);
    }
}
