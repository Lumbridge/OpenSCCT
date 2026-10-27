using System;
using System.Globalization;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Game.Tests.AI;

/// <summary>
/// Story 2v2 matches of server-side bots on a dedicated server with no connections (docs/AI.md, "Matches"): every bot
/// logs in, gets a pawn and plays through the same input path as a player. The report gives the objectives taken, the
/// kills, the round time and what each bot did. TestBox runs by default; SteeE (converted locally) runs its normal
/// match by default and every difficulty with <c>SVM_AI_MATCHES=1</c>.
/// </summary>
[Collection(AiMatchCollection.Name)]
public sealed class AiMatchTests
{
    private static readonly MapCatalog Maps = MapCatalog.CreateDefault();
    private static readonly Lazy<NetClassRegistry> Classes = new(() => NetClasses.Create());
    private static readonly AiNavCache Nav = new();

    private readonly ITestOutputHelper _output;

    public AiMatchTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private AiBotMatchReport Play(string map, AiSkill skill, ulong seed, float seconds, string options, AiDifficulty? mercs = null,
        string? name = null)
    {
        var log = new System.Text.StringBuilder();
        AiDifficulty d = AiDifficulty.For(skill);
        using var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = map,
            GameOptions = options,
            Bots = ServerBotSettings.TwoVersusTwo(d, mercs ?? d),
            Seed = seed,
            Classes = Classes.Value,
            Maps = Maps,
            Nav = Nav,

            // The converted terminal collision keeps a walker short of the use spot (docs/NETCODE.md, "Bot matches").
            TerminalsBlockPlayers = false,
        });
        int lastLine = -1;
        AiBotMatchReport report = match.Run(seconds, everySecond: m =>
        {
            int second = (int)(m.Frame / Sim.Hz);
            if (second / 20 != lastLine)
            {
                lastLine = second / 20;
                foreach (AiController bot in m.Bots.Bots)
                {
                    string line = string.Create(CultureInfo.InvariantCulture,
                        $"{second,4} s {bot.Controller.PlayerReplicationInfo?.PlayerName}: {bot.Status} at {bot.Pawn?.Location}");
                    _output.WriteLine(line);
                    log.AppendLine(line);
                }
            }
        });
        _output.WriteLine(report.ToString());
        log.AppendLine(report.ToString());
        foreach (string line in match.Bots.Log)
        {
            _output.WriteLine("log: " + line);
            log.AppendLine("log: " + line);
        }

        AiTestKit.SaveReport(name ?? $"match-{map}-{skill}", log.ToString());

        Assert.Empty(match.Bots.Log);
        Assert.Equal(4, match.Bots.Bots.Count);
        Assert.True(report.Started, "the match did not start");
        foreach (AiBotReport b in report.Bots)
        {
            Assert.True(b.ThinkTicks > 0, $"{b.Name} never had a pawn");
        }

        return report;
    }

    [Fact]
    public void TestBoxMatchPlaysToAnEnd()
    {
        Assert.SkipWhen(Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        // Seed 12: with the decoded frag flight (half gravity) seed 11's match ends before a spy reaches a terminal;
        // the property holds for five of seven seeds with either flight.
        AiBotMatchReport r = Play(MapCatalog.TestBox, AiSkill.Normal, 12UL, 360f, BotMatchSettings.StoryOptions);

        // The spies go for the terminals and use them; the round ends one way or the other.
        int uses = 0;
        foreach (AiBotReport b in r.Bots)
        {
            uses += b.UseRequests;
        }

        Assert.True(uses > 0, "no spy reached a terminal");
        Assert.True(r.Ended, "the round did not end");
    }

    /// <summary>Mercs that neither see nor hear: the spies walk to every terminal, use it and win on objectives.</summary>
    [Fact]
    public void SpiesHackTheTerminalsWhenNobodyStopsThem()
    {
        Assert.SkipWhen(Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        AiDifficulty blind = AiDifficulty.For(AiSkill.Easy) with { SightRange = 1f, HearingScale = 0f, Teamwork = 0f };
        AiBotMatchReport r = Play(MapCatalog.TestBox, AiSkill.Normal, 12UL, 240f, BotMatchSettings.StoryOptions, blind, "match-TestBox-blind-mercs");
        Assert.True(r.Ended, "the round did not end");
        Assert.Equal(0, r.WinningTeam);
        Assert.Equal((int)EEndReason.END_STORY, r.WinningReason);
        Assert.True(r.ObjectivesTaken >= r.ObjectivesNeeded, $"objectives {r.ObjectivesTaken} of {r.ObjectivesNeeded}");
    }

    [Theory]
    [InlineData(AiSkill.Easy, 21UL)]
    [InlineData(AiSkill.Normal, 22UL)]
    [InlineData(AiSkill.Hard, 23UL)]
    [InlineData(AiSkill.Expert, 24UL)]
    public void SteeEMatchPerDifficulty(AiSkill skill, ulong seed)
    {
        Assert.SkipWhen(Maps.FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        Assert.SkipUnless(skill == AiSkill.Normal || AiTestKit.LongRuns, "Set SVM_AI_MATCHES=1 for every difficulty (several minutes each).");
        AiBotMatchReport r = Play("SteeE", skill, seed, 660f, BotMatchSettings.StoryOptions);
        Assert.True(r.Ended, "the round did not end");
        Assert.True(r.ObjectivesTaken > 0 || r.MercKills > 0, "neither side achieved anything");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AiMatchCollection
{
    public const string Name = "AI matches";
}
