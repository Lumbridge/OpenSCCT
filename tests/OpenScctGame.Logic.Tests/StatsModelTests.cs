using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenScct.Core.Engine;
using OpenScct.Net.Discovery;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class StatsModelTests
{
    [Fact]
    public void AccuracyUsesCareerShotTotalsAndHandlesNoShots()
    {
        var rows = StatsModel.Build(new AccountInfo { Career = new() { Feats = new Dictionary<string, int>
            { [CareerStatCounters.ShotsHit] = 900, [CareerStatCounters.ShotsFired] = 1200 } } });
        Assert.Equal("75.0%", rows.Single(r => r.Label == "Accuracy").Value);
        Assert.Equal("—", StatsModel.Build(new AccountInfo { Career = new() }).Single(r => r.Label == "Accuracy").Value);
    }

    [Fact]
    public void RoleWinRatesUseTheirOwnTrackedRoundsAndMineKillsAppearUnderSpy()
    {
        var rows = StatsModel.Build(new AccountInfo
        {
            Stats = new() { Matches = 100, SpyWins = 40, MercWins = 30,
                SpyRounds = new() { Matches = 4, Wins = 3 }, MercRounds = new() { Matches = 5, Wins = 1 } },
            Career = new() { Feats = new Dictionary<string, int> { [FeatKeys.SpyMineKills] = 37 } },
        });
        Assert.Contains(rows, r => r.Category == "Spy" && r.Label.StartsWith("Win rate") && r.Value == "75.0%");
        Assert.Contains(rows, r => r.Category == "Mercenary" && r.Label.StartsWith("Win rate") && r.Value == "20.0%");
        Assert.Contains(rows, r => r.Category == "Spy" && r.Label == "Mercenaries killed with their mines" && r.Value == "37");
        Assert.Contains(rows, r => r.Category == "Mercenary" && r.Label == "Choke kills");
        Assert.All(StatsModel.Build(new AccountInfo()).Where(r => r.Label.StartsWith("Win rate")), r => Assert.Equal("—", r.Value));
    }

    [Fact]
    public void OldMastersAndZeroDenominatorsAreUnavailableRatherThanInvented()
    {
        var account = JsonSerializer.Deserialize<AccountInfo>("{}", MasterApi.Json)!;
        Assert.Null(account.Career);
        var rows = StatsModel.Build(account);
        Assert.Equal("—", rows.Single(r => r.Label == "Kill / death ratio").Value);
        Assert.Equal("—", rows.Single(r => r.Label == "Win rate").Value);
        Assert.All(rows.Where(r => r.Category == "Weapons"), r => Assert.Equal("—", r.Value));
        Assert.Equal("0", rows.Single(r => r.Label == "Kills").Value);
    }

    [Fact]
    public void CareerCountersAreUncappedAndRatiosUseTheCorrectDenominator()
    {
        var rows = StatsModel.Build(new AccountInfo
        {
            Stats = new() { Matches = 10, SpyWins = 3, MercWins = 2, Kills = 30, Deaths = 12 },
            Career = new() { Feats = new Dictionary<string, int> { [FeatKeys.SpyObjectives] = 12345 } },
        });
        Assert.Equal("50.0%", rows.Single(r => r.Label == "Win rate").Value);
        Assert.Equal("2.50", rows.Single(r => r.Label == "Kill / death ratio").Value);
        Assert.Equal("12,345", rows.Single(r => r.Label == "Objectives completed").Value);
        Assert.All(rows.Where(r => r.Category == "Weapons" && r.Label != "Accuracy"), r => Assert.Equal("0", r.Value));
    }

    [Fact]
    public void EveryTrackedFeatHasAVisibleRowAndMapsPreserveBothSides()
    {
        var rows = StatsModel.Build(new AccountInfo
        {
            Career = new()
            {
                Feats = FeatKeys.All.Select((key, i) => (key, value: i + 100)).ToDictionary(p => p.key, p => p.value),
                Maps = [new() { Map = "Stee", SpyWins = 7, MercWins = 9 }],
            },
        });
        foreach (int value in Enumerable.Range(100, FeatKeys.All.Length))
            Assert.Contains(rows, r => r.Value == StatsModel.Number(value));
        Assert.Contains(rows, r => r.Category == "Maps" && r.Label.EndsWith("Spy wins") && r.Value == "7");
        Assert.Contains(rows, r => r.Category == "Maps" && r.Label.EndsWith("Mercenary wins") && r.Value == "9");
    }
}
