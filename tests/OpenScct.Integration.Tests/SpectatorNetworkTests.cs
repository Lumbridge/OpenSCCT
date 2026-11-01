using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class SpectatorNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => new();
    }

    [Fact]
    public void RemoteVisitorCyclesAcrossTeamsAndReplacesADeadTarget()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 120, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=2?LivesDEF=2?TimesRespawnsATT=10",
            Bots = [new("Spy", BotTeam.Spy, () => new IdleBrain()), new("Merc", BotTeam.Merc, () => new IdleBrain())],
        }, network.Bind, "server", 0);
        using var visitorTransport = network.Bind("visitor");
        using var visitor = new ClientSession(visitorTransport, "server", 0, new ClientSettings
        {
            Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, Gameplay = true,
            PlayerName = "Visitor", LoginOptions = "?SpectatorOnly=true?Team=255",
        });
        long tick = 0;
        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++tick, Sim.Hz));
                match.Step(clock.NowMicroseconds);
                visitor.Tick(clock.NowMicroseconds);
            }
        }
        Step(900);
        var local = Assert.IsType<SPlayerController>(visitor.LocalPlayer);
        var source = Assert.IsType<SPlayerController>(match.Server.Players.Single(p => p.Name == "Visitor").Controller);
        Assert.True(local.PlayerReplicationInfo!.bOnlySpectator);
        Assert.True(local.IsSpectating());
        var first = Assert.IsType<SPlayerController>(source.PC_spectated);
        local.eventFire(1f);
        Step(120);
        var second = Assert.IsType<SPlayerController>(source.PC_spectated);
        Assert.NotSame(first, second);
        Assert.NotEqual(first.PlayerReplicationInfo!.Team, second.PlayerReplicationInfo!.Team);
        Assert.Equal(second.PlayerReplicationInfo.PlayerName, local.SpecTarget?.PlayerReplicationInfo?.PlayerName);
        second.PawnTakeDamage(1000, null, second.Pawn!.Location, default, SDamageType.SDTBase);
        Step(600);
        Assert.Same(first, source.PC_spectated);
        Assert.Equal(first.PlayerReplicationInfo.PlayerName, local.SpecTarget?.PlayerReplicationInfo?.PlayerName);
        Assert.False(match.Server.Game!.bGameEnded);
    }

    [Theory]
    [InlineData(BotTeam.Spy, false)]
    [InlineData(BotTeam.Merc, false)]
    [InlineData(BotTeam.Spy, true)]
    [InlineData(BotTeam.Merc, true)]
    public void RemoteSpectatorCyclesAndReplacesADeadTeammate(BotTeam team, bool manuallyCycleFirst)
        => RunTeammateChange(team, manuallyCycleFirst, disconnect: false);

    [Theory]
    [InlineData(BotTeam.Spy)]
    [InlineData(BotTeam.Merc)]
    public void RemoteSpectatorReplacesADisconnectedTeammateAndUpdatesRemainingLives(BotTeam team)
        => RunTeammateChange(team, manuallyCycleFirst: false, disconnect: true);

    private static void RunTeammateChange(BotTeam team, bool manuallyCycleFirst, bool disconnect)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 119, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=1?LivesDEF=1?TimesRespawnsATT=10",
            Bots = [new("Watcher", team, () => new IdleBrain()), new("First", team, () => new IdleBrain()),
                new("Second", team, () => new IdleBrain()), new("Opponent", team == BotTeam.Spy ? BotTeam.Merc : BotTeam.Spy, () => new IdleBrain())],
        }, network.Bind, "server", 0);
        long tick = 0;
        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++tick, Sim.Hz));
                match.Step(clock.NowMicroseconds);
            }
        }
        Step(900);
        BotClient owner = match.Bots[0];
        SPlayerController watcher = match.ServerController(owner)!;
        Assert.Equal(3, match.Server.Players.Count(p => p.Controller!.PlayerReplicationInfo!.Team == watcher.PlayerReplicationInfo!.Team));
        void Kill(SPlayerController pc) => pc.PawnTakeDamage(1000, null, pc.Pawn!.Location, default, SDamageType.SDTBase);
        Kill(watcher);
        Step(900);
        Assert.True(owner.Controller!.IsSpectating());
        var first = Assert.IsType<SPlayerController>(watcher.PC_spectated);
        if (manuallyCycleFirst)
        {
            owner.Controller.eventFire(1f);
            Step(120);
            Assert.NotSame(first, watcher.PC_spectated);
        }
        var watched = Assert.IsType<SPlayerController>(watcher.PC_spectated);
        var remaining = match.Server.Players.Select(p => p.Controller).OfType<SPlayerController>()
            .Single(p => p != watcher && p != watched && p.PlayerReplicationInfo!.Team == watcher.PlayerReplicationInfo!.Team);
        if (disconnect) match.Bots.Single(b => b.Name == watched.PlayerReplicationInfo!.PlayerName).Session.Disconnect();
        else Kill(watched);
        // A lost close packet is eventually resolved by the normal 15-second idle timeout.
        Step(disconnect ? 1200 : 900);
        Assert.True(ReferenceEquals(remaining, watcher.PC_spectated),
            $"Expected {remaining.PlayerReplicationInfo?.PlayerName}, watching {(watcher.PC_spectated as SPlayerController)?.PlayerReplicationInfo?.PlayerName}, " +
            $"left deleted={watched.bDeleteMe}, pawn={watched.Pawn?.bDeleteMe}, roster={string.Join(',', match.Server.Players.Select(p => p.Name))}, " +
            $"owner target={owner.Controller.SpecTarget?.PlayerReplicationInfo?.PlayerName}, state={owner.Controller.GetStateName()}");
        Assert.Same(remaining.Pawn, watcher.SpecTarget);
        Assert.Equal(remaining.PlayerReplicationInfo!.PlayerName, owner.Controller.SpecTarget?.PlayerReplicationInfo?.PlayerName);
        Assert.False(match.Server.Game!.bGameEnded);
        Assert.Equal(1, match.Server.Game.GameReplicationInfo!.TotalTeamLives[(int)team]);
        Assert.Equal(1, owner.Controller.GameReplicationInfo!.TotalTeamLives[(int)team]);
    }
}
