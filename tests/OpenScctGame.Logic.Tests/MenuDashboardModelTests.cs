using System;
using System.Linq;
using System.Threading.Tasks;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class MenuDashboardModelTests
{
    [Fact]
    public void PreviewPrefersHighlightsAndBoundsLongRemoteNotes()
    {
        string notes = "# OpenSCCT update\nA long introduction.\n\n## Changes\n- **Better menus**\n* [Fixed joining](https://example.test)\n+ Third highlight\n- Fourth highlight";
        Assert.Equal("• Better menus\n• Fixed joining\n• Third highlight", MenuDashboardModel.Preview(notes));
        Assert.Equal("Plain release notes.", MenuDashboardModel.Preview("Plain release notes."));
        Assert.Equal(string.Empty, MenuDashboardModel.Preview(null));
        Assert.True(MenuDashboardModel.Preview("- " + new string('x', 100_000)).Length <= 160);
    }

    [Fact]
    public void NotesRemainPlainTextAndStripFormattingWithoutInterpretingCommands()
    {
        string text = MenuDashboardModel.PlainNotes("## Heading\n<b>Changes</b> &amp; fixes\n- `code` and [url=evil]literal[/url]\n```text\nexample\n```\n---");
        Assert.Equal("Heading\nChanges & fixes\n• code and [url=evil]literal[/url]\nexample", text);
    }

    [Fact]
    public void ActivitySeparatesKindsAndDoesNotOverflowOrCountNegativePlayers()
    {
        var list = new ServerListResponse
        {
            Servers =
            [
                new() { Kind = ServerKind.Game, Players = int.MaxValue },
                new() { Kind = ServerKind.Game, Players = 2 },
                new() { Kind = ServerKind.Game, Players = -7 },
                new() { Kind = ServerKind.Hub, Players = 14 },
            ],
        };
        Assert.Equal(new MenuActivity((long)int.MaxValue + 2, 3), MenuDashboardModel.Activity(list, ServerKind.Game));
        Assert.Equal(new MenuActivity(14, 1), MenuDashboardModel.Activity(list, ServerKind.Hub));
    }

    [Fact]
    public void MissingAccountOrOldMasterDoesNotInventUnlockProgress()
    {
        Assert.Null(MenuDashboardModel.NextUnlock(null));
        Assert.Null(MenuDashboardModel.NextUnlock(new AccountInfo { Stats = new() { Matches = 4 } }));
        Assert.Null(MenuDashboardModel.NextUnlock(new AccountInfo
        {
            Achievements = [new() { Id = "unknown", Current = 99, Target = 100 }],
        }));
    }

    [Fact]
    public void NextUnlockUsesClosestKnownLockedRewardAndCatalogueOrderForTies()
    {
        var account = new AccountInfo
        {
            Achievements =
            [
                new() { Id = "matches_25", Current = 20, Target = 25 },
                new() { Id = "first_match", Current = 4, Target = 5 },
            ],
        };
        MenuUnlock? unlock = MenuDashboardModel.NextUnlock(account);
        Assert.Equal("first_match", unlock?.AchievementId);
        Assert.Equal(0.8f, unlock?.Fraction);
        string[] firstRewards = Achievements.All.Single(a => a.Id == "first_match").Rewards.ToArray();
        Assert.Equal("matches_25", MenuDashboardModel.NextUnlock(account with { Unlocked = firstRewards })?.AchievementId);
        Assert.Equal("matches_25", MenuDashboardModel.NextUnlock(account with
        {
            Achievements = [account.Achievements[0], account.Achievements[1] with { Earned = DateTimeOffset.UtcNow }],
        })?.AchievementId);
    }

    [Fact]
    public void SingleRoundTargetsAndFinishedProgressAreExcludedButCumulativeMetaGoalsRemain()
    {
        Achievement meta = Achievements.All.First(a => a.Round is not null && a.Goal(new AccountProgress()).Target > 1 &&
            a.Rewards.Any(id => CosmeticCatalog.Find(id) is not null));
        var account = new AccountInfo
        {
            Achievements =
            [
                new() { Id = "first_match", Current = 0, Target = 1 },
                new() { Id = "matches_25", Current = 25, Target = 25 },
                new() { Id = meta.Id, Current = 2, Target = 3 },
            ],
        };
        Assert.Equal(meta.Id, MenuDashboardModel.NextUnlock(account)?.AchievementId);
    }

    [Fact]
    public void StaleResponseIsDistinctFromMissingAndZeroActivity()
    {
        var missing = new MenuDashboardFeed<ServerListResponse>(Unavailable: true);
        var empty = new MenuDashboardFeed<ServerListResponse>(new());
        Assert.False(missing.Stale);
        Assert.False(empty.Unavailable);
        Assert.True((empty with { Unavailable = true }).Stale);
        Assert.Equal(new MenuActivity(0, 0), MenuDashboardModel.Activity(empty.Value!, ServerKind.Game));
    }

    [Fact]
    public void PollingDeduplicatesAndRetainsLastGoodDataWithoutRunningWhenHidden()
    {
        var request = new MenuDashboardRequest<string>(30_000);
        var pending = new TaskCompletionSource<string>();
        int calls = 0;
        Task<string> Start() { calls++; return pending.Task; }
        request.Pump(0, true, Start);
        request.Pump(1, true, Start);
        Assert.Equal(1, calls);
        Assert.True(request.View.Loading);
        pending.SetResult("received");
        request.Pump(2, false, Start);
        Assert.Equal("received", request.View.Value);
        request.Pump(50_000, false, Start);
        Assert.Equal(1, calls);

        request.Pump(50_001, true, () => Task.FromException<string>(new InvalidOperationException("offline")));
        request.Pump(50_002, true, Start);
        Assert.Equal("received", request.View.Value);
        Assert.True(request.View.Stale);
        Assert.False(request.View.Loading);
    }

    [Fact]
    public void ChangingMasterDiscardsPendingResponsesAndCachedValues()
    {
        var request = new MenuDashboardRequest<string>(30_000);
        var previousMaster = new TaskCompletionSource<string>();
        request.Pump(0, true, () => previousMaster.Task);
        request.Reset();
        previousMaster.SetResult("wrong server");
        request.Pump(1, false, () => throw new InvalidOperationException("inactive"));
        Assert.Null(request.View.Value);
        request.Pump(2, true, () => Task.FromResult("new server"));
        request.Pump(3, true, () => throw new InvalidOperationException("too early"));
        Assert.Equal("new server", request.View.Value);
        Assert.True(request.Disable());
        Assert.Null(request.View.Value);
        Assert.True(request.View.Unavailable);
    }

    [Fact]
    public void ChangelogRetriesUnavailableUpstreamBeforeNormalCacheInterval()
    {
        var request = new MenuDashboardRequest<ChangelogResponse>(600_000);
        request.Pump(0, true, () => Task.FromResult(new ChangelogResponse { Unavailable = true }));
        request.Pump(1, true, () => throw new InvalidOperationException("too early"), r => r.Unavailable);
        Assert.True(request.View.Unavailable);
        request.Pump(120_000, true, () => throw new InvalidOperationException("too early"));
        request.Pump(120_001, true, () => Task.FromResult(new ChangelogResponse()));
        Assert.True(request.View.Loading);
    }

    [Fact]
    public void ChangelogKeepsClientCacheWhenRestartedMasterHasNoUpstreamNotes()
    {
        var request = new MenuDashboardRequest<ChangelogResponse>(600_000);
        var cached = new ChangelogResponse { Entries = [new() { Tag = "v1.2.3", Notes = "Known release" }] };
        request.Pump(0, true, () => Task.FromResult(cached));
        request.Pump(1, false, () => throw new InvalidOperationException("inactive"));
        request.Pump(600_001, true, () => Task.FromResult(new ChangelogResponse { Unavailable = true }));
        request.Pump(600_002, false, () => throw new InvalidOperationException("inactive"),
            r => r.Unavailable, MenuDashboardModel.MergeChangelog);
        Assert.Equal("v1.2.3", Assert.Single(request.View.Value!.Entries).Tag);
        Assert.True(request.View.Stale);
        Assert.True(request.View.Value.Unavailable);

        // A successful empty result really is empty; the cache must not conceal it forever.
        request.Pump(720_002, true, () => Task.FromResult(new ChangelogResponse()));
        request.Pump(720_003, false, () => throw new InvalidOperationException("inactive"),
            r => r.Unavailable, MenuDashboardModel.MergeChangelog);
        Assert.Empty(request.View.Value!.Entries);
        Assert.False(request.View.Unavailable);
    }

    [Fact]
    public void ChangelogAcceptsMastersOwnLastGoodNotesWhenTheyExist()
    {
        var previous = new ChangelogResponse { Entries = [new() { Tag = "v1.2.3" }] };
        var incoming = new ChangelogResponse { Entries = [new() { Tag = "v1.2.4" }], Unavailable = true };
        Assert.Same(incoming, MenuDashboardModel.MergeChangelog(previous, incoming));
        Assert.True(MenuDashboardModel.MergeChangelog(null, new() { Unavailable = true }).Unavailable);
    }
}
