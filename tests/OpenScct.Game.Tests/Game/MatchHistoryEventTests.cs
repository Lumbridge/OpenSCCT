using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

public sealed class MatchHistoryEventTests
{
    private sealed class Sink : IMatchHistorySink
    {
        public List<(string Kind, string Text)> Events { get; } = [];
        public void Record(string kind, string text) => Events.Add((kind, text));
    }

    [Fact]
    public void AuthoritativeDeathsAndObjectivesReachTheOptionalRecorder()
    {
        var (world, players) = DeathLivesRespawnTests.Match(MatchLifecycleTests.SteeEStory, "?Name=Spy?Team=0", "?Name=Merc?Team=1");
        var sink = new Sink();
        world.Level.SetService<IMatchHistorySink>(sink);
        world.Game.Killed(players[1], players[0], players[0].Pawn, SDamageType.SDTBase);
        world.Game.NotifyObjectiveTaken(new SObjective { ObjectiveName = "Terminal" });
        Assert.Equal("death", sink.Events[0].Kind);
        Assert.Contains("Spy", sink.Events[0].Text);
        Assert.Equal("objective", sink.Events[1].Kind);
        Assert.Contains("Terminal", sink.Events[1].Text);
    }
}
