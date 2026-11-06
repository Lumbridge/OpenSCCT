using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class OfflineMatchCapacityTests
{
    [Theory]
    [InlineData(0, 0, false, 4, 1)]
    [InlineData(3, 0, false, 4, 4)]
    [InlineData(5, 6, false, 6, 6)]
    [InlineData(5, 0, false, 6, 6)]
    [InlineData(5, 2, false, 6, 2)]
    [InlineData(0, 12, false, 12, 12)]
    [InlineData(4, 0, true, 6, 6)]
    public void MapRulesAdmitTheRequestedRosterWithoutChangingTheLayoutOverride(
        int bots, int layout, bool route, int capacity, int count)
    {
        GameplayDoc gameplay = GameplayDoc.Parse(Encoding.UTF8.GetBytes("""
            {"formatVersion":"1.1.0","map":"Synth","mapSettings":{},"tagIndex":{},"eventEdges":[],"missionTree":[],
             "levelInfo":{"id":"LevelInfo0","class":"Engine.LevelInfo","props":{"GEActivationMask":0}},"actors":[]}
            """));
        var settings = new PlaySettings
        {
            Bots = bots, MatchPlayers = layout, Rules = PlayRules.MapSettings,
            Route = route ? new PlayRoute(["0,0,0"], PlaySide.Spy, null, _ => { }) : null,
        };
        string options = settings.GameOptions(gameplay);
        Assert.Contains($"?MaxPlayers={capacity}?", options);
        Assert.Contains($"?SvmMatchPlayers={count}", options);
    }

    [Fact]
    public void NormalMapRulesSpawnThreePlayersOnEachTeam()
    {
        Assert.NotNull(MapCatalog.CreateDefault().FindFolder(MapCatalog.TestBox));
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = MapCatalog.TestBox, Rules = PlayRules.MapSettings, HostMode = PlayHostMode.Inline,
            Side = PlaySide.Spy, Bots = 5, MatchPlayers = 6,
        }, clock);
        List<SBasePawn> Pawns()
        {
            var result = new List<SBasePawn>();
            foreach (SBasePawn pawn in session.Host.World.Level.AllActors<SBasePawn>())
                if (pawn.Controller is not null) result.Add(pawn);
            return result;
        }

        for (int frame = 0; frame < 600; frame++)
        {
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            session.Step(clock.NowMicroseconds);
            session.Input.EndTick();
            if (session.LocalPawn is not null && Pawns().Count == 6)
                break;
        }

        Assert.NotNull(session.LocalPawn);
        List<SBasePawn> pawns = Pawns();
        Assert.Equal(3, pawns.Count(p => p.PlayerReplicationInfo?.Team?.TeamIndex == 0));
        Assert.Equal(3, pawns.Count(p => p.PlayerReplicationInfo?.Team?.TeamIndex == 1));
        Assert.Null(session.LoginError);
    }
}
