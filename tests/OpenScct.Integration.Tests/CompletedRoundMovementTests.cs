using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class CompletedRoundMovementTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(0, "B")]
    [InlineData(2, "A")]
    [InlineData(2, "B")]
    public void ResultsAfterLongEliminationKeepEveryPlayerConnectedWithoutRestartingInput(int victimIndex, string link)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 2875, SimLinkProfile.Get(link));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Profile = NetProfile.Enhanced,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=1?LivesDEF=1",
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
            }
        }
        Tick(900);
        BotClient victim = match.Bots[victimIndex];
        SPlayerController source = match.ServerController(victim)!;
        var body = Assert.IsAssignableFrom<SBasePawn>(source.Pawn);
        source.PawnTakeDamage(1000, null, body.Location, default, SDamageType.SDTBase);
        Tick(120);
        Assert.False(match.Server.Game!.bGameEnded);
        var moves = victim.Session.Movement.PeekEndpoint(victim.Controller!)!.Sequenced!;
        uint stoppedSequence = moves.NextSequence;
        Tick(SequencedMoveTransport.HistoryTicks + 60);
        Assert.Equal(stoppedSequence, moves.NextSequence);

        SPlayerController teammate = match.ServerController(match.Bots[victimIndex + 1])!;
        teammate.PawnTakeDamage(1000, null, teammate.Pawn!.Location, default, SDamageType.SDTBase);
        Tick(120);
        Assert.True(match.Server.Game.bGameEnded);
        int winner = victimIndex == 0 ? 1 : 0;
        var endedSequences = new uint[match.Bots.Count];
        for (int i = 0; i < match.Bots.Count; i++)
        {
            BotClient client = match.Bots[i];
            Assert.Equal(EClientState.Joined, client.Session.State);
            Assert.True(client.Controller!.bGameEnded);
            Assert.Equal(winner, client.Controller.GameReplicationInfo!.WinningTeam);
            Assert.Equal((byte)EEndReason.END_TEAMDEATH, client.Controller.GameReplicationInfo.WinningReason);
            endedSequences[i] = client.Session.Movement.PeekEndpoint(client.Controller)!.Sequenced!.NextSequence;
        }
        // A long results screen must not fill the bounded input ring or request a new prediction epoch.
        Tick(SequencedMoveTransport.HistoryTicks + 60);
        for (int i = 0; i < match.Bots.Count; i++)
        {
            BotClient client = match.Bots[i];
            var ended = client.Session.Movement.PeekEndpoint(client.Controller!)!.Sequenced!;
            Assert.Equal(EClientState.Joined, client.Session.State);
            Assert.Equal(endedSequences[i], ended.NextSequence);
            Assert.Equal(0, ended.ResynchronizationRequests);
        }
        Assert.Equal(stoppedSequence, moves.NextSequence);
    }
}
