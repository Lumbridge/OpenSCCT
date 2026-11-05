using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class PublicLeaderboardTests
{
    [Theory]
    [InlineData("wins", "Bravo")]
    [InlineData("kills", "Charlie")]
    [InlineData("rounds", "Charlie")]
    [InlineData("spy-wins", "Alpha")]
    [InlineData("merc-wins", "Bravo")]
    [InlineData("objectives", "Bravo")]
    [InlineData("headshots", "Bravo")]
    [InlineData("damage", "Bravo")]
    public void RankingsUseStoredCareerTotalsAndKeepPrivateProfilesOut(string metric, string expected)
    {
        using var store = AccountStore.Open(":memory:");
        long alpha = Player(store, "Alpha"), bravo = Player(store, "Bravo"), charlie = Player(store, "Charlie");
        Play(store, alpha, 2, 2, 0, 2, 2, 1, 50);
        Play(store, bravo, 3, 3, 1, 4, 2, 2, 100);
        Play(store, charlie, 4, 1, 0, 5, 0, 0, 10);
        long privateId = Player(store, "Private");
        long hidden = Player(store, "Hidden");
        long banned = Player(store, "Banned");
        foreach (long id in new[] { privateId, hidden, banned }) Play(store, id, 5, 5, 0, 20, 20, 20, 1000);
        store.SetProfilePublic(privateId, false);
        store.SetProfileHidden(hidden, true, "fixture");
        store.AddBan(banned, null, null, "fixture", null);

        (List<PublicPlayerRow> rows, int total) = store.PublicPlayers(null, metric, 0, 50);
        Assert.Equal(3, total);
        Assert.Equal(expected, rows[0].Name);
        Assert.All(rows, r => Assert.Contains(r.Id, new[] { alpha, bravo, charlie }));
        PublicPlayerRow spy = Assert.Single(rows, r => r.Id == alpha);
        Assert.Equal(2, spy.SpyWins);
        Assert.Equal(0, spy.MercWins);
        Assert.Equal(4, spy.Objectives);
        Assert.Equal(2, spy.Headshots);
        Assert.Equal(100, spy.Damage);
    }

    [Fact]
    public void WinRateRequiresTenRoundsAndBreaksTiesBySampleSizeBeforePagination()
    {
        using var store = AccountStore.Open(":memory:");
        long small = Player(store, "Small");
        long large = Player(store, "Large");
        long early = Player(store, "Early");
        Play(store, small, 10, 6, 0);
        Play(store, large, 20, 12, 1);
        Play(store, early, 9, 9, 0);
        var first = store.PublicPlayers(null, "win-rate", 0, 1);
        var second = store.PublicPlayers(null, "win-rate", 1, 1);
        Assert.Equal(2, first.Total);
        Assert.Equal(2, second.Total);
        Assert.Equal(large, Assert.Single(first.Rows).Id);
        Assert.Equal(small, Assert.Single(second.Rows).Id);
    }

    [Fact]
    public void UnknownSortFallsBackToXpAndSearchTreatsWildcardsLiterally()
    {
        using var store = AccountStore.Open(":memory:");
        long literal = Player(store, "Test_Name");
        long other = Player(store, "TestOtherName");
        Play(store, literal, 1, 1, 0);
        Play(store, other, 2, 2, 1);
        var ranked = store.PublicPlayers(null, "xp", 0, 50);
        var untrusted = store.PublicPlayers(null, "wins; DROP TABLE accounts;--", 0, 50);
        Assert.Equal(ranked.Rows.Select(r => r.Id), untrusted.Rows.Select(r => r.Id));
        var searched = store.PublicPlayers("_", "name", 0, 50);
        Assert.Equal(literal, Assert.Single(searched.Rows).Id);
    }

    private static long Player(AccountStore store, string name)
    {
        long id = store.GetOrCreate("leaderboard-" + name).Id;
        Assert.Equal(NameClaim.Done, store.ClaimName(id, name, ignoreCooldown: true, out _));
        return id;
    }

    private static void Play(AccountStore store, long id, int rounds, int wins, int team, int kills = 1, int objectives = 0, int headshots = 0, int damage = 0)
    {
        for (int i = 0; i < rounds; i++)
            store.ApplyResults(new ResultsReport
            {
                WinningTeam = i < wins ? team : 1 - team,
                Rows = [new() { AccountId = id, Team = team, Kills = kills, Deaths = 1, Feats = new Dictionary<string, int>
                {
                    [FeatKeys.SpyObjectives] = objectives,
                    [FeatKeys.MercHeadshots] = headshots,
                    ["career.damagegiven"] = damage,
                } }],
            }, "Leaderboard fixture");
    }
}
