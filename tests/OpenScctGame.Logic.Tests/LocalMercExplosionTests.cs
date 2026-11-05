using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class LocalMercExplosionTests
{
    [Theory]
    [InlineData(false, NetProfile.Stock, false)]
    [InlineData(true, NetProfile.Stock, false)]
    [InlineData(false, NetProfile.Enhanced, false)]
    [InlineData(true, NetProfile.Enhanced, false)]
    [InlineData(false, NetProfile.Stock, true)]
    [InlineData(true, NetProfile.Stock, true)]
    [InlineData(false, NetProfile.Enhanced, true)]
    [InlineData(true, NetProfile.Enhanced, true)]
    public void SoloMercSurvivesRepeatedSelfExplosionAndRespawn(bool mine, NetProfile profile, bool visitMap)
    {
        string map = visitMap ? "AquaE" : MapCatalog.TestBox;
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(map) is null, $"{map} content is required.");
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = map, Side = PlaySide.Merc, Rules = visitMap ? PlayRules.MapSettings : PlayRules.PlayLevel,
            Mode = "Story", OfflineRules = visitMap, UnlimitedGear = visitMap,
            HostMode = PlayHostMode.Inline, Bots = 0, NetworkProfile = profile,
        }, clock);
        long frame = 0;
        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
                Assert.Null(session.ServerFailure);
                Assert.NotEqual(EClientState.Disconnected, session.Client.State);
            }
        }

        Step(900);
        var authority = Assert.IsType<SPlayerController>(session.ServerController);
        if (!visitMap) authority.GameReplicationInfo!.TimesRespawn = 1;
        for (int death = 0; death < 4; death++)
        {
            var body = Assert.IsAssignableFrom<SPAWNDEFENSE>(authority.Pawn);
            var ownerBody = Assert.IsAssignableFrom<SPAWNDEFENSE>(session.LocalPawn);
            Assert.True(body.Health > 0);
            // TestBox has a known clear patch; an authored map keeps its valid player start.
            if (!visitMap) body.SetLocation(new FVector(700, 300, body.CollisionHeight + 2));
            ownerBody.SetLocation(body.Location);
            body.Velocity = ownerBody.Velocity = FVector.Zero;
            session.Host.Movement.PeekEndpoint(authority)?.Sequenced?.ResetLifetime();
            authority.fSpawnProtectionEnd = 0;
            FVector at = body.Location - new FVector(visitMap ? 20 : 100, 0, 0);
            if (mine)
            {
                var explosive = session.Host.World.Level.SpawnActor(new SMineProx { PcOwner = authority }, at, default, body, body)!;
                explosive.Explode = true;
                explosive.fTRef = authority.Level.TimeSeconds;
                explosive.TempsAvantExplosion = .2f;
            }
            else
            {
                var explosive = session.Host.World.Level.SpawnActor(new SProjFrag(), at, default, body, body)!;
                explosive.SetPhysics(EPhysics.PHYS_None);
                explosive.SetTimer(.2f, false);
            }
            Step(60);
            Assert.True(body.Health <= 0, "Own explosion must kill the merc.");
            Assert.False(session.Loader.Game!.bGameEnded);
            Assert.Equal(OpenScct.Game.GameFlow.K.NoResult, authority.GameReplicationInfo!.WinningTeam);
            Step(900);
            Assert.Equal(EClientState.Joined, session.Client.State);
            Assert.True(authority.Pawn is { Health: > 0 } && !ReferenceEquals(body, authority.Pawn),
                $"No respawn: state={authority.GetStateName()}, client={session.Client.Connection?.CloseReason}, " +
                $"server={session.Host.Driver.ClientConnections.FirstOrDefault()?.CloseReason}");
            Assert.True(session.LocalPawn is { Health: > 0 } && !ReferenceEquals(ownerBody, session.LocalPawn));
            // The rendered Visit Map watcher leaves when this changes, even if the transport stays connected.
            Assert.Equal(OpenScct.Game.GameFlow.K.NoResult, session.LocalController!.GameReplicationInfo!.WinningTeam);
        }
    }
}
